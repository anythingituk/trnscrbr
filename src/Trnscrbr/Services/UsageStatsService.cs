using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Trnscrbr.Models;

namespace Trnscrbr.Services;

public sealed partial class UsageStatsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _statsPath;
    private readonly object _syncRoot = new();

    public UsageStatsService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Trnscrbr");
        Directory.CreateDirectory(root);
        _statsPath = Path.Combine(root, "usage.json");
    }

    public UsageStats Load()
    {
        lock (_syncRoot)
        {
            if (!File.Exists(_statsPath))
            {
                return new UsageStats();
            }

            try
            {
                var json = File.ReadAllText(_statsPath);
                return JsonSerializer.Deserialize<UsageStats>(json) ?? new UsageStats();
            }
            catch
            {
                return new UsageStats();
            }
        }
    }

    public UsageStats RecordDictation(string cleanedTranscript, RecordedAudio audio, string provider, string engine)
    {
        lock (_syncRoot)
        {
            var stats = Load();
            var words = CountWords(cleanedTranscript);
            var characters = cleanedTranscript.Length;
            var monthKey = DateTimeOffset.Now.ToString("yyyy-MM");

            stats.Totals.Recordings++;
            stats.Totals.AudioSeconds += audio.Duration.TotalSeconds;
            stats.Totals.Words += words;
            stats.Totals.Characters += characters;

            if (!stats.Monthly.TryGetValue(monthKey, out var month))
            {
                month = new UsageBucket();
                stats.Monthly[monthKey] = month;
            }

            month.Recordings++;
            month.AudioSeconds += audio.Duration.TotalSeconds;
            month.Words += words;
            month.Characters += characters;

            stats.Last = new LastUsageSummary
            {
                At = DateTimeOffset.Now,
                AudioSeconds = audio.Duration.TotalSeconds,
                Words = words,
                Characters = characters,
                WordsPerMinute = audio.Duration.TotalSeconds <= 0 ? 0 : words / audio.Duration.TotalSeconds * 60,
                Provider = provider,
                Engine = engine
            };

            Save(stats);
            return stats;
        }
    }

    public string FormatSummary()
    {
        var stats = Load();
        var currentMonthKey = DateTimeOffset.Now.ToString("yyyy-MM");
        stats.Monthly.TryGetValue(currentMonthKey, out var currentMonth);
        currentMonth ??= new UsageBucket();

        var last = stats.Last.At is null
            ? "No dictations yet."
            : $"Last: {stats.Last.Words} words, {stats.Last.AudioSeconds:0.0}s, {stats.Last.WordsPerMinute:0} wpm, {stats.Last.Provider}/{stats.Last.Engine}";

        return $"""
            {last}

            This month ({currentMonthKey})
            Recordings: {currentMonth.Recordings}
            Audio: {FormatDuration(currentMonth.AudioSeconds)}
            Words: {currentMonth.Words}
            Characters: {currentMonth.Characters}

            Totals
            Recordings: {stats.Totals.Recordings}
            Audio: {FormatDuration(stats.Totals.AudioSeconds)}
            Words: {stats.Totals.Words}
            Characters: {stats.Totals.Characters}
            """;
    }

    private void Save(UsageStats stats)
    {
        var json = JsonSerializer.Serialize(stats, JsonOptions);
        File.WriteAllText(_statsPath, json);
    }

    private static int CountWords(string text)
    {
        return WordRegex().Matches(text).Count;
    }

    private static string FormatDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}h {time.Minutes}m {time.Seconds}s"
            : $"{time.Minutes}m {time.Seconds}s";
    }

    [GeneratedRegex(@"\b[\p{L}\p{N}][\p{L}\p{N}'-]*\b")]
    private static partial Regex WordRegex();
}
