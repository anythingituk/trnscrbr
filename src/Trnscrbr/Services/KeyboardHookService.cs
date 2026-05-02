using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Services;

public sealed class KeyboardHookService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly LowLevelKeyboardProc _proc;
    private readonly AppStateViewModel _state;
    private SynchronizationContext? _context;
    private System.Threading.Timer? _pushToTalkMonitor;
    private IntPtr _hookId;
    private bool _pushToTalkDown;
    private bool _suppressWinKey;
    private bool _ctrlDown;
    private bool _winDown;
    private bool _altDown;
    private bool _shiftDown;
    private bool _spaceDown;
    private bool _toggleRecordingChordDown;

    public KeyboardHookService(AppStateViewModel state)
    {
        _state = state;
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
        var shift = IsKeyDown(Keys.LShiftKey) || IsKeyDown(Keys.RShiftKey) || IsKeyDown(Keys.ShiftKey);
        var toggleHotkey = HotkeyGesture.Parse(_state.Settings.ToggleRecordingHotkey, "Ctrl+Alt+R");
        var pushToTalkHotkey = HotkeyGesture.Parse(_state.Settings.PushToTalkHotkey, "Ctrl+Alt+Space");
        TrackChordKeyState(key, isDown, isUp, ctrl, win, alt, shift);

        if (isDown && IsHotkeyPressed(key, ctrl, alt, win, shift, toggleHotkey))
        {
            if (!_toggleRecordingChordDown)
            {
                _toggleRecordingChordDown = true;
                PostEvent(ToggleRecordingPressed);
            }

            return (IntPtr)1;
        }

        if (_toggleRecordingChordDown && isUp && IsHotkeyChordKey(key, toggleHotkey))
        {
            _toggleRecordingChordDown = IsHotkeyDown(toggleHotkey);
            return (IntPtr)1;
        }

        if (IsWinKey(key) && (isDown || isUp))
        {
            if ((isDown && ctrl && pushToTalkHotkey.Win) || _pushToTalkDown || _suppressWinKey)
            {
                _suppressWinKey = isDown;
                return (IntPtr)1;
            }
        }

        if (isDown && IsHotkeyChordKey(key, pushToTalkHotkey) && IsHotkeyDown(pushToTalkHotkey))
        {
            if (!_pushToTalkDown)
            {
                _pushToTalkDown = true;
                StartPushToTalkMonitor();
                PostEvent(PushToTalkPressed);
            }

            return (IntPtr)1;
        }

        if (_pushToTalkDown && isUp && IsHotkeyChordKey(key, pushToTalkHotkey))
        {
            ReleasePushToTalk();
            return key == pushToTalkHotkey.Key || IsWinKey(key) || IsAltKey(key)
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

    private static bool IsWinKey(Keys key)
    {
        return key is Keys.LWin or Keys.RWin;
    }

    private static bool IsAltKey(Keys key)
    {
        return key is Keys.LMenu or Keys.RMenu or Keys.Menu;
    }

    private static bool IsHotkeyPressed(Keys key, bool ctrl, bool alt, bool win, bool shift, HotkeyGesture hotkey)
    {
        return key == hotkey.Key
            && ctrl == hotkey.Ctrl
            && alt == hotkey.Alt
            && win == hotkey.Win
            && shift == hotkey.Shift;
    }

    private static bool IsHotkeyDown(HotkeyGesture hotkey)
    {
        var ctrl = IsKeyDown(Keys.LControlKey) || IsKeyDown(Keys.RControlKey) || IsKeyDown(Keys.ControlKey);
        var win = IsKeyDown(Keys.LWin) || IsKeyDown(Keys.RWin);
        var alt = IsKeyDown(Keys.LMenu) || IsKeyDown(Keys.RMenu) || IsKeyDown(Keys.Menu);
        var shift = IsKeyDown(Keys.LShiftKey) || IsKeyDown(Keys.RShiftKey) || IsKeyDown(Keys.ShiftKey);
        return ctrl == hotkey.Ctrl
            && alt == hotkey.Alt
            && win == hotkey.Win
            && shift == hotkey.Shift
            && IsKeyDown(hotkey.Key);
    }

    private static bool IsHotkeyChordKey(Keys key, HotkeyGesture hotkey)
    {
        return key == hotkey.Key
            || (hotkey.Ctrl && key is Keys.LControlKey or Keys.RControlKey or Keys.ControlKey)
            || (hotkey.Alt && IsAltKey(key))
            || (hotkey.Win && IsWinKey(key))
            || (hotkey.Shift && key is Keys.LShiftKey or Keys.RShiftKey or Keys.ShiftKey);
    }

    private void TrackChordKeyState(Keys key, bool isDown, bool isUp, bool ctrl, bool win, bool alt, bool shift)
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
            else if (key is Keys.LShiftKey or Keys.RShiftKey or Keys.ShiftKey)
            {
                _shiftDown = true;
            }
            else if (key == Keys.Space)
            {
                _spaceDown = true;
                _ctrlDown = _ctrlDown || ctrl;
                _winDown = _winDown || win;
                _altDown = _altDown || alt;
                _shiftDown = _shiftDown || shift;
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
            else if (key is Keys.LShiftKey or Keys.RShiftKey or Keys.ShiftKey)
            {
                _shiftDown = IsKeyDown(Keys.LShiftKey) || IsKeyDown(Keys.RShiftKey) || IsKeyDown(Keys.ShiftKey);
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
            var pushToTalkHotkey = HotkeyGesture.Parse(_state.Settings.PushToTalkHotkey, "Ctrl+Alt+Space");
            if (_pushToTalkDown && !IsHotkeyDown(pushToTalkHotkey))
            {
                if (!IsHotkeyDown(pushToTalkHotkey)
                    && !_ctrlDown && !_winDown && !_altDown && !_shiftDown && !_spaceDown)
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
        _shiftDown = IsKeyDown(Keys.LShiftKey) || IsKeyDown(Keys.RShiftKey) || IsKeyDown(Keys.ShiftKey);
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

    private sealed record HotkeyGesture(bool Ctrl, bool Alt, bool Win, bool Shift, Keys Key)
    {
        public static HotkeyGesture Parse(string value, string fallback)
        {
            return TryParse(value, out var hotkey)
                ? hotkey
                : TryParse(fallback, out var fallbackHotkey)
                    ? fallbackHotkey
                    : new HotkeyGesture(true, true, false, false, Keys.R);
        }

        private static bool TryParse(string value, out HotkeyGesture hotkey)
        {
            var ctrl = false;
            var alt = false;
            var win = false;
            var shift = false;
            Keys? key = null;

            foreach (var part in value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                switch (part.ToUpperInvariant())
                {
                    case "CTRL":
                        ctrl = true;
                        break;
                    case "ALT":
                        alt = true;
                        break;
                    case "WIN":
                        win = true;
                        break;
                    case "SHIFT":
                        shift = true;
                        break;
                    case "SPACE":
                        key = Keys.Space;
                        break;
                    case "F9":
                        key = Keys.F9;
                        break;
                    case "F10":
                        key = Keys.F10;
                        break;
                    case "R":
                        key = Keys.R;
                        break;
                    case "D":
                        key = Keys.D;
                        break;
                }
            }

            hotkey = new HotkeyGesture(ctrl, alt, win, shift, key ?? Keys.None);
            return key is not null;
        }
    }
}
