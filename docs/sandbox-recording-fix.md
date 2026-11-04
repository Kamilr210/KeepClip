# Nagrywanie w Windows Sandbox

4 listopada 2026 sprawdziliśmy poprawiony bufor na własnej animowanej planszy. Przy ustawieniu 60 FPS KeepClip zapisał 319 otrzymanych klatek przez 10,008 s. FFmpeg zgłosił zero dodanych i zero odrzuconych klatek. W tym Sandboxie ani GDI, ani Windows Graphics Capture nie osiągnęły 60 różnych obrazów/s.

## Co zmieniło się w KeepClip

- Na ścieżce GDI wyłączyliśmy flagę `CAPTUREBLT`. W porównaniu na tym samym pulpicie użytkownik widział migotanie na starym wariancie. Po wyłączeniu flagi kursor był stabilny również w dwóch kolejnych próbach. Bez tej flagi niektóre okna warstwowe lub nakładki mogą nie znaleźć się na nagraniu.
- Dołączony FFmpeg zachowuje czas pobrania każdej klatki GDI. Koder rozpoczyna kolejne segmenty samodzielnie dekodowalną klatką mniej więcej co dwie sekundy. Dzięki temu odtwarzanie i składanie klipu zachowują czas nagrania również wtedy, gdy pulpit dostarcza mniej obrazów niż wybrany limit. Ścieżka DDA na zwykłym pulpicie zachowuje dotychczasowe parametry.
- Okno powtórek dla GDI pokazuje średni FPS raportowany przez proces nagrywający oraz ustawiony limit. Pomiar uwzględnia rozruch procesu. Brak zgodnego FFmpeg z opcjami GDI daje komunikat o konieczności aktualizacji narzędzi.

## Porównanie nagrań przy ustawieniu 60 FPS

| Wynik w tym samym Sandboxie, kolejno A i C | Stary GDI | Poprawiony GDI |
| --- | ---: | ---: |
| Zakodowane klatki | 600 | 319 |
| Klatki dodane przez FFmpeg | 282 | 0 |
| Klatki odrzucone przez FFmpeg | 0 | 0 |
| CPU procesu FFmpeg, udział w całej mocy CPU gościa | 10,98% | 6,27% |
| Szczyt próbkowanej pamięci roboczej FFmpeg | 709 MiB | 615 MiB |

W fazie C plansza rysowała 62,82 obrazu/s. Pięć segmentów trwało od 1,997 do 2,012 s. Po złożeniu MP4 wszystkie 319 klatek miało rosnące znaczniki czasu. Pełne dekodowanie przeszło, największy odstęp wyniósł 60,4 ms. To pojedyncze porównanie na syntetycznej scenie. Nie mierzy spadku FPS gry ani całkowitego obciążenia maszyny.

Test zapisu MP4 i segmentów przeszedł. **Osobny test 60 świeżych klatek/s nie przeszedł**, więc raport całej próby ma kod 1.

Osobno uruchomiliśmy rzeczywisty KeepClip w tej samej VM przez lokalne API konfiguracji, statusu i zapisu bufora. Użyliśmy pustej bazy, H.264/x264 i dźwięku wyjściowego gościa z wyłączonym mikrofonem. Pierwszy plik miał 446 klatek i trwał 14,057 s. Po 30 s pracy z buforem ustawionym na 15 s aplikacja zapisała 512 klatek, 16,043 s i ścieżkę AAC. Bufor wybiera całe segmenty dwusekundowe. Oba pliki zdekodowały się w całości. W drugim klipie odstępy między klatkami dały średnio 31,91 FPS, a największa przerwa wyniosła 46,1 ms. Status aplikacji przed zapisem pokazał średnio 30,88 FPS od uruchomienia procesu, przy limicie 60.

## Próba Windows Graphics Capture

Prototyp otworzył monitor oraz własne okno przez D3D11. Każdy obraz planszy miał zakodowany w pikselach numer. Po skopiowaniu całej tekstury do pamięci CPU sonda odczytywała ten numer. Pozwala to odróżnić nowe obrazy od kolejnych powiadomień o tej samej zawartości.

| WGC, obserwacja po 10 s | Różne numery, monitor | Różne numery, okno |
| --- | ---: | ---: |
| Interwał domyślny | 322 | 319 |
| Żądany interwał 8 ms | 322 | 321 |
| Żądany interwał 16 ms | 320 | 322 |

