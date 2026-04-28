using System.Windows;
using System.Windows.Forms;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Services;

public sealed class TextInsertionService
{
    private readonly AppStateViewModel _state;
    private readonly DiagnosticLogService _diagnosticLog;
    private string? _lastInsertedOutput;

    public TextInsertionService(AppStateViewModel state, DiagnosticLogService diagnosticLog)
    {
        _state = state;
        _diagnosticLog = diagnosticLog;
    }

    public void InsertText(string text)
    {
        var output = _state.Settings.AddTrailingSpace ? text.TrimEnd() + " " : text;
        System.Windows.IDataObject? previousClipboard = null;
        _state.LastTranscript = text;
        _state.LastTranscriptExpiresAt = DateTimeOffset.Now.AddHours(1);

        try
        {
            if (System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.Text)
                || System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.UnicodeText)
                || System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.Bitmap)
                || System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.FileDrop))
            {
                previousClipboard = System.Windows.Clipboard.GetDataObject();
            }

            System.Windows.Clipboard.SetDataObject(output, true);
            SendKeys.SendWait("^v");
            System.Threading.Thread.Sleep(250);
            _lastInsertedOutput = output;
        }
        catch (Exception ex)
        {
            _diagnosticLog.Error("Text insertion failed", ex);
            throw;
        }
        finally
        {
            if (previousClipboard is not null)
            {
                try
                {
                    System.Windows.Clipboard.SetDataObject(previousClipboard);
                }
                catch (Exception ex)
                {
                    _diagnosticLog.Error("Clipboard restore failed", ex);
                    // Clipboard restoration is best effort; complex clipboard formats can fail.
                }
            }
        }
    }

    public bool DeleteLastInsertedText()
    {
        if (string.IsNullOrEmpty(_lastInsertedOutput))
        {
            return false;
        }

        try
        {
            SendBackspaces(_lastInsertedOutput.Length);
            _lastInsertedOutput = null;
            _state.LastTranscript = null;
            _state.LastTranscriptExpiresAt = null;
            return true;
        }
        catch (Exception ex)
        {
            _diagnosticLog.Error("Last insertion removal failed", ex);
            throw;
        }
    }

    private static void SendBackspaces(int count)
    {
        const int chunkSize = 50;
        var remaining = count;

        while (remaining > 0)
        {
            var chunk = Math.Min(chunkSize, remaining);
            SendKeys.SendWait($"{{BACKSPACE {chunk}}}");
            remaining -= chunk;
        }
    }
}
