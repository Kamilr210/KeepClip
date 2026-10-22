using System.Text.RegularExpressions;

namespace KeepClip.Endpoints;

public static class SearchEndpoints
{
    private const int MaxQueryLength = 512;
    private const int MaxQueryTerms = 32;
    private static readonly Regex FtsSafe = new(@"[\wÀ-ſ]+", RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(250));

    internal static string ToFtsQuery(string q)
    {
        if (q.Length > MaxQueryLength) throw new ArgumentException("Search query is too long.", nameof(q));
        var tokens = FtsSafe.Matches(q.ToLowerInvariant()).Select(m => m.Value).ToList();
        if (tokens.Count > MaxQueryTerms) throw new ArgumentException("Search query has too many terms.", nameof(q));
        if (tokens.Count == 0) return "";
        return string.Join(" AND ", tokens.Select(t => $"\"{t}\"*"));
    }

    public static void MapSearchEndpoints(this WebApplication app)
    {
        app.MapGet("/api/search", (string q, string? sort, int? limit, bool? home, SegmentRepository segments) =>
        {
            if (string.IsNullOrEmpty(q)) return Api.Detail(422, Strings.Get("search.queryRequired"));
            string ftsQ;
            try { ftsQ = ToFtsQuery(q); }
            catch (ArgumentException) { return Api.Detail(422, Strings.Get("search.queryTooComplex", MaxQueryLength, MaxQueryTerms), "searchQueryTooComplex"); }
            catch (RegexMatchTimeoutException) { return Api.Detail(422, Strings.Get("search.queryTooComplex", MaxQueryLength, MaxQueryTerms), "searchQueryTooComplex"); }
            if (string.IsNullOrEmpty(ftsQ))
                return Results.Json(new Dictionary<string, object?> { ["query"] = q, ["results"] = Array.Empty<object>() });
            var rows = segments.Search(ftsQ, sort, limit ?? 100, home == true, home == true && Settings.GetHideFolderClips());
            return Results.Json(new Dictionary<string, object?>
            {
                ["query"] = q, ["fts"] = ftsQ, ["results"] = rows,
            });
        });
    }
}
