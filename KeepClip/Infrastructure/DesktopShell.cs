using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KeepClip.Infrastructure;

/// <summary>
/// Native desktop shell — the C# port of <c>main_desktop.py</c>'s pywebview window.
/// A frameless WinForms form hosts a WebView2 control pointed at the in-process ASP.NET
/// server, and a small injected script re-creates <c>window.pywebview.api</c>
/// (pick_folder / minimize / close / toggle_maximize) plus pywebview's two frameless
/// behaviours the CSS relies on: draggable <c>.pywebview-drag-region</c> elements and
/// edge/corner resizing. The window owns the process lifetime — closing it ends the
/// WinForms message loop, after which the caller stops the host and exits.
/// </summary>
internal static class DesktopShell
{
    /// <summary>Open the window and pump messages until it closes. Runs on its own STA thread.</summary>
    public static void Run(string baseUrl)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { /* older OS */ }
        using var form = new ShellForm(baseUrl);
        Application.Run(form);
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;

    /// <summary>Second-launch path (single-instance mutex already taken): bring the
    /// running copy's window to the front so the double-click still "did something".</summary>
    public static void FocusExistingInstance()
    {
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
        catch { /* best effort — the second copy exits either way */ }
    }
}

/// <summary>The frameless host window. Geometry mirrors pywebview: 1280×800, min 900×600.</summary>
internal sealed class ShellForm : Form
{
    // ---- Win32: kick off a native move/resize loop from a web mousedown, exactly like
    //      pywebview's WindowApi.start_native_resize (ReleaseCapture + WM_NCLBUTTONDOWN). ----
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;        // whole-window move (title bar drag)
    private const int HTBOTTOMRIGHT = 17;   // start_native_resize() target

    private static readonly Color AppBg = Color.FromArgb(0x07, 0x0A, 0x10); // --bg, avoids white flash

    private readonly string _baseUrl;
    private readonly WebView2 _web = new();

    // toggle_maximize_window restore state (mirrors WindowApi._is_maximized + _restore_*).
    private bool _isMaximized;
    private Rectangle _restoreBounds;

    public ShellForm(string baseUrl)
    {
        _baseUrl = baseUrl;

        Text = "KeepClip";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(900, 600);   // enforced by Windows during native resize
        BackColor = AppBg;
        ShowInTaskbar = true;
        TryLoadIcon();
        RestoreWindowState();               // last session's geometry; first run = maximized
        FormClosing += (_, _) => SaveWindowState();

        _web.Dock = DockStyle.Fill;
        _web.DefaultBackgroundColor = AppBg;   // applied before the page paints
        Controls.Add(_web);

        Load += async (_, _) => await InitWebViewAsync();

        // Instant-replay "save" hotkey: this form's handle hosts the system-wide
        // RegisterHotKey (fires even with a fullscreen game focused). The service's
        // notifier renders saves as an on-screen toast over the game.
        HandleCreated += (_, _) =>
        {
            HotkeyManager.Attach(this);
            ReplayService.Notifier = SafeToast;
        };
    }

    /// <summary>WM_HOTKEY → dump the replay ring. Fire-and-forget: the save runs on the
    /// thread pool so a multi-second concat never freezes the UI thread. The outcome
    /// shows as an on-screen toast (visible over the game), marshaled back here.</summary>
    protected override void WndProc(ref Message m)
    {
        if (HotkeyManager.HandleMessage(ref m))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // Success shows itself via ReplayService.Notifier (one toast for
                    // every save source); only the failure needs reporting here.
                    await ReplayService.SaveAsync("hotkey");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Powtórka (hotkey): {ex.Message}");
                    // A re-press while a save is still assembling is NOT a failure — stay
                    // silent (the earlier press will play the success cue when it finishes).
                    if (ReplayService.SaveInProgress) return;
                    ReplayService.PlayCue(ok: false);  // audible in-game even if the toast isn't visible
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
        catch { /* window torn down mid-save */ }
    }

    private void TryLoadIcon()
    {
        try
        {
            var ico = Path.Combine(Config.FrontendDir, "icons", "icon.ico");
            if (File.Exists(ico)) Icon = new Icon(ico);
        }
        catch { /* icon is cosmetic; never block startup on it */ }
    }

    private async Task InitWebViewAsync()
    {
        // Keep WebView2's cache/cookies under data/ so a clean uninstall (delete data/)
        // removes them too — no stray profile next to the exe.
        var userData = Path.Combine(Config.DataDir, "webview2");
        Directory.CreateDirectory(userData);

        // --force_high_performance_gpu: on dual-GPU machines Windows likes to hand the
        // WebView2 renderer the power-saving iGPU, whose presenter chokes on the app's own
        // high-fps recordings (a 165 fps 1440p clip played slow-mo/desynced in-app while the
        // same file was fine in a system player on the dGPU). PlatformHEVCDecoderSupport
        // enables hardware HEVC decode in Chromium — future-proofing for an HEVC capture
        // codec. Both flags are ignored gracefully where unsupported.
        var opts = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments =
                "--force_high_performance_gpu --enable-features=PlatformHEVCDecoderSupport",
        };
        var env = await CoreWebView2Environment.CreateAsync(null, userData, opts);
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;

