namespace KeepClip.Endpoints;

/// <summary>Read a clip's transcript segments and edit individual lines.</summary>
public static class SegmentEndpoints
{
    public static void MapSegmentEndpoints(this WebApplication app)
    {
        app.MapGet("/api/segments/{clipId:long}", (long clipId, ClipRepository clips, SegmentRepository segments) =>
        {
            var clip = clips.GetRow(clipId);
            if (clip is null) return Api.Detail(404, "clip not found");
            var segs = segments.ListByClipId(clipId);
            return Results.Json(new Dictionary<string, object?> { ["clip"] = clip, ["segments"] = segs });
        });

        app.MapPatch("/api/segments/{segmentId:long}", (long segmentId, SegmentPayload body, SegmentRepository segments) =>
        {
            Heartbeat.Touch();
            var text = body.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(text)) return Api.Detail(422, "Tekst nie może być pusty.");
            if (!segments.UpdateText(segmentId, text)) return Api.Detail(404, "Segment nie istnieje.");
            DevLog.Add($"Edycja transkrypcji: zapisano segment #{segmentId}");
            return Results.Json(new { ok = true, id = segmentId, text });
        });
    }
}

record SegmentPayload(string text);
