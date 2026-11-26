using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeepClip.Localization;
using WpfButton = System.Windows.Controls.Button;

namespace KeepClip.UI;

public partial class PlayerView : System.Windows.Controls.UserControl
{
    private static readonly Geometry PlayGeo = Geometry.Parse("M 4,2 L 15,9 L 4,16 Z");
    private static readonly Geometry PauseGeo = Geometry.Parse("M 3.5,2.5 L 6.8,2.5 L 6.8,15.5 L 3.5,15.5 Z M 10.2,2.5 L 13.5,2.5 L 13.5,15.5 L 10.2,15.5 Z");
    private static readonly Geometry VolHighGeo = Geometry.Parse("M 2,6 L 6,6 L 10,2.5 L 10,13.5 L 6,10 L 2,10 Z M 12.5,5.2 A 4,4 0 0 1 12.5,10.8 M 14.5,3.2 A 7,7 0 0 1 14.5,12.8");
    private static readonly Geometry VolLowGeo = Geometry.Parse("M 2,6 L 6,6 L 10,2.5 L 10,13.5 L 6,10 L 2,10 Z M 12.5,5.5 A 3.6,3.6 0 0 1 12.5,10.5");
    private static readonly Geometry VolMuteGeo = Geometry.Parse("M 2,6 L 6,6 L 10,2.5 L 10,13.5 L 6,10 L 2,10 Z M 12.5,5.5 L 16.5,10.5 M 16.5,5.5 L 12.5,10.5");
    private static readonly Geometry EnterFsGeo = Geometry.Parse("M 2,6 L 2,2 L 6,2 M 12,2 L 16,2 L 16,6 M 16,12 L 16,16 L 12,16 M 6,16 L 2,16 L 2,12");
    private static readonly Geometry ExitFsGeo = Geometry.Parse("M 6,2 L 6,6 L 2,6 M 12,2 L 12,6 L 16,6 M 16,12 L 12,12 L 12,16 M 2,12 L 6,12 L 6,16");

    private static readonly double[] Speeds = { 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };

    private Clip? _clip;
    private double? _seekOnOpen;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _hideControlsTimer = new() { Interval = TimeSpan.FromSeconds(2.6) };
    private readonly DispatcherTimer _singleClickTimer = new() { Interval = TimeSpan.FromMilliseconds(210) };

    private double _speed = 1.0;
    private bool _playing;
    private bool _muted;
    private bool _ccEnabled = true;
    private bool _suppressSeek;
    private bool _isDraggingSeek;
    private bool _isHoveringControls;
    private bool _controlsVisible = true;
    private bool _isTheater;
    private bool _isFullscreen;
    private bool _isBusy;
    private double _duration = 1;
    private List<Dictionary<string, object?>> _segments = new();
    private readonly List<(double Start, double End, Border RowBorder)> _segmentRows = new();
    private CancellationTokenSource? _proxyCts;

    public event Action<bool>? Closed;
    public event Action<bool>? FullscreenChanged;
    public bool LibraryChanged { get; private set; }
    public bool IsFullscreen => _isFullscreen;
    public bool IsBusy => _isBusy;

    public PlayerView()
    {
        InitializeComponent();
        _timer.Tick += Timer_Tick;
        _hideControlsTimer.Tick += (_, _) =>
        {
            _hideControlsTimer.Stop();
            if (_playing && !_isHoveringControls && !_isDraggingSeek)
                SetControlsVisible(false);
        };
        _singleClickTimer.Tick += (_, _) =>
        {
            _singleClickTimer.Stop();
            TogglePlayPause(showFeedback: true);
        };
    }

    public void Open(Clip clip, double? seekSeconds = null)
    {
        CloseInternal(notify: false);
        _clip = clip;
        _seekOnOpen = seekSeconds;
        LibraryChanged = false;
        _speed = 1.0;
        SpeedBtnText.Text = "1×";
        _isTheater = false;
        SetFullscreen(false);
        ApplyLayoutMode();

        var gameLabel = clip.Game == "_root" ? Loc.T("search.allGames") : clip.Game;
        GameText.Text = gameLabel;
        FileText.Text = clip.Filename;
        FsGameText.Text = gameLabel;
        FsFileText.Text = clip.Filename;
        if (!string.IsNullOrWhiteSpace(clip.Filepath))
        {
            FilePathBtn.Content = clip.Filepath;
            FilePathBtn.Visibility = Visibility.Visible;
        }
        else
        {
            FilePathBtn.Content = "";
            FilePathBtn.Visibility = Visibility.Collapsed;
        }

        _duration = Math.Max(0.5, clip.Duration);
        SeekSlider.Maximum = _duration;
        TotalTimeText.Text = ClipCardItem.FormatDuration(_duration);
        CurrentTimeText.Text = "0:00";

        UpdateFavButton();
        UpdateCloudButton();
        UpdateVolumeIcon();
        LoadSegments();
        LoadDemoStatus();
        SetControlsVisible(true);
        _timer.Start();
        _ = InitVideoSourceAsync();
    }

