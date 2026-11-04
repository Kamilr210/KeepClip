# Testy nagrywania i wydajności — 29.10.2026

To pomiary sprzed poprawki. Porównanie starej i poprawionej ścieżki, próbę WGC oraz sposób odtworzenia testu opisuje [nagrywanie w Sandboxie](sandbox-recording-fix.md).

## Wniosek ze zgłoszenia kursora

Objaw odtworzono na własnej planszy w świeżym Windows Sandbox. Użytkownik
potwierdził migotanie przy GDI + x264, a następnie w próbie rozdzielającej oba
koszty: **A — samo kodowanie CPU: kursor stabilny; B — samo GDI, bez H.264:
kursor migocze**. Są to obserwacje użytkownika, nie wynik automatycznego
detektora migotania. Flaga widoczności kursora pozostawała ustawiona w próbkach
obu faz; taka flaga nie opisuje poprawności wyświetlania kursora przez RDP.

Próba B zażądała 60 FPS, lecz dostarczyła 320 klatek w około 10 s. Diagnostyka
poprawnie zwróciła `failed`/kod 1 dla niezrealizowanego tempa klatek. Nie zmieniono
kryterium, żeby uzyskać zielony wynik. Wcześniejszy klip GDI + x264 nominalnie
miał 600 klatek / 10 s, ale FFmpeg zgłosił **280 klatek dodanych jako duplikaty**
(46,7%). Poprawne dekodowanie pliku nie oznacza więc 60 nowych klatek na sekundę.

To wskazuje na problem ścieżki GDI w środowisku Sandbox/RDP. Próba nie ustala
jeszcze, który element Windows, sterownika lub FFmpeg odpowiada za migotanie.
Nie dowodzi też, że ta sama usterka występuje na zwykłym pulpicie Windows.
Analizę kodu i źródeł producentów zawiera [audyt nagrywania](recording-audit.md).

## Środowisko i granice pomiaru

- Host: AMD Ryzen 7 9800X3D, 8 rdzeni / 16 procesorów logicznych, około 32 GB RAM.
- GPU: GeForce RTX 5080, sterownik 621.01; dostępna również AMD Radeon Graphics.
- Sandbox: `Microsoft Remote Display Adapter`, 16 procesorów logicznych,
  ekran 2032×1110, vGPU ustawione na `Default`, 4096 MB pamięci w konfiguracji.
- W testowym Sandboxie kodery NVENC, AMF i QSV były niedostępne: odpowiednio
  brak `nvcuda.dll`, brak `amfrt64.dll` i błąd inicjalizacji MFX. Na hoście NVENC
  i AMF działały. To nie jest potwierdzenie używania fizycznej integry w Sandbox.
- Sieć, mikrofon, kamera i przekierowanie schowka w testowym Sandboxie były
  wyłączone. Z hosta udostępniono tylko osobny staging narzędzi do odczytu oraz
  pusty katalog wyników do zapisu. Testowa piaskownica została zakończona po
  zachowaniu wyników.

CPU w tabelach oznacza udział **samego procesu FFmpeg**: jego czas CPU podzielony
przez czas rzeczywisty i 16 procesorów logicznych. Nie jest to suma obciążenia
KeepClip, DWM, całej maszyny wirtualnej, gry i obsługi audio. RAM to pamięć
robocza procesu; nie jest to VRAM. Nie mierzono spadku FPS gry ani wykorzystania
silników GPU. Obraz syntetyczny i krótka próba nie zastępują długiej sesji gry.

## Host: syntetyczne kodowanie

Wykonano 30 pomiarów: trzy rozdzielczości/FPS, pięć wariantów i dwa powtórzenia
po 10 s, sekwencyjnie, z odwróconą kolejnością wariantów w drugim powtórzeniu.
Źródło to własne `testsrc2`, tempo wejścia ograniczone przez `-re`, konwersja
BGRA → NV12 i zapis segmentów. Baseline pomija H.264 oraz zapis segmentów.
Nie uruchamiano jednocześnie testów .NET ani Sandboxa.

Parametry koderów pochodzą z produkcyjnego `ReplayCaptureCommand.Create`.
Manifest eksportu zawiera SHA256 źródła; benchmark odrzuca nieaktualny eksport.
Jakość aplikacji to `high`: x264 `veryfast`/CRF20, NVENC `p7`/QP20, AMF
`quality`/CQP20. Dodatkowo porównano NVENC `p4`. Takie wartości nie oznaczają
identycznej jakości obrazu między koderami. Nie oceniano PSNR/SSIM ani wizualnej
różnicy p7/p4, więc niższy koszt p4 nie wystarcza do automatycznej zmiany presetu.

Średnie z dwóch powtórzeń; RAM jest średnią zmierzonych szczytów procesu:

