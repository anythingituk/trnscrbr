using System.Diagnostics;
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
    private IntPtr _hookId;
    private bool _pushToTalkDown;

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
        _hookId = SetHook(_proc);
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
                PushToTalkPressed?.Invoke(this, EventArgs.Empty);
            }
            else if (isUp)
            {
                _pushToTalkDown = false;
                PushToTalkReleased?.Invoke(this, EventArgs.Empty);
            }

            return (IntPtr)1;
        }

        if (ctrl && win && key == Keys.V && isDown)
        {
            PasteLastTranscriptPressed?.Invoke(this, EventArgs.Empty);
            return (IntPtr)1;
        }

        if (key == Keys.Escape && isDown)
        {
            CancelPressed?.Invoke(this, EventArgs.Empty);
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsKeyDown(Keys key)
    {
        return (GetAsyncKeyState((int)key) & 0x8000) != 0;
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