    public void ClosePlayer() => CloseInternal(notify: true);

    private void CloseInternal(bool notify)
    {
        _proxyCts?.Cancel();
        _timer.Stop();
        _hideControlsTimer.Stop();
        _singleClickTimer.Stop();
        if (_isFullscreen)
            SetFullscreen(false);
        _playing = false;
        _isBusy = false;
        UpdatePlayPauseUi();
        VideoMedia.Stop();
        VideoMedia.Close();
        VideoMedia.Source = null;
        if (notify) Closed?.Invoke(LibraryChanged);
    }

    private Window? OwnerWindow => Window.GetWindow(this);

    private async Task InitVideoSourceAsync()
    {
        if (_clip is null) return;
        if (_clip.Storage == "cloud")
        {
            if (!string.IsNullOrEmpty(_clip.RemoteId))
            {
                VideoMedia.Source = App.CloudProxy.GetStreamUri(_clip.RemoteId);
                Play();
            }
            return;
        }

        var status = App.Playback.GetOrStart(_clip, start: true);
        if (status.ready && !string.IsNullOrEmpty(_clip.Filepath))
        {
            var path = status.proxy && App.Playback.TryGetReadyPath(_clip, out var p) ? p : _clip.Filepath;
            VideoMedia.Source = new Uri(path, UriKind.Absolute);
            Play();
            return;
        }

        if (status.preparing)
        {
            _proxyCts = new CancellationTokenSource();
            PreparingOverlay.Visibility = Visibility.Visible;
            PreparingDetail.Text = Loc.Format("player.preparingNotice", ("fps", $"{status.source_fps:0}"));
            while (!_proxyCts.IsCancellationRequested && _clip is not null)
            {
                try { await Task.Delay(300, _proxyCts.Token).ConfigureAwait(true); }
                catch (OperationCanceledException) { break; }
                var st = App.Playback.GetOrStart(_clip, start: false);
                PreparingBar.Value = st.progress;
                PreparingPct.Text = $"{(int)(st.progress * 100)}%";
                if (st.ready)
                {
                    PreparingOverlay.Visibility = Visibility.Collapsed;
                    if (App.Playback.TryGetReadyPath(_clip, out var readyPath))
                    {
                        VideoMedia.Source = new Uri(readyPath, UriKind.Absolute);
                        Play();
                    }
                    break;
                }
                if (st.error is not null)
                {
                    PreparingOverlay.Visibility = Visibility.Collapsed;
                    if (!string.IsNullOrEmpty(_clip.Filepath) && File.Exists(_clip.Filepath))
                    {
                        VideoMedia.Source = new Uri(_clip.Filepath, UriKind.Absolute);
                        Play();
                    }
                    break;
                }
            }
        }
        else if (!string.IsNullOrEmpty(_clip.Filepath) && File.Exists(_clip.Filepath))
        {
            VideoMedia.Source = new Uri(_clip.Filepath, UriKind.Absolute);
            Play();
        }
    }

    private void VideoMedia_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (VideoMedia.NaturalDuration.HasTimeSpan)
        {
            _duration = Math.Max(0.5, VideoMedia.NaturalDuration.TimeSpan.TotalSeconds);
            SeekSlider.Maximum = _duration;
            TotalTimeText.Text = ClipCardItem.FormatDuration(_duration);
        }
        if (_seekOnOpen is > 0)
            SeekTo(_seekOnOpen.Value);

