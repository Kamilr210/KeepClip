namespace KeepClip.Endpoints;

/// <summary>Clip endpoints — thin: list/favorite/thumb hit the repository directly, while
/// fix/cut/delete/retranscribe delegate to <see cref="ClipService"/>.</summary>
public static class ClipEndpoints
{
    public static void MapClipEndpoints(this WebApplication app)
    {
        app.MapGet("/api/clips", (string? game, string? sort, int? limit, int? favorite, ClipRepository clips) =>
            Results.Json(clips.List(game, sort, limit ?? 200, favorite is not null && favorite != 0)));

        app.MapPost("/api/clips/{clipId:long}/favorite", (long clipId, ClipRepository clips) =>
        {
            Heartbeat.Touch();
            var state = clips.ToggleFavorite(clipId);
            if (state is null) return Api.Detail(404, "Klip nie istnieje.");
            DevLog.Add($"Ulubione: klip #{clipId} {(state.Value ? "dodany do ulubionych" : "usunięty z ulubionych")}");
            return Results.Json(new { ok = true, id = clipId, favorite = state.Value });
        });

        app.MapPost("/api/clips/{clipId:long}/retranscribe", (long clipId, ClipService clips) =>
        {
            Heartbeat.Touch();
            return Respond(clips.Retranscribe(clipId));
        });

        app.MapPost("/api/clips/{clipId:long}/fix", (long clipId, ClipService clips) =>
        {
            Heartbeat.Touch();
            return Respond(clips.Fix(clipId));
        });

        app.MapPost("/api/clips/{clipId:long}/cut", (long clipId, CutPayload body, ClipService clips) =>
        {
            Heartbeat.Touch();
            return Respond(clips.Cut(clipId, body.start, body.end, body.target_size_mb));
        });

        app.MapDelete("/api/clips/{clipId:long}", async (long clipId, bool? delete_file, ClipService clips) =>
            Respond(await clips.DeleteAsync(clipId, delete_file ?? true)));

        // Thumbnails (lazy generation + remember failures). A media-serving endpoint, so it
        // talks to the repository directly rather than going through a service.
        app.MapGet("/thumb/{clipId:long}", (long clipId, ClipRepository clips) =>
        {
            var p = Config.ThumbPath(clipId);
            if (!File.Exists(p))
            {
                var row = clips.GetRow(clipId);
                if (row is null || Convert.ToInt64(row["has_thumb"] ?? 0L) == 2)
                    return Api.Detail(404, "no thumbnail");
                if (!Media.MakeThumbnail(clipId, row["filepath"] as string ?? ""))
                {
                    clips.SetThumbState(clipId, 2);   // don't retry on every scroll
                    return Api.Detail(404, "no thumbnail");
                }
                clips.SetThumbState(clipId, 1);
            }
            return Results.File(p, "image/jpeg");
        });
    }

    private static IResult Respond(Result<Dictionary<string, object?>> r)
        => r.IsSuccess ? Results.Json(r.Value) : Api.Detail(r.Code, r.Error!);
}

record CutPayload(double start, double end, double? target_size_mb = null);
