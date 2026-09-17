using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KeepClip.Infrastructure;

internal static class DesktopShell
{
    public static void Run(string baseUrl)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { }
        using var form = new ShellForm(baseUrl);
        Application.Run(form);
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;

    internal const string ShowEventName = @"Local\KeepClip-ShowRequest";

    public static void FocusExistingInstance()
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

internal sealed class ShellForm : Form
{
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    private const int HTBOTTOMRIGHT = 17;

    private static readonly Color AppBg = Color.FromArgb(0x07, 0x0A, 0x10);

    private readonly string _baseUrl;
    private readonly WebView2 _web = new();

    private bool _isMaximized;
    private Rectangle _restoreBounds;

    private NotifyIcon? _tray;
    private bool _exitRequested;

    public ShellForm(string baseUrl)
    {
        _baseUrl = baseUrl;

        Text = "KeepClip";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(900, 600);
        BackColor = AppBg;
        ShowInTaskbar = true;
        TryLoadIcon();
        InitTray();
        StartShowSignalWaiter();
        RestoreWindowState();
        FormClosing += (_, e) =>
        {
            SaveWindowState();

            if (!_exitRequested && e.CloseReason == CloseReason.UserClosing
                && ReplayService.Enabled && ReplayService.BackgroundEnabled)
            {
                e.Cancel = true;
                HideToTray();
            }
        };

        _web.Dock = DockStyle.Fill;
        _web.DefaultBackgroundColor = AppBg;
        Controls.Add(_web);

        Load += async (_, _) => await InitWebViewAsync();

        HandleCreated += (_, _) =>
        {
            HotkeyManager.Attach(this);
            ReplayService.Notifier = SafeToast;
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.F10)
        {
            ForwardF10ToPage(keyData);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ForwardF10ToPage(Keys keyData)
    {
        var core = _web.CoreWebView2;
        if (core is null) return;

        static string Js(bool value) => value ? "true" : "false";
        string script =
            "(function(){var el=document.activeElement||document.body;" +
            "el.dispatchEvent(new KeyboardEvent('keydown',{key:'F10',code:'F10'," +
            "keyCode:121,which:121," +
            $"altKey:{Js((keyData & Keys.Alt) != 0)}," +
            $"ctrlKey:{Js((keyData & Keys.Control) != 0)}," +
            $"shiftKey:{Js((keyData & Keys.Shift) != 0)}," +
            "bubbles:true,cancelable:true}));})();";
        try { core.ExecuteScriptAsync(script); } catch { }
    }

    protected override void WndProc(ref Message m)
    {
        if (HotkeyManager.HandleMessage(ref m))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ReplayService.SaveAsync("hotkey");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Powtórka (hotkey): {ex.Message}");

                    if (ReplayService.SaveInProgress) return;
                    ReplayService.PlayCue(ok: false);
                    SafeToast(false, Strings.Get("replay.saveFailedTitle"), ex.Message);
                }
            });
            return;
        }
        base.WndProc(ref m);
    }

    private void SafeToast(bool ok, string title, string subtitle)
    {
        try
        {
            if (!IsDisposed) BeginInvoke(() => ReplayToast.Display(ok, title, subtitle));
        }
        catch { }
    }

    private void TryLoadIcon()
    {
        try
        {
            var ico = Path.Combine(Config.FrontendDir, "icons", "icon.ico");
            if (File.Exists(ico)) Icon = new Icon(ico);
        }
        catch { }
    }

    private void InitTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(Strings.Get("tray.open"), null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());
        var exitItem = new ToolStripMenuItem(Strings.Get("tray.exit"), null, (_, _) =>
        {
            _exitRequested = true;
            Close();
        }) { Tag = "exit" };
        menu.Items.Add(exitItem);

        _tray = new NotifyIcon
        {
            Text = Strings.Get("tray.tooltip"),
            Icon = Icon ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = false,
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();

        FormClosed += (_, _) =>
        {
            if (_tray is null) return;
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        };
    }

    private void HideToTray()
    {
        Hide();
        if (_tray is null) return;

        RefreshTrayTexts();
        _tray.Visible = true;
    }

    private void RefreshTrayTexts()
    {
        if (_tray is null) return;
        _tray.Text = Strings.Get("tray.tooltip");
        if (_tray.ContextMenuStrip is not { } menu) return;
        foreach (var item in menu.Items)
        {
            if (item is not ToolStripMenuItem entry) continue;
            entry.Text = entry.Tag as string == "exit"
                ? Strings.Get("tray.exit")
                : Strings.Get("tray.open");
        }
    }

    private void RestoreFromTray()
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(RestoreFromTray); } catch { }
            return;
        }
        if (_tray is not null) _tray.Visible = false;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    private void StartShowSignalWaiter()
    {
        var t = new Thread(() =>
        {
            try
            {
                using var ev = new EventWaitHandle(false, EventResetMode.AutoReset, DesktopShell.ShowEventName);
                while (true)
                {
                    ev.WaitOne();
                    if (IsDisposed) return;
                    RestoreFromTray();
                }
            }
            catch { }
        })
        { IsBackground = true, Name = "KeepClip-ShowSignal" };
        t.Start();
    }

    private async Task InitWebViewAsync()
    {
        var userData = Path.Combine(Config.DataDir, "webview2");
        Directory.CreateDirectory(userData);

        var opts = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--enable-features=PlatformHEVCDecoderSupport",
        };
        var env = await CoreWebView2Environment.CreateAsync(null, userData, opts);
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;

        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;

        try { core.Settings.AreBrowserAcceleratorKeysEnabled = false; }
        catch { }

        try { core.Settings.IsNonClientRegionSupportEnabled = true; }
        catch { }

        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri) { UseShellExecute = true }); }
            catch { }
        };

        await core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeJs);
        core.WebMessageReceived += OnWebMessage;

        _web.Source = new Uri(_baseUrl);
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        BridgeRequest? req;
        try { req = JsonSerializer.Deserialize<BridgeRequest>(e.WebMessageAsJson); }
        catch { return; }
        if (req is null || !req.__kc) return;

        object? result = null;
        string? error = null;
        try { result = Dispatch(req.method, req.args); }
        catch (Exception ex) { error = ex.Message; }

        try
        {
            _web.CoreWebView2.PostWebMessageAsJson(
                JsonSerializer.Serialize(new BridgeReply(true, req.id, result, error)));
        }
        catch { }
    }

    private object? Dispatch(string method, JsonElement[]? args)
    {
        switch (method)
        {
            case "minimize_window":
                WindowState = FormWindowState.Minimized;
                return null;

            case "close_window":
                Close();
                return null;

            case "pick_folder":
                return PickFolder();

            case "toggle_maximize_window":
                ToggleMaximize(Arg(args, 0), Arg(args, 1), Arg(args, 2), Arg(args, 3));
                return null;

            case "start_native_resize":
                BeginNativeDrag(HTBOTTOMRIGHT);
                return null;

            case "__start_drag":
                BeginNativeDrag(HTCAPTION);
                return null;

            case "__start_resize":
                BeginNativeDrag(Arg(args, 0));
                return null;

            default:
                return null;
        }
    }

    private void BeginNativeDrag(int hitTest)
    {
        ReleaseCapture();
        PostMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)hitTest, IntPtr.Zero);
    }

    private string? PickFolder()
    {
        using var dlg = new FolderBrowserDialog { ShowNewFolderButton = true };
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null;
    }

    private void RestoreWindowState()
    {
        _restoreBounds = DefaultBounds();
        bool maximized = true;

        var saved = Settings.GetString("window_state");
        var p = saved?.Split(';');
        if (p is { Length: 5 }
            && int.TryParse(p[0], out var m)
            && int.TryParse(p[1], out var x) && int.TryParse(p[2], out var y)
            && int.TryParse(p[3], out var w) && int.TryParse(p[4], out var h))
        {
            var r = new Rectangle(x, y, w, h);
            if (r.Width >= MinimumSize.Width && r.Height >= MinimumSize.Height
                && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r)))
                _restoreBounds = r;
            maximized = m == 1;
        }

        StartPosition = FormStartPosition.Manual;
        _isMaximized = maximized;

        Bounds = maximized ? Screen.FromRectangle(_restoreBounds).WorkingArea : _restoreBounds;
    }

    private static Rectangle DefaultBounds()
    {
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        return new Rectangle(
            wa.X + (wa.Width - 1280) / 2, wa.Y + (wa.Height - 800) / 2, 1280, 800);
    }

    private void SaveWindowState()
    {
        try
        {
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;

            bool maximized = _isMaximized;
            if (maximized)
            {
                var wa = Screen.FromRectangle(bounds).WorkingArea;
                bool hugs = Math.Abs(bounds.X - wa.X) <= 8 && Math.Abs(bounds.Y - wa.Y) <= 8
                    && Math.Abs(bounds.Width - wa.Width) <= 16 && Math.Abs(bounds.Height - wa.Height) <= 16;
                if (!hugs) maximized = false;
            }

            var r = maximized ? _restoreBounds : bounds;
            Settings.SetString("window_state", $"{(maximized ? 1 : 0)};{r.X};{r.Y};{r.Width};{r.Height}");
        }
        catch { }
    }

    private void ToggleMaximize(int availW, int availH, int availLeft, int availTop)
    {
        if (_isMaximized)
        {
            Bounds = _restoreBounds;
            _isMaximized = false;
        }
        else
        {
            _restoreBounds = Bounds;
            Bounds = new Rectangle(availLeft, availTop, availW, availH);
            _isMaximized = true;
        }
    }

    private static int Arg(JsonElement[]? args, int i)
        => args is not null && i < args.Length && args[i].ValueKind == JsonValueKind.Number
            ? (int)args[i].GetDouble()
            : 0;

    private sealed record BridgeRequest(bool __kc, int id, string method, JsonElement[]? args);
    private sealed record BridgeReply(bool __kcReply, int id, object? result, string? error);

    private const string BridgeJs = """
(function () {
  if (window.__keepclip) return;
  window.__keepclip = true;

  var seq = 0, pending = {};
  function call(method, args) {
    return new Promise(function (resolve, reject) {
      var id = ++seq;
      pending[id] = { resolve: resolve, reject: reject };
      window.chrome.webview.postMessage({ __kc: true, id: id, method: method, args: args || [] });
    });
  }
  window.chrome.webview.addEventListener('message', function (e) {
    var d = e.data;
    if (!d || d.__kcReply !== true) return;
    var p = pending[d.id];
    if (!p) return;
    delete pending[d.id];
    if (d.error) p.reject(new Error(d.error)); else p.resolve(d.result);
  });

  window.pywebview = window.pywebview || {};
  window.pywebview.api = {
    pick_folder: function () { return call('pick_folder', []); },
    minimize_window: function () { return call('minimize_window', []); },
    close_window: function () { return call('close_window', []); },
    toggle_maximize_window: function (aw, ah, al, at) { return call('toggle_maximize_window', [aw, ah, al, at]); },
    start_native_resize: function () { return call('start_native_resize', []); }
  };

  document.addEventListener('mousedown', function (ev) {
    if (ev.button !== 0) return;
    var el = ev.target;
    while (el && el.nodeType === 1) {
      if (el.classList && el.classList.contains('pywebview-drag-region')) {
        ev.preventDefault();
        call('__start_drag', []);
        return;
      }
      el = el.parentElement;
    }
  }, true);

  function addGrips() {
    if (!document.body || document.querySelector('[data-keepclip-grips]')) return;
    var host = document.createElement('div');
    host.setAttribute('data-keepclip-grips', '');
    var defs = [
      ['top:0;left:12px;right:12px;height:6px;cursor:ns-resize', 12],
      ['bottom:0;left:12px;right:12px;height:6px;cursor:ns-resize', 15],
      ['left:0;top:12px;bottom:12px;width:6px;cursor:ew-resize', 10],
      ['right:0;top:12px;bottom:12px;width:6px;cursor:ew-resize', 11],
      ['top:0;left:0;width:12px;height:12px;cursor:nwse-resize', 13],
      ['top:0;right:0;width:12px;height:12px;cursor:nesw-resize', 14],
      ['bottom:0;left:0;width:12px;height:12px;cursor:nesw-resize', 16],
      ['bottom:0;right:0;width:12px;height:12px;cursor:nwse-resize', 17]
    ];
    defs.forEach(function (d) {
      var g = document.createElement('div');
      g.style.cssText = 'position:fixed;z-index:2147483647;background:transparent;' +
        'app-region:no-drag;-webkit-app-region:no-drag;' + d[0];
      g.addEventListener('mousedown', function (ev) {
        if (ev.button !== 0) return;
        ev.preventDefault();
        ev.stopPropagation();
        call('__start_resize', [d[1]]);
      }, true);
      host.appendChild(g);
    });
    document.body.appendChild(host);
  }

  function ready() {
    addGrips();
    try { window.dispatchEvent(new Event('pywebviewready')); } catch (e) {}
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', ready);
  else setTimeout(ready, 0);
})();
""";
}
