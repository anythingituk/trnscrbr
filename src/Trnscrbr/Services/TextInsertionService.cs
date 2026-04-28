using System.Windows;
using System.Windows.Forms;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Services;

public sealed class TextInsertionService
{
    private readonly AppStateViewModel _state;

    public TextInsertionService(AppStateViewModel state)
    {
        _state = state;
    }

    public void InsertText(string text)
    {
        var output = _state.Settings.AddTrailingSpace ? text.TrimEnd() + " " : text;
        System.Windows.IDataObject? previousClipboard = null;

        try
        {
            if (System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.Text)
                || System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.UnicodeText)
                || System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.Bitmap)
                || System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.FileDrop))
            {
                previousClipboard = System.Windows.Clipboard.GetDataObject();
            }

            System.Windows.Clipboard.SetText(output);
            SendKeys.SendWait("^v");
            System.Threading.Thread.Sleep(150);
            _state.LastTranscript = text;
            _state.LastTranscriptExpiresAt = DateTimeOffset.Now.AddHours(1);
        }
        finally
        {
            if (previousClipboard is not null)
            {
                try
                {
                    System.Windows.Clipboard.SetDataObject(previousClipboard);
                }
                catch
                {
                    // Clipboard restoration is best effort; complex clipboard formats can fail.
                }
            }
        }
    }
}
