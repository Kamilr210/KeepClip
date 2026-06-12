using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeepClip;

/// <summary>
/// ShadowPlay-style on-screen confirmation that a replay was saved (or failed) —
/// visible over games, because the in-app toast is useless mid-match. A small
/// borderless, topmost, non-activating window in the top-right corner of the
/// primary screen that fades in, lingers ~3.5 s and fades out.
///
/// Two windowing rules are load-bearing here:
///  1. NON-ACTIVATING (WS_EX_NOACTIVATE + ShowWithoutActivation): stealing focus
///     from a fullscreen game minimizes it.
///  2. After the one-time Show(), the window NEVER changes window state again —
///     no Hide(), no Close(). It "disappears" purely by animating Opacity to 0
///     and stays formally visible (and click-through, WS_EX_TRANSPARENT) forever.
///     Field-measured on a sensitive setup: Close() AND even Hide() of this
///     never-activated topmost window minimized a fullscreen game at the exact
///     millisecond of the hide (the same machine loses the game on Teams
///     notifications). An opacity-only, input-transparent layered window gives
///     the shell literally no window-state event to react to.
/// Must be used from the WinForms UI thread (the shell form marshals).
/// </summary>
internal sealed class ReplayToast : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;  // Win11: 2 = round
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;       // no Alt-Tab entry
    private const int WS_EX_TRANSPARENT = 0x00000020;      // click-through: can never take the mouse

    private static ReplayToast? _instance;

    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 30 };
    private readonly Label _icon;
    private readonly Label _titleLb;
    private readonly Label _subLb;
    private readonly Panel _bar;
    private int _ageMs;
    private const int FadeMs = 220, LingerMs = 3500;

    private static readonly Color Good = Color.FromArgb(0x46, 0xD1, 0x69);
    private static readonly Color Bad = Color.FromArgb(0xFF, 0x6B, 0x81);

    /// <summary>Show (or re-show with new content) the toast. UI thread only.</summary>
    public static void Display(bool ok, string title, string subtitle)
    {
        if (_instance is null || _instance.IsDisposed) _instance = new ReplayToast();
        _instance.Present(ok, title, subtitle);
    }

    private ReplayToast()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Opacity = 0;
        BackColor = Color.FromArgb(0x14, 0x18, 0x22);   // matches the app's card tone

        _icon = new Label
        {
            Font = new Font("Segoe UI", 19f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(10, 10, 44, 56),
        };
        _titleLb = new Label
        {
            ForeColor = Color.FromArgb(0xEC, 0xEF, 0xF5),
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            AutoSize = false,
            AutoEllipsis = true,
            Bounds = new Rectangle(58, 14, 308, 24),
        };
        _subLb = new Label
        {
            ForeColor = Color.FromArgb(0x9A, 0xA3, 0xB5),
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = false,
            AutoEllipsis = true,
            Bounds = new Rectangle(58, 40, 308, 22),
        };
        // Slim accent bar on the left edge, like the app's status accents.
        _bar = new Panel { Bounds = new Rectangle(0, 0, 4, 76) };
        Controls.AddRange(new Control[] { _bar, _icon, _titleLb, _subLb });
        ClientSize = new Size(380, 76);

        _anim.Tick += (_, _) =>
        {
            _ageMs += _anim.Interval;
            if (_ageMs <= FadeMs)
                Opacity = Math.Min(1.0, (double)_ageMs / FadeMs);
            else if (_ageMs >= FadeMs + LingerMs)
            {
                var fadeOut = _ageMs - FadeMs - LingerMs;
                Opacity = Math.Max(0.0, 1.0 - (double)fadeOut / FadeMs);
                if (Opacity <= 0)
                    _anim.Stop();   // opacity-0 only — never Hide()/Close(), see class comment
            }
        };
    }

    private void Present(bool ok, string title, string subtitle)
    {
        var accent = ok ? Good : Bad;
        _icon.Text = ok ? "✓" : "✕";
        _icon.ForeColor = accent;
        _bar.BackColor = accent;
        _titleLb.Text = title;
        _subLb.Text = subtitle;

        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(wa.Right - Width - 18, wa.Top + 18);

        _ageMs = 0;
        Opacity = 0;
        if (!Visible) Show();   // one-time only (ShowWithoutActivation ⇒ SW_SHOWNOACTIVATE)
        _anim.Stop();
        _anim.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = 2;
        try { DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int)); }
        catch { /* Win10: square corners, fine */ }
    }
}
