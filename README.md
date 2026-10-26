# KeepClip

Przeglądarka i wyszukiwarka klipów z gier z **transkrypcją mowy (Whisper)** i opcjonalnym **offloadem na Google Drive**. Skanuje folder z nagraniami (np. NVIDIA ShadowPlay / SteelSeries Moments), rozpoznaje co padło w klipie i pozwala **wyszukiwać klipy po wypowiedzianych słowach**, odtwarzać je, wycinać/kompresować fragmenty, porządkować w foldery i oznaczać ulubione.

Aplikacja to natywne okno na Windows (WebView2) z serwerem ASP.NET hostowanym w tym samym procesie. Backend w C# (.NET 10), frontend w HTML/CSS/JS.

---

## Wymagania

- **Windows 10/11 (x64)**
- **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)** — potrzebne do uruchomienia ze źródeł
- **WebView2 Runtime** — na Win 11 i większości Win 10 jest fabrycznie; jak brak, doinstaluj [stąd](https://developer.microsoft.com/microsoft-edge/webview2/)
- *(opcjonalnie)* karta graficzna z Vulkan/CUDA — przyspiesza transkrypcję; bez niej działa na CPU

> **ffmpeg/ffprobe oraz model Whisper NIE są w repozytorium** (generowane narzędzia i duże pliki). `setup.ps1` buduje FFmpeg 9.0.2 z przypiętych źródeł przy użyciu Node 20+ i Dockera z kontenerami Linux albo przyjmuje gotowy artefakt przez `-BuildArtifactDir`. Sprawdza hashe narzędzi, źródeł i materiałów licencyjnych. Istniejących narzędzi nie wymienia bez `-Force`. Model Whisper pobiera się sam przy pierwszej transkrypcji.

---

## Szybki start (uruchomienie ze źródeł)

W folderze repozytorium, w **PowerShell**:

```powershell
# 1. Zbuduj brakujące narzędzia (wymaga Node 20+ i Dockera; ffmpeg + ffprobe -> tools\bin)
powershell -ExecutionPolicy Bypass -File setup.ps1

# 2. Uruchom aplikację
dotnet run --project KeepClip
```

Pierwsze uruchomienie chwilę potrwa (NuGet + kompilacja) — to normalne.

Okno desktopowe automatycznie używa prywatnej sesji bieżącego uruchomienia.
Tryb `server-only` wymaga jawnego klucza `KEEPCLIP_API_TOKEN`; instrukcja
uruchomienia, logowania w przeglądarce i testowania przez Insomnię jest w
[opisie prywatnego API](docs/private-api.md).

### Pierwsze kroki w aplikacji
1. W ustawieniach wskaż **folder z klipami** (domyślnie `…\Videos\NVIDIA`).
2. Kliknij **Skanuj folder**, potem **Transkrybuj nowe**.
3. Przy pierwszej transkrypcji aplikacja **pobierze model Whisper** (~1.6 GB, jednorazowo).

---

## Google Drive (opcjonalnie)

W oficjalnym instalatorze klient OAuth aplikacji jest już wbudowany. Użytkownik otwiera
zakładkę **Chmura**, klika **Połącz z Google Drive** i loguje się na własne konto Google —
nie tworzy projektu w Google Cloud. KeepClip prosi tylko o zakres `drive.file`, więc widzi
wyłącznie pliki, które sam utworzył.

Przy uruchamianiu aplikacji bez instalatora (np. `dotnet run`) deweloper zapisuje klienta
OAuth typu **Desktop app** jako `data\google_client.json`. Używany jest publiczny
`installed.client_id`, PKCE S256 i `state`; aplikacja nie odczytuje ani nie wysyła
`client_secret`. Konfiguracje klienta webowego są odrzucane.

Build instalatora przyjmuje plik pod `installer\embed\google_client.json`, ale generuje
osobną publiczną konfigurację zawierającą wyłącznie `installed.client_id`. Surowy plik
nie trafia do instalatora. Workflow odtwarza `GOOGLE_CLIENT_JSON_B64` w katalogu
tymczasowym, filtruje dozwolone pole i usuwa surowy plik. Kontrola Inno Setup odrzuca
brak konfiguracji lub dodatkowe pola, także przy bezpośrednim wywołaniu kompilatora.
Testy pakowania można uruchomić poleceniem `powershell -NoProfile -ExecutionPolicy Bypass -File tests/oauth-packaging.Tests.ps1`.
Projekt Google OAuth używany w oficjalnym wydaniu musi mieć odbiorców **External** oraz
status publikacji **In production**. Tryb **Testing** służy wyłącznie deweloperom: dopuszcza
tylko wpisanych testerów, a ich autoryzacja Drive wygasa po 7 dniach.

---

## Budowanie i materiały licencyjne

Każda publikacja dołącza [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) i [licenses/](licenses/README.md). Wersje zależności i kompletność materiałów są sprawdzane przed pakowaniem.

FFmpeg 9.0.2 jest kompilowany z przypiętych źródeł wraz z libx264, NVENC, AMF i QSV. Instalator zawiera odpowiadające źródła, zależności, licencje i skrypty; release dołącza też osobny ZIP źródeł. Brak lub zmiana któregokolwiek z tych materiałów blokuje pakowanie. Szczegóły: [licenses/FFMPEG-SOURCE.md](licenses/FFMPEG-SOURCE.md). Licencja własnego projektu pozostaje osobną decyzją.

Lokalne przygotowanie wymaga Node 20+, Dockera z kontenerami Linux, .NET SDK 10.0.401 lub nowszej poprawki 10.0.4xx i zainstalowanego Inno Setup 6:

```powershell
powershell -ExecutionPolicy Bypass -File setup.ps1 -Force
powershell -ExecutionPolicy Bypass -File installer\build.ps1 -PublicRelease
```

Powstają KeepClip-Setup.exe, KeepClip-FFmpeg-Source.zip i plik identyfikacji hashy. Workflow wydania wykonuje budowę FFmpeg na Ubuntu przed jobem Windows; nie wymaga Dockera na runnerze Windows. Publikacja samowystarczalna ma przypięty runtime .NET 10.0.12 w `KeepClip.csproj`, zgodny z `licenses/manifest.json`, również przy użyciu nowszej poprawki SDK. Aktualizacja runtime wymaga jednoczesnego odświeżenia manifestu i materiałów licencyjnych. Sam build nie publikuje wydania.

Do osobnego lokalnego testu pakowania:

```powershell
powershell -ExecutionPolicy Bypass -File installer\build.ps1 -VerifyPackaging -OutputRoot "$env:TEMP\KeepClip-LicenseVerification"
```

Wynik jest w osobnym katalogu dist/verification/ i ma nazwę KeepClip-Packaging-Verification.exe. Nie publikuj go. Skrypt nie instaluje automatycznie Inno Setup.

---

## Czego nie ma w repo (i dlaczego)

Repozytorium trzyma **kod źródłowy**, a nie gotowy produkt. Celowo (i częściowo z konieczności) pominięte są:

| Element | Powód | Skąd się bierze |
|---|---|---|
| `tools\bin\ffmpeg.exe`, `ffprobe.exe` | generowane pliki wykonywalne; źródła i licencje są dołączane do instalatora | `setup.ps1` |
| `models\` (model Whisper) | ~1.6 GB | pobiera się sam przy 1. transkrypcji |
| `data\` (baza, miniatury, `google_client.json`) | dane użytkownika + **sekret OAuth** (nie wolno publikować) | tworzy się przy starcie |
| `bin\`, `obj\`, `publish\` (w tym `.exe`) | generowane przy każdej kompilacji | `dotnet build` / `dotnet publish` |

---

## Rozwiązywanie problemów

- **„Nie udało się zdekodować audio: …mp4"** przy transkrypcji → najczęściej **brak ffmpeg**. Uruchom `setup.ps1` i sprawdź, czy są pliki `tools\bin\ffmpeg.exe` i `ffprobe.exe`. (Jeśli błąd dotyczy tylko jednego klipu — ten klip może nie mieć ścieżki audio albo być uszkodzony.)
- **Transkrypcja bardzo wolna** → silnik wpadł na CPU (brak działającego GPU Vulkan/CUDA). Informację o użytym silniku widać w logu transkrypcji.
- **`dotnet` nie rozpoznane** → brak .NET 10 SDK (patrz [Wymagania](#wymagania)).
