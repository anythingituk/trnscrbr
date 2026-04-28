using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Services;

public sealed class TextInsertionService
{
    private const int ClipboardBusyHResult = unchecked((int)0x800401D0);
    private const int ClipboardRetryCount = 8;
    private const int ClipboardRetryDelayMilliseconds = 60;
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int TokenQuery = 0x0008;
    private const int TokenElevation = 20;
    private const int AccessDenied = 5;

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
            if (IsForegroundWindowElevated())
            {
                throw new ElevatedTargetInsertionException(
                    "Target app is running as administrator. Paste into normal apps, or use Paste Last Transcript after switching targets.");
            }

            if (RetryClipboard(() => System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.Text))
                || RetryClipboard(() => System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.UnicodeText))
                || RetryClipboard(() => System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.Bitmap))
                || RetryClipboard(() => System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.FileDrop)))
            {
                previousClipboard = RetryClipboard(System.Windows.Clipboard.GetDataObject);
            }

            RetryClipboard(() => System.Windows.Clipboard.SetDataObject(output, true));
            WaitForHotkeyKeysReleased();
            SendPasteShortcut();
            System.Threading.Thread.Sleep(250);
            _lastInsertedOutput = output;
        }
        catch (Exception ex)
        {
            _diagnosticLog.Error("Text insertion failed", ex, new Dictionary<string, string>
            {
                ["pasteMethod"] = _state.Settings.PasteMethod,
                ["characters"] = output.Length.ToString(),
                ["targetElevated"] = (ex is ElevatedTargetInsertionException).ToString()
            });
            throw;
        }
        finally
        {
            if (previousClipboard is not null)
            {
                try
                {
                    RetryClipboard(() => System.Windows.Clipboard.SetDataObject(previousClipboard));
                }
                catch (Exception ex)
                {
                    _diagnosticLog.Error("Clipboard restore failed", ex, new Dictionary<string, string>
                    {
                        ["pasteMethod"] = _state.Settings.PasteMethod
                    });
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

    private void SendPasteShortcut()
    {
        var shortcut = string.Equals(_state.Settings.PasteMethod, "Shift+Insert", StringComparison.OrdinalIgnoreCase)
            ? "+{INSERT}"
            : "^v";

        SendKeys.SendWait(shortcut);
    }

    private static void WaitForHotkeyKeysReleased()
    {
        const int maxWaitMilliseconds = 3000;
        const int pollMilliseconds = 20;
        var waited = 0;

        while (waited < maxWaitMilliseconds && IsAnyHotkeyKeyDown())
        {
            Thread.Sleep(pollMilliseconds);
            waited += pollMilliseconds;
        }

        if (IsAnyHotkeyKeyDown())
        {
            throw new InvalidOperationException("Hotkey keys are still held. Release Ctrl, Win, and Space before text insertion.");
        }
    }

    private static bool IsAnyHotkeyKeyDown()
    {
        return IsKeyDown(Keys.LControlKey)
            || IsKeyDown(Keys.RControlKey)
            || IsKeyDown(Keys.ControlKey)
            || IsKeyDown(Keys.LWin)
            || IsKeyDown(Keys.RWin)
            || IsKeyDown(Keys.Space);
    }

    private static bool IsKeyDown(Keys key)
    {
        return (GetAsyncKeyState((int)key) & 0x8000) != 0;
    }

    private static bool IsForegroundWindowElevated()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foregroundWindow, out var processId);
        if (processId == 0 || processId == Environment.ProcessId)
        {
            return false;
        }

        var processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return Marshal.GetLastWin32Error() == AccessDenied;
        }

        try
        {
            if (!OpenProcessToken(processHandle, TokenQuery, out var tokenHandle))
            {
                return Marshal.GetLastWin32Error() == AccessDenied;
            }

            try
            {
                var elevation = new TokenElevationInfo();
                var size = Marshal.SizeOf<TokenElevationInfo>();
                return GetTokenInformation(tokenHandle, TokenElevation, ref elevation, size, out _)
                    && elevation.TokenIsElevated != 0;
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    private static void RetryClipboard(Action action)
    {
        RetryClipboard(() =>
        {
            action();
            return true;
        });
    }

    private static T RetryClipboard<T>(Func<T> action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (COMException ex) when (ex.HResult == ClipboardBusyHResult && attempt < ClipboardRetryCount)
            {
                Thread.Sleep(ClipboardRetryDelayMilliseconds);
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int processAccess, bool inheritHandle, int processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, int desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        ref TokenElevationInfo tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevationInfo
    {
        public int TokenIsElevated;
    }
}

public sealed class ElevatedTargetInsertionException : InvalidOperationException
{
    public ElevatedTargetInsertionException(string message)
        : base(message)
    {
    }
}
