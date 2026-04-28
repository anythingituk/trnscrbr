using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Trnscrbr.Services;

public sealed class KeyboardHookService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly LowLevelKeyboardProc _proc;
    private SynchronizationContext? _context;
    private IntPtr _hookId;
    private bool _pushToTalkDown;
    private bool _pasteLastTranscriptDown;

    public KeyboardHookService()
    {
        _proc = HookCallback;
    }

    public event EventHandler? PushToTalkPressed;
    public event EventHandler? PushToTalkReleased;
    public event EventHandler? CancelPressed;
    public event EventHandler? PasteLastTranscriptPressed;

    public void Start()
    {
        _context = SynchronizationContext.Current;
        _hookId = SetHook(_proc);
        if (_hookId == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Global keyboard hook registration failed.");
        }
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        var key = (Keys)Marshal.ReadInt32(lParam);
        var isDown = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
        var isUp = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;
        var ctrl = IsKeyDown(Keys.LControlKey) || IsKeyDown(Keys.RControlKey) || IsKeyDown(Keys.ControlKey);
        var win = IsKeyDown(Keys.LWin) || IsKeyDown(Keys.RWin);

        if (ctrl && win && key == Keys.Space)
        {
            if (isDown && !_pushToTalkDown)
            {
                _pushToTalkDown = true;
                PostEvent(PushToTalkPressed);
            }
            else if (isUp)
            {
                _pushToTalkDown = false;
                PostEvent(PushToTalkReleased);
            }

            return (IntPtr)1;
        }

        if (_pushToTalkDown && isUp && IsPushToTalkChordKey(key))
        {
            _pushToTalkDown = false;
            PostEvent(PushToTalkReleased);
            return (IntPtr)1;
        }

        if (ctrl && win && key == Keys.V)
        {
            if (isDown && !_pasteLastTranscriptDown)
            {
                _pasteLastTranscriptDown = true;
                PostEvent(PasteLastTranscriptPressed);
            }
            else if (isUp)
            {
                _pasteLastTranscriptDown = false;
            }

            return (IntPtr)1;
        }

        if (_pasteLastTranscriptDown && isUp && IsPasteLastTranscriptChordKey(key))
        {
            _pasteLastTranscriptDown = false;
            return (IntPtr)1;
        }

        if (key == Keys.Escape && isDown)
        {
            PostEvent(CancelPressed);
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsKeyDown(Keys key)
    {
        return (GetAsyncKeyState((int)key) & 0x8000) != 0;
    }

    private static bool IsPushToTalkChordKey(Keys key)
    {
        return key is Keys.Space
            or Keys.LWin
            or Keys.RWin
            or Keys.LControlKey
            or Keys.RControlKey
            or Keys.ControlKey;
    }

    private static bool IsPasteLastTranscriptChordKey(Keys key)
    {
        return key is Keys.V
            or Keys.LWin
            or Keys.RWin
            or Keys.LControlKey
            or Keys.RControlKey
            or Keys.ControlKey;
    }

    private void PostEvent(EventHandler? handler)
    {
        if (handler is null)
        {
            return;
        }

        var context = _context;
        if (context is null)
        {
            handler.Invoke(this, EventArgs.Empty);
            return;
        }

        context.Post(_ => handler.Invoke(this, EventArgs.Empty), null);
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(currentModule?.ModuleName), 0);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
