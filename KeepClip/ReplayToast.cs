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
/// Non-activating is the load-bearing part: stealing focus from a fullscreen game
/// can minimize it. WS_EX_NOACTIVATE + ShowWithoutActivation keep the game focused.
/// Borderless-fullscreen games (today's default) show this fine; true exclusive
/// fullscreen may not composite it — the save beep still covers that case.
/// Must be created on the WinForms UI thread (the shell form marshals).
/// </summary>
internal sealed class ReplayToast : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;  // Win11: 2 = round
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;       // no Alt-Tab entry

    private static ReplayToast? _current;

    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 30 };
    private int _ageMs;
    private const int FadeMs = 220, LingerMs = 3500;

    /// <summary>Replace any visible toast with a new one. UI thread only.</summary>
    public static void Display(bool ok, string title, string subtitle)
    {
        try { _current?.Close(); } catch { /* already disposed */ }
        var t = new ReplayToast(ok, title, subtitle);
        _current = t;
        t.Show();
    }

    private ReplayToast(bool ok, string title, string subtitle)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Opacity = 0;
        BackColor = Color.FromArgb(0x14, 0x18, 0x22);   // matches the app's card tone

        var accent = ok ? Color.FromArgb(0x46, 0xD1, 0x69) : Color.FromArgb(0xFF, 0x6B, 0x81);

        var icon = new Label
        {
            Text = ok ? "✓" : "✕",
            ForeColor = accent,
            Font = new Font("Segoe UI", 19f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(10, 10, 44, 56),
        };
        var titleLb = new Label
        {
            Text = title,
            ForeColor = Color.FromArgb(0xEC, 0xEF, 0xF5),
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            AutoSize = false,
            AutoEllipsis = true,
            Bounds = new Rectangle(58, 14, 308, 24),
        };
        var subLb = new Label
        {
            Text = subtitle,
            ForeColor = Color.FromArgb(0x9A, 0xA3, 0xB5),
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = false,
            AutoEllipsis = true,
            Bounds = new Rectangle(58, 40, 308, 22),
        };
        // Slim accent bar on the left edge, like the app's status accents.
        var bar = new Panel { BackColor = accent, Bounds = new Rectangle(0, 0, 4, 76) };
        Controls.AddRange(new Control[] { bar, icon, titleLb, subLb });

        ClientSize = new Size(380, 76);
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(wa.Right - Width - 18, wa.Top + 18);

        _anim.Tick += (_, _) =>
        {
            _ageMs += _anim.Interval;
            if (_ageMs <= FadeMs)
                Opacity = Math.Min(1.0, (double)_ageMs / FadeMs);
            else if (_ageMs >= FadeMs + LingerMs)
            {
                var fadeOut = _ageMs - FadeMs - LingerMs;
                Opacity = Math.Max(0.0, 1.0 - (double)fadeOut / FadeMs);
                if (Opacity <= 0) Close();
            }
        };
        _anim.Start();
        FormClosed += (_, _) =>
        {
            _anim.Dispose();
            if (ReferenceEquals(_current, this)) _current = null;
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
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
