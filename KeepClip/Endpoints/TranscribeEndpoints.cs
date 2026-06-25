using System.Text.Json;

namespace KeepClip.Endpoints;

/// <summary>Whisper transcription run control + the SSE progress feed.</summary>
public static class TranscribeEndpoints
{
    // The manual SSE path needs its own options (Results.Json uses the DI-configured ones);
    // keep keys verbatim (snake_case), like the rest of the API.
    private static readonly JsonSerializerOptions SseJson =
        new() { PropertyNamingPolicy = null, DictionaryKeyPolicy = null };

    public static void MapTranscribeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/transcribe/status", () => Results.Json(TranscribeState.Snapshot()));

        app.MapPost("/api/transcribe/start", (bool? force) =>
        {
            Heartbeat.Touch();
            bool f = force ?? false;
            if (!TranscribeWorker.Start(f, out int total))
                return Results.Json(new { started = false, reason = "already_running" });
            DevLog.Add($"Transkrypcja: start{(f ? " (ponownie, wszystkie klipy)" : "")} — {total} klipów do przetworzenia");
            return Results.Json(new Dictionary<string, object?> { ["started"] = true, ["force"] = f, ["total"] = total });
        });

        app.MapPost("/api/transcribe/cancel", () =>
        {
            Heartbeat.Touch();
            lock (TranscribeState.Lock)
            {
                if (!TranscribeState.Running)
                    return Results.Json(new { cancelled = false, reason = "not_running" });
                TranscribeState.Cancel = true;   // the worker checks this between clips
            }
            DevLog.Add("Transkrypcja: anulowano (zatrzyma się po bieżącym klipie)");
            return Results.Json(new { cancelled = true });
        });

        // Server-Sent Events progress feed. Emits a frame whenever the snapshot changes,
        // then one final frame once a run has finished.
        app.MapGet("/api/transcribe/stream", async (HttpContext ctx) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";   // disable any proxy buffering

            var ct = ctx.RequestAborted;
            string? last = null;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var snap = TranscribeState.Snapshot();
                    var payload = JsonSerializer.Serialize(snap, SseJson);
                    if (payload != last)
                    {
                        await ctx.Response.WriteAsync($"data: {payload}\n\n", ct);
                        await ctx.Response.Body.FlushAsync(ct);
                        last = payload;
                    }
                    // Terminal state already emitted above (running flipped / finished_at set
                    // both change the payload) → close the stream.
                    if (!snap.running && snap.finished_at is not null) break;
                    await Task.Delay(500, ct);
                }
            }
            catch (OperationCanceledException) { /* client disconnected — normal */ }
        });
    }
}
