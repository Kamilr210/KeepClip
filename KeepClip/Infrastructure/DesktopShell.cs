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

    // Nazwane zdarzenie pozwala drugiemu uruchomieniu przywrócić okno ukryte w zasobniku.
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
    // Rozpoczyna natywne przesuwanie lub skalowanie okna po zdarzeniu myszy z WebView2.
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

    // W trybie nagrywania w tle zamknięcie okna ukrywa aplikację w zasobniku.
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
            // Zamknięcie przez system lub opcję „Zakończ” musi naprawdę zakończyć proces.
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

        // Uchwyt formularza odbiera globalny skrót także wtedy, gdy gra jest na pełnym ekranie.
        HandleCreated += (_, _) =>
        {
            HotkeyManager.Attach(this);
            ReplayService.Notifier = SafeToast;
        };
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
                    // Ponowne naciśnięcie podczas zapisu nie jest błędem użytkownika.
                    if (ReplayService.SaveInProgress) return;
                    ReplayService.PlayCue(ok: false);
                    SafeToast(false, "Nie udało się zapisać powtórki", ex.Message);
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

    // Powiadomienia balonowe są wyłączone, ponieważ potrafią minimalizować gry pełnoekranowe.
    private void InitTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Otwórz KeepClip", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Zakończ i zatrzymaj nagrywanie", null, (_, _) =>
        {
            _exitRequested = true;
            Close();
        });

        _tray = new NotifyIcon
        {
            Text = "KeepClip — nagrywanie w tle",
            Icon = Icon ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = false,
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        // Jawne zwolnienie zapobiega pozostawieniu nieaktywnej ikony w zasobniku.
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
        if (_tray is not null) _tray.Visible = true;
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
        // Profil WebView2 trafia do data, aby deinstalator mógł usunąć go razem z aplikacją.
        var userData = Path.Combine(Config.DataDir, "webview2");
        Directory.CreateDirectory(userData);

        // WebView2 domyślnie korzysta z akceleracji. Wymuszanie konkretnego GPU potrafi powodować
        // migotanie sprzętowego kursora na komputerach z kilkoma układami graficznymi.
        var opts = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--enable-features=PlatformHEVCDecoderSupport",
        };
        var env = await CoreWebView2Environment.CreateAsync(null, userData, opts);
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;

        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;

        // Starsze WebView2 nie obsługuje regionów natywnych; wtedy pozostaje most JS.
        try { core.Settings.IsNonClientRegionSupportEnabled = true; }
        catch { }

        // Nowe okna otwiera w przeglądarce systemowej zamiast w pustym podrzędnym WebView2.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri) { UseShellExecute = true }); }
            catch { }
        };

        // Most musi istnieć przed uruchomieniem skryptów strony.
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

    // Zapisane położenie jest akceptowane tylko wtedy, gdy przecina aktualnie podłączony ekran.
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
        // Maksymalizuje na ekranie, na którym okno znajdowało się poprzednio.
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
            // Dla okna zminimalizowanego rzeczywiste wymiary są dostępne w RestoreBounds.
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;

            // Zmiana rozmiaru okna bez ramki nie zeruje flagi maksymalizacji, więc weryfikuje granice.
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

    // Wstrzyknięty most odtwarza interfejs pywebview używany przez warstwę widoku.
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

  // Przeciąganie oznaczonego obszaru uruchamia natywne przesuwanie okna.
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

  // Niewidoczne uchwyty używają kodów HT Win32 do skalowania każdej krawędzi.
  // Wyłączenie przeciągania jest konieczne u góry, bo natywny pasek przechwytuje mysz.
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
