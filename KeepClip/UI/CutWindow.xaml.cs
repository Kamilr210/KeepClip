using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KeepClip.Localization;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;

namespace KeepClip.UI;

public partial class CutWindow : Window
{
    private static readonly Geometry PlayGeo = Geometry.Parse("M 4,2 L 15,9 L 4,16 Z");
    private static readonly Geometry PauseGeo = Geometry.Parse("M 3.5,2.5 L 6.8,2.5 L 6.8,15.5 L 3.5,15.5 Z M 10.2,2.5 L 13.5,2.5 L 13.5,15.5 L 10.2,15.5 Z");
    private static readonly Geometry VolOnGeo = Geometry.Parse("M 2,6 L 6,6 L 10,2.5 L 10,13.5 L 6,10 L 2,10 Z M 12.5,5.2 A 4,4 0 0 1 12.5,10.8");
    private static readonly Geometry VolOffGeo = Geometry.Parse("M 2,6 L 6,6 L 10,2.5 L 10,13.5 L 6,10 L 2,10 Z M 12.5,5.5 L 16.5,10.5 M 16.5,5.5 L 12.5,10.5");

    private enum DragMode { None, Playhead, LeftTrim, RightTrim }

    private readonly Clip _clip;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly CancellationTokenSource _waveCts = new();
    private double[]? _wavePeaks;
    private double _duration = 1;
    private double _startSec;
    private double _endSec = 1;
    private double _playhead;
    private double _audioVolume = 1.0; // 0.0 .. 3.0
    private double _lastNonZeroVolume = 100.0;
    private bool _playing;
    private bool _suppressAudioSlider;
    private DragMode _dragMode = DragMode.None;
    private double? _selectedMb;
    public Dictionary<string, object?>? CutResult { get; private set; }

    public CutWindow(Clip clip)
    {
        InitializeComponent();
        _clip = clip;
        SourceNameText.Text = clip.Filename;
        VideoClipCaption.Text = clip.Filename;
        _duration = Math.Max(0.5, clip.Duration);
        _startSec = 0;
        _endSec = _duration;

        UpdateRulerLabels();
        UpdateLabels();
        UpdateAudioUi();

        if (!string.IsNullOrEmpty(clip.Filepath) && File.Exists(clip.Filepath))
        {
            PreviewMedia.Source = new Uri(clip.Filepath, UriKind.Absolute);
            PreviewMedia.Play();
            PreviewMedia.Pause();
            _ = LoadRealWaveformAsync(clip.Filepath);
        }

        _timer.Tick += (_, _) =>
        {
            if (!_playing || _dragMode == DragMode.Playhead) return;
            var pos = PreviewMedia.Position.TotalSeconds;
            if (pos >= _endSec)
            {
                PreviewMedia.Position = TimeSpan.FromSeconds(_startSec);
                pos = _startSec;
            }
            _playhead = Math.Clamp(pos, 0, _duration);
            TimeText.Text = $"{ClipCardItem.FormatDuration(_playhead)} / {ClipCardItem.FormatDuration(_duration)}";
            UpdateTimelineVisuals();
        };
        _timer.Start();
        Loaded += (_, _) => UpdateTimelineVisuals();
        Closed += (_, _) =>
        {
            _waveCts.Cancel();
            _waveCts.Dispose();
            _timer.Stop();
            PreviewMedia.Stop();
            PreviewMedia.Close();
        };
    }

