using System.Text.RegularExpressions;

namespace KeepClip.Endpoints;

/// <summary>Full-text transcript search (<c>/api/search</c>). Builds the FTS expression here;
/// the actual query lives in <see cref="SegmentRepository"/>.</summary>
public static class SearchEndpoints
{
    // Turn 'nie no co ty robisz' into '"nie"* AND "no"* AND ...' (AND'ed prefix tokens)
    // so substring/inflection matches work with the diacritic-insensitive tokenizer.
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
