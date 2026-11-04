# Audyt ścieżki nagrywania — 29.10.2026

To analiza kodu i dokumentacji producentów, wykonana przy zgłoszeniu migotania
kursora i obciążenia podczas nagrywania. Nie uruchamiano w ramach tego audytu
nagrywania pulpitu, mikrofonu, benchmarku ani aplikacji. Wyniki pomiarów należy
rozpatrywać osobno. Nie potwierdzono odtworzenia migotania.

## Ustalenia w kodzie KeepClip

| Obszar | Fakt i znaczenie |
| --- | --- |
| Windows Sandbox / RDP | `DisplayHelper.PreferGdiCapture()` wybiera GDI dla konta `WDAGUtilityAccount`, sesji terminalowej, zmiennej `SESSIONNAME` zaczynającej się od `RDP-` lub `KEEPCLIP_CAPTURE_GDI=1`. W Sandbox jest więc inna ścieżka niż na zwykłym pulpicie. |
| Początkowa ścieżka | `ReplayService.StartLocked()` wybiera tylko `TierGdi` albo `TierCpu`. Istniejący `TierGpu` nie jest wybierany przez kod startu; watchdog może jedynie zwiększać numer poziomu. |
| Kopiowanie i konwersja | `TierCpu` zawsze używa `ddagrab → hwdownload → BGRA → scale/pad → NV12`. Wybór kodera NVENC/AMF/QSV nie usuwa tego pobierania obrazu z GPU ani konwersji programowej. `TierCpu` nie oznacza jednak automatycznie kodowania x264. |
| Ustawienia kodera | NVENC zawsze ma `p7`, AMF `transcoding`/`quality`, QSV `slow`, x264 `veryfast`. Poziom jakości low/medium/high zmienia QP/CRF, lecz nie zmienia tych presetów ani rozdzielczości. |
| Rozdzielczość | `DisplayHelper.MeasurePrimary()` korzysta z rozdzielczości monitora; ogranicza szerokość do 3840 i zaokrągla wymiary do parzystych. Domyślnie nagrywanie ma 60 FPS oraz jakość high. |
| Kolejka GDI | `ReplayCaptureCommand` dopuszcza 128 pakietów wejściowych obrazu. Dla pełnych klatek BGRA 3840×2160 byłoby to około 4,25 GB samych pikseli przy zapełnieniu kolejki. Jest to oszacowanie pojemności, a nie zmierzony pobór pamięci. |
| Kursor | Zarówno GDI, jak i DDA już mają `draw_mouse=0`. Kod KeepClip nie wywołuje `ShowCursor` ani `SetCursor`. Ponowne dodanie `draw_mouse=0` nie jest naprawą obecnego zgłoszenia. |
| Próba kodera | `ProbeEncoder()` testuje 3 klatki 320×240, kolejno NVENC, AMF i QSV; każdy proces ma limit 15 s. Próba odbywa się pod blokadą `Gate`. Nie sprawdza docelowej rozdzielczości, 60/90 FPS ani pełnego grafu przechwytywania. |
| Awaria po udanej próbie | Wybrany `_encoder` jest zachowywany. Watchdog zmienia tylko metodę przechwytywania; nie wybiera innego kodera. Koder działający w krótkiej próbie, ale zawodzący przy rzeczywistym nagraniu może więc zawodzić ponownie także po przejściu na GDI. |
| Restart i zatrzymanie | Po opóźnieniu watchdog sprawdza tylko `Enabled` i `_ff`. Nie sprawdza `_stopRequested` ani identyfikatora sesji. Należy przetestować scenariusz: awaria → oczekujący restart → `Shutdown()` przy nadal zapisanym `replay_enabled=1`. Sam odczyt kodu wskazuje możliwość ponownego startu po zatrzymaniu. |
| Dźwięk | Gdy nie wybrano identyfikatora wyjścia, `AudioPump` tworzy przechwytywanie dla wszystkich aktywnych wyjść audio, mimo etykiety „Domyślne” w interfejsie. Każdy dodatkowy strumień może wymagać mieszania i resamplingu. Pompa zapisuje do FFmpeg co około 20 ms. |
| Diagnostyka | `GET /api/replay/status` udostępnia osobne pola `encoder` oraz `capture`. Widoczny status interfejsu prezentuje koder; do porównania CPU/GPU trzeba zachować oba pola. Proces używa `-loglevel error`, więc zwykły log nie jest pomiarem osiągniętego FPS. |

