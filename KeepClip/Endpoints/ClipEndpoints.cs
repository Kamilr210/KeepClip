using System.Text;

namespace KeepClip.Endpoints;

public static class ClipEndpoints
{
    public static void MapClipEndpoints(this WebApplication app)
    {
        app.MapGet("/api/clips", (string? game, string? sort, int? limit, int? favorite, ClipRepository clips) =>
            Results.Json(clips.List(game, sort, limit ?? 200, favorite is not null && favorite != 0)));

        app.MapGet("/api/single-operation/status", () =>
            Results.Json(SingleOperationProgress.Snapshot()));

        app.MapPost("/api/clips/{clipId:long}/favorite", (long clipId, ClipRepository clips) =>
        {
            Heartbeat.Touch();
            var state = clips.ToggleFavorite(clipId);
            if (state is null) return Api.Detail(404, Strings.Get("clip.notFound"));
            DevLog.Add($"Ulubione: klip #{clipId} {(state.Value ? "dodany do ulubionych" : "usunięty z ulubionych")}");
            return Results.Json(new { ok = true, id = clipId, favorite = state.Value });
        });

        app.MapMethods("/api/clips/{clipId:long}/transcript.txt", new[] { "GET", "HEAD" },
            (long clipId, ClipRepository clips, SegmentRepository segments) =>
        {
            Heartbeat.Touch();
            var clip = clips.GetById(clipId);
            if (clip is null) return Api.Detail(404, Strings.Get("clip.notFound"), "clipNotFound");

            var rows = segments.ListByClipId(clipId);
            if (rows.Count == 0)
                return Api.Detail(404, Strings.Get("transcript.empty"), "transcriptEmpty");

            var text = new StringBuilder();
            text.AppendLine(clip.Filename);
            var recorded = DateTimeOffset.FromUnixTimeMilliseconds((long)(clip.Mtime * 1000)).LocalDateTime;
            text.AppendLine($"{clip.Game} · {recorded:yyyy-MM-dd HH:mm}");
            text.AppendLine();
            foreach (var row in rows)
            {
                var start = TimeSpan.FromSeconds(Convert.ToDouble(row["start_s"] ?? 0.0));
                var stamp = start.TotalHours >= 1 ? $"{start:h\\:mm\\:ss}" : $"{start:mm\\:ss}";
                text.AppendLine($"[{stamp}] {(row["text"] as string ?? "").Trim()}");
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(text.ToString());
            var name = Path.GetFileNameWithoutExtension(clip.Filename);
            return Results.File(bytes, "text/plain; charset=utf-8",
                DisplayHelper.SafeFolderName($"{name}.txt"));
        });

        app.MapPost("/api/clips/{clipId:long}/retranscribe", (long clipId, ClipService clips) =>
        {
            Heartbeat.Touch();
            var operationId = SingleOperationProgress.Start("transcription", clipId);
            try
            {
                var result = clips.Retranscribe(clipId,
                    (progress, stage) => SingleOperationProgress.Report(operationId, progress, stage));
                SingleOperationProgress.Finish(operationId, result.IsSuccess, result.Error);
                return Respond(result);
            }
            catch (Exception ex)
            {
                SingleOperationProgress.Finish(operationId, false, ex.Message);
                throw;
            }
        });

        app.MapPost("/api/clips/{clipId:long}/fix", (long clipId, ClipService clips) =>
        {
            Heartbeat.Touch();
            return Respond(clips.Fix(clipId));
        });

        app.MapPost("/api/clips/{clipId:long}/cut", (long clipId, CutPayload body, ClipService clips) =>
        {
            Heartbeat.Touch();
            var operationId = SingleOperationProgress.Start("cut", clipId);
            try
            {
                var result = clips.Cut(clipId, body.start, body.end, body.target_size_mb,
                    (progress, stage) => SingleOperationProgress.Report(operationId, progress, stage));
                SingleOperationProgress.Finish(operationId, result.IsSuccess, result.Error);
                return Respond(result);
            }
            catch (Exception ex)
            {
                SingleOperationProgress.Finish(operationId, false, ex.Message);
                throw;
            }
        });

        app.MapDelete("/api/clips/{clipId:long}", async (long clipId, bool? delete_file, ClipService clips) =>
            Respond(await clips.DeleteAsync(clipId, delete_file ?? true)));

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
            clips.SetThumbState(clipId, 2);
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
