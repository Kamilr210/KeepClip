using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using KeepClip.Localization;
using KeepClip.Themes;
using WpfApplication = System.Windows.Application;

namespace KeepClip;

public partial class App : WpfApplication
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyIcon(IntPtr hIcon);
    private const int SW_RESTORE = 9;
    internal const string ShowEventName = @"Local\KeepClip-ShowRequest";

    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private System.Windows.Forms.NotifyIcon? _tray;
    private DispatcherTimer? _trayTimer;
    private Icon? _baseIcon;
    private Icon? _recordingIcon;
    private Icon? _savingIcon;
    private System.Windows.Forms.ToolStripMenuItem? _trayStatusItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayToggleRecItem;
    private System.Windows.Forms.ToolStripMenuItem? _traySaveReplayItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayOpenLastItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayOpenFolderItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayReplaySettingsItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayOpenAppItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayExitItem;
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
        ReplayService.StateChanged -= OnReplayStateChanged;
        Loc.Instance.PropertyChanged -= OnLanguageChanged;
        _trayTimer?.Stop();
        ReplayService.Shutdown();
        CloudProxy?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
            _tray = null;
        }
        _recordingIcon?.Dispose();
        _savingIcon?.Dispose();
        _baseIcon?.Dispose();
        _showSignal?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    internal bool ShouldHideToTrayOnClose()
        => !_exitRequested;

    internal void HideToTray()
    {
        MainWindow?.Hide();
        RefreshTrayState();
    }

    internal void RestoreFromTray()
    {
        if (MainWindow is null) return;
        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized)
            MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
        RefreshTrayState();
    }

    internal void RequestFullExit()
    {
        _exitRequested = true;
        MainWindow?.Close();
    }

    private void InitTray(Window main)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new DarkTrayColorTable()),
            BackColor = Color.FromArgb(24, 24, 32),
            ForeColor = Color.FromArgb(240, 240, 248),
        };

        _trayStatusItem = new System.Windows.Forms.ToolStripMenuItem { Enabled = false };
        _trayToggleRecItem = new System.Windows.Forms.ToolStripMenuItem("", null, (_, _) => OnTrayToggleRecording());
        _traySaveReplayItem = new System.Windows.Forms.ToolStripMenuItem("", null, (_, _) => OnTraySaveReplay());
        _trayOpenLastItem = new System.Windows.Forms.ToolStripMenuItem("", null, (_, _) => Dispatcher.Invoke(OnTrayOpenLastReplay));
        _trayOpenFolderItem = new System.Windows.Forms.ToolStripMenuItem("", null, (_, _) => OnTrayOpenClipsFolder());
        _trayReplaySettingsItem = new System.Windows.Forms.ToolStripMenuItem("", null, (_, _) => Dispatcher.Invoke(OnTrayOpenReplaySettings));
        _trayOpenAppItem = new System.Windows.Forms.ToolStripMenuItem("", null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
        _trayExitItem = new System.Windows.Forms.ToolStripMenuItem("", null, (_, _) => Dispatcher.Invoke(RequestFullExit));

        menu.Items.Add(_trayStatusItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(_trayToggleRecItem);
        menu.Items.Add(_traySaveReplayItem);
        menu.Items.Add(_trayOpenLastItem);
        menu.Items.Add(_trayOpenFolderItem);
        menu.Items.Add(_trayReplaySettingsItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(_trayOpenAppItem);
        menu.Items.Add(_trayExitItem);
        menu.Opening += (_, _) => RefreshTrayState();

        try
        {
            var ico = Path.Combine(Config.FrontendDir, "icons", "icon.ico");
            if (File.Exists(ico)) _baseIcon = new Icon(ico);
        }
        catch { }
        _baseIcon ??= (Icon)SystemIcons.Application.Clone();
        _recordingIcon = CreateBadgeIcon(_baseIcon, Color.FromArgb(239, 68, 68));
        _savingIcon = CreateBadgeIcon(_baseIcon, Color.FromArgb(245, 158, 11));

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "KeepClip",
            Icon = _baseIcon,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left)
                Dispatcher.Invoke(RestoreFromTray);
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);

        ReplayService.StateChanged += OnReplayStateChanged;
        Loc.Instance.PropertyChanged += OnLanguageChanged;

        _trayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _trayTimer.Tick += (_, _) => RefreshTrayState();
        _trayTimer.Start();

        RefreshTrayState();
    }

    private void OnReplayStateChanged() => Dispatcher.BeginInvoke(RefreshTrayState);
    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(RefreshTrayState);

    private void RefreshTrayState()
    {
        if (_tray is null) return;

        bool running = ReplayService.IsRunning;
        bool saving = ReplayService.SaveInProgress;
        bool enabled = ReplayService.Enabled;
        string hotkey = ReplayService.Hotkey;

        _tray.Icon = saving
            ? (_savingIcon ?? _baseIcon)
            : running
                ? (_recordingIcon ?? _baseIcon)
                : _baseIcon;

        string statusText = saving
            ? Loc.T("tray.statusSaving")
            : running
                ? $"{Loc.T("tray.statusRecording")} ({hotkey})"
                : Loc.T("tray.statusStopped");

        string tooltip = $"KeepClip — {statusText}";
        _tray.Text = tooltip.Length <= 63 ? tooltip : tooltip[..62] + "…";

        if (_trayStatusItem is not null)
            _trayStatusItem.Text = saving
                ? $"⏳ {statusText}"
                : running
                    ? $"● {statusText}"
                    : $"○ {statusText}";

        if (_trayToggleRecItem is not null)
            _trayToggleRecItem.Text = (running || enabled)
                ? Loc.T("tray.stopRecording")
                : Loc.T("tray.startRecording");

        if (_traySaveReplayItem is not null)
        {
            _traySaveReplayItem.Text = $"{Loc.T("tray.saveReplay")} ({hotkey})";
            _traySaveReplayItem.Enabled = running && !saving;
        }

        if (_trayOpenLastItem is not null) _trayOpenLastItem.Text = Loc.T("tray.openLastReplay");
        if (_trayOpenFolderItem is not null) _trayOpenFolderItem.Text = Loc.T("tray.openClipsFolder");
        if (_trayReplaySettingsItem is not null) _trayReplaySettingsItem.Text = Loc.T("tray.replaySettings");
        if (_trayOpenAppItem is not null) _trayOpenAppItem.Text = Loc.T("tray.openApp");
        if (_trayExitItem is not null) _trayExitItem.Text = Loc.T("tray.exitApp");
    }

    private void OnTrayToggleRecording()
    {
        bool nextEnabled = !(ReplayService.IsRunning || ReplayService.Enabled);
        Settings.SetString("replay_enabled", nextEnabled ? "1" : "0");
        _ = Task.Run(() =>
        {
            ReplayService.ApplyConfig();
            Dispatcher.BeginInvoke(RefreshTrayState);
        });
    }

    private void OnTraySaveReplay()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ReplayService.SaveAsync("tray");
                _ = Dispatcher.BeginInvoke(() =>
                {
                    RefreshTrayState();
                    if (MainWindow is UI.MainWindow mw && mw.IsVisible)
                        mw.RefreshAll();
                });
            }
            catch (Exception ex)
            {
                ReplayService.PlayCue(ok: false);
                _ = Dispatcher.BeginInvoke(() => ReplayToast.Display(false, Strings.Get("replay.failedTitle"), ex.Message));
            }
        });
    }

    private void OnTrayOpenLastReplay()
    {
        RestoreFromTray();
        if (MainWindow is UI.MainWindow mw)
            mw.OpenLastReplay();
    }

    private void OnTrayOpenReplaySettings()
    {
        RestoreFromTray();
        if (MainWindow is UI.MainWindow mw)
            mw.OpenReplayOverlay();
    }

    private static void OnTrayOpenClipsFolder()
    {
        try
        {
            var root = Settings.GetClipsRoot();
            if (Directory.Exists(root))
                Process.Start(new ProcessStartInfo { FileName = root, UseShellExecute = true });
        }
        catch { }
    }

    private static Icon CreateBadgeIcon(Icon baseIcon, Color dotColor)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawIcon(baseIcon, new Rectangle(0, 0, 32, 32));

            const int d = 13;
            int x = 32 - d - 1;
            int y = 32 - d - 1;
            using var borderBrush = new SolidBrush(Color.FromArgb(245, 16, 16, 22));
            g.FillEllipse(borderBrush, x - 2, y - 2, d + 4, d + 4);
            using var dotBrush = new SolidBrush(dotColor);
            g.FillEllipse(dotBrush, x, y, d, d);
        }
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private sealed class DarkTrayColorTable : System.Windows.Forms.ProfessionalColorTable
    {
        private static readonly Color Bg = Color.FromArgb(24, 24, 32);
        private static readonly Color Hover = Color.FromArgb(46, 46, 62);
        private static readonly Color Border = Color.FromArgb(56, 56, 74);

        public override Color ToolStripDropDownBackground => Bg;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
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

