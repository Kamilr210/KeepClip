namespace KeepClip.Endpoints;

/// <summary>Instant-replay buffer control: status, config, manual save, audio devices.</summary>
public static class ReplayEndpoints
{
    public static void MapReplayEndpoints(this WebApplication app)
    {
        app.MapGet("/api/replay/status", () =>
        {
            Heartbeat.Touch();
            return Results.Json(ReplayService.Status());
        });

        app.MapPost("/api/replay/config", (ReplayConfigPayload body) =>
        {
            Heartbeat.Touch();
            if (body.duration_s is < 15 or > 600)
                return Api.Detail(400, "Długość powtórki musi być w zakresie 15–600 sekund.");
            if (body.fps is not null && body.fps is not (30 or 60 or 90))
                return Api.Detail(400, "Obsługiwane wartości FPS: 30, 60 lub 90.");
            if (body.quality is not null && body.quality is not ("low" or "medium" or "high"))
                return Api.Detail(400, "Jakość musi być jedną z: low, medium, high.");
            if (body.hotkey is not null && !HotkeyManager.TryParse(body.hotkey, out _, out _))
                return Api.Detail(400, "Nieprawidłowy skrót — użyj modyfikatora i klawisza, np. Alt+F10.");

            if (body.enabled is not null) Settings.SetString("replay_enabled", body.enabled.Value ? "1" : "0");
            if (body.background is not null) Settings.SetString("replay_background", body.background.Value ? "1" : "0");
            if (body.duration_s is not null) Settings.SetString("replay_duration_s", body.duration_s.Value.ToString());
            if (body.fps is not null) Settings.SetString("replay_fps", body.fps.Value.ToString());
            if (body.quality is not null) Settings.SetString("replay_quality", body.quality);
            if (body.hotkey is not null) Settings.SetString("replay_hotkey", body.hotkey);
            if (body.mic is not null) Settings.SetString("replay_mic", body.mic.Value ? "1" : "0");
            // Audio device picks: the modal always sends both (null = "default" option), so a
            // null clears the stored ID back to the robust default rather than meaning "absent".
            Settings.SetString("replay_audio_output", body.audio_output ?? "");
            Settings.SetString("replay_audio_input", body.audio_input ?? "");

            ReplayService.ApplyConfig();   // start/stop/restart the buffer to match
            HotkeyManager.Refresh();       // re-register the (possibly new) combo
            return Results.Json(ReplayService.Status());
        });

        app.MapPost("/api/replay/save", async () =>
        {
            Heartbeat.Touch();
            try { return Results.Json(await ReplayService.SaveAsync("ui")); }
            catch (InvalidOperationException ex) { return Api.Detail(409, ex.Message); }
            catch (Exception ex) { return Api.Detail(500, ex.Message); }
        });

        // Audio output/input devices for the replay settings dropdowns.
        app.MapGet("/api/replay/audio-devices", () =>
        {
            Heartbeat.Touch();
            return Results.Json(ReplayService.AudioDevices());
        });
    }
}

record ReplayConfigPayload(bool? enabled, bool? background, int? duration_s, int? fps, string? quality, string? hotkey, bool? mic, string? audio_output, string? audio_input);
