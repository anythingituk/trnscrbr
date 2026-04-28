namespace Trnscrbr.Models;

public sealed class SettingsExport
{
    public int Version { get; set; } = 1;
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.Now;
    public AppSettings Settings { get; set; } = new();
    public string Note { get; set; } = "API keys, transcripts, raw audio, diagnostics, and usage history are not included.";
}
