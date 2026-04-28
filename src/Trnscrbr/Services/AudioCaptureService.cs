using System.IO;
using NAudio.Wave;
using Trnscrbr.Models;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Services;

public sealed class AudioCaptureService : IDisposable
{
    private readonly AppStateViewModel _state;
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _recordingPath;
    private DateTimeOffset? _startedAt;
    private bool _cancelled;

    public AudioCaptureService(AppStateViewModel state)
    {
        _state = state;
    }

    public event EventHandler<double>? InputLevelChanged;

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
        StopAndDelete();

        var directory = Path.Combine(Path.GetTempPath(), "Trnscrbr");
        Directory.CreateDirectory(directory);

        _recordingPath = Path.Combine(directory, $"recording-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}.wav");
        _startedAt = DateTimeOffset.Now;
        _cancelled = false;

        _waveIn = new WaveInEvent
        {
            DeviceNumber = ResolveDeviceNumber(),
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 50
        };

        _writer = new WaveFileWriter(_recordingPath, _waveIn.WaveFormat);
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
    }

    public RecordedAudio? Stop()
    {
        if (_waveIn is null || _writer is null || _recordingPath is null)
        {
            return null;
        }

        var path = _recordingPath;
        var startedAt = _startedAt ?? DateTimeOffset.Now;
        var format = _writer.WaveFormat;
        var microphone = _state.Settings.MicrophoneName;

        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.StopRecording();
        _waveIn.Dispose();
        _waveIn = null;

        _writer.Dispose();
        _writer = null;
        _recordingPath = null;
        _startedAt = null;

        if (_cancelled)
        {
            TryDelete(path);
            return null;
        }

        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 44)
        {
            TryDelete(path);
            return null;
        }

        return new RecordedAudio(
            path,
            DateTimeOffset.Now - startedAt,
            format.SampleRate,
            format.Channels,
            file.Length,
            microphone);
    }

    public void StopAndDelete()
    {
        _cancelled = true;
        var path = _recordingPath;

        try
        {
            _waveIn?.StopRecording();
        }
        catch
        {
            // Cancellation is best effort; disposal below releases the device.
        }

        _waveIn?.Dispose();
        _waveIn = null;

        _writer?.Dispose();
        _writer = null;
        _recordingPath = null;
        _startedAt = null;

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
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        _writer?.Flush();
        InputLevelChanged?.Invoke(this, CalculatePeak(e.Buffer, e.BytesRecorded));
    }

    private int ResolveDeviceNumber()
    {
        if (_state.Settings.MicrophoneName == "Windows default")
        {
            return 0;
        }

        for (var i = 0; i < WaveIn.DeviceCount; i++)
        {
            if (WaveIn.GetCapabilities(i).ProductName == _state.Settings.MicrophoneName)
            {
                return i;
            }
        }

        return 0;
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
