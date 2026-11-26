using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KeepClip.Localization;
using KeepClip.Themes;
using WpfApplication = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using WpfClipboard = System.Windows.Clipboard;
using WpfColor = System.Windows.Media.Color;

namespace KeepClip.UI;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private sealed record ChoiceItem(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly DispatcherTimer _transcribePoll = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _thumbPoll = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private int _thumbPollTicks;
    private Storyboard? _orbStoryboard;
    private bool _sidebarCollapsed;
    private bool _isMosaic;
    private long? _activeFolderId;
    private long? _selectionFolderId;
    private readonly HashSet<long> _selectedClipIds = new();
    private bool _suppressUiEvents = true;
    private WindowState _preFullscreenWindowState = WindowState.Normal;
    private double _preFsLeft;
    private double _preFsTop;
    private double _preFsWidth = 1280;
    private double _preFsHeight = 800;

    public bool IsSelectionMode => _selectionFolderId is not null;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

#if KEEPCLIP_DEV
        BtnDevLogs.Visibility = Visibility.Visible;
        BtnDevResources.Visibility = Visibility.Visible;
#endif

        InitCombos();
        InitOrbsAnimation();

        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); RefreshClips(); };
        _transcribePoll.Tick += (_, _) => PollTranscribe();
        _thumbPoll.Tick += (_, _) => PollThumbnails();

        EmbeddedPlayer.Closed += libraryChanged =>
        {
            PlayerOverlay.Visibility = Visibility.Collapsed;
            if (libraryChanged) RefreshAll();
        };
        EmbeddedPlayer.FullscreenChanged += OnPlayerFullscreenChanged;
        EmbeddedReplay.CloseRequested += () => ReplayOverlay.Visibility = Visibility.Collapsed;

        StateChanged += (_, _) =>
        {
            if (MaximizeBtn is not null)
                MaximizeBtn.Content = WindowState == WindowState.Maximized ? "❐" : "☐";
        };
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        HotkeyManager.Attach(this, OnReplayHotkeyPressed);
        ReplayService.Notifier = (ok, title, sub) => Dispatcher.BeginInvoke(() => ReplayToast.Display(ok, title, sub));

        _suppressUiEvents = false;
        RefreshAll();
        _ = CheckForUpdatesAsync();
        _ = RefreshCloudCardAsync();

        if (!Settings.IsConfigured() && App.Clips.Count() == 0)
        {
            var dlg = new ConfigFolderDialog(true, Settings.GetClipsRoot()) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                Settings.SetClipsRoot(dlg.SelectedPath);
                await RunScanAsync();
            }
        }
        else if (App.Clips.Count() > 0 && !Settings.IsConfigured())
        {
            Settings.SetClipsRoot(Settings.GetClipsRoot());
        }

        var obState = OnboardingService.Get();
        if (!obState.completed && !obState.skipped)
        {
            var ob = new OnboardingWindow { Owner = this };
            ob.ShowDialog();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (WpfApplication.Current is App app && app.ShouldHideToTrayOnClose())
        {
            e.Cancel = true;
            if (PlayerOverlay.Visibility == Visibility.Visible)
                EmbeddedPlayer.PausePlayback();
            app.HideToTray();
        }
    }

    private void InitCombos()
    {
        SortCombo.ItemsSource = new[]
        {
            new ChoiceItem("newest", Loc.T("search.sortNewest")),
            new ChoiceItem("oldest", Loc.T("search.sortOldest")),
            new ChoiceItem("largest", Loc.T("search.sortLargest")),
            new ChoiceItem("smallest", Loc.T("search.sortSmallest")),
            new ChoiceItem("relevance", Loc.T("search.sortRelevance")),
        };
        SortCombo.DisplayMemberPath = "Label";
        SortCombo.SelectedValuePath = "Value";
        SortCombo.SelectedIndex = 0;

        ThemeCombo.ItemsSource = new[]
        {
            new ChoiceItem("purple", Loc.T("settings.themePurple")),
            new ChoiceItem("green", Loc.T("settings.themeGreen")),
        };
        ThemeCombo.DisplayMemberPath = "Label";
        ThemeCombo.SelectedValuePath = "Value";
        ThemeCombo.SelectedValue = ThemeManager.CurrentTheme;

        LangCombo.ItemsSource = new[]
        {
            new ChoiceItem("en", "English"),
            new ChoiceItem("pl", "Polski"),
            new ChoiceItem("uk", "Українська"),
            new ChoiceItem("ru", "Русский"),
        };
        LangCombo.DisplayMemberPath = "Label";
        LangCombo.SelectedValuePath = "Value";
        LangCombo.SelectedValue = Loc.Instance.CurrentLanguage;

        HideFolderClipsBox.IsChecked = Settings.GetHideFolderClips();
        FavoritesAfterRecentBox.IsChecked = Settings.GetFavoritesAfterRecent();
        SettingsFolderPath.Text = Settings.GetClipsRoot();

        var v = UpdateService.Current;
        VersionText.Text = $"KeepClip v{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)} (WPF)";
    }

    private void InitOrbsAnimation()
    {
        var anim1X = new DoubleAnimation(0, 220, TimeSpan.FromSeconds(18)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        var anim1Y = new DoubleAnimation(0, 160, TimeSpan.FromSeconds(22)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        var anim2X = new DoubleAnimation(0, -200, TimeSpan.FromSeconds(20)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        var anim2Y = new DoubleAnimation(0, -140, TimeSpan.FromSeconds(24)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };

        Storyboard.SetTarget(anim1X, Orb1Transform);
        Storyboard.SetTargetProperty(anim1X, new PropertyPath(TranslateTransform.XProperty));
        Storyboard.SetTarget(anim1Y, Orb1Transform);
        Storyboard.SetTargetProperty(anim1Y, new PropertyPath(TranslateTransform.YProperty));
        Storyboard.SetTarget(anim2X, Orb2Transform);
        Storyboard.SetTargetProperty(anim2X, new PropertyPath(TranslateTransform.XProperty));
        Storyboard.SetTarget(anim2Y, Orb2Transform);
        Storyboard.SetTargetProperty(anim2Y, new PropertyPath(TranslateTransform.YProperty));

        _orbStoryboard = new Storyboard();
        _orbStoryboard.Children.Add(anim1X);
        _orbStoryboard.Children.Add(anim1Y);
        _orbStoryboard.Children.Add(anim2X);
        _orbStoryboard.Children.Add(anim2Y);
        _orbStoryboard.Begin();
    }

    internal void RefreshAll()
    {
        RefreshGameFilter();
        RefreshStats();
        RefreshClips();
        RefreshFolders();
        RefreshFavorites();
        ThumbnailWorker.Ensure();
        _thumbPollTicks = 0;
        _thumbPoll.Start();
    }

    private void PollThumbnails()
    {
        _thumbPollTicks++;
        if (_thumbPollTicks > 12) { _thumbPoll.Stop(); return; }
        if (ClipsItems.ItemsSource is not IEnumerable<ClipCardItem> items) return;
        bool anyNew = items.Any(c => c.Thumbnail is null && File.Exists(Config.ThumbPath(c.Id)));
        if (anyNew)
        {
            RefreshClips();
            RefreshFavorites();
        }
    }

    private void RefreshGameFilter()
    {
        var current = GameFilterCombo.SelectedValue as string ?? "";
        var st = App.Stats.Get(null, null);
        var games = st["games"] as List<Dictionary<string, object?>> ?? new();
        var items = new List<ChoiceItem> { new("", Loc.T("search.allGames")) };
        foreach (var row in games)
        {
            var g = row["game"] as string ?? "";
            if (!string.IsNullOrEmpty(g))
                items.Add(new ChoiceItem(g, g == "_root" ? Loc.T("search.allGames") : g));
        }
        _suppressUiEvents = true;
        GameFilterCombo.ItemsSource = items;
        GameFilterCombo.DisplayMemberPath = "Label";
        GameFilterCombo.SelectedValuePath = "Value";
        GameFilterCombo.SelectedValue = current;
        if (GameFilterCombo.SelectedIndex < 0) GameFilterCombo.SelectedIndex = 0;
        _suppressUiEvents = false;
    }

    private void RefreshStats()
    {
        long? diskTotal = null, diskFree = null;
        try
        {
            var root = Settings.GetClipsRoot();
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
            {
                var di = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
                diskTotal = di.TotalSize;
                diskFree = di.AvailableFreeSpace;
            }
        }
        catch { }

        var st = App.Stats.Get(diskTotal, diskFree);
        StatClipsText.Text = Convert.ToInt64(st["clips"] ?? 0L).ToString();
        StatTransText.Text = Convert.ToInt64(st["transcribed"] ?? 0L).ToString();
        StatFavText.Text = Convert.ToInt64(st["favorites"] ?? 0L).ToString();

        long localBytes = Convert.ToInt64(st["local_bytes"] ?? 0L);
        long cloudBytes = Convert.ToInt64(st["cloud_bytes"] ?? 0L);

        if (diskTotal is > 0 && diskFree is >= 0)
        {
            double usedRatio = Math.Clamp(1.0 - (double)diskFree.Value / diskTotal.Value, 0, 1);
            LocalDiskBar.Value = usedRatio;
            LocalFreeText.Text = $"{ClipCardItem.FormatBytes(diskFree.Value)} free";
            LocalLegendText.Text = $"{Loc.T("storageLabel.clips")}: {ClipCardItem.FormatBytes(localBytes)} · {ClipCardItem.FormatBytes(diskTotal.Value)}";
        }
        else
        {
            LocalDiskBar.Value = 0;
            LocalFreeText.Text = "—";
            LocalLegendText.Text = $"{Loc.T("storageLabel.clips")}: {ClipCardItem.FormatBytes(localBytes)}";
        }

        CloudLegendText.Text = $"{Loc.T("storageLabel.clips")}: {ClipCardItem.FormatBytes(cloudBytes)}";
    }

    private void RefreshClips()
    {
        var query = SearchBox.Text.Trim();
        var game = GameFilterCombo.SelectedValue as string;
        var sort = SortCombo.SelectedValue as string ?? "newest";
        bool hideFolder = Settings.GetHideFolderClips();

        List<ClipCardItem> list;
        if (!string.IsNullOrEmpty(query))
        {
            try
            {
                var fts = SearchService.ToFtsQuery(query);
                var hits = string.IsNullOrEmpty(fts)
                    ? new List<Dictionary<string, object?>>()
                    : App.Segments.Search(fts, sort, 100, home: true, hideFolderClips: hideFolder);
                list = hits.Select(ClipCardItem.FromSearchRow).ToList();
            }
            catch
            {
                list = new List<ClipCardItem>();
            }
            ClipsEmptyText.Text = Loc.T("search.noResults");
        }
        else
        {
            var rows = App.Clips.List(game, sort, 200, favoritesOnly: false, home: true, hideFolderClips: hideFolder);
            if (_isMosaic && sort == "newest" && Settings.GetFavoritesAfterRecent() && rows.Count > 4)
            {
                var head = rows.Take(4).ToList();
                var tail = rows.Skip(4).OrderByDescending(r => Convert.ToInt64(r.GetValueOrDefault("favorite") ?? 0L) != 0).ToList();
                rows = head.Concat(tail).ToList();
            }
            list = rows.Select((r, i) =>
            {
                var item = ClipCardItem.FromRow(r, _isMosaic && i == 0);
                item.Selected = _selectedClipIds.Contains(item.Id);
                return item;
            }).ToList();
            ClipsEmptyText.Text = Loc.T("search.noVisibleClips");
        }

        ClipsItems.ItemsSource = list;
        ClipsEmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshFavorites()
    {
        var favs = App.Clips.List(null, "newest", 200, favoritesOnly: true);
        FavClipsItems.ItemsSource = favs.Select(r => ClipCardItem.FromRow(r)).ToList();
        FavCountText.Text = $"{favs.Count} {Loc.ClipWord(favs.Count)}";
        FavEmptyText.Visibility = favs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshFolders()
    {
        FoldersGridPanel.Children.Clear();
        foreach (var f in App.Folders.List())
        {
            long id = Convert.ToInt64(f["id"]);
            string name = f["name"] as string ?? "";
            long count = Convert.ToInt64(f["clip_count"] ?? 0L);

            var card = new Border
            {
                Width = 220,
                Margin = new Thickness(0, 0, 14, 14),
                Style = (Style)FindResource("InteractiveCardBorder"),
                Padding = new Thickness(16),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "📁", FontSize = 24, Margin = new Thickness(0, 0, 0, 8) });
            sp.Children.Add(new TextBlock { Text = name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(new TextBlock { Text = $"{count} {Loc.ClipWord((int)count)}", Style = (Style)FindResource("SubtleText"), Margin = new Thickness(0, 4, 0, 0) });
            card.Child = sp;
            card.MouseLeftButtonUp += (_, _) => OpenFolderDetail(id);
            FoldersGridPanel.Children.Add(card);
        }

        if (_activeFolderId is { } activeId)
            OpenFolderDetail(activeId);
    }

    private void OpenFolderDetail(long folderId)
    {
        var res = App.Folders.ListClips(folderId, "newest", 500);
        if (res is null)
        {
            _activeFolderId = null;
            FoldersIndexPanel.Visibility = Visibility.Visible;
            FolderDetailPanel.Visibility = Visibility.Collapsed;
            return;
        }
        _activeFolderId = folderId;
        var (folder, clips) = res.Value;
        FolderDetailName.Text = folder["name"] as string ?? "";
        FolderDetailCount.Text = $"{clips.Count} {Loc.ClipWord(clips.Count)}";
        FolderHideHomeBox.IsChecked = Convert.ToInt64(folder["hidden_from_home"] ?? 0L) != 0;
        UploadFolderBtn.Visibility = OAuthService.IsConnected() ? Visibility.Visible : Visibility.Collapsed;
        FolderClipsItems.ItemsSource = clips.Select(r => ClipCardItem.FromRow(r)).ToList();

        FoldersIndexPanel.Visibility = Visibility.Collapsed;
        FolderDetailPanel.Visibility = Visibility.Visible;
    }

    // Navigation & Titlebar
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        SidebarCol.Width = new GridLength(_sidebarCollapsed ? 76 : 260);
        BrandPanel.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarToggleBtn.Content = _sidebarCollapsed ? "▶" : "◀";
        var vis = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        foreach (var tb in new[] { LblNavClips, LblNavFolders, LblNavFavorites, LblNavCloud, LblScan, LblReplay, LblTranscribe, LblRetranscribe, LblSettings })
            tb.Visibility = vis;
    }

    private void NavView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn || btn.CommandParameter is not string view) return;
        SwitchView(view);
    }

    private void CloudCard_Click(object sender, MouseButtonEventArgs e) => SwitchView("cloud");

    private void SwitchView(string view)
    {
        foreach (var b in new[] { NavClips, NavFolders, NavFavorites, NavCloud, NavSettings })
            b.Tag = null;

        ViewClips.Visibility = Visibility.Collapsed;
        ViewFolders.Visibility = Visibility.Collapsed;
        ViewFavorites.Visibility = Visibility.Collapsed;
        ViewCloud.Visibility = Visibility.Collapsed;
        ViewSettings.Visibility = Visibility.Collapsed;

        switch (view)
        {
            case "clips": NavClips.Tag = "active"; ViewClips.Visibility = Visibility.Visible; RefreshClips(); break;
            case "folders": NavFolders.Tag = "active"; ViewFolders.Visibility = Visibility.Visible; RefreshFolders(); break;
            case "favorites": NavFavorites.Tag = "active"; ViewFavorites.Visibility = Visibility.Visible; RefreshFavorites(); break;
            case "cloud": NavCloud.Tag = "active"; ViewCloud.Visibility = Visibility.Visible; _ = RefreshCloudViewAsync(); break;
            case "settings": NavSettings.Tag = "active"; ViewSettings.Visibility = Visibility.Visible; break;
        }
    }

    // Search & Layout
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressUiEvents) RefreshClips();
    }

    private void LayoutGrid_Click(object sender, RoutedEventArgs e)
    {
        _isMosaic = false;
        LayoutGridBtn.Style = (Style)FindResource("PrimaryButton");
        LayoutMosaicBtn.Style = (Style)FindResource("DarkButton");
        RefreshClips();
    }

    private void LayoutMosaic_Click(object sender, RoutedEventArgs e)
    {
        _isMosaic = true;
        LayoutMosaicBtn.Style = (Style)FindResource("PrimaryButton");
        LayoutGridBtn.Style = (Style)FindResource("DarkButton");
        RefreshClips();
    }

    // Clip Card Actions
    private void ClipCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ClipCardItem item) return;
        if (IsSelectionMode)
        {
            item.Selected = !item.Selected;
            UpdateSelectionSet(item);
            return;
        }
        OpenPlayer(item.Id, item.HitTime);
    }

    private void CardSelect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox cb && cb.DataContext is ClipCardItem item)
            UpdateSelectionSet(item);
        e.Handled = true;
    }

    private void UpdateSelectionSet(ClipCardItem item)
    {
        if (item.Selected) _selectedClipIds.Add(item.Id);
        else _selectedClipIds.Remove(item.Id);
        SelectionAddBtn.IsEnabled = _selectedClipIds.Count > 0;
        var f = _selectionFolderId is { } fid
            ? App.Folders.List().FirstOrDefault(x => Convert.ToInt64(x["id"]) == fid)
            : null;
        var fname = f?["name"] as string ?? "";
        SelectionInfoText.Text = $"{Loc.Format("selection.info", ("folder", fname))} — {_selectedClipIds.Count} {Loc.T("selection.selectedWord")}";
    }

    private void CardFav_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not WpfButton btn || btn.Tag is not ClipCardItem item) return;
        var st = App.Clips.ToggleFavorite(item.Id);
        if (st is not null)
        {
            item.Favorite = st.Value;
            RefreshStats();
            RefreshFavorites();
        }
    }

    private void CardMenu_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not WpfButton btn || btn.Tag is not ClipCardItem item) return;
        var clip = App.Clips.GetById(item.Id);
        if (clip is null) return;

        var menu = new System.Windows.Controls.ContextMenu();
        AddItem(Loc.T("clipMenu.play"), () => OpenPlayer(clip.Id, item.HitTime));
        if (!string.IsNullOrEmpty(clip.Filepath) && Path.Exists(clip.Filepath))
            AddItem(Loc.T("clipMenu.reveal"), () => { try { Process.Start(ExplorerLauncher.CreateStartInfo(clip.Filepath)); } catch { } });
        AddItem(Loc.T("clipMenu.copyPath"), () => { try { WpfClipboard.SetText(clip.Filepath ?? ""); } catch { } });
        menu.Items.Add(new Separator());
        AddItem(Loc.T("playerActions.cut"), () =>
        {
            var cw = new CutWindow(clip) { Owner = this };
            if (cw.ShowDialog() == true) RefreshAll();
        });
        AddItem(Loc.T("playerActions.retranscribe"), async () =>
        {
            var res = await Task.Run(() => App.ClipService.Retranscribe(clip.Id));
            RefreshAll();
            ReplayToast.Display(res.IsSuccess, Loc.T("playerActions.retranscribe"), res.IsSuccess ? clip.Filename : (res.Error ?? ""));
        });
        if (OAuthService.IsConnected())
        {
            AddItem(clip.Storage == "cloud" ? Loc.T("clipMenu.downloadCloud") : Loc.T("clipMenu.uploadCloud"), async () =>
            {
                try
                {
                    if (clip.Storage == "cloud") await CloudService.DownloadClipAsync(clip.Id);
                    else await CloudService.UploadClipAsync(clip.Id);
                    RefreshAll();
                }
                catch (Exception ex) { ReplayToast.Display(false, Loc.T("nav.cloud"), ex.Message); }
            });
        }
        if (_activeFolderId is { } fid && ViewFolders.Visibility == Visibility.Visible)
        {
            AddItem(Loc.T("clipMenu.removeFromFolder"), () =>
            {
                App.Folders.RemoveClip(fid, clip.Id);
                RefreshFolders();
            });
        }
        menu.Items.Add(new Separator());
        AddItem(Loc.T("playerActions.delete"), async () =>
        {
            if (!PromptDialog.Confirm(this, Loc.T("confirm.deleteClip.title"), clip.Filename, Loc.T("confirmDefault.yesLabel")))
                return;
            await App.ClipService.DeleteAsync(clip.Id, deleteFile: true);
            RefreshAll();
        });

        menu.PlacementTarget = btn;
        menu.IsOpen = true;

        void AddItem(string header, Action act)
        {
            var mi = new System.Windows.Controls.MenuItem { Header = header };
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
        }
    }

    private void OpenPlayer(long clipId, double? seekSeconds = null)
    {
        var clip = App.Clips.GetById(clipId);
        if (clip is null) return;
        EmbeddedPlayer.Open(clip, seekSeconds);
        PlayerOverlay.Visibility = Visibility.Visible;
    }

    private void OnPlayerFullscreenChanged(bool isFullscreen)
    {
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
        if (isFullscreen)
        {
            _preFullscreenWindowState = WindowState;
            if (WindowState == WindowState.Normal)
            {
                _preFsLeft = Left;
                _preFsTop = Top;
                _preFsWidth = Width;
                _preFsHeight = Height;
            }
            else
            {
                _preFsLeft = RestoreBounds.Left;
                _preFsTop = RestoreBounds.Top;
                _preFsWidth = RestoreBounds.Width;
                _preFsHeight = RestoreBounds.Height;
            }

            WindowControlsPanel.Visibility = Visibility.Collapsed;
            if (chrome is not null)
            {
                chrome.CaptionHeight = 0;
                chrome.ResizeBorderThickness = new Thickness(0);
            }

            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var screen = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
            var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var topLeft = transform.Transform(new System.Windows.Point(screen.Left, screen.Top));
            var bottomRight = transform.Transform(new System.Windows.Point(screen.Right, screen.Bottom));

            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            Left = topLeft.X;
            Top = topLeft.Y;
            Width = Math.Max(320, bottomRight.X - topLeft.X);
            Height = Math.Max(240, bottomRight.Y - topLeft.Y);
        }
        else
        {
            Topmost = false;
            ResizeMode = ResizeMode.CanResize;
            if (chrome is not null)
            {
                chrome.CaptionHeight = 34;
                chrome.ResizeBorderThickness = new Thickness(6);
            }
            WindowControlsPanel.Visibility = Visibility.Visible;
            Left = _preFsLeft;
            Top = _preFsTop;
            Width = _preFsWidth;
            Height = _preFsHeight;
            WindowState = _preFullscreenWindowState;
        }
    }

    private void ReplayOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == ReplayOverlay)
        {
            EmbeddedReplay.StopPolling();
            ReplayOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (ReplayOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                EmbeddedReplay.StopPolling();
                ReplayOverlay.Visibility = Visibility.Collapsed;
            }
            return;
        }
        if (PlayerOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (EmbeddedPlayer.IsFullscreen) EmbeddedPlayer.ExitFullscreen();
                else EmbeddedPlayer.ClosePlayer();
                return;
            }
            if (EmbeddedPlayer.IsBusy) return;
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

            if (e.Key is Key.Space or Key.K)
            {
                e.Handled = true;
                EmbeddedPlayer.TogglePlayPause(showFeedback: true);
            }
            else if (e.Key == Key.F)
            {
                e.Handled = true;
                EmbeddedPlayer.ToggleFullscreen();
            }
            else if (e.Key == Key.T)
            {
                e.Handled = true;
                EmbeddedPlayer.ToggleTheater();
            }
            else if (e.Key == Key.M)
            {
                e.Handled = true;
                EmbeddedPlayer.ToggleMute();
            }
            else if (e.Key == Key.C)
            {
                e.Handled = true;
                EmbeddedPlayer.ToggleCc();
            }
            else if (e.Key is Key.Left or Key.J)
            {
                e.Handled = true;
                EmbeddedPlayer.Rewind5();
            }
            else if (e.Key is Key.Right or Key.L)
            {
                e.Handled = true;
                EmbeddedPlayer.Forward5();
            }
            else if (e.Key == Key.Up)
            {
                e.Handled = true;
                EmbeddedPlayer.AdjustVolume(0.1);
            }
            else if (e.Key == Key.Down)
            {
                e.Handled = true;
                EmbeddedPlayer.AdjustVolume(-0.1);
            }
        }
    }

    // Folders Actions
    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptDialog.AskText(this, Loc.T("folders.newFolderLabel"), "", Loc.T("folders.addBtn"));
        if (string.IsNullOrWhiteSpace(name)) return;
        var created = App.Folders.Create(name);
        if (created is null)
            ReplayToast.Display(false, Loc.T("nav.folders"), Strings.Get("folder.duplicate", name));
        RefreshFolders();
    }

    private void BackFolders_Click(object sender, RoutedEventArgs e)
    {
        _activeFolderId = null;
        FolderDetailPanel.Visibility = Visibility.Collapsed;
        FoldersIndexPanel.Visibility = Visibility.Visible;
        RefreshFolders();
    }

    private void RenameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_activeFolderId is not { } fid) return;
        var newName = PromptDialog.AskText(this, Loc.T("folderDetail.rename"), "", Loc.T("player.editSave"), FolderDetailName.Text);
        if (string.IsNullOrWhiteSpace(newName)) return;
        App.Folders.Rename(fid, newName);
        RefreshFolders();
    }

    private void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_activeFolderId is not { } fid) return;
        if (!PromptDialog.Confirm(this, Loc.T("folderDetail.delete"), FolderDetailName.Text, Loc.T("confirmDefault.yesLabel")))
            return;
        App.Folders.Delete(fid);
        BackFolders_Click(sender, e);
    }

    private void FolderHideHome_Click(object sender, RoutedEventArgs e)
    {
        if (_activeFolderId is not { } fid) return;
        App.Folders.SetHiddenFromHome(fid, FolderHideHomeBox.IsChecked == true);
        RefreshClips();
    }

    private void SelectClipsForFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_activeFolderId is not { } fid) return;
        _selectionFolderId = fid;
        _selectedClipIds.Clear();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectionMode)));
        SelectionBar.Visibility = Visibility.Visible;
        SelectionAddBtn.IsEnabled = false;
        SelectionInfoText.Text = $"{Loc.Format("selection.info", ("folder", FolderDetailName.Text))} — 0 {Loc.T("selection.selectedWord")}";
        SwitchView("clips");
    }

    private void SelectionCancel_Click(object sender, RoutedEventArgs e)
    {
        _selectionFolderId = null;
        _selectedClipIds.Clear();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectionMode)));
        SelectionBar.Visibility = Visibility.Collapsed;
        SwitchView("folders");
    }

    private void SelectionAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_selectionFolderId is not { } fid) return;
        foreach (var cid in _selectedClipIds)
            App.Folders.AddClip(fid, cid);
        _selectionFolderId = null;
        _selectedClipIds.Clear();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectionMode)));
        SelectionBar.Visibility = Visibility.Collapsed;
        SwitchView("folders");
    }

    private async void UploadFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_activeFolderId is not { } fid) return;
        UploadFolderBtn.IsEnabled = false;
        try
        {
            var (uploaded, total, skipped, failed) = await CloudService.UploadFolderAsync(fid);
            ReplayToast.Display(failed == 0, Loc.T("nav.cloud"), $"{uploaded}/{total} (skip {skipped}, err {failed})");
            RefreshAll();
        }
        finally
        {
            UploadFolderBtn.IsEnabled = true;
        }
    }

    // Busy Overlay Helper ("Please wait...")
    private async Task ShowBusyAsync(string subtitle)
    {
        BusyTitleText.Text = Loc.T("busy.pleaseWait");
        BusySubText.Text = subtitle;
        BusyOverlay.Visibility = Visibility.Visible;
        await Dispatcher.Yield(DispatcherPriority.Render);
    }

    private void HideBusy() => BusyOverlay.Visibility = Visibility.Collapsed;

    // Tools: Scan, Transcribe, Replay
    private async void Scan_Click(object sender, RoutedEventArgs e) => await RunScanAsync();

    private async Task RunScanAsync()
    {
        BtnScan.IsEnabled = false;
        await ShowBusyAsync(Loc.T("busy.scanning"));
        try
        {
            var res = await Task.Run(() => Scanner.Scan(CancellationToken.None));
            RefreshAll();
            int added = Convert.ToInt32(res.GetValueOrDefault("added") ?? 0);
            int removed = Convert.ToInt32(res.GetValueOrDefault("removed") ?? 0);
            ReplayToast.Display(true, Loc.T("nav.scanFolder"), $"+{added} / -{removed}");
        }
        finally
        {
            HideBusy();
            BtnScan.IsEnabled = true;
        }
    }

    private void TranscribeNew_Click(object sender, RoutedEventArgs e) => _ = StartTranscribeAsync(force: false);
    private void RetranscribeAll_Click(object sender, RoutedEventArgs e) => _ = StartTranscribeAsync(force: true);

    private async Task StartTranscribeAsync(bool force)
    {
        await ShowBusyAsync(Loc.T("busy.startingTranscribe"));
        bool started;
        try
        {
            started = await Task.Run(() => TranscribeWorker.Start(force, out _));
        }
        finally
        {
            HideBusy();
        }
        if (!started)
        {
            ReplayToast.Display(false, Loc.T("nav.transcribeNew"), Loc.T("toast.transcribeAlreadyRunning"));
            return;
        }
        ProgressCloseBtn.Visibility = Visibility.Collapsed;
        ProgressBanner.Visibility = Visibility.Visible;
        BtnCancelTranscribe.Visibility = Visibility.Visible;
        _transcribePoll.Start();
        PollTranscribe();
    }

    private void CancelTranscribe_Click(object sender, RoutedEventArgs e) => TranscribeWorker.Cancel();

    private void PollTranscribe()
    {
        var snap = TranscribeState.Snapshot();
        ProgressBar.Maximum = Math.Max(1, snap.total);
        ProgressBar.Value = snap.done;
        ProgressText.Text = $"{snap.done} / {snap.total}";
        ProgressCurrentText.Text = snap.error ?? (snap.running ? Loc.T("progress.transcribing") : Loc.T("progress.done"));

        if (!snap.running)
        {
            _transcribePoll.Stop();
            BtnCancelTranscribe.Visibility = Visibility.Collapsed;
            ProgressCloseBtn.Visibility = Visibility.Visible;
            RefreshAll();
        }
    }

    private void ProgressClose_Click(object sender, RoutedEventArgs e) => ProgressBanner.Visibility = Visibility.Collapsed;

    private void Replay_Click(object sender, RoutedEventArgs e)
    {
        OpenReplayOverlay();
    }

    internal void OpenReplayOverlay()
    {
        EmbeddedReplay.Open();
        ReplayOverlay.Visibility = Visibility.Visible;
    }

    internal void OpenLastReplay()
    {
        long? clipId = null;
        if (ReplayService.LastSavedPath is { Length: > 0 } lastPath)
            clipId = App.Clips.GetIdByFilepath(lastPath);
        if (clipId is null)
        {
            var latest = App.Clips.List(null, "newest", 1, false);
            if (latest.Count > 0) clipId = Convert.ToInt64(latest[0]["id"]);
        }
        if (clipId is long id)
        {
            RefreshAll();
            OpenPlayer(id);
        }
        else
        {
            ReplayToast.Display(false, Loc.T("tray.noReplayTitle"), Loc.T("tray.noReplaySubtitle"));
        }
    }

    private void OnReplayHotkeyPressed()
    {
        _ = Task.Run(async () =>
        {
            try { await ReplayService.SaveAsync("hotkey"); }
            catch (Exception ex)
            {
                if (ReplayService.SaveInProgress) return;
                ReplayService.PlayCue(ok: false);
                _ = Dispatcher.BeginInvoke(() => ReplayToast.Display(false, Strings.Get("replay.saveFailedTitle"), ex.Message));
            }
        });
    }

    // Cloud
    private async Task RefreshCloudCardAsync()
    {
        var st = await OAuthService.StatusAsync();
        bool conn = st.TryGetValue("connected", out var c) && c is true;
        SidebarCloudDot.Fill = new SolidColorBrush(conn ? WpfColor.FromRgb(0x43, 0xD1, 0x7A) : WpfColor.FromRgb(0x8F, 0x8F, 0x8F));
        SidebarCloudStatus.Text = conn ? (st.GetValueOrDefault("email") as string ?? Loc.T("cloudx.sccOn")) : Loc.T("cloudx.sccOff");
    }

    private async Task RefreshCloudViewAsync()
    {
        CloudSetupPanel.Visibility = Visibility.Collapsed;
        CloudConnectPanel.Visibility = Visibility.Collapsed;
        CloudAccountPanel.Visibility = Visibility.Collapsed;

        if (!OAuthService.IsConfigured())
        {
            CloudDataPathText.Text = Config.GoogleClientPath;
            CloudSetupPanel.Visibility = Visibility.Visible;
            return;
        }

        var st = await OAuthService.StatusAsync();
        bool conn = st.TryGetValue("connected", out var c) && c is true;
        if (!conn)
        {
            CloudConnectPanel.Visibility = Visibility.Visible;
            return;
        }

        CloudAccName.Text = st.GetValueOrDefault("name") as string ?? "Google Drive";
        CloudAccEmail.Text = st.GetValueOrDefault("email") as string ?? "";
        long used = Convert.ToInt64(st.GetValueOrDefault("quota_used") ?? 0L);
        long limit = Convert.ToInt64(st.GetValueOrDefault("quota_limit") ?? 0L);
        CloudQuotaBar.Value = limit > 0 ? Math.Clamp((double)used / limit, 0, 1) : 0;
        CloudQuotaText.Text = limit > 0 ? $"{ClipCardItem.FormatBytes(used)} / {ClipCardItem.FormatBytes(limit)}" : ClipCardItem.FormatBytes(used);
        if (limit > 0)
        {
            CloudDiskBar.Value = Math.Clamp((double)used / limit, 0, 1);
            CloudFreeText.Text = $"{ClipCardItem.FormatBytes(Math.Max(0, limit - used))} free";
        }
        CloudAccountPanel.Visibility = Visibility.Visible;
        await RefreshCloudCardAsync();
    }

    private void CloudRecheck_Click(object sender, RoutedEventArgs e) => _ = RefreshCloudViewAsync();

    private async void CloudConnect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var url = OAuthService.BeginConnect();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var oauthUri) || oauthUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("Invalid OAuth URL.");
            Process.Start(new ProcessStartInfo(oauthUri.AbsoluteUri) { UseShellExecute = true });
            CloudConnectBtn.IsEnabled = false;
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(1500);
                if (OAuthService.IsConnected()) break;
            }
            CloudConnectBtn.IsEnabled = true;
            await RefreshCloudViewAsync();
        }
        catch (Exception ex)
        {
            CloudConnectBtn.IsEnabled = true;
            ReplayToast.Display(false, Loc.T("nav.cloud"), ex.Message);
        }
    }

    private async void CloudSync_Click(object sender, RoutedEventArgs e)
    {
        await ShowBusyAsync(Loc.T("busy.syncing"));
        try
        {
            var res = await CloudService.SyncFromCloudAsync();
            RefreshAll();
            ReplayToast.Display(true, Loc.T("nav.cloud"), $"+{res.Imported}, ~{res.Updated}");
        }
        catch (Exception ex) { ReplayToast.Display(false, Loc.T("nav.cloud"), ex.Message); }
        finally { HideBusy(); }
    }

    private async void CloudDisconnect_Click(object sender, RoutedEventArgs e)
    {
        await OAuthService.DisconnectAsync();
        await RefreshCloudViewAsync();
        await RefreshCloudCardAsync();
    }

    // Settings
    private void SettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = SettingsSearchBox.Text.Trim();
        foreach (var card in new[] { SetGroupAppearance, SetGroupLanguage, SetGroupClips, SetGroupHelp })
            card.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Visible;
        // Filter cards by matching child TextBlocks
        if (!string.IsNullOrEmpty(q))
        {
            foreach (var card in new[] { SetGroupAppearance, SetGroupLanguage, SetGroupClips, SetGroupHelp })
            {
                bool match = FindVisualChildren<TextBlock>(card).Any(tb => tb.Text.Contains(q, StringComparison.OrdinalIgnoreCase));
                card.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            if (child is T t) yield return t;
            foreach (var childOfChild in FindVisualChildren<T>(child)) yield return childOfChild;
        }
    }

    private void ThemeCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents || ThemeCombo.SelectedValue is not string theme) return;
        ThemeManager.Apply(theme);
    }

    private void OrbToggle_Click(object sender, RoutedEventArgs e)
    {
        bool on = OrbToggleBox.IsChecked == true;
        OrbsCanvas.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on) _orbStoryboard?.Resume();
        else _orbStoryboard?.Pause();
    }

    private async void LangCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents || LangCombo.SelectedValue is not string lang) return;
        await ShowBusyAsync(Loc.T("busy.switchingLanguage"));
        try
        {
            Loc.Instance.CurrentLanguage = lang;
            _suppressUiEvents = true;
            var prevSort = SortCombo.SelectedValue as string ?? "newest";
            var prevTheme = ThemeCombo.SelectedValue as string ?? ThemeManager.CurrentTheme;
            InitCombos();
            SortCombo.SelectedValue = prevSort;
            ThemeCombo.SelectedValue = prevTheme;
            _suppressUiEvents = false;
            RefreshAll();
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        finally
        {
            HideBusy();
        }
    }

    private async void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ConfigFolderDialog(false, Settings.GetClipsRoot()) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            Settings.SetClipsRoot(dlg.SelectedPath);
            SettingsFolderPath.Text = dlg.SelectedPath;
            await RunScanAsync();
        }
    }

    private void HideFolderClips_Click(object sender, RoutedEventArgs e)
    {
        Settings.SetHideFolderClips(HideFolderClipsBox.IsChecked == true);
        RefreshClips();
    }

    private void FavoritesAfterRecent_Click(object sender, RoutedEventArgs e)
    {
        Settings.SetFavoritesAfterRecent(FavoritesAfterRecentBox.IsChecked == true);
        RefreshClips();
    }

    private void RunOnboarding_Click(object sender, RoutedEventArgs e)
    {
        var ob = new OnboardingWindow { Owner = this };
        ob.ShowDialog();
    }

    private void OpenDiscord_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://discord.gg/nZ3BK7BPKv") { UseShellExecute = true }); } catch { }
    }

    // Updates
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var res = await UpdateService.CheckAsync();
            if (res.GetValueOrDefault("available") is true)
            {
                UpdateVersionText.Text = $"{res.GetValueOrDefault("latest_version")}";
                UpdateBanner.Visibility = Visibility.Visible;
            }
        }
        catch { }
    }

    private void UpdateLater_Click(object sender, RoutedEventArgs e) => UpdateBanner.Visibility = Visibility.Collapsed;
    private async void UpdateInstall_Click(object sender, RoutedEventArgs e)
    {
        UpdateService.BeginInstall();
        UpdateBanner.Visibility = Visibility.Collapsed;
        ReplayToast.Display(true, Loc.T("update.title"), "Downloading...");
        await Task.CompletedTask;
    }

    // Dev Tools
    private void DevLogs_Click(object sender, RoutedEventArgs e)
    {
#if KEEPCLIP_DEV
        new LogsWindow { Owner = this }.Show();
#endif
    }

    private void DevResources_Click(object sender, RoutedEventArgs e)
    {
#if KEEPCLIP_DEV
        new ResourcesWindow { Owner = this }.Show();
#endif
    }
}
