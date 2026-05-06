using System.Diagnostics;
using System.Windows.Threading;
using Trnscrbr.ViewModels;
using Trnscrbr.Views;

namespace Trnscrbr.Services;

public sealed class RecordingCoordinator
{
    private static readonly TimeSpan PendingTranscriptLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DeferredGuidanceDuration = TimeSpan.FromSeconds(5);
    private readonly AppStateViewModel _state;
    private readonly TextInsertionService _insertion;
    private readonly FloatingButtonWindow _floatingButton;
    private readonly AudioCaptureService _audioCapture;
    private readonly CredentialStore _credentialStore;
    private readonly OpenAiProviderService _openAiProvider;
    private readonly LocalProviderService _localProvider;
    private readonly DiagnosticLogService _diagnosticLog;
    private readonly UsageStatsService _usageStats;
    private readonly Action _showMicrophoneSettings;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _pendingPasteOfferTimer;
    private readonly DispatcherTimer _deferredGuidanceTimer;
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _processingCancellation;
    private DateTimeOffset? _recordingStartedAt;
    private InsertionTargetSnapshot? _recordingTarget;
    private InsertionTargetSnapshot? _pendingPasteTarget;
    private bool _pushToTalkActive;
    private bool _pendingPasteOfferShown;

    public RecordingCoordinator(
        AppStateViewModel state,
        TextInsertionService insertion,
        FloatingButtonWindow floatingButton,
        AudioCaptureService audioCapture,
        CredentialStore credentialStore,
        OpenAiProviderService openAiProvider,
        LocalProviderService localProvider,
        DiagnosticLogService diagnosticLog,
        UsageStatsService usageStats,
        Action showMicrophoneSettings)
    {
        _state = state;
        _insertion = insertion;
        _floatingButton = floatingButton;
        _audioCapture = audioCapture;
        _credentialStore = credentialStore;
        _openAiProvider = openAiProvider;
        _localProvider = localProvider;
        _diagnosticLog = diagnosticLog;
        _usageStats = usageStats;
        _showMicrophoneSettings = showMicrophoneSettings;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _audioCapture.InputLevelChanged += (_, level) =>
        {
            _dispatcher.BeginInvoke(() => _state.InputLevel = level);
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => UpdateElapsed();
        _pendingPasteOfferTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _pendingPasteOfferTimer.Tick += (_, _) => OfferPendingPasteIfOriginalTargetFocused();
        _deferredGuidanceTimer = new DispatcherTimer { Interval = DeferredGuidanceDuration };
        _deferredGuidanceTimer.Tick += (_, _) =>
        {
            _deferredGuidanceTimer.Stop();
            if (_state.RecordingState == RecordingState.Idle
                && IsDeferredPasteGuidance(_state.StatusMessage))
            {
                _state.StatusMessage = "Ready";
            }
        };
    }

    public void HandlePushToTalkPressed()
    {
        if (!_state.IsProviderConfigured)
        {
            _diagnosticLog.Info("Push-to-talk ignored", new Dictionary<string, string>
            {
                ["reason"] = "provider-not-configured",
                ["state"] = _state.RecordingState.ToString()
            });
            ShowProviderRequired();
            return;
        }

        if (_pushToTalkActive)
        {
            _diagnosticLog.Info("Push-to-talk ignored", new Dictionary<string, string>
            {
                ["reason"] = "already-active",
                ["state"] = _state.RecordingState.ToString()
            });
            return;
        }

        _pushToTalkActive = true;

        if (_state.RecordingState == RecordingState.Idle)
        {
            _diagnosticLog.Info("Push-to-talk start accepted");
            StartRecording();
        }
        else
        {
            _diagnosticLog.Info("Push-to-talk ignored", new Dictionary<string, string>
            {
                ["reason"] = "not-idle",
                ["state"] = _state.RecordingState.ToString()
            });
        }
    }

    public void HandlePushToTalkReleased()
    {
        if (!_state.IsProviderConfigured)
        {
            _pushToTalkActive = false;
            return;
        }

        if (_state.RecordingState == RecordingState.Recording)
        {
            _diagnosticLog.Info("Push-to-talk release accepted");
            StopAndProcess();
        }
        else
        {
            _diagnosticLog.Info("Push-to-talk release ignored", new Dictionary<string, string>
            {
                ["state"] = _state.RecordingState.ToString()
            });
        }

        _pushToTalkActive = false;
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
            if (HasRecoverableTranscript())
            {
                ForgetLastTranscript();
            }

            return;
        }

        _timer.Stop();
        _processingCancellation?.Cancel();
        _audioCapture.StopAndDelete();
        _state.Elapsed = TimeSpan.Zero;
        _state.InputLevel = 0;
        _state.RecordingState = RecordingState.Idle;
        _state.StatusMessage = "Cancelled";
        ClearPendingPasteOffer();
        _floatingButton.ShowTransient();
    }

