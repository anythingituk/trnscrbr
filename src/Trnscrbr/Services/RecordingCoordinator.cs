using System.Windows.Threading;
using Trnscrbr.ViewModels;
using Trnscrbr.Views;

namespace Trnscrbr.Services;

public sealed class RecordingCoordinator
{
    private static readonly TimeSpan TapThreshold = TimeSpan.FromMilliseconds(250);

    private readonly AppStateViewModel _state;
    private readonly TextInsertionService _insertion;
    private readonly FloatingButtonWindow _floatingButton;
    private readonly AudioCaptureService _audioCapture;
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher;
    private DateTimeOffset? _pressStartedAt;
    private DateTimeOffset? _recordingStartedAt;

    public RecordingCoordinator(
        AppStateViewModel state,
        TextInsertionService insertion,
        FloatingButtonWindow floatingButton,
        AudioCaptureService audioCapture)
    {
        _state = state;
        _insertion = insertion;
        _floatingButton = floatingButton;
        _audioCapture = audioCapture;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _audioCapture.InputLevelChanged += (_, level) =>
        {
            _dispatcher.BeginInvoke(() => _state.InputLevel = level);
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => UpdateElapsed();
    }

    public void HandlePushToTalkPressed()
    {
        _pressStartedAt = DateTimeOffset.Now;

        if (!_state.IsProviderConfigured)
        {
            ShowProviderRequired();
            return;
        }

        if (_state.RecordingState == RecordingState.Idle)
        {
            StartRecording();
        }
    }

    public void HandlePushToTalkReleased()
    {
        if (_pressStartedAt is null)
        {
            return;
        }

        var duration = DateTimeOffset.Now - _pressStartedAt.Value;
        _pressStartedAt = null;

        if (!_state.IsProviderConfigured)
        {
            return;
        }

        if (duration >= TapThreshold && _state.RecordingState == RecordingState.Recording)
        {
            StopAndProcess();
        }
    }

    public void ToggleRecording()
    {
        if (!_state.IsProviderConfigured)
        {
            ShowProviderRequired();
            return;
        }

        if (_state.RecordingState == RecordingState.Recording)
        {
            StopAndProcess();
        }
        else if (_state.RecordingState == RecordingState.Idle || _state.RecordingState == RecordingState.Error)
        {
            StartRecording();
        }
    }

    public void Cancel()
    {
        if (_state.RecordingState is not (RecordingState.Recording or RecordingState.Processing))
        {
            return;
        }

        _timer.Stop();
        _audioCapture.StopAndDelete();
        _state.Elapsed = TimeSpan.Zero;
        _state.InputLevel = 0;
        _state.RecordingState = RecordingState.Idle;
        _state.StatusMessage = "Cancelled";
        _floatingButton.ShowTransient();
    }

    public void PasteLastTranscript()
    {
        if (_state.LastTranscript is null || _state.LastTranscriptExpiresAt < DateTimeOffset.Now)
        {
            _state.StatusMessage = "No recent transcript";
            _floatingButton.ShowTransient();
            return;
        }

        _insertion.InsertText(_state.LastTranscript);
        _state.StatusMessage = "Pasted last transcript";
        _floatingButton.ShowTransient();
    }

    private void StartRecording()
    {
        _recordingStartedAt = DateTimeOffset.Now;
        _state.Elapsed = TimeSpan.Zero;
        _state.RecordingState = RecordingState.Recording;
        _state.StatusMessage = "Recording";
        _floatingButton.ShowNearTaskbar();
        try
        {
            _audioCapture.Start();
            _timer.Start();
        }
        catch (Exception ex)
        {
            _state.RecordingState = RecordingState.Error;
            _state.StatusMessage = $"Microphone failed: {ex.Message}";
            _floatingButton.ShowTransient();
        }
    }

    private async void StopAndProcess()
    {
        _timer.Stop();
        _state.InputLevel = 0;
        var recordedAudio = _audioCapture.Stop();
        if (recordedAudio is null)
        {
            _state.RecordingState = RecordingState.Error;
            _state.StatusMessage = "No microphone input recorded";
            _floatingButton.ShowTransient();
            return;
        }

        _state.RecordingState = RecordingState.Processing;
        _state.StatusMessage = "Transcribing";
        _floatingButton.ShowTransient();

        await Task.Delay(1200);

        if (_state.RecordingState != RecordingState.Processing)
        {
            _audioCapture.DeleteRecording(recordedAudio);
            return;
        }

        _state.RecordingState = RecordingState.Error;
        _state.StatusMessage = $"Transcription provider not implemented yet ({recordedAudio.Duration.TotalSeconds:0.0}s recorded)";
        _floatingButton.ShowTransient();
        _audioCapture.DeleteRecording(recordedAudio);
    }

    private void ShowProviderRequired()
    {
        _state.RecordingState = RecordingState.Error;
        _state.StatusMessage = "Provider required. Right-click for Settings.";
        _floatingButton.ShowNearTaskbar();
    }

    private void UpdateElapsed()
    {
        if (_recordingStartedAt is not null)
        {
            _state.Elapsed = DateTimeOffset.Now - _recordingStartedAt.Value;
        }
    }
}
