# KeepClip — Plan Refaktoryzacji

## Cel

Przekształcenie monolitycznego projektu (1004-linijowy Program.cs, statyczne klasy, SQL inline, brak modeli domen) w czystą, czytelną architekturę bez over-engineeringu — **bez DDD, bez CQRS**, z Clean Code: separation of concerns, typed models, DI, endpoint groups.

---

## Krok 1 — Domain Models

Utwórz folder `KeepClip/Domain/` i przenieś wszystkie kształty danych z `Dictionary<string, object?>` do typowanych records.

### Pliki do utworzenia

**`Domain/Clip.cs`**
```csharp
namespace KeepClip.Domain;

public record Clip(
    long Id,
    string Game,
    string Filename,
    double Duration,
    long SizeBytes,
    double Mtime,
    int HasThumb,
    string? TranscribedAt,
    int Favorite,
    string? Storage,
    string? RemoteId,
    string? Filepath
);
```

**`Domain/Segment.cs`**
```csharp
namespace KeepClip.Domain;

public record Segment(long Id, long ClipId, double StartS, double EndS, string Text);

public record SegmentWithSnippet(
    long SegmentId, double StartS, double EndS, string Text,
    long ClipId, string Game, string Filename, double Duration,
    long SizeBytes, double Mtime, int HasThumb, int Favorite, string? Storage,
    string Snippet, double Score
);
```

**`Domain/Folder.cs`**
```csharp
namespace KeepClip.Domain;

public record ClipFolder(long Id, string Name, string CreatedAt, int ClipCount, List<long> SampleClipIds);
```

**`Domain/ReplayConfig.cs`**
```csharp
namespace KeepClip.Domain;

public record ReplayStatus(bool Enabled, int DurationS, int Fps, string Quality, string Hotkey, bool Mic, string? AudioOutput, string? AudioInput);
```

**`Domain/TranscribeSnapshot.cs`**
```csharp
namespace KeepClip.Domain;

public record TranscribeSnapshot(bool Running, string? CurrentClip, int Done, int Total, double? Percent, string? FinishedAt, string? Error);
```

**`Domain/SearchResult.cs`**
```csharp
namespace KeepClip.Domain;

public record SearchResult(string Query, string Fts, List<SegmentWithSnippet> Results);
```

**`Domain/Stats.cs`**
```csharp
namespace KeepClip.Domain;

public record Stats(
    long Clips, long Transcribed, long Segments,
    long TotalBytes, long LocalBytes, long CloudBytes,
    long Favorites, long? DiskTotal, long? DiskFree,
    List<GameStat> Games
);

public record GameStat(string Game, long Clips, long Done);
```

**`Domain/CloudStatus.cs`**
```csharp
namespace KeepClip.Domain;

public record CloudStatus(bool Configured, bool Connected, string? AccountEmail, long? UsageBytes, long? CapacityBytes);
```

### Zasady
- Domain nie zależy od niczego — zero using na Microsoft.Data.Sqlite, zero na ASP.NET.
- Records są immutable. Mutowalne pola (Favorite, Storage itp.) operują przez with-expression lub zwracają nowy record.
- Konwersja z `Dictionary<string, object?>` → record odbywa się w Repository (krok 2).

---

## Krok 2 — Repositories

Utwórz folder `KeepClip/Data/` i przenieś cały SQL z Program.cs, CloudService.cs, TranscribeWorker.cs, Scanner.cs do repozytoriów.

### Przenieś pliki
- `Db.cs` → `Data/Db.cs` (zmień namespace na `KeepClip.Data`)
- `DbExtensions.cs` → `Data/DbExtensions.cs` (zmień namespace na `KeepClip.Data`)

### Pliki do utworzenia

**`Data/ClipRepository.cs`**
```csharp
namespace KeepClip.Data;

public class ClipRepository
{
    public IReadOnlyList<Clip> List(string? game, string sort, int limit);
    public Clip? GetById(long id);
    public long Count();
    public long CountTranscribed();
    public long TotalSizeBytes();
    public long LocalSizeBytes();
    public long CloudSizeBytes();
    public long CountFavorites();
    long Insert(string game, string filename, string filepath, long sizeBytes, double mtime);
    void UpdateAfterFix(long id, long sizeBytes, double mtime, double duration, int hasThumb);
    void SetFavorite(long id, int favorite);
    void Delete(long id);
    void SetCloudStorage(long id, string? storage, string? remoteId);
    void SetTranscribed(long id, string timestamp, string language);
    List<GameStat> GetGameStats();
}
```

