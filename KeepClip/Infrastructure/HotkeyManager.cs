using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeepClip.Infrastructure;

internal static class HotkeyManager
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int HotkeyId = 0xC11D;
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;
    private const uint MOD_NOREPEAT = 0x4000;

    private static Form? _form;
    private static bool _registered;

    public static bool IsActive { get; private set; }

    public static void Attach(Form form)
    {
        _form = form;
        form.HandleDestroyed += (_, _) => { _registered = false; IsActive = false; };
        Refresh();
    }

    public static void Refresh()
    {
        var form = _form;
        if (form is null || form.IsDisposed) return;
        if (form.InvokeRequired)
        {
            try { form.BeginInvoke(Refresh); } catch { }
            return;
        }

        if (_registered)
        {
            try { UnregisterHotKey(form.Handle, HotkeyId); } catch { }
            _registered = false;
            IsActive = false;
        }
        if (!ReplayService.Enabled) return;
        if (!TryParse(ReplayService.Hotkey, out var mods, out var vk)) return;
        _registered = RegisterHotKey(form.Handle, HotkeyId, mods | MOD_NOREPEAT, vk);
        IsActive = _registered;
    }

    public static bool HandleMessage(ref Message m)
        => m.Msg == WM_HOTKEY && (int)m.WParam == HotkeyId;

    public static bool TryParse(string combo, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(combo)) return false;
        foreach (var raw in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= MOD_CONTROL; continue;
                case "alt": mods |= MOD_ALT; continue;
                case "shift": mods |= MOD_SHIFT; continue;
                case "win" or "meta": mods |= MOD_WIN; continue;
            }
            if (vk != 0) return false;
            var token = raw.Length == 1 ? raw.ToUpperInvariant() : raw;
            if (!Enum.TryParse<Keys>(token, ignoreCase: true, out var key)) return false;
            vk = (uint)key;
        }
        return vk != 0 && mods != 0;
    }
}
