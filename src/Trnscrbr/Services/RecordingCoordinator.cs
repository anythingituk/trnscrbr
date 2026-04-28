using System.Windows.Threading;
using Trnscrbr.ViewModels;
using Trnscrbr.Views;

namespace Trnscrbr.Services;

public sealed class RecordingCoordinator
{
    private static readonly TimeSpan TapThreshold = TimeSpan.FromMilliseconds(140);

    private readonly AppStateViewModel _state;
    private readonly TextInsertionService _insertion;
    private readonly FloatingButtonWindow _floatingButton;
    private readonly AudioCaptureService _audioCapture;
    private readonly CredentialStore _credentialStore;
    private readonly OpenAiProviderService _openAiProvider;
    private readonly DiagnosticLogService _diagnosticLog;
    private readonly UsageStatsService _usageStats;
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _processingCancellation;
    private DateTimeOffset? _pressStartedAt;
    private DateTimeOffset? _recordingStartedAt;

    public RecordingCoordinator(
        AppStateViewModel state,
        TextInsertionService insertion,
        FloatingButtonWindow floatingButton,
        AudioCaptureService audioCapture,
        CredentialStore credentialStore,
        OpenAiProviderService openAiProvider,
        DiagnosticLogService diagnosticLog,
        UsageStatsService usageStats)
    {
        _state = state;
        _insertion = insertion;
        _floatingButton = floatingButton;
        _audioCapture = audioCapture;
        _credentialStore = credentialStore;
        _openAiProvider = openAiProvider;
        _diagnosticLog = diagnosticLog;
        _usageStats = usageStats;
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

        if (_state.RecordingState == RecordingState.Recording)
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
        _processingCancellation?.Cancel();
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
        try
        {
            _audioCapture.Start();
            _state.RecordingState = RecordingState.Recording;
            _state.StatusMessage = "Recording";
            _floatingButton.ShowNearTaskbar();
            _timer.Start();
        }
        catch (Exception ex)
        {
            _diagnosticLog.Error("Microphone start failed", ex, new Dictionary<string, string>
            {
                ["microphone"] = _state.Settings.MicrophoneName
            });
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
            _diagnosticLog.Error("No microphone input recorded", metadata: new Dictionary<string, string>
            {
                ["microphone"] = _state.Settings.MicrophoneName
            });
            _state.RecordingState = RecordingState.Error;
            _state.StatusMessage = "No microphone input recorded";
            _floatingButton.ShowTransient();
            return;
        }

        var apiKey = _credentialStore.ReadOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _diagnosticLog.Error("OpenAI API key missing");
            _audioCapture.DeleteRecording(recordedAudio);
            _state.RecordingState = RecordingState.Error;
            _state.StatusMessage = "OpenAI API key required. Right-click for Settings.";
            _floatingButton.ShowTransient();
            return;
        }

        _processingCancellation?.Dispose();
        _processingCancellation = new CancellationTokenSource();
        _state.RecordingState = RecordingState.Processing;
        _state.StatusMessage = "Transcribing";
        _floatingButton.ShowNearTaskbar();
        _diagnosticLog.Info("Processing recording", new Dictionary<string, string>
        {
            ["duration"] = recordedAudio.Duration.TotalSeconds.ToString("0.00"),
            ["sampleRate"] = recordedAudio.SampleRate.ToString(),
            ["channels"] = recordedAudio.Channels.ToString(),
            ["fileSize"] = recordedAudio.FileSizeBytes.ToString(),
            ["microphone"] = recordedAudio.MicrophoneName
        });

        try
        {
            var result = await _openAiProvider.TranscribeAndCleanAsync(
                apiKey,
                recordedAudio,
                _state,
                _processingCancellation.Token);

            if (_state.RecordingState != RecordingState.Processing)
            {
                return;
            }

            _insertion.InsertText(result.CleanedTranscript);
            var usage = _usageStats.RecordDictation(
                result.CleanedTranscript,
                recordedAudio,
                _state.Settings.ProviderName,
                _state.Settings.ActiveEngine,
                result.InputTokens,
                result.OutputTokens,
                result.EstimatedCostUsd);
            _state.RecordingState = RecordingState.Idle;
            var currentMonth = _usageStats.GetCurrentMonth();
            var threshold = (double)_state.Settings.MonthlyCostWarning;
            _state.StatusMessage = threshold > 0 && currentMonth.EstimatedCostUsd >= threshold
                ? $"Inserted transcript. Monthly estimate ${currentMonth.EstimatedCostUsd:0.00}."
                : $"Inserted transcript ({usage.Last.WordsPerMinute:0} wpm)";
            _floatingButton.ShowTransient();
        }
        catch (OperationCanceledException)
        {
            _state.RecordingState = RecordingState.Idle;
            _state.StatusMessage = "Cancelled";
            _floatingButton.ShowTransient();
        }
        catch (Exception ex)
        {
            _diagnosticLog.Error("Transcription or insertion failed", ex, new Dictionary<string, string>
            {
                ["provider"] = _state.Settings.ProviderName,
                ["engine"] = _state.Settings.ActiveEngine
            });
            _state.RecordingState = RecordingState.Error;
            _state.StatusMessage = ex.Message;
            _floatingButton.ShowTransient();
        }
        finally
        {
            _audioCapture.DeleteRecording(recordedAudio);
        }
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
