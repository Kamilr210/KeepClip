namespace KeepClip.Endpoints;

public static class OnboardingEndpoints
{
    public static void MapOnboardingEndpoints(this WebApplication app)
    {
        app.MapGet("/api/onboarding", () =>
        {
            Heartbeat.Touch();
            return Results.Json(OnboardingService.Get());
        });

        app.MapPost("/api/onboarding", (OnboardingPatchPayload body) =>
        {
            Heartbeat.Touch();
            var state = OnboardingService.Patch(
                body.started, body.completed, body.skipped, body.current_step, body.seen_hint);
            LogEvent(body);
            return Results.Json(state);
        });

        app.MapPost("/api/onboarding/restart", () =>
        {
            Heartbeat.Touch();
            DevLog.Add("Samouczek: onboarding_restarted");
            return Results.Json(OnboardingService.Restart());
        });
    }

    private static void LogEvent(OnboardingPatchPayload body)
    {
        var step = string.IsNullOrWhiteSpace(body.current_step) ? "-" : body.current_step;
        if (body.completed == true) DevLog.Add($"Samouczek: onboarding_completed (v{OnboardingService.CurrentVersion})");
        else if (body.skipped == true) DevLog.Add($"Samouczek: onboarding_skipped na kroku {step}");
        else if (body.started == true && body.current_step is null) DevLog.Add("Samouczek: onboarding_started");
        else if (!string.IsNullOrWhiteSpace(body.seen_hint)) DevLog.Add($"Samouczek: hint_seen {body.seen_hint}");
        else if (step != "-") DevLog.Add($"Samouczek: onboarding_step_viewed {step}");
    }
}

record OnboardingPatchPayload(
    bool? started, bool? completed, bool? skipped, string? current_step, string? seen_hint);