        if (_playing && !_isBusy)
        {
            VideoMedia.Play();
            VideoMedia.SpeedRatio = _speed;
        }
        else
        {
            _playing = false;
            VideoMedia.Pause();
            UpdatePlayPauseUi();
        }
    }

    private void VideoMedia_MediaEnded(object sender, RoutedEventArgs e)
    {
        _playing = false;
        UpdatePlayPauseUi();
        SetControlsVisible(true);
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_playing || _isDraggingSeek) return;
        var pos = VideoMedia.Position.TotalSeconds;
        _suppressSeek = true;
        SeekSlider.Value = Math.Clamp(pos, 0, _duration);
        _suppressSeek = false;
        CurrentTimeText.Text = ClipCardItem.FormatDuration(pos);
        UpdateSubtitle(pos);
        HighlightActiveSegment(pos);
    }

    private void UpdateSubtitle(double pos)
    {
        if (!_ccEnabled || _segments.Count == 0)
        {
            SubtitleOverlay.Visibility = Visibility.Collapsed;
            return;
        }
        foreach (var seg in _segments)
        {
            double s = Convert.ToDouble(seg["start_s"] ?? 0.0);
            double e = Convert.ToDouble(seg["end_s"] ?? 0.0);
            if (pos >= s && pos <= e)
            {
                SubtitleText.Text = (seg["text"] as string ?? "").Trim();
                SubtitleOverlay.Visibility = string.IsNullOrEmpty(SubtitleText.Text) ? Visibility.Collapsed : Visibility.Visible;
                return;
            }
        }
        SubtitleOverlay.Visibility = Visibility.Collapsed;
    }

    private void HighlightActiveSegment(double pos)
    {
        if (_segmentRows.Count == 0) return;
        var activeBg = (System.Windows.Media.Brush)FindResource("AccentPanelBrush");
        foreach (var (s, e, bd) in _segmentRows)
        {
            bool active = pos >= s && pos <= e;
            bd.Background = active ? activeBg : System.Windows.Media.Brushes.Transparent;
        }
    }

    // --- Controls Overlay Auto-Hide & Visual Feedback ---

    private void SetControlsVisible(bool visible)
    {
        _controlsVisible = visible;
        var targetOpacity = visible ? 1.0 : 0.0;
        var dur = TimeSpan.FromMilliseconds(visible ? 160 : 280);

        ControlsOverlay.IsHitTestVisible = visible;
        ControlsOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(targetOpacity, dur));

        if (_isFullscreen)
        {
            FullscreenTopOverlay.Visibility = Visibility.Visible;
            FullscreenTopOverlay.IsHitTestVisible = visible;
            FullscreenTopOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(targetOpacity, dur));
        }

        SubtitleOverlay.Margin = visible
            ? new Thickness(28, 0, 28, 86)
            : new Thickness(28, 0, 28, 22);
    }

    private void BumpControlsVisibility()
    {
        if (!_controlsVisible) SetControlsVisible(true);
        _hideControlsTimer.Stop();
        if (_playing && !_isHoveringControls && !_isDraggingSeek)
            _hideControlsTimer.Start();
    }

    private void PlayerView_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isFullscreen) BumpControlsVisibility();
    }

    private void VideoHost_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) => BumpControlsVisibility();

    private void VideoHost_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _isHoveringControls = false;
        if (_playing && !_isDraggingSeek)
        {
            _hideControlsTimer.Stop();
            _hideControlsTimer.Start();
        }
    }

    private void ControlsOverlay_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _isHoveringControls = true;
        _hideControlsTimer.Stop();
        if (!_controlsVisible) SetControlsVisible(true);
    }

    private void ControlsOverlay_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _isHoveringControls = false;
        if (_playing && !_isDraggingSeek)
            _hideControlsTimer.Start();
    }

    private void ShowCenterFeedback(string text)
    {
        CenterFeedbackText.Text = text;
        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520))));
        CenterFeedback.BeginAnimation(OpacityProperty, anim);
    }

    // --- Playback Controls ---

    public void TogglePlayPause(bool showFeedback = false)
    {
        if (_isBusy) return;
        if (_playing)
        {
            Pause();
            if (showFeedback) ShowCenterFeedback("⏸");
        }
        else
        {
            if (VideoMedia.Position.TotalSeconds >= _duration - 0.15)
                SeekTo(0);
            Play();
            if (showFeedback) ShowCenterFeedback("▶");
        }
    }

    private void Play()
    {
        if (_isBusy) return;
        VideoMedia.Play();
        VideoMedia.SpeedRatio = _speed;
        _playing = true;
        UpdatePlayPauseUi();
        _hideControlsTimer.Stop();
        if (!_isHoveringControls) _hideControlsTimer.Start();
    }

    public void PausePlayback() => Pause();

    private void Pause()
    {
        VideoMedia.Pause();
        _playing = false;
        UpdatePlayPauseUi();
        _hideControlsTimer.Stop();
        SetControlsVisible(true);
    }

    private void UpdatePlayPauseUi()
    {
        PlayPauseIcon.Data = _playing ? PauseGeo : PlayGeo;
        PlayPauseBtn.ToolTip = _playing ? Loc.T("player.pause") : Loc.T("player.play");
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayPause(showFeedback: false);

    private void VideoSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _singleClickTimer.Stop();
            ToggleFullscreen();
            return;
        }
        _singleClickTimer.Stop();
        _singleClickTimer.Start();
    }

    public void Rewind5()
    {
        SeekTo(Math.Max(0, VideoMedia.Position.TotalSeconds - 5));
        ShowCenterFeedback("-5s");
        BumpControlsVisibility();
    }

    public void Forward5()
    {
        SeekTo(Math.Min(_duration, VideoMedia.Position.TotalSeconds + 5));
        ShowCenterFeedback("+5s");
        BumpControlsVisibility();
    }

    private void Rewind_Click(object sender, RoutedEventArgs e) => Rewind5();
    private void Forward_Click(object sender, RoutedEventArgs e) => Forward5();

    private void SeekTo(double seconds)
    {
        seconds = Math.Clamp(seconds, 0, _duration);
        VideoMedia.Position = TimeSpan.FromSeconds(seconds);
        if (!_playing) VideoMedia.Pause();
        _suppressSeek = true;
        SeekSlider.Value = seconds;
        _suppressSeek = false;
        CurrentTimeText.Text = ClipCardItem.FormatDuration(seconds);
        UpdateSubtitle(seconds);
        HighlightActiveSegment(seconds);
    }

    private void SeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSeek = true;
        _hideControlsTimer.Stop();
    }

    private void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSeek = false;
        SeekTo(SeekSlider.Value);
        BumpControlsVisibility();
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSeek) return;
        CurrentTimeText.Text = ClipCardItem.FormatDuration(e.NewValue);
        if (!_isDraggingSeek)
            SeekTo(e.NewValue);
        else
        {
            VideoMedia.Position = TimeSpan.FromSeconds(e.NewValue);
            UpdateSubtitle(e.NewValue);
        }
    }

    private void SeekSlider_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (SeekSlider.ActualWidth <= 0) return;
        var pos = e.GetPosition(SeekSlider);
        double ratio = Math.Clamp(pos.X / SeekSlider.ActualWidth, 0, 1);
        double sec = ratio * _duration;
        SeekHoverText.Text = ClipCardItem.FormatDuration(sec);
        SeekHoverBadge.Visibility = Visibility.Visible;
        SeekHoverBadge.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        double badgeW = SeekHoverBadge.DesiredSize.Width;
        double left = Math.Clamp(pos.X - badgeW / 2, 0, Math.Max(0, SeekSlider.ActualWidth - badgeW));
        Canvas.SetLeft(SeekHoverBadge, left);
    }

    private void SeekSlider_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        SeekHoverBadge.Visibility = Visibility.Collapsed;
    }

    // --- Volume ---

    public void ToggleMute()
    {
        _muted = !_muted;
        VideoMedia.IsMuted = _muted;
        UpdateVolumeIcon();
        ShowCenterFeedback(_muted ? "🔇" : "🔊");
        BumpControlsVisibility();
    }

    public void AdjustVolume(double delta)
    {
        VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + delta, 0, 1);
        BumpControlsVisibility();
    }

    private void Mute_Click(object sender, RoutedEventArgs e) => ToggleMute();

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VideoMedia is null) return;
        VideoMedia.Volume = e.NewValue;
        if (e.NewValue > 0 && _muted)
        {
            _muted = false;
            VideoMedia.IsMuted = false;
        }
        UpdateVolumeIcon();
    }

    private void UpdateVolumeIcon()
    {
        if (VolumeIcon is null) return;
        bool silent = _muted || VolumeSlider.Value <= 0.001;
        VolumeIcon.Data = silent ? VolMuteGeo : VolumeSlider.Value < 0.5 ? VolLowGeo : VolHighGeo;
        MuteBtn.ToolTip = silent ? Loc.T("player.unmute") : Loc.T("player.mute");
    }

    // --- Speed Menu (YouTube-style popup) ---

    private void Speed_Click(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        foreach (var spd in Speeds)
        {
            var label = Math.Abs(spd - 1.0) < 0.01 ? $"1×  ({Loc.T("player.speedNormal")})" : $"{spd:0.##}×";
            var mi = new System.Windows.Controls.MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = Math.Abs(_speed - spd) < 0.01
            };
            mi.Click += (_, _) =>
            {
                _speed = spd;
                if (_playing) VideoMedia.SpeedRatio = spd;
                SpeedBtnText.Text = $"{spd:0.##}×";
            };
            menu.Items.Add(mi);
        }
        menu.PlacementTarget = SpeedBtn;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
    }

    // --- Subtitles (CC) ---

    public void ToggleCc()
    {
        if (!CcBtn.IsEnabled) return;
        _ccEnabled = !_ccEnabled;
        CcActiveBar.Visibility = _ccEnabled ? Visibility.Visible : Visibility.Collapsed;
        CcBtn.Opacity = _ccEnabled ? 1.0 : 0.55;
        UpdateSubtitle(VideoMedia.Position.TotalSeconds);
        BumpControlsVisibility();
    }

    private void Cc_Click(object sender, RoutedEventArgs e) => ToggleCc();

    // --- Theater & Fullscreen Modes ---

    public void ToggleTheater()
    {
        if (_isFullscreen)
        {
            SetFullscreen(false);
            return;
        }
        _isTheater = !_isTheater;
        ApplyLayoutMode();
    }

    private void Theater_Click(object sender, RoutedEventArgs e) => ToggleTheater();

    public void ToggleFullscreen() => SetFullscreen(!_isFullscreen);

    public void ExitFullscreen() => SetFullscreen(false);

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void ExitFullscreen_Click(object sender, RoutedEventArgs e) => SetFullscreen(false);

    private void SetFullscreen(bool fullscreen)
    {
        if (_isFullscreen == fullscreen) return;
        _isFullscreen = fullscreen;
        ApplyLayoutMode();
        FullscreenChanged?.Invoke(_isFullscreen);
        if (_isFullscreen)
        {
            SetControlsVisible(true);
            BumpControlsVisibility();
        }
        else
            SetControlsVisible(true);
    }

    private void ApplyLayoutMode()
    {
        if (_isFullscreen)
        {
            RootBorder.Padding = new Thickness(0);
            TopHeaderBar.Visibility = Visibility.Collapsed;
            BelowVideoPanel.Visibility = Visibility.Collapsed;
            VideoHostBorder.CornerRadius = new CornerRadius(0);
            FullscreenTopOverlay.Visibility = Visibility.Visible;
            FullscreenIcon.Data = ExitFsGeo;
            FullscreenBtn.ToolTip = Loc.T("player.exitFullscreen");
            TheaterBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            RootBorder.Padding = new Thickness(22, 12, 22, 16);
            TopHeaderBar.Visibility = Visibility.Visible;
            BelowVideoPanel.Visibility = _isTheater ? Visibility.Collapsed : Visibility.Visible;
            VideoHostBorder.CornerRadius = new CornerRadius(12);
            FullscreenTopOverlay.Visibility = Visibility.Collapsed;
            FullscreenIcon.Data = EnterFsGeo;
            FullscreenBtn.ToolTip = Loc.T("player.fullscreen");
            TheaterBtn.Visibility = Visibility.Visible;
            TheaterBtn.ToolTip = _isTheater ? Loc.T("player.exitTheater") : Loc.T("player.theater");
            TheaterIcon.Stroke = _isTheater
                ? (System.Windows.Media.Brush)FindResource("AccentPaleBrush")
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF0, 0xF3, 0xFA));
        }
    }

    // --- Screenshot ---

    private void Snap_Click(object sender, RoutedEventArgs e)
    {
        if (_clip is null) return;
        try
        {
            var rtb = new RenderTargetBitmap((int)Math.Max(1, VideoMedia.ActualWidth), (int)Math.Max(1, VideoMedia.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(VideoMedia);
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "KeepClip");
            Directory.CreateDirectory(dir);
            var outPath = Path.Combine(dir, $"snap_{_clip.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            using var fs = File.Create(outPath);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            enc.Save(fs);
            ReplayToast.Display(true, Loc.T("toast.screenshotSaved"), Path.GetFileName(outPath));
        }
        catch (Exception ex)
        {
            ReplayToast.Display(false, "Screenshot", ex.Message);
        }
    }

    // --- Clip Actions ---

    private void UpdateFavButton()
    {
        if (_clip is null) return;
        FavBtn.Content = $"{(_clip.Favorite != 0 ? "★" : "☆")} {Loc.T("playerActions.favorite")}";
    }

    private void UpdateCloudButton()
    {
        if (_clip is null) return;
        CloudBtn.Visibility = OAuthService.IsConnected() ? Visibility.Visible : Visibility.Collapsed;
        CloudBtn.Content = _clip.Storage == "cloud" ? "☁ ↓" : "☁ ↑";
    }

    private void Fav_Click(object sender, RoutedEventArgs e)
    {
        if (_clip is null) return;
        var st = App.Clips.ToggleFavorite(_clip.Id);
        if (st is null) return;
        _clip = App.Clips.GetById(_clip.Id) ?? _clip;
        UpdateFavButton();
        LibraryChanged = true;
    }

    private void Cut_Click(object sender, RoutedEventArgs e)
    {
        if (_clip is null) return;
        Pause();
        var cw = new CutWindow(_clip) { Owner = OwnerWindow };
        if (cw.ShowDialog() == true)
        {
            LibraryChanged = true;
            ReplayToast.Display(true, Loc.T("cutOverlay.title"), cw.CutResult?.GetValueOrDefault("output_name") as string ?? "");
        }
    }

    private async void Cloud_Click(object sender, RoutedEventArgs e)
    {
        if (_clip is null) return;
        CloudBtn.IsEnabled = false;
        try
        {
            if (_clip.Storage == "cloud")
                await CloudService.DownloadClipAsync(_clip.Id);
            else
                await CloudService.UploadClipAsync(_clip.Id);

            _clip = App.Clips.GetById(_clip.Id) ?? _clip;
            UpdateCloudButton();
            LibraryChanged = true;
            ReplayToast.Display(true, Loc.T("nav.cloud"), _clip.Filename);
        }
        catch (Exception ex)
        {
            ReplayToast.Display(false, Loc.T("nav.cloud"), ex.Message);
        }
        finally
        {
            CloudBtn.IsEnabled = true;
        }
    }

    private void Folders_Click(object sender, RoutedEventArgs e)
    {
        if (_clip is null) return;
        var menu = new System.Windows.Controls.ContextMenu();
        var assigned = App.Folders.GetFolderIdsForClip(_clip.Id).ToHashSet();
        foreach (var f in App.Folders.List())
        {
            var fid = Convert.ToInt64(f["id"]);
            var name = f["name"] as string ?? "";
            var item = new System.Windows.Controls.MenuItem { Header = name, IsCheckable = true, IsChecked = assigned.Contains(fid) };
            item.Click += (_, _) =>
            {
                if (_clip is null) return;
                if (item.IsChecked) App.Folders.AddClip(fid, _clip.Id);
                else App.Folders.RemoveClip(fid, _clip.Id);
                LibraryChanged = true;
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0)
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = Loc.T("folders.noFolders"), IsEnabled = false });
        menu.PlacementTarget = FoldersBtn;
        menu.IsOpen = true;
    }

    private async void Retranscribe_Click(object sender, RoutedEventArgs e)
    {
        if (_clip is null) return;
        var clipId = _clip.Id;
        var fname = _clip.Filename;
        _isBusy = true;
        Pause();
        RetranscribeBtn.IsEnabled = false;
        RetranscribeBtn.Content = Loc.T("toast.retranscribeBtn");
        PlayerBusyText.Text = Loc.T("busy.startingTranscribe");
        PlayerBusyOverlay.Visibility = Visibility.Visible;
        try
        {
            var res = await Task.Run(() => App.ClipService.Retranscribe(clipId));
            if (res.IsSuccess)
            {
                LoadSegments();
                LibraryChanged = true;
                ReplayToast.Display(true, Loc.T("playerActions.retranscribe"), fname);
            }
            else
            {
                ReplayToast.Display(false, Loc.T("playerActions.retranscribe"), res.Error ?? "");
            }
        }
        finally
        {
            _isBusy = false;
            PlayerBusyOverlay.Visibility = Visibility.Collapsed;
            RetranscribeBtn.Content = Loc.T("playerActions.retranscribe");
            RetranscribeBtn.IsEnabled = true;
        }
    }

    private void TranscriptTxt_Click(object sender, RoutedEventArgs e)
    {
        if (_clip is null) return;
        if (_segments.Count == 0)
        {
            ReplayToast.Display(false, Loc.T("playerActions.transcriptTxt"), Strings.Get("transcript.empty"));
            return;
        }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = DisplayHelper.SafeFolderName($"{Path.GetFileNameWithoutExtension(_clip.Filename)}.txt"),
            Filter = "Text files (*.txt)|*.txt"
        };
        if (dlg.ShowDialog(OwnerWindow) != true) return;

        var sb = new StringBuilder();
        sb.AppendLine(_clip.Filename);
        var recorded = DateTimeOffset.FromUnixTimeMilliseconds((long)(_clip.Mtime * 1000)).LocalDateTime;
        sb.AppendLine($"{_clip.Game} · {recorded:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        foreach (var row in _segments)
        {
            var start = TimeSpan.FromSeconds(Convert.ToDouble(row["start_s"] ?? 0.0));
            var stamp = start.TotalHours >= 1 ? $"{start:h\\:mm\\:ss}" : $"{start:mm\\:ss}";
            sb.AppendLine($"[{stamp}] {(row["text"] as string ?? "").Trim()}");
        }
        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
    }

    private async void Fix_Click(object sender, RoutedEventArgs e)
    {
        if (_clip is null) return;
        var clipId = _clip.Id;
        _isBusy = true;
        FixBtn.IsEnabled = false;
        FixBtn.Content = Loc.T("playerActions.fixing");
        PlayerBusyText.Text = Loc.T("busy.processing");
        PlayerBusyOverlay.Visibility = Visibility.Visible;
        Pause();
        VideoMedia.Close();
        try
        {
            var res = await Task.Run(() => App.ClipService.Fix(clipId));
            if (res.IsSuccess)
            {
                _clip = App.Clips.GetById(clipId) ?? _clip;
                LoadSegments();
                LibraryChanged = true;
                _isBusy = false;
                _ = InitVideoSourceAsync();
                ReplayToast.Display(true, Loc.T("playerActions.fix"), res.Value?.GetValueOrDefault("message") as string ?? "");
            }
            else
            {
                ReplayToast.Display(false, Loc.T("playerActions.fix"), res.Error ?? "");
            }
        }
        finally
        {
            _isBusy = false;
            PlayerBusyOverlay.Visibility = Visibility.Collapsed;
            FixBtn.Content = Loc.T("playerActions.fix");
            FixBtn.IsEnabled = true;
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_clip is null || OwnerWindow is null) return;
        if (!PromptDialog.Confirm(OwnerWindow, Loc.T("confirm.deleteClip.title"), _clip.Filename, Loc.T("confirmDefault.yesLabel")))
            return;
        var clipId = _clip.Id;
        Pause();
        VideoMedia.Close();
        var res = await App.ClipService.DeleteAsync(clipId, deleteFile: true);
        if (res.IsSuccess)
        {
            LibraryChanged = true;
            ClosePlayer();
        }
        else
        {
            ReplayToast.Display(false, Loc.T("playerActions.delete"), res.Error ?? "");
        }
    }

    private void LoadSegments()
    {
        SegmentsPanel.Children.Clear();
        _segmentRows.Clear();
        if (_clip is null) return;
        _segments = App.Segments.ListByClipId(_clip.Id);
        TranscriptTxtBtn.IsEnabled = _segments.Count > 0;
        CcBtn.IsEnabled = _segments.Count > 0;
        CcActiveBar.Visibility = _segments.Count > 0 && _ccEnabled ? Visibility.Visible : Visibility.Collapsed;

        if (_segments.Count == 0)
        {
            SegmentsPanel.Children.Add(new TextBlock
            {
                Text = Loc.T("player.notTranscribed"),
                Style = (Style)FindResource("SubtleText"),
                Margin = new Thickness(6)
            });
            return;
        }

        foreach (var seg in _segments)
        {
            long segId = Convert.ToInt64(seg["id"]);
            double startS = Convert.ToDouble(seg["start_s"] ?? 0.0);
            double endS = Convert.ToDouble(seg["end_s"] ?? startS);
            string text = (seg["text"] as string ?? "").Trim();

            var rowBorder = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 1, 0, 1) };
            var row = new DockPanel();
            var timeBtn = new WpfButton
            {
                Content = ClipCardItem.FormatDuration(startS),
                Style = (Style)FindResource("GhostButton"),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 11.5,
            };
            timeBtn.Click += (_, _) => { SeekTo(startS); if (!_playing) Play(); };

            var delBtn = new WpfButton
            {
                Content = "✕",
                Style = (Style)FindResource("GhostButton"),
                Padding = new Thickness(6, 2, 6, 2),
                FontSize = 11,
                Margin = new Thickness(6, 0, 0, 0),
            };
            delBtn.Click += (_, _) =>
            {
                if (App.Segments.Delete(segId)) { LoadSegments(); LibraryChanged = true; }
            };

            var editBtn = new WpfButton
            {
                Content = "✎",
                Style = (Style)FindResource("GhostButton"),
                Padding = new Thickness(6, 2, 6, 2),
                FontSize = 11,
                Margin = new Thickness(6, 0, 0, 0),
            };
            editBtn.Click += (_, _) =>
            {
                if (OwnerWindow is null) return;
                var updated = PromptDialog.AskText(OwnerWindow, Loc.T("player.editSegment"), "", Loc.T("player.editSave"), text);
                if (!string.IsNullOrWhiteSpace(updated) && App.Segments.UpdateText(segId, updated))
                {
                    LoadSegments();
                    LibraryChanged = true;
                }
            };

            DockPanel.SetDock(timeBtn, Dock.Left);
            DockPanel.SetDock(delBtn, Dock.Right);
            DockPanel.SetDock(editBtn, Dock.Right);
            row.Children.Add(timeBtn);
            row.Children.Add(delBtn);
            row.Children.Add(editBtn);
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            rowBorder.Child = row;
            SegmentsPanel.Children.Add(rowBorder);
            _segmentRows.Add((startS, endS, rowBorder));
        }
    }

    private void LoadDemoStatus()
    {
        if (_clip is null) return;
        var st = App.ClipDemoService.Get(_clip.Id);
        if (!st.IsSuccess) return;
        var val = st.Value!;
        DemoPathBox.Text = val.path ?? "";
        bool hasDemo = !string.IsNullOrEmpty(val.path);
        DemoShowBtn.Visibility = hasDemo && val.available ? Visibility.Visible : Visibility.Collapsed;
        DemoRemoveBtn.Visibility = hasDemo ? Visibility.Visible : Visibility.Collapsed;
        DemoStatusText.Text = !hasDemo
            ? Loc.T("demo.empty")
            : val.available
                ? Loc.Format("demo.linked", ("name", val.filename ?? ""))
                : Loc.Format("demo.missing", ("name", val.filename ?? ""));
    }

    private void DemoBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "CS2 Demo (*.dem)|*.dem", Title = Strings.Get("demo.pickTitle") };
        if (dlg.ShowDialog(OwnerWindow) == true) DemoPathBox.Text = dlg.FileName;
    }

    private async void DemoAssign_Click(object sender, RoutedEventArgs e)
    {
        if (_clip is null) return;
        var res = await App.ClipDemoService.AssignAsync(_clip.Id, DemoPathBox.Text, CancellationToken.None);
        if (res.IsSuccess)
        {
            LoadDemoStatus();
            ReplayToast.Display(true, Loc.T("demo.title"), Loc.T("demo.saved"));
        }
        else
        {
            ReplayToast.Display(false, Loc.T("demo.title"), res.Error ?? "");
        }
    }

    private void DemoShow_Click(object sender, RoutedEventArgs e)
    {
        var p = DemoPathBox.Text.Trim();
        if (Path.Exists(p))
            try { Process.Start(ExplorerLauncher.CreateStartInfo(p)); } catch { }
    }

    private void DemoRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_clip is null) return;
        App.ClipDemoService.Remove(_clip.Id);
        LoadDemoStatus();
    }

    private void FilePath_Click(object sender, RoutedEventArgs e)
    {
        var path = _clip?.Filepath;
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path))
                Process.Start(ExplorerLauncher.CreateStartInfo(path));
            else if (Path.GetDirectoryName(path) is { Length: > 0 } dir && Directory.Exists(dir))
                Process.Start(ExplorerLauncher.CreateStartInfo(dir));
        }
        catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => ClosePlayer();
}
