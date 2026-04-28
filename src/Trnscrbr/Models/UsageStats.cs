namespace Trnscrbr.Models;

public sealed class UsageStats
{
    public UsageBucket Totals { get; set; } = new();
    public Dictionary<string, UsageBucket> Monthly { get; set; } = new();
    public LastUsageSummary Last { get; set; } = new();
}

public sealed class UsageBucket
{
    public int Recordings { get; set; }
    public double AudioSeconds { get; set; }
    public int Words { get; set; }
    public int Characters { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public double EstimatedCostUsd { get; set; }
}

public sealed class LastUsageSummary
{
    public DateTimeOffset? At { get; set; }
    public double AudioSeconds { get; set; }
    public int Words { get; set; }
    public int Characters { get; set; }
    public double WordsPerMinute { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public double EstimatedCostUsd { get; set; }
    public string Provider { get; set; } = "";
    public string Engine { get; set; } = "";
}
