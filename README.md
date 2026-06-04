# KeepClip

Przeglądarka i wyszukiwarka klipów z gier z **transkrypcją mowy (Whisper)** i opcjonalnym **offloadem na Google Drive**. Skanuje folder z nagraniami (np. NVIDIA ShadowPlay / SteelSeries Moments), rozpoznaje co padło w klipie i pozwala **wyszukiwać klipy po wypowiedzianych słowach**, odtwarzać je, wycinać/kompresować fragmenty, porządkować w foldery i oznaczać ulubione.

Aplikacja to natywne okno na Windows (WebView2) z serwerem ASP.NET hostowanym w tym samym procesie. Backend w C# (.NET 10), frontend w HTML/CSS/JS.

---

## Wymagania

- **Windows 10/11 (x64)**
- **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)** — potrzebne do uruchomienia ze źródeł
- **WebView2 Runtime** — na Win 11 i większości Win 10 jest fabrycznie; jak brak, doinstaluj [stąd](https://developer.microsoft.com/microsoft-edge/webview2/)
- *(opcjonalnie)* karta graficzna z Vulkan/CUDA — przyspiesza transkrypcję; bez niej działa na CPU

> **ffmpeg/ffprobe oraz model Whisper NIE są w repozytorium** (za duże pliki / cudze oprogramowanie). ffmpeg dociąga `setup.ps1`, a model Whisper pobiera się sam przy pierwszej transkrypcji.

---

## Szybki start (uruchomienie ze źródeł)

W folderze repozytorium, w **PowerShell**:

```powershell
# 1. Pobierz brakujące zależności (ffmpeg + ffprobe -> tools\bin)
powershell -ExecutionPolicy Bypass -File setup.ps1

# 2. Uruchom aplikację
dotnet run --project KeepClip
```

Pierwsze uruchomienie chwilę potrwa (NuGet + kompilacja) — to normalne.

### Pierwsze kroki w aplikacji
1. W ustawieniach wskaż **folder z klipami** (domyślnie `…\Videos\NVIDIA`).
2. Kliknij **Skanuj folder**, potem **Transkrybuj nowe**.
3. Przy pierwszej transkrypcji aplikacja **pobierze model Whisper** (~1.6 GB, jednorazowo).

---

## Google Drive (opcjonalnie)

Offload klipów do chmury wymaga własnego OAuth „Desktop app" z Google Cloud Console.
Pobrany plik JSON zapisz jako `data\google_client.json`. Dokładne kroki pokazuje sama aplikacja w zakładce **Chmura** (dopóki pliku nie ma, widać tam instrukcję). Aplikacja prosi tylko o zakres `drive.file` — widzi wyłącznie pliki, które sama utworzyła.

---

## Budowanie instalatora (dla użytkowników końcowych)

Żeby zrobić samowystarczalny instalator `.exe` (z wbudowanym runtime .NET, ffmpeg i frontendem — odbiorca nie musi nic instalować):

```powershell
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

Wynik: `installer\dist\KeepClip-Setup.exe` (do wrzucenia na GitHub Releases). Wymaga [Inno Setup 6](https://jrsoftware.org/isdl.php) (skrypt spróbuje doinstalować go przez winget).

---

## Czego nie ma w repo (i dlaczego)

Repozytorium trzyma **kod źródłowy**, a nie gotowy produkt. Celowo (i częściowo z konieczności) pominięte są:

| Element | Powód | Skąd się bierze |
|---|---|---|
| `tools\bin\ffmpeg.exe`, `ffprobe.exe` | pliki >100 MB — GitHub ich nie przyjmuje; do tego cudze oprogramowanie | `setup.ps1` |
| `models\` (model Whisper) | ~1.6 GB | pobiera się sam przy 1. transkrypcji |
| `data\` (baza, miniatury, `google_client.json`) | dane użytkownika + **sekret OAuth** (nie wolno publikować) | tworzy się przy starcie |
| `bin\`, `obj\`, `publish\` (w tym `.exe`) | generowane przy każdej kompilacji | `dotnet build` / `dotnet publish` |

---

## Rozwiązywanie problemów

- **„Nie udało się zdekodować audio: …mp4"** przy transkrypcji → najczęściej **brak ffmpeg**. Uruchom `setup.ps1` i sprawdź, czy są pliki `tools\bin\ffmpeg.exe` i `ffprobe.exe`. (Jeśli błąd dotyczy tylko jednego klipu — ten klip może nie mieć ścieżki audio albo być uszkodzony.)
- **Transkrypcja bardzo wolna** → silnik wpadł na CPU (brak działającego GPU Vulkan/CUDA). Informację o użytym silniku widać w logu transkrypcji.
- **`dotnet` nie rozpoznane** → brak .NET 10 SDK (patrz [Wymagania](#wymagania)).