    private async Task LoadRealWaveformAsync(string filepath)
    {
        var peaks = await Task.Run(() => Media.ExtractAudioWaveform(filepath, 180, _waveCts.Token));
        if (_waveCts.IsCancellationRequested || peaks is null) return;
        _wavePeaks = peaks;
        UpdateTimelineVisuals();
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
            if (AudioMixerPopup.IsOpen) AudioMixerPopup.IsOpen = false;
            else DialogResult = false;
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
            SeekTo(_playhead - 2);
        }
        else if (e.Key == Key.Right)
        {
            e.Handled = true;
            SeekTo(_playhead + 2);
        }
    }

    private void PreviewMedia_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (PreviewMedia.NaturalDuration.HasTimeSpan)
        {
            _duration = Math.Max(0.5, PreviewMedia.NaturalDuration.TimeSpan.TotalSeconds);
            _startSec = 0;
            _endSec = _duration;
            UpdateRulerLabels();
            UpdateLabels();
            UpdateTimelineVisuals();
        }
    }

    private void UpdateRulerLabels()
    {
        RulerT0.Text = "0:00";
        RulerT1.Text = ClipCardItem.FormatDuration(_duration * 0.25);
        RulerT2.Text = ClipCardItem.FormatDuration(_duration * 0.50);
        RulerT3.Text = ClipCardItem.FormatDuration(_duration);
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
            if (_playhead < _startSec || _playhead >= _endSec)
                SeekTo(_startSec);
            PreviewMedia.Play();
            _playing = true;
            PlayPauseIcon.Data = PauseGeo;
        }
    }

    private void StepBack_Click(object sender, RoutedEventArgs e) => SeekTo(_playhead - 2);
    private void StepFwd_Click(object sender, RoutedEventArgs e) => SeekTo(_playhead + 2);

    private void SeekTo(double seconds)
    {
        _playhead = Math.Clamp(seconds, 0, _duration);
        PreviewMedia.Position = TimeSpan.FromSeconds(_playhead);
        TimeText.Text = $"{ClipCardItem.FormatDuration(_playhead)} / {ClipCardItem.FormatDuration(_duration)}";
        UpdateTimelineVisuals();
    }

    // --- Interactive NLE Timeline Scrubbing & Trim Handles ---

    private double XToSeconds(double x)
    {
        double w = VideoTrackHost.ActualWidth;
        if (w <= 0) return 0;
        return Math.Clamp(x / w, 0, 1) * _duration;
    }

    private void Timeline_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_dragMode != DragMode.None) return;
        _dragMode = DragMode.Playhead;
        ((IInputElement)sender).CaptureMouse();
        SeekTo(XToSeconds(e.GetPosition(VideoTrackHost).X));
        e.Handled = true;
    }

    private void Timeline_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragMode != DragMode.Playhead) return;
        SeekTo(XToSeconds(e.GetPosition(VideoTrackHost).X));
    }

    private void Timeline_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragMode != DragMode.Playhead) return;
        _dragMode = DragMode.None;
        ((IInputElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    private void LeftHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragMode = DragMode.LeftTrim;
        LeftTrimHandle.CaptureMouse();
        e.Handled = true;
    }

    private void RightHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragMode = DragMode.RightTrim;
        RightTrimHandle.CaptureMouse();
        e.Handled = true;
    }

    private void TrimHandle_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragMode is not (DragMode.LeftTrim or DragMode.RightTrim)) return;
        double sec = XToSeconds(e.GetPosition(VideoTrackHost).X);
        if (_dragMode == DragMode.LeftTrim)
        {
            _startSec = Math.Clamp(sec, 0, Math.Max(0, _endSec - 0.5));
            UpdateLabels();
            SeekTo(_startSec);
        }
        else
        {
            _endSec = Math.Clamp(sec, Math.Min(_duration, _startSec + 0.5), _duration);
            UpdateLabels();
            SeekTo(_endSec);
        }
    }

    private void TrimHandle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragMode is not (DragMode.LeftTrim or DragMode.RightTrim)) return;
        _dragMode = DragMode.None;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    private void TimelineTrack_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTimelineVisuals();

    private void UpdateTimelineVisuals()
    {
        if (VideoTrackHost is null || VideoTrackHost.ActualWidth <= 0 || _duration <= 0) return;
        double w = VideoTrackHost.ActualWidth;
        double startRatio = Math.Clamp(_startSec / _duration, 0, 1);
        double endRatio = Math.Clamp(_endSec / _duration, 0, 1);
        double startX = startRatio * w;
        double endX = endRatio * w;
        double clipW = Math.Max(20, endX - startX);

        // Video Track V1
        Canvas.SetLeft(VideoActiveClip, startX);
        VideoActiveClip.Width = clipW;
        Canvas.SetLeft(LeftTrimHandle, Math.Clamp(startX, 0, Math.Max(0, w - 10)));
        Canvas.SetLeft(RightTrimHandle, Math.Clamp(endX - 10, 0, Math.Max(0, w - 10)));

        // Audio Track A1
        Canvas.SetLeft(AudioActiveClip, startX);
        AudioActiveClip.Width = clipW;
        RenderAudioWaveform(clipW, startRatio, endRatio);

        // Shared Playhead
        double playX = Math.Clamp(_playhead / _duration, 0, 1) * w;
        Canvas.SetLeft(PlayheadLine, Math.Clamp(playX - 1, 0, Math.Max(0, w - 2)));
        Canvas.SetLeft(PlayheadHead, Math.Clamp(playX - 5, 0, Math.Max(0, w - 10)));
    }

    private void RenderAudioWaveform(double clipWidth, double startRatio, double endRatio)
    {
        WaveformCanvas.Children.Clear();
        double innerW = Math.Max(8, clipWidth - 6);
        int barCount = Math.Clamp((int)(innerW / 4.5), 6, 150);
        double step = innerW / barCount;
        double maxH = 34;
        bool muted = _audioVolume <= 0.001;

        var barBrush = muted
            ? new SolidColorBrush(WpfColor.FromArgb(0x55, 0xFF, 0x5C, 0x75))
            : _audioVolume > 1.05
                ? new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xA5, 0x24))
                : new SolidColorBrush(WpfColor.FromRgb(0x43, 0xD1, 0x7A));

        double gainScale = muted ? 0.06 : Math.Clamp(_audioVolume, 0.08, 2.2);
        var peaks = _wavePeaks;

        for (int i = 0; i < barCount; i++)
        {
            double amp;
            if (peaks is { Length: > 0 })
            {
                double t0 = startRatio + ((double)i / barCount) * (endRatio - startRatio);
                double t1 = startRatio + ((double)(i + 1) / barCount) * (endRatio - startRatio);
                int p0 = Math.Clamp((int)(t0 * peaks.Length), 0, peaks.Length - 1);
                int p1 = Math.Clamp((int)Math.Ceiling(t1 * peaks.Length), p0 + 1, peaks.Length);
                double maxLocal = 0;
                for (int p = p0; p < p1; p++)
                    if (peaks[p] > maxLocal) maxLocal = peaks[p];
                amp = maxLocal;
            }
            else
            {
                // Subtle loading baseline while FFmpeg decodes real peaks
                amp = 0.14;
            }

            double h = Math.Clamp(amp * maxH * gainScale, 2.5, maxH);
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(2, step - 1.5),
                Height = h,
                RadiusX = 1,
                RadiusY = 1,
                Fill = barBrush
            };
            Canvas.SetLeft(rect, 3 + i * step);
            Canvas.SetTop(rect, (38 - h) / 2.0);
            WaveformCanvas.Children.Add(rect);
        }
    }

    // --- Slide-Out Audio Volume / Mute Controls ---

    private void AudioHeader_Click(object sender, MouseButtonEventArgs e)
    {
        AudioMixerPopup.IsOpen = !AudioMixerPopup.IsOpen;
    }

    private void AudioMixerToggle_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        AudioMixerPopup.IsOpen = !AudioMixerPopup.IsOpen;
    }

    private void AudioVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressAudioSlider || !IsLoaded) return;
        SetAudioPercent(e.NewValue);
    }

    private void AudioPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton btn && double.TryParse(btn.Tag as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            SetAudioPercent(pct);
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (_audioVolume <= 0.001)
            SetAudioPercent(_lastNonZeroVolume > 5 ? _lastNonZeroVolume : 100);
        else
            SetAudioPercent(0);
    }

    private void SetAudioPercent(double pct)
    {
        pct = Math.Clamp(Math.Round(pct), 0, 300);
        if (pct > 0) _lastNonZeroVolume = pct;
        _audioVolume = pct / 100.0;

        _suppressAudioSlider = true;
        AudioVolumeSlider.Value = pct;
        _suppressAudioSlider = false;

        PreviewMedia.IsMuted = _audioVolume <= 0.001;
        PreviewMedia.Volume = Math.Clamp(_audioVolume, 0.0, 1.0);
        UpdateAudioUi();
        UpdateTimelineVisuals();
    }

    private void UpdateAudioUi()
    {
        if (AudioPctBadge is null) return;
        int pct = (int)Math.Round(_audioVolume * 100);
        bool muted = pct == 0;
        bool boosted = pct > 100;

        var color = muted
            ? WpfColor.FromRgb(0xFF, 0x5C, 0x75)
            : boosted
                ? WpfColor.FromRgb(0xF5, 0xA5, 0x24)
                : WpfColor.FromRgb(0x43, 0xD1, 0x7A);
        var brush = new SolidColorBrush(color);

        AudioPctBadge.Text = muted ? "0% ▾" : $"{pct}% ▾";
        AudioPctBadge.Foreground = brush;
        AudioGainLabel.Text = muted ? Loc.T("cutOverlay.audioMuted") : $"{pct}%";
        AudioGainLabel.Foreground = brush;

        var geo = muted ? VolOffGeo : VolOnGeo;
        MuteIcon.Data = geo;
        MuteIcon.Stroke = brush;
        MuteIcon.Fill = brush;
        PopupMuteIcon.Data = geo;
        PopupMuteIcon.Stroke = brush;
        PopupMuteIcon.Fill = brush;

        AudioTrackBadge.Background = new SolidColorBrush(muted
            ? WpfColor.FromRgb(0x6E, 0x22, 0x32)
            : boosted
                ? WpfColor.FromRgb(0x8A, 0x5A, 0x12)
                : WpfColor.FromRgb(0x1F, 0x6E, 0x43));

        AudioActiveClip.Background = new SolidColorBrush(muted
            ? WpfColor.FromRgb(0x2B, 0x16, 0x1D)
            : boosted
                ? WpfColor.FromRgb(0x33, 0x26, 0x12)
                : WpfColor.FromRgb(0x14, 0x33, 0x25));
        AudioActiveClip.BorderBrush = new SolidColorBrush(muted
            ? WpfColor.FromRgb(0x8F, 0x32, 0x46)
            : boosted
                ? WpfColor.FromRgb(0xC9, 0x86, 0x1E)
                : WpfColor.FromRgb(0x2D, 0x8C, 0x56));
    }

    // --- Trim Bounds Helpers ---

    private void SetStartHere_Click(object sender, RoutedEventArgs e)
    {
        _startSec = Math.Clamp(_playhead, 0, Math.Max(0, _endSec - 0.5));
        UpdateLabels();
        UpdateTimelineVisuals();
    }

    private void SetEndHere_Click(object sender, RoutedEventArgs e)
    {
        _endSec = Math.Clamp(_playhead, Math.Min(_duration, _startSec + 0.5), _duration);
        UpdateLabels();
        UpdateTimelineVisuals();
    }

    private void UpdateLabels()
    {
        var len = Math.Max(0, _endSec - _startSec);
        VideoRangeBadge.Text = $"{ClipCardItem.FormatDuration(_startSec)} – {ClipCardItem.FormatDuration(_endSec)}";
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

    // --- Target Size Presets & Export ---

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn) return;
        foreach (var child in PresetsPanel.Children)
        {
            if (child is WpfButton b)
                b.Style = (Style)FindResource(b == btn ? "PrimaryButton" : "DarkButton");
        }

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
            foreach (var child in PresetsPanel.Children)
            {
                if (child is WpfButton b)
                    b.Style = (Style)FindResource("DarkButton");
            }
            UpdateLabels();
        }
    }

    private async void Go_Click(object sender, RoutedEventArgs e)
    {
        GoBtn.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        CutProgress.Visibility = Visibility.Visible;
        CutProgress.Value = 0;

        double start = _startSec;
        double end = _endSec;
        double? targetMb = _selectedMb;
        double audioVol = _audioVolume;

        PreviewMedia.Pause();
        _playing = false;
        PlayPauseIcon.Data = PlayGeo;

        var res = await Task.Run(() => App.ClipService.Cut(_clip.Id, start, end, targetMb, (p, _stage) =>
        {
            _ = Dispatcher.BeginInvoke(() => CutProgress.Value = p);
        }, audioVol));

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
