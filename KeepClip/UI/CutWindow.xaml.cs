using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KeepClip.Localization;
using WpfButton = System.Windows.Controls.Button;

namespace KeepClip.UI;

public partial class CutWindow : Window
{
    private static readonly Geometry PlayGeo = Geometry.Parse("M 4,2 L 15,9 L 4,16 Z");
    private static readonly Geometry PauseGeo = Geometry.Parse("M 3.5,2.5 L 6.8,2.5 L 6.8,15.5 L 3.5,15.5 Z M 10.2,2.5 L 13.5,2.5 L 13.5,15.5 L 10.2,15.5 Z");
    private static readonly Geometry VolOnGeo = Geometry.Parse("M 2,6 L 6,6 L 10,2.5 L 10,13.5 L 6,10 L 2,10 Z M 12.5,5.2 A 4,4 0 0 1 12.5,10.8");
    private static readonly Geometry VolOffGeo = Geometry.Parse("M 2,6 L 6,6 L 10,2.5 L 10,13.5 L 6,10 L 2,10 Z M 12.5,5.5 L 16.5,10.5 M 16.5,5.5 L 12.5,10.5");

    private readonly Clip _clip;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private double _duration = 1;
    private bool _playing;
    private bool _muted;
    private bool _suppressSeek;
    private bool _isDraggingSeek;
    private double? _selectedMb;
    public Dictionary<string, object?>? CutResult { get; private set; }