Kluczowe miejsca: `KeepClip/ReplayService.cs` (start, próba kodera, watchdog),
`KeepClip/Infrastructure/ReplayCaptureCommand.cs` (argumenty FFmpeg),
`KeepClip/Infrastructure/DisplayHelper.cs` (Sandbox/RDP i wymiary),
`KeepClip/Infrastructure/AudioPump.cs` (strumienie audio),
`frontend/app.js` (`renderReplayStatus`).

## Znaczenie dla wydajności

Sam strumień BGRA 3840×2160 przy 60 FPS zawiera około 1,99 GB pikseli na sekundę.
To rachunek rozmiaru klatek, nie wynik benchmarku pamięci. Pobieranie klatek z GPU,
konwersja kolorów, kodowanie i zapis segmentów to odrębne koszty. Obniżenie samej
jakości kompresji nie usuwa kosztu przechwytywania tej samej liczby pikseli.

Dokumentacja FFmpeg opisuje `ddagrab` jako źródło klatek D3D11; przetwarzanie
programowe wymaga ich pobrania przez `hwdownload`. Opcja `draw_mouse` dotyczy
sprzętowego kursora, a kursor narysowany przez grę pozostaje częścią obrazu.
[Źródło dokumentacji FFmpeg](https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/doc/filters.texi).

Preset `p7` NVENC jest opisany przez FFmpeg jako najwolniejszy, nastawiony na
najlepszą jakość. Porównanie z `p4` ma sens jako osobny pomiar kompromisu jakości
i obciążenia; nie oznacza automatycznie, że samo ustawienie `p7` jest przyczyną
migotania. [Definicje opcji NVENC](https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/libavcodec/nvenc_h264.c).

## Migotanie kursora: co wiadomo, a czego nie potwierdzono

Windows Sandbox korzysta z sesji RDP. Według Microsoft vGPU jest domyślnie
włączone na urządzeniach innych niż Arm64; po jego wyłączeniu renderowanie
wykonuje WARP. Dokumentacja ta nie gwarantuje dostępności NVENC, AMF ani QSV
wewnątrz Sandbox. [Konfiguracja Windows Sandbox](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file).

W przejrzanym źródle `gdigrab` każda klatka jest pobierana przez
`BitBlt` z `SRCCOPY | CAPTUREBLT`. `draw_mouse=0` wyłącza dodatkowe rysowanie
kursora w kopii obrazu, ale nie wyłącza pobierania klatki. Nie znaleziono tam
wywołania `ShowCursor`. [Źródło gdigrab](https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/libavdevice/gdigrab.c).

Microsoft opisuje osobną obsługę wskaźnika w Desktop Duplication: kursor może
być częścią klatki albo oddzielną nakładką. [Desktop Duplication API](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api).

Interakcja GDI, kompozytora i kursora w sesji Sandbox/RDP jest zatem hipotezą do
sprawdzenia. Z powyższych źródeł nie wynika potwierdzenie konkretnej przyczyny
zgłoszonego migotania. Trzeba rozróżnić migotanie kursora widoczne na ekranie
od artefaktu widocznego dopiero w zapisanym nagraniu. Źródła FFmpeg podlinkowane
powyżej dotyczą aktualnej gałęzi upstream; przy analizie szczegółowego błędu
natywnego należy porównać je także ze źródłami konkretnego użytego buildu.

## Porównania potrzebne do pełnej diagnozy

1. Zanotować system hosta/Sandbox, ustawienie vGPU, rozdzielczość, 30/60/90 FPS,
   poziom jakości oraz rzeczywiste pola `capture` i `encoder`.
2. Oddzielić koszt samego kodowania syntetycznego obrazu od kosztu GDI/DDA,
   konwersji, audio i zapisu bufora. Wynik kodera nie jest pomiarem FPS gry.
3. Porównać identyczną własną scenę bez nagrywania i z nagrywaniem: czas CPU
   procesów, pamięć, tempo zapisu, liczbę zakodowanych klatek oraz opóźnienia.
   Powtórzyć pomiar; nie mieszać rozruchu, próby kodera i fazy ustalonej.
4. Przeprowadzić osobny test widocznego kursora w kontrolowanym środowisku.
   Nagranie z wyłączonym kursorem nie dowodzi, że kursor nie migotał na ekranie.
5. Sprawdzić zatrzymanie w trakcie backoff, zmianę parametrów i awarię kodera
   po udanej próbie; po końcu nie powinien pozostać proces FFmpeg.

Nie zmieniono ustawień nagrywania ani zachowania produkcyjnego na podstawie
samego audytu. Wartość ewentualnej optymalizacji należy potwierdzić pomiarem
oraz odtwarzalnym testem regresji.
