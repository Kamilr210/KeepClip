using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KeepClip.Localization;
using NAudio.CoreAudioApi;
using WpfColor = System.Windows.Media.Color;

namespace KeepClip.UI;

public partial class ReplayView : System.Windows.Controls.UserControl
{
    private sealed record AudioChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }
    private sealed record ComboChoice(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public event Action? CloseRequested;

    public ReplayView()
    {
        InitializeComponent();
        _pollTimer.Tick += (_, _) => RefreshStatus();
    }

    public void Open()
    {
        DurationCombo.ItemsSource = new[]
        {
            new ComboChoice("30", "30 s"),
            new ComboChoice("60", "1 min"),
            new ComboChoice("120", "2 min"),
            new ComboChoice("180", "3 min"),
            new ComboChoice("300", "5 min"),
        };
        DurationCombo.DisplayMemberPath = "Label";
        DurationCombo.SelectedValuePath = "Value";

        FpsCombo.ItemsSource = new[]
        {
            new ComboChoice("30", "30 FPS"),
            new ComboChoice("60", "60 FPS"),
            new ComboChoice("90", "90 FPS"),
        };
        FpsCombo.DisplayMemberPath = "Label";
        FpsCombo.SelectedValuePath = "Value";

        QualityCombo.ItemsSource = new[]
        {
            new ComboChoice("low", Loc.T("replay.qLow")),
            new ComboChoice("medium", Loc.T("replay.qMedium")),
            new ComboChoice("high", Loc.T("replay.qHigh")),
        };
        QualityCombo.DisplayMemberPath = "Label";
        QualityCombo.SelectedValuePath = "Value";

        LoadAudioDevices();
        LoadValues();
        RefreshStatus();
        _pollTimer.Start();
    }

    public void StopPolling() => _pollTimer.Stop();

    private void LoadAudioDevices()
    {
        var outputs = new List<AudioChoice> { new("", Loc.T("replay.audioDefault")) };
        var inputs = new List<AudioChoice> { new("", Loc.T("replay.audioDefault")) };
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                outputs.Add(new AudioChoice(d.ID, d.FriendlyName));
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                inputs.Add(new AudioChoice(d.ID, d.FriendlyName));
        }
        catch { }
        AudioOutputCombo.ItemsSource = outputs;
        AudioInputCombo.ItemsSource = inputs;
    }

    private void LoadValues()
    {
        EnabledBox.IsChecked = ReplayService.Enabled;
        BackgroundBox.IsChecked = ReplayService.BackgroundEnabled;
        MicBox.IsChecked = ReplayService.MicEnabled;
        DurationCombo.SelectedValue = ReplayService.DurationS.ToString();
        FpsCombo.SelectedValue = ReplayService.Fps.ToString();
        QualityCombo.SelectedValue = ReplayService.Quality;
        HotkeyBox.Text = ReplayService.Hotkey;
        AudioOutputCombo.SelectedValue = ReplayService.AudioOutputId ?? "";
        AudioInputCombo.SelectedValue = ReplayService.AudioInputId ?? "";
    }

    private void RefreshStatus()
    {
        var status = (Dictionary<string, object?>)ReplayService.Status();
        bool enabled = status["enabled"] is true;
        bool running = status["running"] is true;
        bool saving = status["saving"] is true;
        bool hotkeyActive = status["hotkey_active"] is true;
        var enc = status["encoder"] as string;
        var err = status["error"] as string;

        SaveNowBtn.IsEnabled = running && !saving;

        if (!enabled)
        {
            StatusDot.Fill = new SolidColorBrush(WpfColor.FromRgb(0x8F, 0x8F, 0x8F));
            StatusText.Text = Loc.T("replay.stOff");
        }
        else if (saving)
        {
            StatusDot.Fill = new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xA5, 0x24));
            StatusText.Text = Loc.T("replay.stSaving");
        }
        else if (running)
        {
            StatusDot.Fill = new SolidColorBrush(WpfColor.FromRgb(0x43, 0xD1, 0x7A));
            StatusText.Text = Loc.Format("replay.stOn", ("enc", enc ?? "FFmpeg"));
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xA5, 0x24));
            StatusText.Text = Loc.T("replay.stStarting");
        }

        HotkeyWarnText.Text = Loc.T("replay.stHotkeyDead");
        HotkeyWarnText.Visibility = enabled && !hotkeyActive ? Visibility.Visible : Visibility.Collapsed;

        ErrorText.Text = err ?? "";
        ErrorText.Visibility = !string.IsNullOrWhiteSpace(err) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None)
            return;

        var parts = new List<string>();
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (parts.Count == 0) return;

        parts.Add(key.ToString());
        var combo = string.Join("+", parts);
        if (HotkeyManager.TryParse(combo, out _, out _))
            HotkeyBox.Text = combo;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(DurationCombo.SelectedValue as string, out int dur) || dur is < 15 or > 600)
        {
            ErrorText.Text = Strings.Get("replay.badDuration");
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        if (!int.TryParse(FpsCombo.SelectedValue as string, out int fps) || fps is not (30 or 60 or 90))
        {
            ErrorText.Text = Strings.Get("replay.badFps");
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        var quality = QualityCombo.SelectedValue as string ?? "high";
        var hotkey = HotkeyBox.Text.Trim();
        if (!HotkeyManager.TryParse(hotkey, out _, out _))
        {
            ErrorText.Text = Strings.Get("replay.badHotkey");
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Settings.SetString("replay_enabled", EnabledBox.IsChecked == true ? "1" : "0");
        Settings.SetString("replay_background", BackgroundBox.IsChecked == true ? "1" : "0");
        Settings.SetString("replay_mic", MicBox.IsChecked == true ? "1" : "0");
        Settings.SetString("replay_duration_s", dur.ToString());
        Settings.SetString("replay_fps", fps.ToString());
        Settings.SetString("replay_quality", quality);
        Settings.SetString("replay_hotkey", hotkey);
        Settings.SetString("replay_audio_output", AudioOutputCombo.SelectedValue as string ?? "");
        Settings.SetString("replay_audio_input", AudioInputCombo.SelectedValue as string ?? "");

        ReplayService.ApplyConfig();
        HotkeyManager.Refresh();
        RefreshStatus();
        ReplayToast.Display(true, Loc.T("replay.title"), Loc.T("replay.cfgSaved"));
    }

    private async void SaveNow_Click(object sender, RoutedEventArgs e)
    {
        SaveNowBtn.IsEnabled = false;
        try
        {
            var res = await ReplayService.SaveAsync("ui");
            var file = res.GetValueOrDefault("filename") as string ?? "";
            ReplayToast.Display(true, Strings.Get("replay.savedTitle"), file);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            RefreshStatus();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        StopPolling();
        CloseRequested?.Invoke();
    }
}

