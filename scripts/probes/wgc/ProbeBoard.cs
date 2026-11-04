using System.Diagnostics;
using System.Runtime.InteropServices;

internal sealed class ProbeBoard : Form
{
    public const int MarkerX = 32, MarkerY = 160, Cell = 16, Bits = 16;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Font _heading = new("Segoe UI", 22, FontStyle.Bold);
    private readonly Font _text = new("Segoe UI", 13);
    private readonly bool _periodActive;
    private long _paints;
    private double _nextPaint;
    public long PaintCount => Interlocked.Read(ref _paints);
    internal bool AllowClose;
    public event Action? StopRequested;

    public ProbeBoard()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1280, 720);
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Text = "KeepClip WGC synthetic probe";
        BackColor = Color.FromArgb(12, 21, 32);
        _periodActive = timeBeginPeriod(1) == 0;
        _timer.Tick += (_, _) =>
        {
            double now = _clock.Elapsed.TotalSeconds;
            if (now >= _nextPaint) { _nextPaint = now + 1.0 / 90; Invalidate(); }
        };
        Shown += (_, _) => _timer.Start();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) StopRequested?.Invoke(); };
        FormClosing += (_, e) => { if (!AllowClose) { e.Cancel = true; StopRequested?.Invoke(); } };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ushort marker = (ushort)Interlocked.Increment(ref _paints);
        int shift = (int)(_clock.ElapsedMilliseconds / 8 % 120);
        using var grid = new SolidBrush(Color.FromArgb(28, 47, 66));
        using var accent = new SolidBrush(Color.FromArgb(110, 235, 182));
        for (int y = 220; y < Height; y += 80)
            for (int x = -120; x < Width; x += 120) e.Graphics.FillRectangle(grid, x + shift, y, 54, 38);
        int circleX = (int)((Math.Sin(_clock.Elapsed.TotalSeconds) + 1) * Math.Max(1, Width - 180) / 2);
        e.Graphics.FillEllipse(accent, circleX, Math.Max(230, Height / 2), 160, 160);
        e.Graphics.DrawString("KeepClip — Windows Graphics Capture", _heading, accent, 28, 22);
        e.Graphics.DrawString("Próba świeżych klatek WGC; Esc: przerwij. Bez mikrofonu i H.264.", _text, Brushes.White, 30, 78);
        e.Graphics.DrawString("Licznik każdej namalowanej klatki: " + marker, _text, Brushes.White, 30, 112);
        e.Graphics.FillRectangle(Brushes.White, MarkerX, MarkerY, Cell, Cell);
        e.Graphics.FillRectangle(Brushes.Black, MarkerX + Cell, MarkerY, Cell, Cell);
        int parity = 0;
        for (int bit = 0; bit < Bits; bit++)
        {
            bool set = (marker & (1 << bit)) != 0;
            if (set) parity ^= 1;
            e.Graphics.FillRectangle(set ? Brushes.White : Brushes.Black, MarkerX + (bit + 2) * Cell, MarkerY, Cell, Cell);
        }
        e.Graphics.FillRectangle(parity != 0 ? Brushes.White : Brushes.Black, MarkerX + (Bits + 2) * Cell, MarkerY, Cell, Cell);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _heading.Dispose(); _text.Dispose(); if (_periodActive) timeEndPeriod(1); }
        base.Dispose(disposing);
    }

    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint timeEndPeriod(uint period);
}
