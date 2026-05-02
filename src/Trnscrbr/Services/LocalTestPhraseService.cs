using Trnscrbr.Models;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Services;

public sealed class LocalTestPhraseService
{
    private readonly AudioCaptureService _audioCapture;
    private readonly LocalProviderService _localProvider;

    public LocalTestPhraseService(
        AudioCaptureService audioCapture,
        LocalProviderService localProvider)
    {
        _audioCapture = audioCapture;
        _localProvider = localProvider;
    }

    public async Task<LocalTestPhraseResult> RunAsync(
        AppStateViewModel state,
        Action<string> reportStatus,
        CancellationToken cancellationToken = default)
    {
        RecordedAudio? recordedAudio = null;

        try
        {
            state.RecordingState = RecordingState.Recording;
            state.StatusMessage = "Recording local test phrase";
            reportStatus("Recording test phrase. Speak now for 5 seconds...");
            _audioCapture.Start();

            for (var remaining = 5; remaining > 0; remaining--)
            {
                reportStatus($"Recording test phrase. Speak now: {remaining}s");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            recordedAudio = _audioCapture.Stop();
            state.RecordingState = RecordingState.Processing;
            state.StatusMessage = "Transcribing test phrase";

            if (recordedAudio is null)
            {
                var summary = _audioCapture.LastCaptureSummary;
                return new LocalTestPhraseResult(
                    false,
                    $"No microphone input captured from {state.Settings.MicrophoneName}. Peak level: {summary.PeakLevel:0.000}.",
                    string.Empty,
                    true);
            }

            reportStatus("Transcribing local test phrase...");
            var transcript = await _localProvider.TranscribeOnlyAsync(recordedAudio, state, cancellationToken);
            return string.IsNullOrWhiteSpace(transcript)
                ? new LocalTestPhraseResult(
                    true,
                    "Local test completed, but Whisper returned an empty transcript. Try speaking louder or choosing a larger model.",
                    string.Empty,
                    false)
                : new LocalTestPhraseResult(
                    true,
                    "Local test completed. Transcript shown below; nothing was pasted.",
                    transcript,
                    false);
        }
        finally
        {
            if (state.RecordingState is RecordingState.Recording)
            {
                _audioCapture.StopAndDelete();
            }

            _audioCapture.DeleteRecording(recordedAudio);
            state.InputLevel = 0;
            state.Elapsed = TimeSpan.Zero;
        }
    }
}

public sealed record LocalTestPhraseResult(
    bool IsSuccess,
    string Message,
    string Transcript,
    bool NoInputCaptured);