| Profil | CPU x264 | NVENC p7 | NVENC p4 | AMF | Baseline |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1080p30 — CPU procesu | 2,03% | 0,48% | 0,32% | 0,22% | 0,11% |
| 1080p60 — CPU procesu | 5,82% | 0,77% | 0,75% | 0,78% | 0,52% |
| 1440p60 — CPU procesu | 12,82% | 5,06% | 3,94% | 5,01% | 3,38% |
| 1080p30 — szczyt RAM | 681 MiB | 539 MiB | 306 MiB | 230 MiB | 48 MiB |
| 1080p60 — szczyt RAM | 722 MiB | 573 MiB | 358 MiB | 276 MiB | 52 MiB |
| 1440p60 — szczyt RAM | 1191 MiB | 849 MiB | 482 MiB | 441 MiB | 72 MiB |

Wszystkie 30 próbek zakończyło się poprawnie i osiągnęło zadaną liczbę klatek.
QSV był niedostępny na tym hoście i nie został zaliczony jako zmierzony wynik.
Rozrzut CPU x264 w 1440p60 wynosił 11,84–13,79%; dla AMF 3,95–6,07%. To mała
próba, nie uniwersalna obietnica obciążenia. Koszt CPU pozostaje także przy
kodowaniu sprzętowym, ponieważ przygotowanie obrazu nadal odbywa się na CPU.

Raport lokalny: `TestResults/recording-benchmark/20261029-172430-8c076687/report.html`.
Obok są `results.json`, `summary.md`, eksport komend oraz osobne polecenia,
próbki CPU/RAM i logi każdej fazy. Pierwszy przebieg narzędzia wykrył błąd
sumowania pustego katalogu podczas próby kodera; naprawiono go i dodano regresję.

## Sandbox: przechwytywanie GDI

Własna animowana plansza, bez dźwięku. Segmenty sprawdzono przez ffprobe i pełne
dekodowanie z `-xerror`; podgląd pojedynczej klatki potwierdził testową planszę.

| Próba | CPU FFmpeg | Szczyt RAM | Wynik |
| --- | ---: | ---: | --- |
| GDI + x264, 30 FPS, 10 s | 6,88% | 710 MiB | 300 klatek wyjściowych, 6 duplikatów, 1 opuszczona |
| GDI + x264, 60 FPS, 10 s | 9,83% | 710 MiB | 600 klatek wyjściowych, **280 duplikatów** |
| A: testsrc2 + x264, bez GDI, 60 FPS | 9,48% | 801 MiB | 600 klatek, kursor stabilny według użytkownika |
| B: GDI → null, bez H.264, żądane 60 FPS | 0,06% | 55 MiB | **320 klatek, około 32 FPS**, migotanie według użytkownika |

Niski czas CPU procesu w B nie obejmuje pracy kompozytora ani całej VM.
Faza A używała syntetycznego wejścia, B pobierała widoczną planszę, więc nie
należy odejmować ich wyników jako ścisłego rozkładu kosztów tej samej sceny.
Obie fazy pozostawiły widoczną tę samą planszę do obserwacji kursora.

Wyniki strukturalnej kontroli plików GDI30/60: `passed`, kod 0. Wynik
przepustowości A/B: `failed`, kod 1, ponieważ B nie dostarczyło wymaganych
klatek. To zachowany wynik diagnostyczny badanego środowiska, nie wyłączony test.

Lokalne dowody:

- `TestResults/recording-sandbox/20261029-173016-9868b70b/output/sandbox-20261029-173044-3bbba1ee/`
  — klipy, dekodowanie, `capture-60.stdout.txt` z `dup_frames=280`, JSON i podgląd.
- `TestResults/recording-sandbox/20261029-173016-9868b70b/output/sandbox-20261029-173648-425f2ed3/`
  — obie fazy kontroli, metryki i jawny wynik nieosiągnięcia 60 FPS.

## Powtarzanie pomiarów

Eksport rzeczywistych parametrów aplikacji, na własnych danych testowych:

```powershell
$previousExport = $env:KEEPCLIP_EXPORT_RECORDING_COMMANDS
try {
    $env:KEEPCLIP_EXPORT_RECORDING_COMMANDS = '1'
    dotnet test tests/KeepClip.Tests/KeepClip.Tests.csproj -c Release --artifacts-path TestResults/recording-build --filter FullyQualifiedName~Encoding_manifest_tracks_real_capture_commands
    if ($LASTEXITCODE -ne 0) { throw 'Eksport parametrów nie przeszedł testu.' }
} finally { $env:KEEPCLIP_EXPORT_RECORDING_COMMANDS = $previousExport }

powershell -NoProfile -ExecutionPolicy Bypass -File scripts/recording-benchmark.ps1 -CommandManifest TestResults/recording/command-manifest.json -DurationSeconds 10 -Repeats 2 -CompareNvencP4 -Include1440p
```