    public void ForgetLastTranscript()
    {
        ClearLastTranscript();
        ClearPendingPasteOffer();
        _state.StatusMessage = "Ready";
        _floatingButton.ShowTransient();
    }

    public void PasteLastTranscript()
    {
        if (_state.LastTranscript is null || _state.LastTranscriptExpiresAt < DateTimeOffset.Now)
        {
            _state.LastTranscript = null;
            _state.LastTranscriptExpiresAt = null;
            ClearPendingPasteOffer();
            _state.StatusMessage = "No recent transcript";
            _floatingButton.ShowTransient();
            return;
        }

        try
        {
            _insertion.InsertText(_state.LastTranscript);
            ClearPendingPasteOffer();
            _state.StatusMessage = "Pasted last transcript";
            _floatingButton.ShowTransient();
        }
        catch (DeferredTextInsertionException ex)
        {
            ShowDeferredPasteGuidance(ex.Message);
            _floatingButton.ShowTransient();
        }
        catch (Exception ex)
        {
            _diagnosticLog.Error("Paste last transcript failed", ex);
            _state.RecordingState = RecordingState.Error;
            _state.StatusMessage = "Paste failed. Try again.";
            _floatingButton.ShowTransient();
        }
    }

    private void StartRecording()
    {
        ClearLastTranscript();
        _recordingStartedAt = DateTimeOffset.Now;
        _recordingTarget = _insertion.CaptureFocusedInsertionTarget();
        ClearPendingPasteOffer();
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
        _recordingStartedAt = null;
        var recordedAudio = _audioCapture.Stop();
        if (recordedAudio is null)
        {
            var summary = _audioCapture.LastCaptureSummary;
            _diagnosticLog.Error("No microphone input recorded", metadata: new Dictionary<string, string>
            {
                ["microphone"] = _state.Settings.MicrophoneName,
                ["duration"] = summary.Duration.TotalSeconds.ToString("0.00"),
                ["callbacks"] = summary.CallbackCount.ToString(),
                ["recordedBytes"] = summary.RecordedBytes.ToString(),
                ["audioBytes"] = summary.AudioBytes.ToString(),
                ["peakLevel"] = summary.PeakLevel.ToString("0.000")
            });
            _state.RecordingState = RecordingState.Error;
            _state.StatusMessage = $"No input from {_state.Settings.MicrophoneName}. Choose another microphone.";
            _floatingButton.ShowTransient();
            _ = _dispatcher.BeginInvoke(_showMicrophoneSettings);
            return;
        }

        var useLocalMode = string.Equals(_state.Settings.ProviderMode, "Local mode", StringComparison.OrdinalIgnoreCase);
        var apiKey = useLocalMode ? null : _credentialStore.ReadOpenAiApiKey();
        if (!useLocalMode && string.IsNullOrWhiteSpace(apiKey))
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
        var processingStopwatch = Stopwatch.StartNew();
        var slowLocalNoticeShown = false;
        var slowLocalTimer = useLocalMode
            ? CreateSlowLocalTranscriptionTimer(processingStopwatch, () => slowLocalNoticeShown = true)
            : null;
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
            var result = useLocalMode
                ? await _localProvider.TranscribeAndCleanAsync(
                    recordedAudio,
                    _state,
                    _processingCancellation.Token)
                : await _openAiProvider.TranscribeAndCleanAsync(
                    apiKey!,
                    recordedAudio,
                    _state,
                    _processingCancellation.Token);

            if (_state.RecordingState != RecordingState.Processing)
            {
                return;
            }

            var cleanedTranscript = result.CleanedTranscript.Trim();
            var isDeleteAction = string.Equals(
                cleanedTranscript,
                OpenAiProviderService.DeleteLastInsertionAction,
                StringComparison.Ordinal);
            var isDiscardAction = string.Equals(
                cleanedTranscript,
                OpenAiProviderService.DiscardCurrentTranscriptAction,
                StringComparison.Ordinal);
            var isFillerOnlyTranscript = IsFillerOnlyTranscript(cleanedTranscript);
            var isVoiceAction = isDeleteAction || isDiscardAction || isFillerOnlyTranscript;
            var insertionDeferred = false;

            if (isDeleteAction)
            {
                var deleted = _insertion.DeleteLastInsertedText();
                ClearPendingPasteOffer();
                _state.StatusMessage = deleted
                    ? "Removed last insertion"
                    : "No Trnscrbr insertion to remove";
            }
            else if (isDiscardAction)
            {
                _state.StatusMessage = "Discarded dictation";
            }
            else if (isFillerOnlyTranscript)
            {
                _state.StatusMessage = "Discarded unclear dictation";
            }
            else
            {
                try
                {
                    _insertion.InsertText(result.CleanedTranscript, _recordingTarget);
                    ClearPendingPasteOffer();
                }
                catch (DeferredTextInsertionException ex)
                {
                    insertionDeferred = true;
                    StartPendingPasteOffer(_recordingTarget);
                    _diagnosticLog.Info("Automatic transcript insertion deferred", new Dictionary<string, string>
                    {
                        ["reason"] = ex.Message
                    });
                    _state.StatusMessage = "Ready";
                }
            }

            var usage = _usageStats.RecordDictation(
                isVoiceAction ? string.Empty : result.CleanedTranscript,
                recordedAudio,
                _state.Settings.ProviderName,
                _state.Settings.ActiveEngine,
                result.InputTokens,
                result.OutputTokens,
                result.EstimatedCostUsd);
            _state.RecordingState = RecordingState.Idle;
            var currentMonth = _usageStats.GetCurrentMonth();
            var threshold = (double)_state.Settings.MonthlyCostWarning;
            if (!isVoiceAction && !insertionDeferred)
            {
                var insertedMessage = threshold > 0 && currentMonth.EstimatedCostUsd >= threshold
                    ? $"Inserted transcript. Monthly estimate ${currentMonth.EstimatedCostUsd:0.00}."
                    : "Inserted transcript";

                _state.StatusMessage = useLocalMode && ShouldShowLocalSpeedTip(processingStopwatch.Elapsed, slowLocalNoticeShown)
                    ? $"{insertedMessage} Tip: Small is faster for local AI."
                    : insertedMessage;
            }

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
            slowLocalTimer?.Stop();
            _audioCapture.DeleteRecording(recordedAudio);
            _recordingTarget = null;
        }
    }

    private void StartPendingPasteOffer(InsertionTargetSnapshot? target)
    {
        _pendingPasteTarget = target;
        _pendingPasteOfferShown = false;
        _state.LastTranscriptExpiresAt = DateTimeOffset.Now.Add(PendingTranscriptLifetime);

        if (target is not null)
        {
            _pendingPasteOfferTimer.Start();
        }
    }

    private void ClearPendingPasteOffer()
    {
        _pendingPasteOfferTimer.Stop();
        _deferredGuidanceTimer.Stop();
        _pendingPasteTarget = null;
        _pendingPasteOfferShown = false;
    }

    private void ShowDeferredPasteGuidance(string message)
    {
        _deferredGuidanceTimer.Stop();
        _state.StatusMessage = message;
        _deferredGuidanceTimer.Start();
    }

    private void OfferPendingPasteIfOriginalTargetFocused()
    {
        if (_pendingPasteTarget is null
            || string.IsNullOrWhiteSpace(_state.LastTranscript)
            || _state.LastTranscriptExpiresAt is null
            || _state.LastTranscriptExpiresAt <= DateTimeOffset.Now)
        {
            ClearPendingPasteOffer();
            return;
        }

        if (_pendingPasteOfferShown || !_insertion.IsFocusedInsertionTarget(_pendingPasteTarget))
        {
            return;
        }

        _pendingPasteOfferShown = true;
        _state.StatusMessage = "Ready to paste transcript";
        _floatingButton.ShowTransient();
    }

    private bool HasRecoverableTranscript()
    {
        return !string.IsNullOrWhiteSpace(_state.LastTranscript)
            && _state.LastTranscriptExpiresAt > DateTimeOffset.Now;
    }

    private void ClearLastTranscript()
    {
        _state.LastTranscript = null;
        _state.LastTranscriptExpiresAt = null;
    }

    private static bool IsDeferredPasteGuidance(string message)
    {
        return message.StartsWith("Paste skipped;", StringComparison.Ordinal)
            || string.Equals(message, "Ready to paste transcript", StringComparison.Ordinal);
    }

    private DispatcherTimer CreateSlowLocalTranscriptionTimer(Stopwatch stopwatch, Action markNoticeShown)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += (_, _) =>
        {
            if (_state.RecordingState != RecordingState.Processing)
            {
                timer.Stop();
                return;
            }

            if (stopwatch.Elapsed < TimeSpan.FromSeconds(20))
            {
                return;
            }

            markNoticeShown();
            _state.StatusMessage = "Still transcribing with local AI. Larger models can take longer on this PC.";
        };
        timer.Start();
        return timer;
    }

    private bool ShouldShowLocalSpeedTip(TimeSpan elapsed, bool slowLocalNoticeShown)
    {
        if (!slowLocalNoticeShown && elapsed < TimeSpan.FromSeconds(30))
        {
            return false;
        }

        return !string.Equals(_state.Settings.LocalWhisperModelPresetId, "small", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowProviderRequired()
    {
        _state.RecordingState = RecordingState.Error;
        _state.StatusMessage = string.Equals(_state.Settings.ProviderMode, "Local mode", StringComparison.OrdinalIgnoreCase)
            ? "Local AI setup required. Right-click for Settings."
            : "Provider required. Right-click for Settings.";
        _floatingButton.ShowNearTaskbar();
    }

    private void UpdateElapsed()
    {
        if (_state.RecordingState == RecordingState.Recording && _recordingStartedAt is not null)
        {
            _state.Elapsed = DateTimeOffset.Now - _recordingStartedAt.Value;
        }
    }

    private static bool IsFillerOnlyTranscript(string transcript)
    {
        var words = transcript
            .Split([' ', '\t', '\r', '\n', ',', '.', '!', '?', ';', ':', '-', '"', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim().ToLowerInvariant())
            .ToArray();

        if (words.Length == 0 || words.Length > 6)
        {
            return false;
        }

        var fillerWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "okay",
            "ok",
            "so",
            "well",
            "right",
            "um",
            "uh",
            "er",
            "erm",
            "like",
            "yeah",
            "yes"
        };

        return words.All(fillerWords.Contains);
    }
}
