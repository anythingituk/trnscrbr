using System.IO;
using NAudio.Wave;
using Trnscrbr.Models;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Services;

public sealed class AudioCaptureService : IDisposable
{
    private const int WavHeaderBytes = 44;
    private const int MinimumAudioDataBytes = 3200;

    private readonly AppStateViewModel _state;
    private readonly object _syncRoot = new();
    private readonly Queue<byte[]> _preBuffer = new();
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _recordingPath;
    private DateTimeOffset? _startedAt;
    private bool _cancelled;
    private bool _isRecording;
    private int _preBufferBytes;
    private int? _activeDeviceNumber;
    private WaveFormat _waveFormat = new(16000, 16, 1);
    private int _recordingCallbackCount;
    private long _recordingBytes;
    private double _recordingPeak;

    public AudioCaptureService(AppStateViewModel state)
    {
        _state = state;
    }

    public event EventHandler<double>? InputLevelChanged;

    public AudioCaptureSummary LastCaptureSummary { get; private set; } = AudioCaptureSummary.Empty;

    public IReadOnlyList<AudioInputDevice> GetInputDevices()
    {
        var devices = new List<AudioInputDevice>
        {
            new(-1, "Windows default", _state.Settings.MicrophoneName == "Windows default")
        };

        for (var i = 0; i < WaveIn.DeviceCount; i++)
        {
            var capabilities = WaveIn.GetCapabilities(i);
            devices.Add(new AudioInputDevice(i, capabilities.ProductName, _state.Settings.MicrophoneName == capabilities.ProductName));
        }

        return devices;
    }

    public void Start()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Trnscrbr");
        Directory.CreateDirectory(directory);

        _recordingPath = Path.Combine(directory, $"recording-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}.wav");
        _startedAt = DateTimeOffset.Now;
        _cancelled = false;
        LastCaptureSummary = AudioCaptureSummary.Empty;
        EnsureCaptureStarted();