    public CutWindow(Clip clip)
    {
        InitializeComponent();
        _clip = clip;
        SourceNameText.Text = clip.Filename;
        _duration = Math.Max(0.5, clip.Duration);

        StartSlider.Maximum = _duration;
        EndSlider.Maximum = _duration;
        EndSlider.Value = _duration;
        SeekSlider.Maximum = _duration;
        UpdateLabels();

        if (!string.IsNullOrEmpty(clip.Filepath) && File.Exists(clip.Filepath))
        {
            PreviewMedia.Source = new Uri(clip.Filepath, UriKind.Absolute);
            PreviewMedia.Play();
            PreviewMedia.Pause();
        }

        _timer.Tick += (_, _) =>
        {
            if (!_playing || _isDraggingSeek) return;
            var pos = PreviewMedia.Position.TotalSeconds;
            if (pos >= EndSlider.Value)
            {
                PreviewMedia.Position = TimeSpan.FromSeconds(StartSlider.Value);
                pos = StartSlider.Value;
            }
            _suppressSeek = true;
            SeekSlider.Value = Math.Clamp(pos, 0, _duration);
            _suppressSeek = false;
            TimeText.Text = $"{ClipCardItem.FormatDuration(pos)} / {ClipCardItem.FormatDuration(_duration)}";
            UpdateRangeVisuals(pos);
        };
        _timer.Start();
        Loaded += (_, _) => UpdateRangeVisuals(0);
        Closed += (_, _) =>
        {
            _timer.Stop();
            PreviewMedia.Stop();
            PreviewMedia.Close();
        };
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
            return;
        }
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            TogglePlayPause();
        }
        else if (e.Key == Key.Left)
        {
            e.Handled = true;
            SeekTo(PreviewMedia.Position.TotalSeconds - 2);
        }
        else if (e.Key == Key.Right)
        {
            e.Handled = true;
            SeekTo(PreviewMedia.Position.TotalSeconds + 2);
        }
    }

    private void PreviewMedia_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (PreviewMedia.NaturalDuration.HasTimeSpan)
        {
            _duration = Math.Max(0.5, PreviewMedia.NaturalDuration.TimeSpan.TotalSeconds);
            StartSlider.Maximum = _duration;
            EndSlider.Maximum = _duration;
            EndSlider.Value = _duration;
            SeekSlider.Maximum = _duration;
            UpdateLabels();
            UpdateRangeVisuals(0);
        }
    }

    private void PreviewSurface_Click(object sender, MouseButtonEventArgs e) => TogglePlayPause();
    private void PlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayPause();

    private void TogglePlayPause()
    {
        if (_playing)
        {
            PreviewMedia.Pause();
            _playing = false;
            PlayPauseIcon.Data = PlayGeo;
        }
        else
        {
            if (PreviewMedia.Position.TotalSeconds < StartSlider.Value || PreviewMedia.Position.TotalSeconds >= EndSlider.Value)
                PreviewMedia.Position = TimeSpan.FromSeconds(StartSlider.Value);
            PreviewMedia.Play();
            _playing = true;
            PlayPauseIcon.Data = PauseGeo;
        }
    }

    private void StepBack_Click(object sender, RoutedEventArgs e) => SeekTo(PreviewMedia.Position.TotalSeconds - 2);
    private void StepFwd_Click(object sender, RoutedEventArgs e) => SeekTo(PreviewMedia.Position.TotalSeconds + 2);

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _muted = !_muted;
        PreviewMedia.IsMuted = _muted;
        MuteIcon.Data = _muted ? VolOffGeo : VolOnGeo;
    }

    private void SeekTo(double seconds)
    {
        seconds = Math.Clamp(seconds, 0, _duration);
        PreviewMedia.Position = TimeSpan.FromSeconds(seconds);
        _suppressSeek = true;
        SeekSlider.Value = seconds;
        _suppressSeek = false;
        TimeText.Text = $"{ClipCardItem.FormatDuration(seconds)} / {ClipCardItem.FormatDuration(_duration)}";
        UpdateRangeVisuals(seconds);
    }

    private void SeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _isDraggingSeek = true;

    private void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSeek = false;
        SeekTo(SeekSlider.Value);
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSeek) return;
        PreviewMedia.Position = TimeSpan.FromSeconds(e.NewValue);
        TimeText.Text = $"{ClipCardItem.FormatDuration(e.NewValue)} / {ClipCardItem.FormatDuration(_duration)}";
        UpdateRangeVisuals(e.NewValue);
    }

    private void RangeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        if (StartSlider.Value > EndSlider.Value - 0.5)
        {
            if (sender == StartSlider) StartSlider.Value = Math.Max(0, EndSlider.Value - 0.5);
            else EndSlider.Value = Math.Min(_duration, StartSlider.Value + 0.5);
        }
        UpdateLabels();
        UpdateRangeVisuals(SeekSlider.Value);
    }

    private void SetStartHere_Click(object sender, RoutedEventArgs e)
    {
        var cur = SeekSlider.Value;
        StartSlider.Value = Math.Clamp(cur, 0, Math.Max(0, EndSlider.Value - 0.5));
        UpdateLabels();
        UpdateRangeVisuals(cur);
    }

    private void SetEndHere_Click(object sender, RoutedEventArgs e)
    {
        var cur = SeekSlider.Value;
        EndSlider.Value = Math.Clamp(cur, Math.Min(_duration, StartSlider.Value + 0.5), _duration);
        UpdateLabels();
        UpdateRangeVisuals(cur);
    }

    private void RangeTrackGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateRangeVisuals(SeekSlider.Value);

    private void UpdateRangeVisuals(double playheadSeconds)
    {
        if (RangeTrackGrid is null || RangeTrackGrid.ActualWidth <= 0 || _duration <= 0) return;
        double w = RangeTrackGrid.ActualWidth;
        double startX = Math.Clamp(StartSlider.Value / _duration, 0, 1) * w;
        double endX = Math.Clamp(EndSlider.Value / _duration, 0, 1) * w;
        Canvas.SetLeft(RangeFillBar, startX);
        RangeFillBar.Width = Math.Max(4, endX - startX);

        double playX = Math.Clamp(playheadSeconds / _duration, 0, 1) * w;
        Canvas.SetLeft(PlayheadTick, Math.Clamp(playX - 1.25, 0, Math.Max(0, w - 2.5)));
    }

    private void UpdateLabels()
    {
        var start = StartSlider.Value;
        var end = EndSlider.Value;
        var len = Math.Max(0, end - start);
        StartLabel.Text = ClipCardItem.FormatDuration(start);
        EndLabel.Text = ClipCardItem.FormatDuration(end);
        DurationLabel.Text = $"{Loc.T("cutOverlay.durationLabel")} {len:0.0} s";
        if (_selectedMb is > 0)
        {
            var kbps = (_selectedMb.Value * 8192.0) / Math.Max(1.0, len);
            EstimateText.Text = $"~{kbps:0} kbps · {_selectedMb.Value:0} MB";
        }
        else
        {
            EstimateText.Text = Loc.T("cutOverlay.noComprBtn");
        }
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn) return;
        foreach (WpfButton b in PresetsPanel.Children)
            b.Style = (Style)FindResource(b == btn ? "PrimaryButton" : "DarkButton");

        var tag = btn.Tag as string;
        if (double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var mb))
        {
            _selectedMb = mb;
            CustomMbBox.Text = "";
        }
        else
        {
            _selectedMb = null;
        }
        UpdateLabels();
    }

    private void CustomMbBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (double.TryParse(CustomMbBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var mb) && mb > 0)
        {
            _selectedMb = mb;
            foreach (WpfButton b in PresetsPanel.Children)
                b.Style = (Style)FindResource("DarkButton");
            UpdateLabels();
        }
    }

    private async void Go_Click(object sender, RoutedEventArgs e)
    {
        GoBtn.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        CutProgress.Visibility = Visibility.Visible;
        CutProgress.Value = 0;

        double start = StartSlider.Value;
        double end = EndSlider.Value;
        double? targetMb = _selectedMb;

        PreviewMedia.Pause();
        _playing = false;
        PlayPauseIcon.Data = PlayGeo;

        var res = await Task.Run(() => App.ClipService.Cut(_clip.Id, start, end, targetMb, (p, _) =>
        {
            Dispatcher.BeginInvoke(() => CutProgress.Value = p);
        }));

        if (res.IsSuccess)
        {
            CutResult = res.Value;
            DialogResult = true;
        }
        else
        {
            CutProgress.Visibility = Visibility.Collapsed;
            ErrorText.Text = res.Error ?? "Cut failed";
            ErrorText.Visibility = Visibility.Visible;
            GoBtn.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
