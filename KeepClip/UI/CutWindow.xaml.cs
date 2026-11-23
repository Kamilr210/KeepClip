using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using KeepClip.Localization;
using WpfButton = System.Windows.Controls.Button;

namespace KeepClip.UI;

public partial class CutWindow : Window
{
    private readonly Clip _clip;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private double _duration = 1;
    private bool _playing;
    private bool _suppressSeek;
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
            if (!_playing) return;
            var pos = PreviewMedia.Position.TotalSeconds;
            if (pos >= EndSlider.Value)
            {
                PreviewMedia.Position = TimeSpan.FromSeconds(StartSlider.Value);
                pos = StartSlider.Value;
            }
            _suppressSeek = true;
            SeekSlider.Value = pos;
            _suppressSeek = false;
            TimeText.Text = $"{ClipCardItem.FormatDuration(pos)} / {ClipCardItem.FormatDuration(_duration)}";
        };
        _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            PreviewMedia.Stop();
            PreviewMedia.Close();
        };
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
        }
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_playing)
        {
            PreviewMedia.Pause();
            _playing = false;
            PlayPauseBtn.Content = "▶";
        }
        else
        {
            if (PreviewMedia.Position.TotalSeconds < StartSlider.Value || PreviewMedia.Position.TotalSeconds >= EndSlider.Value)
                PreviewMedia.Position = TimeSpan.FromSeconds(StartSlider.Value);
            PreviewMedia.Play();
            _playing = true;
            PlayPauseBtn.Content = "⏸";
        }
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSeek) return;
        PreviewMedia.Position = TimeSpan.FromSeconds(e.NewValue);
        TimeText.Text = $"{ClipCardItem.FormatDuration(e.NewValue)} / {ClipCardItem.FormatDuration(_duration)}";
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