SQL do przeniesienia z Program.cs:
- `SELECT COUNT(*) FROM clips` (stats)
- `SELECT ... FROM clips WHERE game = $game / ORDER BY ... LIMIT $limit` (list clips)
- `SELECT favorite FROM clips WHERE id = $id` + `UPDATE clips SET favorite` (toggle favorite)
- `SELECT filepath, filename, storage, remote_id FROM clips WHERE id = $id` (delete clip)
- `DELETE FROM clips WHERE id = $id` (delete)
- `SELECT filepath, has_thumb FROM clips WHERE id = $id` (thumbnail)
- `UPDATE clips SET has_thumb = ... / has_thumb=2` (thumbnail)
- `SELECT filepath, filename, game FROM clips WHERE id = $id` (cut clip)
- `INSERT INTO clips(game, filename, filepath, size_bytes, mtime) VALUES` (cut clip — register)
- `SELECT * FROM clips WHERE id = $id` (segments)
- `SELECT filepath FROM clips WHERE id = $id` (retranscribe)
- `UPDATE clips SET transcribed_at=$ts, language=$lang WHERE id=$id` (retranscribe)
- `UPDATE clips SET size_bytes=$sz, mtime=$mt, duration=$du, has_thumb=$ht WHERE id=$id` (fix clip)

**`Data/SegmentRepository.cs`**
```csharp
namespace KeepClip.Data;

public class SegmentRepository
{
    public IReadOnlyList<Segment> ListByClipId(long clipId);
    void DeleteByClipId(long clipId);
    void Insert(long clipId, double startS, double endS, string text);
    void UpdateText(long segmentId, string text);
    void DeleteById(long segmentId);
    void ShiftTimestamps(long clipId, double offset);
    void DropSegmentsBeforeZero(long clipId);
}
```

SQL do przeniesienia z Program.cs:
- `DELETE FROM segments WHERE clip_id = $id` (retranscribe)
- `INSERT INTO segments (clip_id, start_s, end_s, text) VALUES` (retranscribe)
- `SELECT id, start_s, end_s, text FROM segments WHERE clip_id=$id ORDER BY start_s` (list)
- `UPDATE segments SET text=$t WHERE id=$id` (edit)
- `SELECT id, start_s, end_s FROM segments WHERE clip_id=$id` (fix clip — shift)
- `DELETE FROM segments WHERE id=$id` (fix clip — drop)
- `UPDATE segments SET start_s=$st, end_s=$en WHERE id=$id` (fix clip — shift)

**`Data/FolderRepository.cs`**
```csharp
namespace KeepClip.Data;

public class FolderRepository
{
    IReadOnlyList<ClipFolder> List();
    ClipFolder? GetById(long id);
    long Insert(string name);
    void Rename(long id, string name);
    void Delete(long id);
    void AddClip(long folderId, long clipId);
    void RemoveClip(long folderId, long clipId);
    List<long> GetClipIdsForFolder(long folderId);
    IReadOnlyList<Clip> GetClipsInFolder(long folderId, string sort, int limit);
    List<long> GetFolderIdsForClip(long clipId);
}
```

SQL do przeniesienia z Program.cs:
- Wszystkie zapytania `/api/folders/*` i `/api/folders/{id}/clips/*`

**`Data/StatsRepository.cs`**
```csharp
namespace KeepClip.Data;

public class StatsRepository
{
    Stats Get();
}
```

SQL z endpointu `/api/stats`.

### Zasady
- Repository zwraca typowane records (z Domain), nigdy `Dictionary<string, object?>`.
- Repository przyjmuje Db connection przez parametr — nie trzyma stanu.
- Metody Repository są transactional tam gdzie trzeba (INSERT + SELECT last_insert_rowid w jednej transakcji).
- Po tym kroku Program.cs nie powinien mieć żadnego bezpośredniego SQL.

---

