using System.Windows;
using System.Windows.Input;
using KeepClip.Localization;
using Microsoft.Win32;

namespace KeepClip.UI;

public partial class ConfigFolderDialog : Window
{
    private readonly bool _isFirstRun;
    public string SelectedPath { get; private set; } = "";

    public ConfigFolderDialog(bool isFirstRun, string currentPath)
    {
        InitializeComponent();
        _isFirstRun = isFirstRun;
        TitleText.Text = isFirstRun ? Loc.T("config.titleFirst") : Loc.T("config.titleChange");
        IntroText.Text = isFirstRun ? Loc.T("config.introFirst") : Loc.T("config.introChange");
        PathLabel.Text = Loc.T("configLabel.folderPath");
        CancelBtn.Content = Loc.T("confirm.cancelBtn");
        CancelBtn.Visibility = isFirstRun ? Visibility.Collapsed : Visibility.Visible;
        CloseXBtn.Visibility = isFirstRun ? Visibility.Collapsed : Visibility.Visible;
        SaveBtn.Content = isFirstRun ? Loc.T("config.saveFirst") : Loc.T("config.saveChange");
        PathBox.Text = currentPath;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_isFirstRun)
        {
            e.Handled = true;
            DialogResult = false;
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Save_Click(sender, e);
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = Loc.T("ui.browseFolder"),
            InitialDirectory = Directory.Exists(PathBox.Text) ? PathBox.Text : Config.DefaultClipsRoot
        };
        if (dlg.ShowDialog(this) == true)
            PathBox.Text = dlg.FolderName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (TranscribeState.Snapshot().running)
        {
            ShowError(Strings.Get("config.transcribeBusy"));
            return;
        }
        var path = PathBox.Text.Trim().Trim('"').Trim('\'');
        if (string.IsNullOrEmpty(path))
        {
            ShowError(Strings.Get("config.pathRequired"));
            return;
        }
        if (!Path.Exists(path))
        {
            ShowError(Strings.Get("config.pathMissing", path));
            return;
        }
        if (!Directory.Exists(path))
        {
            ShowError(Strings.Get("config.notAFolder", path));
            return;
        }
        SelectedPath = path;
        DialogResult = true;
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
