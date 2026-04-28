using System.IO;
using System.Text;

namespace Trnscrbr.Services;

public sealed class DiagnosticLogService
{
    private readonly string _logPath;

    public DiagnosticLogService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Trnscrbr");
        Directory.CreateDirectory(root);
        LogDirectory = root;
        _logPath = Path.Combine(root, "diagnostics.log");
    }

    public string LogDirectory { get; }

    public void Info(string message, IReadOnlyDictionary<string, string>? metadata = null)
    {
        Write("info", message, metadata);
    }

    public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, string>? metadata = null)
    {
        var details = exception is null ? message : $"{message}: {exception.GetType().Name}: {exception.Message}";
        Write("error", details, metadata);
    }

    public string ReadRecent(int maxLines = 80)
    {
        if (!File.Exists(_logPath))
        {
            return "No diagnostics yet.";
        }

        var lines = File.ReadLines(_logPath).TakeLast(maxLines);
        return string.Join(Environment.NewLine, lines);
    }

    private void Write(string level, string message, IReadOnlyDictionary<string, string>? metadata)
    {
        var builder = new StringBuilder();
        builder.Append(DateTimeOffset.Now.ToString("O"));
        builder.Append(" [");
        builder.Append(level);
        builder.Append("] ");
        builder.Append(Redact(message));

        if (metadata is not null)
        {
            foreach (var (key, value) in metadata)
            {
                builder.Append(" | ");
                builder.Append(key);
                builder.Append('=');
                builder.Append(Redact(value));
            }
        }

        File.AppendAllText(_logPath, builder.ToString() + Environment.NewLine);
    }

    private static string Redact(string value)
    {
        return value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("Bearer ", "Bearer [redacted]", StringComparison.OrdinalIgnoreCase);
    }
}
