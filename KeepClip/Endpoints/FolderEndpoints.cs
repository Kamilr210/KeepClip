namespace KeepClip.Endpoints;

public static class FolderEndpoints
{
    public static void MapFolderEndpoints(this WebApplication app)
    {
        app.MapGet("/api/folders", (FolderRepository folders) =>
        {
            Heartbeat.Touch();
            return Results.Json(folders.List());
        });

        app.MapPost("/api/folders", (FolderPayload body, FolderRepository folders) =>
        {
            Heartbeat.Touch();
            var name = body.name?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) return Api.Detail(400, "Nazwa folderu jest wymagana.");
            if (name.Length > 100) return Api.Detail(400, "Nazwa folderu za długa (max 100 znaków).");
            var row = folders.Create(name);
            if (row is null) return Api.Detail(409, $"Folder o nazwie „{name}\" już istnieje.");
            DevLog.Add($"Folder: utworzono „{name}” (#{row["id"]})");
            return Results.Json(row);
        });

        app.MapPatch("/api/folders/{folderId:long}", (long folderId, FolderPayload body, FolderRepository folders) =>
        {
            Heartbeat.Touch();
            var name = body.name?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) return Api.Detail(400, "Nazwa folderu jest wymagana.");
            return folders.Rename(folderId, name) switch
            {
                FolderWrite.NotFound => Api.Detail(404, "Folder nie istnieje."),
                FolderWrite.Duplicate => Api.Detail(409, $"Folder o nazwie „{name}\" już istnieje."),
                _ => Log(),
            };

            IResult Log()
            {
                DevLog.Add($"Folder #{folderId}: zmieniono nazwę na „{name}”");
                return Results.Json(new { ok = true, id = folderId, name });
            }
        });

        app.MapDelete("/api/folders/{folderId:long}", (long folderId, FolderRepository folders) =>
        {
            Heartbeat.Touch();
            var deletedName = folders.Delete(folderId);
            if (deletedName is null) return Api.Detail(404, "Folder nie istnieje.");
            DevLog.Add($"Folder: usunięto „{deletedName}” (#{folderId})");
            return Results.Json(new Dictionary<string, object?> { ["ok"] = true, ["deleted_name"] = deletedName });
        });

        app.MapGet("/api/folders/{folderId:long}/clips", (long folderId, string? sort, int? limit, FolderRepository folders) =>
        {
            Heartbeat.Touch();
            var result = folders.ListClips(folderId, sort, limit ?? 500);
            if (result is null) return Api.Detail(404, "Folder nie istnieje.");
            return Results.Json(new Dictionary<string, object?> { ["folder"] = result.Value.folder, ["clips"] = result.Value.clips });
        });

        app.MapPost("/api/folders/{folderId:long}/clips/{clipId:long}", (long folderId, long clipId, FolderRepository folders) =>
        {
            Heartbeat.Touch();
            return folders.AddClip(folderId, clipId) switch
            {
                AddClipResult.NoFolder => Api.Detail(404, "Folder nie istnieje."),
                AddClipResult.NoClip => Api.Detail(404, "Klip nie istnieje."),
                _ => LogOk(),
            };

            IResult LogOk()
            {
                DevLog.Add($"Folder #{folderId}: dodano klip #{clipId}");
                return Results.Json(new { ok = true });
            }
        });

        app.MapDelete("/api/folders/{folderId:long}/clips/{clipId:long}", (long folderId, long clipId, FolderRepository folders) =>
        {
            Heartbeat.Touch();
            folders.RemoveClip(folderId, clipId);
            DevLog.Add($"Folder #{folderId}: usunięto klip #{clipId}");
            return Results.Json(new { ok = true });
        });

        app.MapGet("/api/clips/{clipId:long}/folders", (long clipId, FolderRepository folders) =>
        {
            Heartbeat.Touch();
            return Results.Json(new Dictionary<string, object?> { ["folder_ids"] = folders.GetFolderIdsForClip(clipId) });
        });
    }
}

record FolderPayload(string name);
