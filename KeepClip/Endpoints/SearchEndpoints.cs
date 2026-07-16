using System.Text.RegularExpressions;

namespace KeepClip.Endpoints;

public static class SearchEndpoints
{
// Każde słowo staje się tokenem prefiksowym połączonym logiczną koniunkcją, dzięki czemu
    // wyszukiwanie toleruje odmianę słów i znaki diakrytyczne.
    private static readonly Regex FtsSafe = new(@"[\wÀ-ſ]+", RegexOptions.Compiled);

    private static string ToFtsQuery(string q)
    {
        var tokens = FtsSafe.Matches(q.ToLowerInvariant()).Select(m => m.Value).ToList();
        if (tokens.Count == 0) return "";
        return string.Join(" AND ", tokens.Select(t => $"\"{t}\"*"));
    }

    public static void MapSearchEndpoints(this WebApplication app)
    {
        app.MapGet("/api/search", (string q, string? sort, int? limit, SegmentRepository segments) =>
        {
            if (string.IsNullOrEmpty(q)) return Api.Detail(422, "q is required");
            var ftsQ = ToFtsQuery(q);
            if (string.IsNullOrEmpty(ftsQ))
                return Results.Json(new Dictionary<string, object?> { ["query"] = q, ["results"] = Array.Empty<object>() });
            var rows = segments.Search(ftsQ, sort, limit ?? 100);
            return Results.Json(new Dictionary<string, object?>
            {
                ["query"] = q, ["fts"] = ftsQ, ["results"] = rows,
            });
        });
    }
}
