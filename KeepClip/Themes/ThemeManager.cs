using System.Windows;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;

namespace KeepClip.Themes;

internal static class ThemeManager
{
    public static string CurrentTheme => Settings.GetString("accent_theme") == "green" ? "green" : "purple";

    public static void Apply(string theme)
    {
        var norm = theme == "green" ? "green" : "purple";
        Settings.SetString("accent_theme", norm);

        var res = WpfApplication.Current?.Resources;
        if (res is null) return;

        if (norm == "green")
        {
            res["AccentColor"] = WpfColor.FromRgb(0x28, 0xD9, 0x12);
            res["AccentVividColor"] = WpfColor.FromRgb(0x00, 0xE6, 0x76);
            res["AccentPaleColor"] = WpfColor.FromRgb(0xBA, 0xFF, 0xAA);
            res["AccentPanelColor"] = WpfColor.FromRgb(0x15, 0x38, 0x1D);
            res["AccentOrbColor"] = WpfColor.FromRgb(0x23, 0xE6, 0x00);
            res["AccentOrbDeepColor"] = WpfColor.FromRgb(0x0A, 0x70, 0x1C);
        }
        else
        {
            res["AccentColor"] = WpfColor.FromRgb(0x7C, 0x5C, 0xFF);
            res["AccentVividColor"] = WpfColor.FromRgb(0xB1, 0x4B, 0xFF);
            res["AccentPaleColor"] = WpfColor.FromRgb(0xC9, 0xB8, 0xFF);
            res["AccentPanelColor"] = WpfColor.FromRgb(0x2B, 0x1F, 0x4A);
            res["AccentOrbColor"] = WpfColor.FromRgb(0x7C, 0x22, 0xE0);
            res["AccentOrbDeepColor"] = WpfColor.FromRgb(0x4A, 0x0D, 0x9A);
        }
    }
}

