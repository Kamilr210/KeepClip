using System.ComponentModel;
using System.Globalization;
using System.Windows.Media.Imaging;
using KeepClip.Localization;

namespace KeepClip.UI;

internal sealed class ClipCardItem : INotifyPropertyChanged
{
    public long Id { get; init; }
    public string Game { get; init; } = "";
    public string Filename { get; init; } = "";
    public string Filepath { get; init; } = "";
    public long SizeBytes { get; init; }
    public double Mtime { get; init; }
    public double Duration { get; init; }
    public bool Transcribed { get; init; }
    public string Storage { get; init; } = "local";
    public string? RemoteId { get; init; }
    public string? Snippet { get; init; }
    public double? HitTime { get; init; }
    public bool IsMosaicHero { get; init; }

    private bool _favorite;
    public bool Favorite
    {
        get => _favorite;
        set
        {
            if (_favorite == value) return;
            _favorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Favorite)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FavStar)));
        }
    }

    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }

    public string FavStar => Favorite ? "★" : "☆";
    public bool IsCloud => Storage == "cloud";
    public bool HasSnippet => !string.IsNullOrWhiteSpace(Snippet);
    public string DisplayGame => Game == "_root" ? Loc.T("search.allGames") : Game;
    public string DurationText => FormatDuration(Duration);
    public string SizeText => FormatBytes(SizeBytes);
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds((long)(Mtime * 1000))
        .LocalDateTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
    public string MetaText => $"{DurationText} · {SizeText} · {DateText}";
    public string TranscribedBadge => Transcribed ? "✓ " + Loc.T("stat.transcribed") : Loc.T("clip.notTranscribed");

    public BitmapImage? Thumbnail
    {
        get
        {
            var path = Config.ThumbPath(Id);
            if (!File.Exists(path)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 360;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static ClipCardItem FromClip(Clip c, bool mosaicHero = false) => new()
    {
        Id = c.Id,
        Game = c.Game,
        Filename = c.Filename,
        Filepath = c.Filepath ?? "",
        SizeBytes = c.SizeBytes,
        Mtime = c.Mtime,
        Duration = c.Duration,
        Transcribed = !string.IsNullOrEmpty(c.TranscribedAt),
        Favorite = c.Favorite != 0,
        Storage = c.Storage ?? "local",
        RemoteId = c.RemoteId,
        IsMosaicHero = mosaicHero,
    };

    public static ClipCardItem FromRow(Dictionary<string, object?> row, bool mosaicHero = false) => new()
    {
        Id = Convert.ToInt64(row["id"] ?? 0L),
        Game = row.GetValueOrDefault("game") as string ?? "",
        Filename = row.GetValueOrDefault("filename") as string ?? "",
        Filepath = row.GetValueOrDefault("filepath") as string ?? "",
        SizeBytes = Convert.ToInt64(row.GetValueOrDefault("size_bytes") ?? 0L),
        Mtime = Convert.ToDouble(row.GetValueOrDefault("mtime") ?? 0.0),
        Duration = Convert.ToDouble(row.GetValueOrDefault("duration") ?? 0.0),
        Transcribed = !string.IsNullOrEmpty(row.GetValueOrDefault("transcribed_at") as string),
        Favorite = Convert.ToInt64(row.GetValueOrDefault("favorite") ?? 0L) != 0,
        Storage = row.GetValueOrDefault("storage") as string ?? "local",
        RemoteId = row.GetValueOrDefault("remote_id") as string,
        IsMosaicHero = mosaicHero,
    };

    public static ClipCardItem FromSearchRow(Dictionary<string, object?> row) => new()
    {
        Id = Convert.ToInt64(row["clip_id"] ?? 0L),
        Game = row.GetValueOrDefault("game") as string ?? "",
        Filename = row.GetValueOrDefault("filename") as string ?? "",
        Filepath = row.GetValueOrDefault("filepath") as string ?? "",
        SizeBytes = Convert.ToInt64(row.GetValueOrDefault("size_bytes") ?? 0L),
        Mtime = Convert.ToDouble(row.GetValueOrDefault("mtime") ?? 0.0),
        Duration = Convert.ToDouble(row.GetValueOrDefault("duration") ?? 0.0),
        Transcribed = true,
        Favorite = Convert.ToInt64(row.GetValueOrDefault("favorite") ?? 0L) != 0,
        Storage = row.GetValueOrDefault("storage") as string ?? "local",
        RemoteId = row.GetValueOrDefault("remote_id") as string,
        Snippet = (row.GetValueOrDefault("snippet") as string ?? "").Replace("<mark>", "").Replace("</mark>", ""),
        HitTime = Convert.ToDouble(row.GetValueOrDefault("start_s") ?? 0.0),
    };

    public static string FormatDuration(double sec)
    {
        if (sec <= 0 || double.IsNaN(sec)) return "0:00";
        var ts = TimeSpan.FromSeconds(sec);
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}" : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        string[] units = { "KB", "MB", "GB", "TB" };
        int idx = -1;
        while (v >= 1024 && idx < units.Length - 1) { v /= 1024; idx++; }
        return $"{v:0.##} {units[Math.Max(0, idx)]}";
    }
}
