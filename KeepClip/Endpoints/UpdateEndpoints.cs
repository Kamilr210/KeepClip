namespace KeepClip.Endpoints;

/// <summary>Sprawdzanie i instalacja aktualizacji aplikacji (GitHub Releases).</summary>
public static class UpdateEndpoints
{
    public static void MapUpdateEndpoints(this WebApplication app)
    {
        app.MapGet("/api/update/check", async () => Results.Json(await UpdateService.CheckAsync()));

        app.MapGet("/api/update/status", () => Results.Json(UpdateService.Status()));

        app.MapPost("/api/update/install", () =>
        {
            Heartbeat.Touch();
            if (!UpdateService.BeginInstall())
                return Api.Detail(409, "Aktualizacja już trwa.");
            DevLog.Add("Aktualizacja: pobieranie instalatora rozpoczęte");
            return Results.Json(new { ok = true });
        });
    }
}