Opcja `-IncludeSyntheticAudio` dodaje własny ton sinusoidalny i AAC, nigdy
mikrofon ani dźwięk systemowy. Nie była użyta w powyższym pomiarze.

Przygotowanie nowej, izolowanej piaskownicy (nie uruchamia jej automatycznie):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/prepare-recording-sandbox.ps1 -DurationSeconds 10
```

Otwórz wypisany plik `.wsb`, obserwuj planszę i zachowaj folder `output`.
`-ControlsOnly` przygotowuje zamiast nagrań próbę A/B. `-Vgpu Disable` umożliwia
osobny eksperyment WARP; nie był wykonywany w tym zadaniu. Sam probe odmawia
uruchomienia na hoście, wymaga konta `WDAGUtilityAccount` i sprawdza hashe
narzędzi. Nie omijaj tego zabezpieczenia. Zamknięcie okna Sandbox kończy jego
środowisko; wyniki w osobnym mapowanym folderze hosta pozostają.

Zwykłe `Testy.cmd`/Full nie uruchamia nagrywania, ciężkiego benchmarku ani VM.
Sprawdza 21 lekkich kontroli narzędzi pomiaru oraz testy logiki bufora.

## Zakres zmian i dalsza diagnoza

Dodano testy przerwanego/anulowanego zapisu, zablokowanego segmentu, spójności
snapshotu oraz parametrów FPS/audio/kursora. Rozszerzono QA o bezpieczne testy
narzędzi. Nie zmieniono produkcyjnego zachowania nagrywania ani ustawień
użytkownika. Zastane zmiany QA zostały zachowane na gałęzi
`codex/recording-performance`; nie wykonano commita, pushu ani wydania.

Dodano też dwa testy natywne `Integration/ReplayBufferFfmpegTests.cs`: własny
obraz i opcjonalny ton audio → cztery segmenty MPEG-TS → produkcyjny
`ReplayWriter` → MP4. Potwierdzają 240 klatek w około 8 s, H.264/AAC, rosnące
PTS/DTS, pełne dekodowanie, zachowanie hashy segmentów i sprzątanie plików.
Pierwsze dekodowanie do wyjścia null kwantyzowało czas do wywnioskowanego FPS
i zgłosiło powtarzające się DTS; analiza pakietów potwierdziła poprawny MP4.
Walidator zachowuje teraz źródłowy timebase (`-fps_mode passthrough
-enc_time_base demux`) i dodatkowo sprawdza wszystkie znaczniki pakietów.

Wykonane kontrole końcowe:

| Kontrola | Wynik |
| --- | --- |
| Pełne QA | PASS, kod 0; `TestResults/quality/20261029-174011-0400fa7a/report.html` |
| Release / PublicRelease | 642/642 oraz 635/635, bez pominięć |
| JavaScript | 50/50 |
| Nowe lekkie kontrole benchmarku | 21/21, włączone do Full |
| Cała kategoria FFmpeg, w tym nowe testy zapisu bufora | 9/9; `TestResults/recording-native/recording-native-verified.trx` |
| Kompilacja i analizatory | 0 błędów i 0 ostrzeżeń |
| Pokrycie aplikacji w Full | 74,25% linii |
| Host — benchmark | 30/30 zmierzonych próbek, kod 0; QSV jawnie niedostępny |
| Sandbox — poprawność zapisanych segmentów | PASS, kod 0; nie oznacza płynnych 60 nowych klatek/s |
| Sandbox — kontrola wydajności GDI 60 FPS | FAIL, kod 1: 320 zamiast około 600 klatek |

Kontrola składni skryptów, odmowa uruchomienia przechwytywania na hoście,
kompilacja klasy planszy oraz przygotowanie konfiguracji `ControlsOnly` również
zostały sprawdzone. Ostatnia przygotowana konfiguracja ControlsOnly nie została
uruchomiona ponownie; właściwa próba A/B odbyła się w tej samej, wcześniej
zatwierdzonej piaskownicy. Testy mikrofonu, WASAPI i rzeczywistej gry nie były
wykonywane. Nie zmieniono progów ani asercji, by zamaskować wynik GDI.

Najbardziej uzasadnione następne działania to osobna poprawka wyboru ścieżki
przechwytywania w Sandbox/RDP oraz widoczna diagnostyka rzeczywistego FPS,
duplikatów i CPU fallback. Na zwykłym Windows warto zbadać zgodną ścieżkę bez
kopiowania GPU→CPU oraz preset NVENC p4 z oceną jakości obrazu. Nie wystarczy
przełączyć `TierGpu` jednym warunkiem: obecna gałąź omija skalowanie i wymaga
zgodności formatu/adaptora. Ryzyka watchdog/restart i wielu strumieni audio
opisano w audycie jako wynik inspekcji kodu, bez deklarowania ich odtworzenia.
