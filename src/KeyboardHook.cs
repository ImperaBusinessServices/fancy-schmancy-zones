using System.Runtime.InteropServices;

namespace FancySchmancyZones;

/// <summary>
/// A global low-level keyboard hook. Unlike RegisterHotKey, this sees keystrokes
/// directly, so it still works when another utility has "claimed" combos.
/// The handler returns true to swallow the key (stop it reaching other apps).
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    public delegate bool KeyDownHandler(int vkCode);

    private readonly KeyDownHandler _onKeyDown;
    private readonly LowLevelKeyboardProc _proc; // keep the delegate alive for the hook's lifetime
    private IntPtr _hook = IntPtr.Zero;

    public KeyboardHook(KeyDownHandler onKeyDown)
    {
        _onKeyDown = onKeyDown;
        _proc = Callback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    public bool Installed => _hook != IntPtr.Zero;

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                int vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode is the first field
                if (_onKeyDown(vk)) return (IntPtr)1; // handled -> swallow
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>Is a key currently held down? (Ctrl/Alt/Shift etc.)</summary>
    public static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc proc, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
