using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;

namespace KeepClip.Infrastructure;

internal sealed class ReplayToast : Window
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    private static ReplayToast? _instance;

    private readonly DispatcherTimer _anim = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly TextBlock _icon;
    private readonly TextBlock _titleLb;
    private readonly TextBlock _subLb;
    private readonly Border _bar;
    private int _ageMs;
    private const int FadeMs = 220, LingerMs = 3500;

    private static readonly WpfBrush GoodBrush = new SolidColorBrush(WpfColor.FromRgb(0x46, 0xD1, 0x69));
    private static readonly WpfBrush BadBrush = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x6B, 0x81));

    public static void Display(bool ok, string title, string subtitle)
    {
        if (_instance is null) _instance = new ReplayToast();
        _instance.Present(ok, title, subtitle);
    }

    private ReplayToast()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = 380;
        Height = 76;
        Opacity = 0;

        _bar = new Border { Width = 4, CornerRadius = new CornerRadius(10, 0, 0, 10) };
        _icon = new TextBlock
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Width = 44,
            TextAlignment = TextAlignment.Center,
        };
        _titleLb = new TextBlock
        {
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0xEC, 0xEF, 0xF5)),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _subLb = new TextBlock
        {
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x9A, 0xA3, 0xB5)),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 14, 0),
            Children = { _titleLb, _subLb }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_bar, 0);
        Grid.SetColumn(_icon, 1);
        Grid.SetColumn(stack, 2);
        grid.Children.Add(_bar);
        grid.Children.Add(_icon);
        grid.Children.Add(stack);

        Content = new Border
        {
            Background = new SolidColorBrush(WpfColor.FromRgb(0x14, 0x18, 0x22)),
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0x28, 0x2E, 0x3D)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = grid
        };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
        };

        _anim.Tick += (_, _) =>
        {
            _ageMs += 30;
            if (_ageMs <= FadeMs)
                Opacity = Math.Min(1.0, (double)_ageMs / FadeMs);
            else if (_ageMs >= FadeMs + LingerMs)
            {
                var fadeOut = _ageMs - FadeMs - LingerMs;
                Opacity = Math.Max(0.0, 1.0 - (double)fadeOut / FadeMs);
                if (Opacity <= 0)
                {
                    _anim.Stop();
                    Hide();
                }
            }
        };
    }

    private void Present(bool ok, string title, string subtitle)
    {
        var accent = ok ? GoodBrush : BadBrush;
        _icon.Text = ok ? "✓" : "✕";
        _icon.Foreground = accent;
        _bar.Background = accent;
        _titleLb.Text = title;
        _subLb.Text = subtitle;

        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 18;
        Top = wa.Top + 18;

        _ageMs = 0;
        Opacity = 0;
        if (!IsVisible) Show();
        _anim.Stop();
        _anim.Start();
    }
}