## Krok 3 — Endpoint Groups

Utwórz folder `KeepClip/Endpoints/` i wydziel każdy obszar API do osobnego pliku.

### Pliki do utworzenia

**`Endpoints/ClipEndpoints.cs`** — endpointy:
- `GET /api/clips`
- `POST /api/clips/{id}/favorite`
- `DELETE /api/clips/{id}`
- `POST /api/clips/{id}/fix`
- `POST /api/clips/{id}/cut`
- `POST /api/clips/{id}/retranscribe`
- `GET /thumb/{id}`

**`Endpoints/FolderEndpoints.cs`** — endpointy:
- `GET /api/folders`
- `POST /api/folders`
- `PATCH /api/folders/{id}`
- `DELETE /api/folders/{id}`
- `GET /api/folders/{id}/clips`
- `POST /api/folders/{id}/clips/{clipId}`
- `DELETE /api/folders/{id}/clips/{clipId}`
- `GET /api/clips/{id}/folders`

**`Endpoints/SearchEndpoints.cs`** — endpointy:
- `GET /api/search`

**`Endpoints/TranscribeEndpoints.cs`** — endpointy:
- `GET /api/transcribe/status`
- `POST /api/transcribe/start`
- `POST /api/transcribe/cancel`
- `GET /api/transcribe/stream`

**`Endpoints/ReplayEndpoints.cs`** — endpointy:
- `GET /api/replay/status`
- `POST /api/replay/config`
- `POST /api/replay/save`
- `GET /api/replay/audio-devices`

**`Endpoints/CloudEndpoints.cs`** — endpointy:
- `GET /api/cloud/status`
- `POST /api/cloud/connect`
- `POST /api/cloud/disconnect`
- `POST /api/clips/{id}/upload`
- `POST /api/clips/{id}/download`
- `POST /api/folders/{id}/upload`

**`Endpoints/StatsEndpoints.cs`** — endpointy:
- `GET /api/stats`

**`Endpoints/ConfigEndpoints.cs`** — endpointy:
- `GET /api/config`
- `POST /api/config`
- `POST /api/heartbeat`
- `POST /api/scan`
- `GET /api/ping`
- `POST /api/shutdown`

**`Endpoints/MediaEndpoints.cs`** — endpointy:
- `GET /video/{id}`
- `POST /api/show-in-explorer`

### Struktura każdego pliku

```csharp
using KeepClip.Data;
using KeepClip.Domain;
using KeepClip.Infrastructure;
using KeepClip.Services;

namespace KeepClip.Endpoints;

public static class ClipEndpoints
{
    public static RouteGroupBuilder MapClipEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/clips");

        group.MapGet("/", (string? game, string? sort, int? limit, ClipRepository repo) =>
        {
            var order = /* sort mapping */;
            return Results.Json(repo.List(game, sort ?? "newest", limit ?? 200));
        });

        group.MapDelete("/{clipId:long}", async (long clipId, bool? delete_file, ClipService clips) =>
        {
            // delegation to service
        });

        // ... reszta endpointów

        return group;
    }
}
```

### Zasady
- Każdy plik endpointów jest extension method na `WebApplication`.
- Endpointy są cienkie — walidacja + wywołanie service/repo + formatowanie odpowiedzi.
- Zero SQL w endpointach.
- Request body records (`ConfigPayload`, `CutPayload`, itp.) definiowane w tym samym pliku co endpoint (lub w Domain jeśli są współdzielone).
- Po tym kroku Program.cs zawiera tylko startup, DI registration i `app.MapXxxEndpoints()`.

---

## Krok 4 — Services

Utwórz folder `KeepClip/Services/` i wydziel logikę biznesową z endpointów.

### Pliki do utworzenia

**`Services/ClipService.cs`**
```csharp
namespace KeepClip.Services;

public class ClipService
{
    // Fix broken clip: Media.FixBrokenClip + Trash + update DB + regenerate thumbnail + shift segments
    public Result<FixResult> Fix(long clipId);

    // Cut clip: Media.CutClip + register in library if inside clips root
    public Result<CutResult> Cut(long clipId, double start, double end, double? targetSizeMb);

    // Delete clip: remove file, thumbnail, DB rows, cloud remote
    public Task<DeleteResult> DeleteAsync(long clipId, bool deleteFile);

    // Toggle favorite
    public bool ToggleFavorite(long clipId);

    // Retranscribe single clip
    public Result<RetranscribeResult> Retranscribe(long clipId);
}
```

