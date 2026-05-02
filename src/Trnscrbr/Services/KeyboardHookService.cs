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
    private System.Threading.Timer? _pushToTalkMonitor;
    private IntPtr _hookId;
    private bool _pushToTalkDown;
    private bool _suppressWinKey;
    private bool _ctrlDown;
    private bool _winDown;
    private bool _altDown;
    private bool _spaceDown;
    private bool _toggleRecordingChordDown;

    public KeyboardHookService()
    {
        _proc = HookCallback;
    }

    public event EventHandler? PushToTalkPressed;
    public event EventHandler? PushToTalkReleased;
    public event EventHandler? ToggleRecordingPressed;
    public event EventHandler? CancelPressed;

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
        _pushToTalkMonitor?.Dispose();
        _pushToTalkMonitor = null;

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
        var alt = IsKeyDown(Keys.LMenu) || IsKeyDown(Keys.RMenu) || IsKeyDown(Keys.Menu);
        TrackChordKeyState(key, isDown, isUp, ctrl, win, alt);

        if (isDown && IsToggleRecordingChord(key, ctrl, alt))
        {
            if (!_toggleRecordingChordDown)
            {
                _toggleRecordingChordDown = true;
                PostEvent(ToggleRecordingPressed);
            }

            return (IntPtr)1;
        }

        if (_toggleRecordingChordDown && isUp && IsToggleRecordingChordKey(key))
        {
            _toggleRecordingChordDown = IsToggleRecordingChordDown();
            return (IntPtr)1;
        }

        if (IsWinKey(key) && (isDown || isUp))
        {
            if ((isDown && ctrl) || _pushToTalkDown || _suppressWinKey)
            {
                _suppressWinKey = isDown;
                return (IntPtr)1;
            }
        }

        if (isDown && IsPushToTalkChordKey(key) && IsPushToTalkChordDown())
        {
            if (!_pushToTalkDown)
            {
                _pushToTalkDown = true;
                StartPushToTalkMonitor();
                PostEvent(PushToTalkPressed);
            }

            return (IntPtr)1;
        }

        if (_pushToTalkDown && isUp && IsPushToTalkChordKey(key))
        {
            ReleasePushToTalk();
            return key == Keys.Space || IsWinKey(key) || IsAltKey(key)
                ? (IntPtr)1
                : CallNextHookEx(_hookId, nCode, wParam, lParam);
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
            or Keys.LMenu
            or Keys.RMenu
            or Keys.Menu
            or Keys.LControlKey
            or Keys.RControlKey
            or Keys.ControlKey;
    }

    private static bool IsWinKey(Keys key)
    {
        return key is Keys.LWin or Keys.RWin;
    }

    private static bool IsAltKey(Keys key)
    {
        return key is Keys.LMenu or Keys.RMenu or Keys.Menu;
    }

    private static bool IsToggleRecordingChord(Keys key, bool ctrl, bool alt)
    {
        return key == Keys.R && ctrl && alt;
    }

    private static bool IsToggleRecordingChordKey(Keys key)
    {
        return key is Keys.R
            or Keys.LMenu
            or Keys.RMenu
            or Keys.Menu
            or Keys.LControlKey
            or Keys.RControlKey
            or Keys.ControlKey;
    }

    private static bool IsToggleRecordingChordDown()
    {
        var ctrl = IsKeyDown(Keys.LControlKey) || IsKeyDown(Keys.RControlKey) || IsKeyDown(Keys.ControlKey);
        var alt = IsKeyDown(Keys.LMenu) || IsKeyDown(Keys.RMenu) || IsKeyDown(Keys.Menu);
        return ctrl && alt && IsKeyDown(Keys.R);
    }

    private static bool IsPushToTalkChordDown()
    {
        var ctrl = IsKeyDown(Keys.LControlKey) || IsKeyDown(Keys.RControlKey) || IsKeyDown(Keys.ControlKey);
        var win = IsKeyDown(Keys.LWin) || IsKeyDown(Keys.RWin);
        var alt = IsKeyDown(Keys.LMenu) || IsKeyDown(Keys.RMenu) || IsKeyDown(Keys.Menu);
        return ctrl && (win || alt) && IsKeyDown(Keys.Space);
    }

    private void TrackChordKeyState(Keys key, bool isDown, bool isUp, bool ctrl, bool win, bool alt)
    {
        if (isDown)
        {
            if (key is Keys.LControlKey or Keys.RControlKey or Keys.ControlKey)
            {
                _ctrlDown = true;
            }
            else if (IsWinKey(key))
            {
                _winDown = true;
            }
            else if (IsAltKey(key))
            {
                _altDown = true;
            }
            else if (key == Keys.Space)
            {
                _spaceDown = true;
                _ctrlDown = _ctrlDown || ctrl;
                _winDown = _winDown || win;
                _altDown = _altDown || alt;
            }
        }
        else if (isUp)
        {
            if (key is Keys.LControlKey or Keys.RControlKey or Keys.ControlKey)
            {
                _ctrlDown = IsKeyDown(Keys.LControlKey) || IsKeyDown(Keys.RControlKey) || IsKeyDown(Keys.ControlKey);
            }
            else if (IsWinKey(key))
            {
                _winDown = IsKeyDown(Keys.LWin) || IsKeyDown(Keys.RWin);
            }
            else if (IsAltKey(key))
            {
                _altDown = IsKeyDown(Keys.LMenu) || IsKeyDown(Keys.RMenu) || IsKeyDown(Keys.Menu);
            }
            else if (key == Keys.Space)
            {
                _spaceDown = false;
            }
        }
    }

    private void StartPushToTalkMonitor()
    {
        _pushToTalkMonitor?.Dispose();
        _pushToTalkMonitor = new System.Threading.Timer(_ =>
        {
            if (_pushToTalkDown && !IsPushToTalkChordDown())
            {
                if (!_ctrlDown && !_winDown && !_altDown && !_spaceDown)
                {
                    ReleasePushToTalk();
                }
            }
        }, null, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
    }

    private void ReleasePushToTalk()
    {
        if (!_pushToTalkDown)
        {
            return;
        }

        _pushToTalkDown = false;
        _ctrlDown = IsKeyDown(Keys.LControlKey) || IsKeyDown(Keys.RControlKey) || IsKeyDown(Keys.ControlKey);
        _winDown = IsKeyDown(Keys.LWin) || IsKeyDown(Keys.RWin);
        _altDown = IsKeyDown(Keys.LMenu) || IsKeyDown(Keys.RMenu) || IsKeyDown(Keys.Menu);
        _spaceDown = IsKeyDown(Keys.Space);
        _pushToTalkMonitor?.Dispose();
        _pushToTalkMonitor = null;
        PostEvent(PushToTalkReleased);
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
