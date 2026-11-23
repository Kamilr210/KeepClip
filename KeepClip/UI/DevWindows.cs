#if KEEPCLIP_DEV
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;

namespace KeepClip.UI;

internal sealed class LogsWindow : Window
{
    private readonly System.Windows.Controls.TextBox _output;
    private readonly System.Windows.Controls.CheckBox _autoScroll;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public LogsWindow()
    {
        Title = "Logi aplikacji (DEV)";
        Width = 820;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 36, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0) });
        Background = new SolidColorBrush(WpfColor.FromRgb(0x0D, 0x10, 0x18));
        Foreground = new SolidColorBrush(WpfColor.FromRgb(0xEC, 0xEF, 0xF5));

        _autoScroll = new System.Windows.Controls.CheckBox { Content = "Auto-przewijanie", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Foreground = Foreground, Margin = new Thickness(0, 0, 12, 0) };
        var copyBtn = new System.Windows.Controls.Button { Content = "Kopiuj", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 8, 0) };
        var clearBtn = new System.Windows.Controls.Button { Content = "Wyczyść", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 8, 0) };
        var closeBtn = new System.Windows.Controls.Button { Content = "✕", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(12, 6, 12, 6) };
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(_autoScroll, true);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(copyBtn, true);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(clearBtn, true);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(closeBtn, true);

        copyBtn.Click += (_, _) => { try { System.Windows.Clipboard.SetText(_output?.Text ?? ""); } catch { } };
        clearBtn.Click += (_, _) => { DevLog.Clear(); Refresh(); };
        closeBtn.Click += (_, _) => Close();

        var header = new DockPanel { Margin = new Thickness(14, 12, 14, 10) };
        var actions = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Children = { _autoScroll, copyBtn, clearBtn, closeBtn } };
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(new TextBlock { Text = "Logi aplikacji [DEV]", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

        _output = new System.Windows.Controls.TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            Background = new SolidColorBrush(WpfColor.FromRgb(0x15, 0x17, 0x1B)),
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0xD4, 0xDA, 0xE6)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12),
        };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_output);
        Content = root;

        Refresh();
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        _output.Text = string.Join(Environment.NewLine, DevLog.GetSince(0).lines);
        if (_autoScroll.IsChecked == true) _output.ScrollToEnd();
    }
}

internal sealed class ResourcesWindow : Window
{
    private readonly TextBlock _summary = new() { FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock _procs = new() { FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.NoWrap };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public ResourcesWindow()
    {
        Title = "Zużycie zasobów (DEV)";
        Width = 860;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 36, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0) });
        Background = new SolidColorBrush(WpfColor.FromRgb(0x0D, 0x10, 0x18));
        Foreground = new SolidColorBrush(WpfColor.FromRgb(0xEC, 0xEF, 0xF5));

        var refreshBtn = new System.Windows.Controls.Button { Content = "Odśwież", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 8, 0) };
        var closeBtn = new System.Windows.Controls.Button { Content = "✕", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(12, 6, 12, 6) };
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(refreshBtn, true);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(closeBtn, true);
        refreshBtn.Click += (_, _) => Refresh();
        closeBtn.Click += (_, _) => Close();

        var header = new DockPanel { Margin = new Thickness(16, 14, 16, 10) };
        var actions = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Children = { refreshBtn, closeBtn } };
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(new TextBlock { Text = "Zużycie zasobów [DEV]", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(16, 4, 16, 16),
            Content = new StackPanel { Children = { _summary, _procs } }
        };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(scroll);
        Content = root;

        Refresh();
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        var snap = App.ResourceMonitor.Sample();
        var t = snap.total;
        string cpuStr = t.cpu_percent is { } c ? $"{c:0.0}%" : "—";
        string ramStr = t.ram_bytes is { } r ? $"{r / (1024.0 * 1024.0):0.0} MB" : "—";
        string gpuStr = t.gpu_percent is { } g ? $"{g:0.0}%" : "—";
        string vramStr = t.gpu_dedicated_bytes is { } v ? $"{v / (1024.0 * 1024.0):0.0} MB" : "—";

        _summary.Text =
            $"Transkrypcja: {(snap.transcribing ? "TAK" : "NIE")}  |  " +
            $"CPU: {cpuStr}  |  RAM: {ramStr}  |  GPU: {gpuStr}  |  VRAM: {vramStr}";

        var lines = new List<string> { $"{"Proces",-26} {"PID",7} {"CPU",7} {"RAM MB",9} {"GPU",7} {"VRAM MB",9}" };
        lines.Add(new string('-', 72));
        foreach (var p in snap.processes)
        {
            var u = p.usage;
            lines.Add(
                $"{p.name,-26} {p.pid,7} " +
                $"{(u.cpu_percent is { } pc ? $"{pc,6:0.0}%" : "      —")} " +
                $"{(u.ram_bytes is { } pr ? $"{pr / (1024.0 * 1024.0),9:0.0}" : "        —")} " +
                $"{(u.gpu_percent is { } pg ? $"{pg,6:0.0}%" : "      —")} " +
                $"{(u.gpu_dedicated_bytes is { } pv ? $"{pv / (1024.0 * 1024.0),9:0.0}" : "        —")}");
        }
        _procs.Text = string.Join(Environment.NewLine, lines);
    }
}
#endif
