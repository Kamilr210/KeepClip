namespace KeepClip.Endpoints;

public static class CloudEndpoints
{
    public static void MapCloudEndpoints(this WebApplication app)
    {
        app.MapGet("/api/cloud/status", async () =>
        {
            Heartbeat.Touch();
            return Results.Json(await OAuthService.StatusAsync());
        });

        app.MapGet("/api/cloud/avatar", async (HttpContext http) =>
        {
            Heartbeat.Touch();
            try
            {
                var photo = await OAuthService.GetProfilePhotoAsync();
                if (photo is null) return Results.NotFound();
                http.Response.Headers.CacheControl = "private, max-age=300";
                return Results.File(photo.Value.Data, photo.Value.ContentType);
            }
            catch
            {
                return Results.NotFound();
            }
        });

        app.MapPost("/api/cloud/connect", () =>
        {
            Heartbeat.Touch();
            if (!OAuthService.IsConfigured())
                return Api.Detail(400, "Brak pliku google_client.json w folderze data.");
            try
            {
                var url = OAuthService.BeginConnect();
                DevLog.Add("Chmura: rozpoczęto łączenie z Google Drive (otwarto zgodę OAuth)");
                return Results.Json(new { auth_url = url });
            }
            catch (Exception ex) { return Api.Detail(500, ex.Message); }
        });

        app.MapPost("/api/cloud/disconnect", async () =>
        {
            Heartbeat.Touch();
            await OAuthService.DisconnectAsync();
            DevLog.Add("Chmura: rozłączono z Google Drive");
            return Results.Json(new { ok = true });
        });

        app.MapPost("/api/cloud/sync", async () =>
        {
            Heartbeat.Touch();
            if (!OAuthService.IsConnected()) return Api.Detail(400, "Nie połączono z Google Drive.");
            try
            {
                var result = await CloudService.SyncFromCloudAsync();
                DevLog.Add(
                    $"Chmura: synchronizacja — znaleziono {result.Found}, " +
                    $"przywrócono {result.Imported}, zaktualizowano {result.Updated}, pominięto {result.Skipped}");
                return Results.Json(new
                {
                    imported = result.Imported,
                    updated = result.Updated,
                    found = result.Found,
                    skipped = result.Skipped,
                });
            }
            catch (GoogleDrive.GoogleApiException ex) when (ex.IsInvalidGrant)
            {
                return Api.Detail(401, "Wygasło połączenie z Google Drive — połącz ponownie.");
            }
            catch (Exception ex) { return Api.Detail(500, ex.Message); }
        });

        app.MapPost("/api/clips/{clipId:long}/upload", async (long clipId) =>
        {
            Heartbeat.Touch();
            if (!OAuthService.IsConnected()) return Api.Detail(400, "Nie połączono z Google Drive.");
            try
            {
                bool already = await CloudService.UploadClipAsync(clipId);
                DevLog.Add($"Chmura: wysłano klip #{clipId}{(already ? " (był już w chmurze)" : " — lokalny plik do Kosza")}");
                return Results.Json(new { ok = true, already });
            }
            catch (FileNotFoundException ex) { return Api.Detail(404, ex.Message); }
            catch (GoogleDrive.GoogleApiException ex) when (ex.IsInvalidGrant)
            {
                return Api.Detail(401, "Wygasło połączenie z Google Drive — połącz ponownie.");
            }
            catch (Exception ex) { return Api.Detail(500, ex.Message); }
        });

        app.MapPost("/api/clips/{clipId:long}/download", async (long clipId) =>
        {
            Heartbeat.Touch();
            if (!OAuthService.IsConnected()) return Api.Detail(400, "Nie połączono z Google Drive.");
            try
            {
                bool already = await CloudService.DownloadClipAsync(clipId);
                DevLog.Add($"Chmura: zdjęto klip #{clipId} z chmury na dysk{(already ? " (był już lokalnie)" : "")}");
                return Results.Json(new { ok = true, already });
            }
            catch (FileNotFoundException ex) { return Api.Detail(404, ex.Message); }
            catch (GoogleDrive.GoogleApiException ex) when (ex.IsInvalidGrant)
            {
                return Api.Detail(401, "Wygasło połączenie z Google Drive — połącz ponownie.");
            }
            catch (Exception ex) { return Api.Detail(500, ex.Message); }
        });

        app.MapPost("/api/folders/{folderId:long}/upload", async (long folderId) =>
        {
            Heartbeat.Touch();
            if (!OAuthService.IsConnected()) return Api.Detail(400, "Nie połączono z Google Drive.");
            try
            {
                var (uploaded, total, skipped, failed) = await CloudService.UploadFolderAsync(folderId);
                DevLog.Add($"Chmura: wysłano folder #{folderId} — {uploaded}/{total} wysłanych (pominięto {skipped}, błędów {failed})");
                return Results.Json(new { uploaded, total, skipped, failed });
            }
            catch (Exception ex) { return Api.Detail(500, ex.Message); }
        });
    }
}
