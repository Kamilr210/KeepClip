using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeepClip.Localization;
using Microsoft.Win32;
using WpfButton = System.Windows.Controls.Button;

namespace KeepClip.UI;

public partial class PlayerWindow : Window
{
    private Clip _clip;
    private readonly double? _seekOnOpen;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly double[] _speeds = { 0.5, 1.0, 1.25, 1.5, 2.0 };
    private int _speedIdx = 1;
    private bool _playing;
    private bool _muted;
    private bool _ccEnabled = true;
    private bool _suppressSeek;
    private bool _isFullscreen;
    private double _duration = 1;
    private List<Dictionary<string, object?>> _segments = new();
    private CancellationTokenSource? _proxyCts;

    public bool LibraryChanged { get; private set; }

    public PlayerWindow(Clip clip, double? seekSeconds = null)
    {
        InitializeComponent();
        _clip = clip;
        _seekOnOpen = seekSeconds;

        GameText.Text = clip.Game == "_root" ? Loc.T("search.allGames") : clip.Game;
        FileText.Text = clip.Filename;
        _duration = Math.Max(0.5, clip.Duration);
        SeekSlider.Maximum = _duration;
        TotalTimeText.Text = ClipCardItem.FormatDuration(_duration);

        UpdateFavButton();
        UpdateCloudButton();
        LoadSegments();
        LoadDemoStatus();
        _ = InitVideoSourceAsync();

        _timer.Tick += Timer_Tick;
        _timer.Start();
        Closed += (_, _) =>
        {
            _proxyCts?.Cancel();
            _timer.Stop();
            VideoMedia.Stop();
            VideoMedia.Close();
        };
    }

    private async Task InitVideoSourceAsync()
    {
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
            while (!_proxyCts.IsCancellationRequested)
            {
                await Task.Delay(300, _proxyCts.Token).ConfigureAwait(true);
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
            VideoMedia.Position = TimeSpan.FromSeconds(_seekOnOpen.Value);
    }

    private void VideoMedia_MediaEnded(object sender, RoutedEventArgs e)
    {
        _playing = false;
        PlayPauseBtn.Content = "▶";
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_playing) return;
        var pos = VideoMedia.Position.TotalSeconds;
        _suppressSeek = true;
        SeekSlider.Value = pos;
        _suppressSeek = false;
        CurrentTimeText.Text = ClipCardItem.FormatDuration(pos);
        UpdateSubtitle(pos);
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

    private void Play()
    {
        VideoMedia.Play();
        VideoMedia.SpeedRatio = _speeds[_speedIdx];
        _playing = true;
        PlayPauseBtn.Content = "⏸";
    }

    private void Pause()
    {
        VideoMedia.Pause();
        _playing = false;
        PlayPauseBtn.Content = "▶";
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_playing) Pause();
        else Play();
    }

    private void Start_Click(object sender, RoutedEventArgs e) => SeekTo(0);
    private void End_Click(object sender, RoutedEventArgs e) => SeekTo(Math.Max(0, _duration - 0.5));
    private void Rewind_Click(object sender, RoutedEventArgs e) => SeekTo(Math.Max(0, VideoMedia.Position.TotalSeconds - 5));
    private void Forward_Click(object sender, RoutedEventArgs e) => SeekTo(Math.Min(_duration, VideoMedia.Position.TotalSeconds + 5));

