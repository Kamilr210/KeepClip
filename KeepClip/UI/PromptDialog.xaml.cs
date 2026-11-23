using System.Windows;
using KeepClip.Localization;

namespace KeepClip.UI;

public partial class PromptDialog : Window
{
    private readonly bool _isInput;
    public string InputValue => InputBox.Text.Trim();

    private PromptDialog(string title, string body, string okLabel, string cancelLabel, bool isDanger, bool isInput, string? initialValue)
    {
        InitializeComponent();
        _isInput = isInput;
        TitleText.Text = title;
        BodyText.Text = body;
        BodyText.Visibility = string.IsNullOrWhiteSpace(body) ? Visibility.Collapsed : Visibility.Visible;
        CancelBtn.Content = cancelLabel;
        OkBtn.Content = okLabel;
        if (isDanger)
            OkBtn.Style = (Style)FindResource("DangerButton");
        if (isInput)
        {
            InputBox.Visibility = Visibility.Visible;
            InputBox.Text = initialValue ?? "";
            Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
        }
    }

    public static bool Confirm(Window owner, string title, string body, string okLabel, bool isDanger = true)
    {
        var dlg = new PromptDialog(title, body, okLabel, Loc.T("confirm.cancelBtn"), isDanger, false, null) { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    public static string? AskText(Window owner, string title, string body, string okLabel, string? initial = null)
    {
        var dlg = new PromptDialog(title, body, okLabel, Loc.T("confirm.cancelBtn"), false, true, initial) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.InputValue : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_isInput && string.IsNullOrWhiteSpace(InputBox.Text))
        {
            ErrorText.Text = Strings.Get("folder.nameRequired");
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

