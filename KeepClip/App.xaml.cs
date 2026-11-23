using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using KeepClip.Localization;
using KeepClip.Themes;
using WpfApplication = System.Windows.Application;

namespace KeepClip;

public partial class App : WpfApplication
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;
    internal const string ShowEventName = @"Local\KeepClip-ShowRequest";

    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _exitRequested;

    internal static ClipRepository Clips { get; } = new();
    internal static SegmentRepository Segments { get; } = new();
    internal static FolderRepository Folders { get; } = new();
    internal static StatsRepository Stats { get; } = new();
    internal static PlaybackProxyService Playback { get; } = new();
    internal static ClipService ClipService { get; } = new(Clips, Segments, Playback);
    internal static ClipDemoService ClipDemoService { get; } = new(Clips);
    internal static CloudVideoProxy CloudProxy { get; private set; } = null!;
#if KEEPCLIP_DEV
    internal static DevResourceMonitor ResourceMonitor { get; } = new();
#endif

    protected override void OnStartup(StartupEventArgs e)
    {
        CrashLog.Install();

        _singleInstance = new Mutex(true, @"Local\KeepClip-SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            FocusExistingInstance();
            Shutdown(0);
            return;
        }

        DevLog.Install();
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write("wątek UI WPF", args.Exception);
            args.Handled = true;
        };

        base.OnStartup(e);

        Db.InitDb();
        Directory.CreateDirectory(Config.ThumbsDir);
        Directory.CreateDirectory(Config.TmpDir);
        Directory.CreateDirectory(Config.PlaybackDir);

        ThemeManager.Apply(ThemeManager.CurrentTheme);
        CloudProxy = new CloudVideoProxy();

        ReplayService.ApplyConfig();

        var main = new UI.MainWindow();
        MainWindow = main;
        InitTray(main);
        StartShowSignalWaiter(main);
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ReplayService.Shutdown();
        CloudProxy?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
            _tray = null;
        }
        _showSignal?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    internal bool ShouldHideToTrayOnClose()
        => !_exitRequested && ReplayService.Enabled && ReplayService.BackgroundEnabled;

    internal void HideToTray()
    {
        MainWindow?.Hide();
        if (_tray is null) return;
        RefreshTrayTexts();
        _tray.Visible = true;
    }

    internal void RestoreFromTray()
    {
        if (MainWindow is null) return;
        if (_tray is not null) _tray.Visible = false;
        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized)
            MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    internal void RequestFullExit()
    {
        _exitRequested = true;
        MainWindow?.Close();
    }

    private void InitTray(Window main)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(Strings.Get("tray.open"), null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var exitItem = new System.Windows.Forms.ToolStripMenuItem(Strings.Get("tray.exit"), null, (_, _) =>
        {
            Dispatcher.Invoke(RequestFullExit);
        }) { Tag = "exit" };
        menu.Items.Add(exitItem);

        Icon? icon = null;
        try
        {
            var ico = Path.Combine(Config.FrontendDir, "icons", "icon.ico");
            if (File.Exists(ico)) icon = new Icon(ico);
        }
        catch { }

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = Strings.Get("tray.tooltip"),
            Icon = icon ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = false,
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);
    }

    private void RefreshTrayTexts()
    {
        if (_tray is null) return;
        _tray.Text = Strings.Get("tray.tooltip");
        if (_tray.ContextMenuStrip is not { } menu) return;
        foreach (var item in menu.Items)
        {
            if (item is not System.Windows.Forms.ToolStripMenuItem entry) continue;
            entry.Text = entry.Tag as string == "exit"
                ? Strings.Get("tray.exit")
                : Strings.Get("tray.open");
        }
    }

    private void StartShowSignalWaiter(Window main)
    {
        try
        {
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var t = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        if (!_showSignal.WaitOne()) break;
                        Dispatcher.BeginInvoke(RestoreFromTray);
                    }
                    catch { break; }
                }
            }) { IsBackground = true, Name = "KeepClip-ShowWaiter" };
            t.Start();
        }
        catch { }
    }

    private static void FocusExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var ev))
            {
                using (ev) ev.Set();
                return;
            }
        }
        catch { }
        try
        {
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(self.ProcessName))
            {
                using (p)
                {
                    if (p.Id == self.Id || p.MainWindowHandle == IntPtr.Zero) continue;
                    ShowWindowAsync(p.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(p.MainWindowHandle);
                    return;
                }
            }
        }
        catch { }
    }
}