    private void SeekTo(double seconds)
    {
        VideoMedia.Position = TimeSpan.FromSeconds(seconds);
        _suppressSeek = true;
        SeekSlider.Value = seconds;
        _suppressSeek = false;
        CurrentTimeText.Text = ClipCardItem.FormatDuration(seconds);
        UpdateSubtitle(seconds);
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSeek) return;
        SeekTo(e.NewValue);
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _muted = !_muted;
        VideoMedia.IsMuted = _muted;
        MuteBtn.Content = _muted ? "🔇" : "🔊";
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VideoMedia is null) return;
        VideoMedia.Volume = e.NewValue;
        if (e.NewValue > 0 && _muted)
        {
            _muted = false;
            VideoMedia.IsMuted = false;
            MuteBtn.Content = "🔊";
        }
    }

    private void Speed_Click(object sender, RoutedEventArgs e)
    {
        _speedIdx = (_speedIdx + 1) % _speeds.Length;
        var spd = _speeds[_speedIdx];
        VideoMedia.SpeedRatio = spd;
        SpeedBtn.Content = $"{spd:0.##}×";
    }

    private void Cc_Click(object sender, RoutedEventArgs e)
    {
        _ccEnabled = !_ccEnabled;
        CcBtn.Opacity = _ccEnabled ? 1.0 : 0.45;
        UpdateSubtitle(VideoMedia.Position.TotalSeconds);
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        _isFullscreen = !_isFullscreen;
        WindowState = _isFullscreen ? WindowState.Maximized : WindowState.Normal;
        WindowStyle = _isFullscreen ? WindowStyle.None : WindowStyle.SingleBorderWindow;
    }

    private void Snap_Click(object sender, RoutedEventArgs e)
    {
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

    private void UpdateFavButton()
        => FavBtn.Content = $"{(_clip.Favorite != 0 ? "★" : "☆")} {Loc.T("playerActions.favorite")}";

    private void UpdateCloudButton()
    {
        CloudBtn.Visibility = OAuthService.IsConnected() ? Visibility.Visible : Visibility.Collapsed;
        CloudBtn.Content = _clip.Storage == "cloud" ? "☁ ↓" : "☁ ↑";
    }

    private void Fav_Click(object sender, RoutedEventArgs e)
    {
        var st = App.Clips.ToggleFavorite(_clip.Id);
        if (st is null) return;
        _clip = App.Clips.GetById(_clip.Id) ?? _clip;
        UpdateFavButton();
        LibraryChanged = true;
    }

    private void Cut_Click(object sender, RoutedEventArgs e)
    {
        Pause();
        var cw = new CutWindow(_clip) { Owner = this };
        if (cw.ShowDialog() == true)
        {
            LibraryChanged = true;
            ReplayToast.Display(true, Loc.T("cutOverlay.title"), cw.CutResult?.GetValueOrDefault("output_name") as string ?? "");
        }
    }

    private async void Cloud_Click(object sender, RoutedEventArgs e)
    {
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
        var menu = new System.Windows.Controls.ContextMenu();
        var assigned = App.Folders.GetFolderIdsForClip(_clip.Id).ToHashSet();
        foreach (var f in App.Folders.List())
        {
            var fid = Convert.ToInt64(f["id"]);
            var name = f["name"] as string ?? "";
            var item = new System.Windows.Controls.MenuItem { Header = name, IsCheckable = true, IsChecked = assigned.Contains(fid) };
            item.Click += (_, _) =>
            {
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
        RetranscribeBtn.IsEnabled = false;
        RetranscribeBtn.Content = Loc.T("toast.retranscribeBtn");
        try
        {
            var res = await Task.Run(() => App.ClipService.Retranscribe(_clip.Id));
            if (res.IsSuccess)
            {
                LoadSegments();
                LibraryChanged = true;
                ReplayToast.Display(true, Loc.T("playerActions.retranscribe"), _clip.Filename);
            }
            else
            {
                ReplayToast.Display(false, Loc.T("playerActions.retranscribe"), res.Error ?? "");
            }
        }
        finally
        {
            RetranscribeBtn.Content = Loc.T("playerActions.retranscribe");
            RetranscribeBtn.IsEnabled = true;
        }
    }

    private void TranscriptTxt_Click(object sender, RoutedEventArgs e)
    {
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
        if (dlg.ShowDialog(this) != true) return;

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
        FixBtn.IsEnabled = false;
        FixBtn.Content = Loc.T("playerActions.fixing");
        Pause();
        VideoMedia.Close();
        try
        {
            var res = await Task.Run(() => App.ClipService.Fix(_clip.Id));
            if (res.IsSuccess)
            {
                _clip = App.Clips.GetById(_clip.Id) ?? _clip;
                LoadSegments();
                LibraryChanged = true;
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
            FixBtn.Content = Loc.T("playerActions.fix");
            FixBtn.IsEnabled = true;
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.Confirm(this, Loc.T("confirm.deleteClip.title"), _clip.Filename, Loc.T("confirmDefault.yesLabel")))
            return;
        Pause();
        VideoMedia.Close();
        var res = await App.ClipService.DeleteAsync(_clip.Id, deleteFile: true);
        if (res.IsSuccess)
        {
            LibraryChanged = true;
            Close();
        }
        else
        {
            ReplayToast.Display(false, Loc.T("playerActions.delete"), res.Error ?? "");
        }
    }

    private void LoadSegments()
    {
        SegmentsPanel.Children.Clear();
        _segments = App.Segments.ListByClipId(_clip.Id);
        TranscriptTxtBtn.IsEnabled = _segments.Count > 0;
        CcBtn.IsEnabled = _segments.Count > 0;

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
            string text = (seg["text"] as string ?? "").Trim();

            var row = new DockPanel { Margin = new Thickness(4, 3, 4, 3) };
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
                var updated = PromptDialog.AskText(this, Loc.T("player.editSegment"), "", Loc.T("player.editSave"), text);
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
            SegmentsPanel.Children.Add(row);
        }
    }

    private void LoadDemoStatus()
    {
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
        if (dlg.ShowDialog(this) == true) DemoPathBox.Text = dlg.FileName;
    }

    private async void DemoAssign_Click(object sender, RoutedEventArgs e)
    {
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
        App.ClipDemoService.Remove(_clip.Id);
        LoadDemoStatus();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        else if (e.Key == Key.Space && Keyboard.FocusedElement is not System.Windows.Controls.TextBox)
        {
            e.Handled = true;
            PlayPause_Click(sender, e);
        }
        else if (e.Key == Key.Left) Rewind_Click(sender, e);
        else if (e.Key == Key.Right) Forward_Click(sender, e);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
