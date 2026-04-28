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
    private readonly DispatcherTimer _timer;
    private DateTimeOffset? _pressStartedAt;
    private DateTimeOffset? _recordingStartedAt;

    public RecordingCoordinator(AppStateViewModel state, TextInsertionService insertion, FloatingButtonWindow floatingButton)
    {
        _state = state;
        _insertion = insertion;
        _floatingButton = floatingButton;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => UpdateElapsedAndInputLevel();
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
        _timer.Start();
    }

    private async void StopAndProcess()
    {
        _timer.Stop();
        _state.InputLevel = 0;
        _state.RecordingState = RecordingState.Processing;
        _state.StatusMessage = "Transcribing";
        _floatingButton.ShowTransient();

        await Task.Delay(1200);

        if (_state.RecordingState != RecordingState.Processing)
        {
            return;
        }

        _state.RecordingState = RecordingState.Error;
        _state.StatusMessage = "Transcription provider not implemented yet";
        _floatingButton.ShowTransient();
    }

    private void ShowProviderRequired()
    {
        _state.RecordingState = RecordingState.Error;
        _state.StatusMessage = "Provider required. Right-click for Settings.";
        _floatingButton.ShowNearTaskbar();
    }

    private void UpdateElapsedAndInputLevel()
    {
        if (_recordingStartedAt is not null)
        {
            _state.Elapsed = DateTimeOffset.Now - _recordingStartedAt.Value;
        }

        var wave = (Math.Sin(DateTimeOffset.Now.ToUnixTimeMilliseconds() / 90.0) + 1) / 2;
        _state.InputLevel = 0.2 + (wave * 0.8);
    }
}
