using System.Text.RegularExpressions;

namespace KeepClip.Services;

public static class SearchService
{
    public const int MaxQueryLength = 512;
    public const int MaxQueryTerms = 32;
    private static readonly Regex FtsSafe = new(@"[\wÀ-ſ]+", RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(250));

    public static string ToFtsQuery(string q)
    {
        if (q.Length > MaxQueryLength) throw new ArgumentException("Search query is too long.", nameof(q));
        var tokens = FtsSafe.Matches(q.ToLowerInvariant()).Select(m => m.Value).ToList();
        if (tokens.Count > MaxQueryTerms) throw new ArgumentException("Search query has too many terms.", nameof(q));
        if (tokens.Count == 0) return "";
        return string.Join(" AND ", tokens.Select(t => $"\"{t}\"*"));
    }
}