        lock (_syncRoot)
        {
            _writer = new WaveFileWriter(_recordingPath, _waveFormat);
            foreach (var chunk in _preBuffer)
            {
                _writer.Write(chunk, 0, chunk.Length);
            }

            _writer.Flush();
            _isRecording = true;
            _recordingCallbackCount = 0;
            _recordingBytes = 0;
            _recordingPeak = 0;
        }
    }

    public RecordedAudio? Stop()
    {
        if (_writer is null || _recordingPath is null)
        {
            return null;
        }

        var path = _recordingPath;
        var startedAt = _startedAt ?? DateTimeOffset.Now;
        var format = _waveFormat;
        var microphone = _state.Settings.MicrophoneName;
        var duration = DateTimeOffset.Now - startedAt;
        var callbackCount = 0;
        var recordedBytes = 0L;
        var peak = 0d;

        lock (_syncRoot)
        {
            callbackCount = _recordingCallbackCount;
            recordedBytes = _recordingBytes;
            peak = _recordingPeak;
            _isRecording = false;
            _writer.Dispose();
            _writer = null;
            _recordingPath = null;
            _startedAt = null;
        }

        StopIdleCaptureIfDisabled();

        if (_cancelled)
        {
            LastCaptureSummary = new AudioCaptureSummary(duration, callbackCount, recordedBytes, peak, 0);
            TryDelete(path);
            return null;
        }

        var file = new FileInfo(path);
        var audioBytes = file.Exists ? Math.Max(0, file.Length - WavHeaderBytes) : 0;
        LastCaptureSummary = new AudioCaptureSummary(duration, callbackCount, recordedBytes, peak, audioBytes);
        if (!file.Exists || file.Length - WavHeaderBytes < MinimumAudioDataBytes)
        {
            TryDelete(path);
            return null;
        }

        return new RecordedAudio(
            path,
            duration,
            format.SampleRate,
            format.Channels,
            file.Length,
            microphone);
    }

    public void StopAndDelete()
    {
        _cancelled = true;
        var path = _recordingPath;

        lock (_syncRoot)
        {
            _isRecording = false;
            _writer?.Dispose();
            _writer = null;
            _recordingPath = null;
            _startedAt = null;
        }

        StopIdleCaptureIfDisabled();

        if (path is not null)
        {
            TryDelete(path);
        }
    }

    public void DeleteRecording(RecordedAudio? audio)
    {
        if (audio is not null)
        {
            TryDelete(audio.FilePath);
        }
    }

    public void Dispose()
    {
        StopAndDelete();
        StopCapture();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_syncRoot)
        {
            if (_isRecording)
            {
                _writer?.Write(e.Buffer, 0, e.BytesRecorded);
                _writer?.Flush();
                _recordingCallbackCount++;
                _recordingBytes += e.BytesRecorded;
                _recordingPeak = Math.Max(_recordingPeak, CalculatePeak(e.Buffer, e.BytesRecorded));
            }
            else
            {
                AddToPreBuffer(e.Buffer, e.BytesRecorded);
            }
        }

        InputLevelChanged?.Invoke(this, CalculatePeak(e.Buffer, e.BytesRecorded));
    }

    public void ApplyPreBufferSetting()
    {
        if (_state.Settings.CaptureStartupBufferMilliseconds > 0)
        {
            EnsureCaptureStarted();
        }
        else if (!_isRecording)
        {
            StopCapture();
        }
    }

    private void EnsureCaptureStarted()
    {
        var deviceNumber = ResolveDeviceNumber();
        if (_waveIn is not null)
        {
            if (_activeDeviceNumber == deviceNumber || _isRecording)
            {
                return;
            }

            StopCapture();
        }

        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = _waveFormat,
            BufferMilliseconds = 20,
            NumberOfBuffers = 3
        };
        _activeDeviceNumber = deviceNumber;

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
    }

    private void StopIdleCaptureIfDisabled()
    {
        if (_state.Settings.CaptureStartupBufferMilliseconds == 0 && !_isRecording)
        {
            StopCapture();
        }
    }

    private void StopCapture()
    {
        try
        {
            if (_waveIn is not null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.StopRecording();
            }
        }
        catch
        {
            // Stopping capture can race with device removal or app shutdown.
        }

        _waveIn?.Dispose();
        _waveIn = null;
        _activeDeviceNumber = null;

        lock (_syncRoot)
        {
            _preBuffer.Clear();
            _preBufferBytes = 0;
        }
    }

    private void AddToPreBuffer(byte[] buffer, int bytesRecorded)
    {
        if (_state.Settings.CaptureStartupBufferMilliseconds <= 0)
        {
            _preBuffer.Clear();
            _preBufferBytes = 0;
            return;
        }

        var chunk = new byte[bytesRecorded];
        Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRecorded);
        _preBuffer.Enqueue(chunk);
        _preBufferBytes += chunk.Length;

        var maxBytes = _waveFormat.AverageBytesPerSecond * _state.Settings.CaptureStartupBufferMilliseconds / 1000;
        while (_preBufferBytes > maxBytes && _preBuffer.Count > 0)
        {
            _preBufferBytes -= _preBuffer.Dequeue().Length;
        }
    }

    private int ResolveDeviceNumber()
    {
        if (_state.Settings.MicrophoneName == "Windows default")
        {
            return -1;
        }

        var availableDevices = new List<string>();
        for (var i = 0; i < WaveIn.DeviceCount; i++)
        {
            var capabilities = WaveIn.GetCapabilities(i);
            availableDevices.Add(capabilities.ProductName);
            if (capabilities.ProductName == _state.Settings.MicrophoneName)
            {
                return i;
            }
        }

        var available = availableDevices.Count == 0
            ? "none"
            : string.Join(", ", availableDevices);
        throw new InvalidOperationException(
            $"Microphone '{_state.Settings.MicrophoneName}' is not available. Available microphones: {available}. Choose Windows default or reconnect the microphone.");
    }

    private static double CalculatePeak(byte[] buffer, int bytesRecorded)
    {
        var peak = 0;
        for (var index = 0; index < bytesRecorded - 1; index += 2)
        {
            var sample = Math.Abs(BitConverter.ToInt16(buffer, index));
            if (sample > peak)
            {
                peak = sample;
            }
        }

        return peak / 32768.0;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary audio must not block the app if deletion races with antivirus or file handles.
        }
    }
}

public sealed record AudioCaptureSummary(
    TimeSpan Duration,
    int CallbackCount,
    long RecordedBytes,
    double PeakLevel,
    long AudioBytes)
{
    public static AudioCaptureSummary Empty { get; } = new(TimeSpan.Zero, 0, 0, 0, 0);
}