        // App-shell feel without breaking the app: no link status bar, no back/forward
        // swipe. Context menus + devtools stay on (right-click paste in the segment
        // editor; inspection when something misbehaves).
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;

        // Frameless window dragging: let the page tag its title-bar strip with CSS
        // `app-region: drag` and have WebView2 run the window move natively. This is
        // reliable (including dragging onto another monitor), unlike the postMessage →
        // WM_NCLBUTTONDOWN fallback, which can stall because the web-content process
        // owns the mouse capture mid-drag. Guarded: older WebView2 runtimes lack the
        // setter and throw, in which case the JS bridge (__start_drag) still works.
        try { core.Settings.IsNonClientRegionSupportEnabled = true; }
        catch { /* older runtime → JS drag fallback remains active */ }

        // target=_blank / window.open → OS browser, not a dead child webview.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri) { UseShellExecute = true }); }
            catch { /* ignore unlaunchable URIs */ }
        };

        // The pywebview.api shim must exist before the page's own scripts run.
        await core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeJs);
        core.WebMessageReceived += OnWebMessage;

        _web.Source = new Uri(_baseUrl);
    }

    // ---- bridge: JS → host. Runs on the UI thread, so Win32 + dialogs are safe here. ----

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

        // Resolve/reject the JS-side promise.
        try
        {
            _web.CoreWebView2.PostWebMessageAsJson(
                JsonSerializer.Serialize(new BridgeReply(true, req.id, result, error)));
        }
        catch { /* page navigated away mid-call */ }
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

            case "start_native_resize":     // pywebview API parity (no caller in the current UI)
                BeginNativeDrag(HTBOTTOMRIGHT);
                return null;

            case "__start_drag":            // mousedown on a .pywebview-drag-region
                BeginNativeDrag(HTCAPTION);
                return null;

            case "__start_resize":          // mousedown on an injected edge/corner grip
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

    // ---- window-state persistence (geometry + maximized flag across sessions) ----

    /// <summary>
    /// Apply the previous session's geometry. No saved state (first run) starts
    /// maximized — the frameless flavor: bounds = the screen's WORK area, so the
    /// taskbar stays visible (real WindowState.Maximized would cover it). Saved
    /// bounds are validated against the current monitors (one may be unplugged)
    /// and the form's minimum size before being trusted.
    /// </summary>
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
        // Maximize onto the screen the window last lived on, not always the primary.
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
            // Minimized bounds are the off-screen -32000 placeholder; the form's own
            // RestoreBounds holds the real ones. (This form never uses Maximized.)
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;

            // Dragging/resizing a "maximized" frameless window never clears the flag —
            // if the bounds no longer hug a work area, what's on screen is the truth.
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
        catch { /* persisting geometry must never block app close */ }
    }

    /// <summary>Port of <c>toggle_maximize_window</c>: the frontend passes the work area
    /// (screen minus taskbar); we snap to it and remember the previous bounds to restore.</summary>
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

    /// <summary>
    /// Injected before page scripts. Recreates <c>window.pywebview.api</c> over WebView2's
    /// postMessage channel (with promise correlation), wires <c>.pywebview-drag-region</c>
    /// to a native window move, lays down thin edge/corner resize grips, and fires
    /// <c>pywebviewready</c> — the exact surface app.js + index.html expect.
    /// </summary>
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

  // Move the window by dragging any .pywebview-drag-region element.
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

  // Thin invisible grips along the window border → native edge/corner resize.
  // HT codes: TOP 12, BOTTOM 15, LEFT 10, RIGHT 11, and the four corners 13/14/16/17.
  function addGrips() {
    if (!document.body || document.querySelector('[data-keepclip-grips]')) return;
    var host = document.createElement('div');
    host.setAttribute('data-keepclip-grips', '');
    var defs = [
      ['top:0;left:10px;right:10px;height:6px;cursor:ns-resize', 12],
      ['bottom:0;left:10px;right:10px;height:6px;cursor:ns-resize', 15],
      ['left:0;top:10px;bottom:10px;width:6px;cursor:ew-resize', 10],
      ['right:0;top:10px;bottom:10px;width:6px;cursor:ew-resize', 11],
      ['top:0;left:0;width:10px;height:10px;cursor:nwse-resize', 13],
      ['top:0;right:0;width:10px;height:10px;cursor:nesw-resize', 14],
      ['bottom:0;left:0;width:10px;height:10px;cursor:nesw-resize', 16],
      ['bottom:0;right:0;width:10px;height:10px;cursor:nwse-resize', 17]
    ];
    defs.forEach(function (d) {
      var g = document.createElement('div');
      g.style.cssText = 'position:fixed;z-index:2147483647;background:transparent;' + d[0];
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