Windows potwierdził ustawienie 8 ms, lecz mediana odstępu między obrazami nadal wynosiła 31,25 ms. Plansza rysowała ponad 63 razy/s, a wszystkie odczytane markery były prawidłowe. Pomiar obejmuje kopiowanie całej powierzchni do pamięci CPU, bez kodowania. Ten prototyp pozostał osobnym narzędziem testowym. [Dokumentacja interwału WGC](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.minupdateinterval)

Wcześniejszy bezpośredni test GDI, bez FFmpeg, dał 32,1 pobrania/s. Samo `BitBlt` zajmowało średnio 30,7 ms. Dokładniejszy zegar potrafił odmierzać 60 terminów/s, ale nie skrócił kopiowania pulpitu. W osobnej próbie FFmpeg z DDA nie udało się utworzyć urządzenia D3D11VA w gościu. Wyniki dotyczą konfiguracji z Microsoft Remote Display Adapter i vGPU ustawionym na `Default`.

## Powtórzenie

Z katalogu głównego repozytorium przygotuj jedną z dwóch izolowanych prób. Polecenia wypisują ścieżkę do pliku `.wsb` do otwarcia. Na czas pomiaru pozostaw widoczną planszę. Wyniki zostaną w katalogu `output` obok pliku konfiguracji.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/prepare-recording-sandbox.ps1 -GdiVfrProbe -DurationSeconds 10
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/prepare-wgc-sandbox.ps1
```

Po próbie GDI otwórz `report.html` w podkatalogu zakończonego przebiegu. Znajdziesz tam osobny wynik dla duplikatów, czasu segmentów, odtwarzania klipu i celu FPS, a także link do MP4. W przypadku WGC sześć pomiarów znajdziesz w `output/summary.json`.

Obie konfiguracje wyłączają sieć, mikrofon, kamerę i współdzielenie schowka. W zwykłej kontroli jakości nagrywanie nie jest uruchamiane. [Instrukcja sondy WGC](../scripts/probes/wgc/README.md) i [wcześniejsze pomiary wydajności](recording-performance.md).

## Kompletność ostatniej klatki zapisanej powtórki

Dłuższa próba NVENC na fizycznej RTX 5080 wykazała osobny problem. Osiem pełnych nagrań dekodowało się poprawnie, ale osiem klipów zapisanych w trakcie pracy bufora zawierało ucięty ostatni pakiet H.264. Kopiowanie otwartego pliku TS mogło kończyć się wewnątrz jednostki PES. Sam sukces składania MP4 przez kopiowanie strumienia nie wykrywał uszkodzenia.

`ReplaySegmentSnapshot` ogranicza teraz kopię do liczby bajtów dostępnych na początku zapisu i zachowuje wyłącznie potwierdzony kompletny prefiks transportu. Uwzględnia granice PES, jawne długości audio/wideo oraz końcowe wypełnienie pakietów używane przez dołączony muxer MPEG-TS FFmpeg. Rozwiązanie obejmuje format bufora H.264/HEVC z opcjonalnym AAC. Niepewna końcowa jednostka zostaje pominięta. Kompletne jednostki są kopiowane bez ponownego kodowania, a nagrywanie bufora trwa dalej.

Otwarty segment z klatkami B kończy się na ostatniej potwierdzonej pełnej grupie obrazów w kolejności prezentacji. Zapobiega to luce, gdy kolejna klatka referencyjna jest już zapisana, ale poprzedzające ją w odtwarzaniu klatki B jeszcze nie dotarły. Zakończone kompletne segmenty zachowują wszystkie klatki.

Weryfikacja objęła dokładne odtworzenie ośmiu wcześniejszych wadliwych snapshotów. Każdy poprawiony klip zachował 1020 kompletnych pakietów bez zmiany bajtów i przeszedł ścisłe dekodowanie. W dwóch nowych próbach na RTX 5080 klipy zapisane z aktywnego bufora także przeszły dekodowanie i były dokładnymi ciągłymi fragmentami pełnego nagrania, z identycznymi względnymi znacznikami czasu. Zapisane fragmenty miały 60 różnych obrazów/s i maksymalny odstęp 16,667 ms.

Zwykłe QA sprawdza granice transportu, wzrost pliku podczas kopiowania i zachowanie kompletnych danych. Opcja `-IncludeFfmpeg` dodaje regresję z H.264, klatkami B i AAC, uciętymi zarówno na granicy 188-bajtowego pakietu TS, jak i wewnątrz niego. Test najpierw potwierdza błąd dekodowania przy starym sposobie składania, następnie zapisuje klip przez produkcyjny `ReplayWriter` i wymaga ścisłego dekodowania bez błędów, oczekiwanej liczby klatek oraz nienaruszonego bufora.
