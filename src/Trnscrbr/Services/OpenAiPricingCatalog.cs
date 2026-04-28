namespace Trnscrbr.Services;

public static class OpenAiPricingCatalog
{
    // OpenAI pricing checked 2026-04-28:
    // gpt-4o-mini-transcribe estimated at $0.003/minute.
    // gpt-5.4-mini standard pricing: $0.75 / 1M input tokens, $4.50 / 1M output tokens.
    public const double Gpt4OMiniTranscribeUsdPerMinute = 0.003;
    public const double Gpt54MiniInputUsdPerMillionTokens = 0.75;
    public const double Gpt54MiniOutputUsdPerMillionTokens = 4.50;

    public static double EstimateTranscriptionCost(TimeSpan duration)
    {
        return Math.Max(0, duration.TotalMinutes) * Gpt4OMiniTranscribeUsdPerMinute;
    }

    public static double EstimateCleanupCost(int inputTokens, int outputTokens)
    {
        return (inputTokens / 1_000_000d * Gpt54MiniInputUsdPerMillionTokens)
            + (outputTokens / 1_000_000d * Gpt54MiniOutputUsdPerMillionTokens);
    }
}