**`Services/FolderService.cs`**
```csharp
namespace KeepClip.Services;

public class FolderService
{
    long Create(string name);
    void Rename(long id, string name);
    void Delete(long id);
    void AddClip(long folderId, long clipId);
    void RemoveClip(long folderId, long clipId);
}
```

**`Services/SearchService.cs`**
```csharp
namespace KeepClip.Services;

public class SearchService
{
    SearchResult Search(string query, string sort, int limit);
    // wewnątrz: konwersja na FTS query + zapytanie do repo
}
```

**`Services/CloudOrchestrator.cs`** (wydzielone z CloudService)
```csharp
namespace KeepClip.Services;

public class CloudOrchestrator
{
    Task<bool> UploadClipAsync(long clipId);
    Task<bool> DownloadClipAsync(long clipId);
    Task<(int uploaded, int total, int skipped, int failed)> UploadFolderAsync(long folderId);
    Task TryTrashRemoteAsync(string remoteId);
    Task<CloudStatus> GetStatusAsync();
    void Disconnect();
    string BeginConnect();
    bool IsConfigured();
    bool IsConnected();
}
```

**`Services/TranscribeService.cs`**
```csharp
namespace KeepClip.Services;

public class TranscribeService
{
    bool Start(bool force, out int total);
    void Cancel();
    TranscribeSnapshot GetStatus();
}
```

**`Services/ReplayOrchestrator.cs`**
```csharp
namespace KeepClip.Services;

public class ReplayOrchestrator
{
    ReplayStatus GetStatus();
    void ApplyConfig();
    Task<Dictionary<string, object?>> SaveAsync(string source);
    void Shutdown();
    object AudioDevices();
}
```

**`Services/ConfigService.cs`**
```csharp
namespace KeepClip.Services;

public class ConfigService
{
    object GetConfig();
    object SetClipsRoot(string path);
    object Scan();
}
```

### Result type
```csharp
// Domain/Result.cs
namespace KeepClip.Domain;

public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public int Code { get; }
    public string? Error { get; }

    public static Result<T> Ok(T value) => new(true, value, 0, null);
    public static Result<T> Fail(int code, string error) => new(false, default, code, error);
}
```

### Zasady
- Serwisy są instance-based (nie static) — przygotowanie pod DI (krok 7).
- Serwisy przyjmują repozytoria i infrastrukturę przez konstruktor.
- Serwisy nie znają HttpContext — zwracają Result<T> lub DTO.
- Endpointy tłumaczą Result<T> na HTTP responses (200, 404, 409, itd.).

---

## Krok 5 — Rozbij CloudService

Podziel `CloudService.cs` (523 linii) na:

**`Services/CloudOrchestrator.cs`** — orchestration upload/download/try-trash. Używa GoogleDrive + OAuthService + ClipRepository.

**`Infrastructure/OAuthService.cs`** — PKCE flow, token refresh, loopback HTTP listener. Wydzielone z CloudService:
- `BeginConnect()` → generuje auth URL, startuje loopback listener
- `IsConnected()` / `IsConfigured()`
- `DisconnectAsync()`
- Wewnętrzne: token refresh, credential store
- Metoda `ProxyVideoAsync(HttpContext, string remoteId)` → zostaje w CloudOrchestrator (bo wymaga HttpContext)

**`Infrastructure/GoogleDrive.cs`** — bez zmian, już jest czystym REST clientem.

---

## Krok 6 — Rozbij ReplayService

Podziel `ReplayService.cs` (1047 linii) na:

**`Services/ReplayOrchestrator.cs`** — facade z publicznym API: `Status()`, `ApplyConfig()`, `SaveAsync()`, `Shutdown()`, `AudioDevices()`.

**`Infrastructure/ReplayCapture.cs`** — zarządzanie procesem ffmpeg, ring buffer, segment numbering, watchdog restart. Zawiera:
- Start/stop capture process
- Build ffmpeg arguments
- Ring buffer management
- Segment file management
- Crash recovery

