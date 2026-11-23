using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace KeepClip.Infrastructure;

internal static class HotkeyManager
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int HotkeyId = 0xC11D;
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;
    private const uint MOD_NOREPEAT = 0x4000;

    private static Window? _window;
    private static HwndSource? _source;
    private static IntPtr _hwnd;
    private static bool _registered;
    private static Action? _onPressed;

    public static bool IsActive { get; private set; }

    public static void Attach(Window window, Action onPressed)
    {
        _window = window;
        _onPressed = onPressed;
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
        window.Closed += (_, _) =>
        {
            Unregister();
            _source?.RemoveHook(WndProc);
            _source = null;
            _hwnd = IntPtr.Zero;
        };
        Refresh();
    }

    public static void Refresh()
    {
        var window = _window;
        if (window is null || _hwnd == IntPtr.Zero) return;
        if (!window.Dispatcher.CheckAccess())
        {
            window.Dispatcher.BeginInvoke(Refresh);
            return;
        }

        Unregister();
        if (!ReplayService.Enabled) return;
        if (!TryParse(ReplayService.Hotkey, out var mods, out var vk)) return;
        _registered = RegisterHotKey(_hwnd, HotkeyId, mods | MOD_NOREPEAT, vk);
        IsActive = _registered;
    }

    private static void Unregister()
    {
        if (_registered && _hwnd != IntPtr.Zero)
        {
            try { UnregisterHotKey(_hwnd, HotkeyId); } catch { }
        }
        _registered = false;
        IsActive = false;
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _onPressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public static bool TryParse(string combo, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(combo)) return false;
        foreach (var raw in combo.Split('+', StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0) return false;
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= MOD_CONTROL; continue;
                case "alt": mods |= MOD_ALT; continue;
                case "shift": mods |= MOD_SHIFT; continue;
                case "win" or "meta": mods |= MOD_WIN; continue;
            }
            if (vk != 0) return false;
            var token = raw.Length == 1 && raw[0] is >= '0' and <= '9' ? "D" + raw : raw.Length == 1 ? raw.ToUpperInvariant() : raw;
            if (!Enum.TryParse<Key>(token, ignoreCase: true, out var key)) return false;
            if (key is Key.None or Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
                return false;
            var vkey = (uint)KeyInterop.VirtualKeyFromKey(key);
            if (vkey is 0 or > 255) return false;
            vk = vkey;
        }
        return vk != 0 && mods != 0;
    }
}
