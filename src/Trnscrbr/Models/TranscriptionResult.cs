namespace Trnscrbr.Models;

public sealed record TranscriptionResult(
    string CleanedTranscript,
    int InputTokens,
    int OutputTokens,
    double EstimatedCostUsd);