**`Infrastructure/AudioPump.cs`** — wydzielona z ReplayService (już jest osobną klasą, ale w tym samym pliku). NAudio/WASAPI capture.

**`Infrastructure/DisplayHelper.cs`** — pomiar ekranu, foreground window detection. Wydzielone z ReplayService.

### Uwaga
- ReplayService jest najbardziej delikatnym kodem w projekcie (ffmpeg process management, NAudio, Win32 P/Invoke).
- Refaktoruj **ostrożnie**, krok po kroku, z testowaniem po każdej zmianie.
- Najpierw wydziel AudioPump i DisplayHelper (mają najmniejsze couplingi), potem ReplayCapture, zostaw ReplayOrchestrator na koniec.
- Nie zmieniaj logiki — tylko przemieszczaj metody między plikami.

---

## Krok 7 — Dependency Injection

Przekształć statyczne klasy serwisowe na instancje rejestrowane w DI.

### Program.cs — DI registration

```csharp
var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.AddSingleton<ClipRepository>();
builder.Services.AddSingleton<FolderRepository>();
builder.Services.AddSingleton<SegmentRepository>();
builder.Services.AddSingleton<StatsRepository>();

// Services
builder.Services.AddSingleton<ClipService>();
builder.Services.AddSingleton<FolderService>();
builder.Services.AddSingleton<SearchService>();
builder.Services.AddSingleton<CloudOrchestrator>();
builder.Services.AddSingleton<TranscribeService>();
builder.Services.AddSingleton<ReplayOrchestrator>();
builder.Services.AddSingleton<ConfigService>();

// Infrastructure
builder.Services.AddSingleton<OAuthService>();
builder.Services.AddSingleton<ThumbnailWorker>();
builder.Services.AddSingleton<TranscribeWorker>();
builder.Services.AddSingleton<Scanner>();

// Settings & config
builder.Services.AddSingleton<SettingsService>();
```

### Konwersja static → instance

Wzorzec dla każdej klasy serwisowej:

**Before (static):**
```csharp
public static class ClipService
{
    public static FixResult Fix(long id) { ... }
}
// Wywołanie: ClipService.Fix(id)
```

**After (instance z DI):**
```csharp
public class ClipService
{
    private readonly ClipRepository _repo;
    private readonly SegmentRepository _segRepo;
    private readonly Media _media;
    private readonly Trash _trash;

    public ClipService(ClipRepository repo, SegmentRepository segRepo, Media media, Trash trash)
    {
        _repo = repo; _segRepo = segRepo; _media = media; _trash = trash;
    }

    public FixResult Fix(long id) { ... }
}
// Wywołanie: clips.Fix(id) — przez DI w endpointach
```

### Klasy, które ZOSTAJĄ static
- `Config` — compile-time constants, bez stanu
- `Db` — schema init, connection factory
- `DbExtensions` — extension methods
- `CredentialStore` — Win32 P/Invoke wrapper, bez stanu oprócz nazwy klucza
- `Trash` — Win32 P/Invoke wrapper
- `Heartbeat` — prosty volatile counter
- `DevLog` — conditional compilation, singleton by design
- `Media` — stateless ffmpeg wrapper (może zostać static jako utility)
- `Transcriber` — stateless Whisper wrapper (może zostać static)

### Klasy do konwersji na instance
- `CloudService` → `CloudOrchestrator` + `OAuthService`
- `ReplayService` → `ReplayOrchestrator` + `ReplayCapture`
- `Scanner` → instance
- `ThumbnailWorker` → instance
- `TranscribeWorker` → instance
- `TranscribeState` → instance lub zostaje jako thread-safe singleton
- `Settings` → `SettingsService` instance
- `HotkeyManager` → instance

---

## Docelowa struktura plików

```
KeepClip/
├── Program.cs                          ~50 linii (startup + DI + MapEndpoints)
├── Domain/
│   ├── Clip.cs
│   ├── Segment.cs
│   ├── ClipFolder.cs
│   ├── ReplayStatus.cs
│   ├── TranscribeSnapshot.cs
│   ├── SearchResult.cs
│   ├── Stats.cs
│   ├── CloudStatus.cs
│   └── Result.cs
├── Data/
│   ├── Db.cs                           ← istniejący (zmieniony namespace)
│   ├── DbExtensions.cs                 ← istniejący (zmieniony namespace)
│   ├── ClipRepository.cs
│   ├── SegmentRepository.cs
│   ├── FolderRepository.cs
│   └── StatsRepository.cs
├── Services/
│   ├── ClipService.cs
│   ├── FolderService.cs
│   ├── SearchService.cs
│   ├── CloudOrchestrator.cs
│   ├── TranscribeService.cs
│   ├── ReplayOrchestrator.cs
│   └── ConfigService.cs
├── Infrastructure/
│   ├── CredentialStore.cs              ← istniejący
│   ├── DesktopShell.cs                 ← istniejący
│   ├── GoogleDrive.cs                  ← istniejący
│   ├── OAuthService.cs                 ← z CloudService
│   ├── Media.cs                        ← istniejący
│   ├── ReplayCapture.cs               ← z ReplayService
│   ├── AudioPump.cs                   ← z ReplayService
│   ├── ReplayToast.cs                 ← istniejący
│   ├── HotkeyManager.cs               ← istniejący
│   ├── Trash.cs                       ← istniejący
│   ├── Heartbeat.cs                   ← istniejący
│   └── Transcriber.cs                ← istniejący
├── Endpoints/
│   ├── ClipEndpoints.cs
│   ├── FolderEndpoints.cs
│   ├── SearchEndpoints.cs
│   ├── CloudEndpoints.cs
│   ├── TranscribeEndpoints.cs
│   ├── ReplayEndpoints.cs
│   ├── StatsEndpoints.cs
│   ├── ConfigEndpoints.cs
│   └── MediaEndpoints.cs
├── Workers/
│   ├── ThumbnailWorker.cs             ← istniejący
│   ├── TranscribeWorker.cs            ← istniejący
│   └── Scanner.cs                     ← istniejący
├── Config.cs                           ← istniejący (static, zostaje)
├── Settings.cs                         ← istniejący → migrate to SettingsService
├── TranscribeState.cs                  ← istniejący
├── DevLog.cs                          ← istniejący
└── Properties/
    └── launchSettings.json
```

---

## Kolejność wykonania (krytyczna)

1. **Krok 1 — Domain models** — najbezpieczniejszy, zero zmian zachowania, tylko dodanie records
2. **Krok 3 — Endpoint groups** — wyciągnij endpointy z Program.cs do osobnych plików (na razie wywołują static klasy, jak teraz)
3. **Krok 2 — Repositories** — przenieś SQL z Program.cs do repozytoriów, endpointy wywołują repozytoria
4. **Krok 4 — Services** — wyciągnij logikę biznesową z endpointów do serwisów
5. **Krok 5 — Split CloudService** — wydziel OAuthService
6. **Krok 7 — DI** — konwersja static → instance, rejestracja w IServiceCollection
7. **Krok 6 — Split ReplayService** — najtrudniejszy, zrób na samym końcu

### Po każdym kroku
- Skompiluj projekt (`dotnet build`)
- Przetestuj ręcznie: uruchom aplikację, sprawdź czy endpointy działają
- Nie przechodź do następnego kroku jeśli projekt się nie kompiluje
- Commituj po każdym ukończonym kroku

---

## Czego NIE robić

- **Nie wprowadzaj DDD** — aplikacja nie ma złożonego domain, aggregate roots są zbędne
- **Nie wprowadzaj CQRS** — SQLite dla jednego użytkownika, command/query separation nie da korzyści
- **Nie wprowadzaj MediatR** — over-engineering dla tego rozmiaru projektu
- **Nie wprowadzaj interfejsów dla wszystkiego** — interfejsy tylko tam gdzie są potrzebne do testowania lub gdzie jest realna polimorficzność
- **Nie dodawaj ORM (Entity Framework)** — SQLite z ręcznym SQL jest wystarczający i czytelny
- **Nie dodawaj warthy AutoMapper** — records z prostym mapowaniem wystarczą
- **Nie twórz projektów (.csproj)** — wszystko zostaje w jednym projekcie, podziały na foldery wystarczą