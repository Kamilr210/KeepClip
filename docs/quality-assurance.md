# Kontrola jakości KeepClip

QA ma jeden lokalny punkt wejścia: `scripts/quality-check.ps1`. Ten sam skrypt
wykonuje GitHub Actions i Codex. Wynik jest zielony dopiero po wykonaniu wymaganych
kontroli, a nie tylko po skompilowaniu projektu. Kontrola nie publikuje aplikacji.

## Rozpoznana architektura

| Obszar | Implementacja i znaczenie dla testów |
| --- | --- |
| Solution | `KeepClip.slnx`: aplikacja `KeepClip` i projekt `KeepClip.Tests`; oba `net10.0-windows` |
| Środowisko | Windows x64, .NET SDK z `global.json` (10.0.403, nowsza poprawka w paśmie 10.0.4xx dozwolona), Windows Desktop Runtime 10.0.14 lub nowsza poprawka |
| Host | ASP.NET Core Minimal API i WinForms/WebView2 w jednym procesie; tryb `server-only` umożliwia testowanie bez GUI |
| Warstwy | `Endpoints` → `Services` → `Data`, modele w `Domain`/`Application`, zadania w `Workers`, integracje w `Infrastructure` |
| Baza | `Microsoft.Data.Sqlite`, SQLitePCLRaw i ręczny SQL; testy używają rzeczywistej osobnej bazy SQLite, także FTS i transakcji |
| Interfejs | Statyczne HTML/CSS/JavaScript w `frontend/`, bez instalacji pakietów npm; osobna strona marketingowa w `keepClip-Web/` |
| Multimedia | FFmpeg/ffprobe, NAudio/WASAPI, Whisper.net z runtime CPU/Vulkan, WebView2 do odtwarzania |
| Chmura | Własny klient HTTP Google Drive, OAuth/PKCE i Windows Credential Manager; testy transportu i sesji używają atrap |
| Budowanie wydania | `installer/build.ps1`, profil `win-x64.pubxml`, Inno Setup; przypięty runtime publikacji i kontrolowane licencje w `licenses/manifest.json` |

Wersje bibliotek są w plikach projektów i `packages.lock.json`. Lockfile zapisuje
także zależności przechodnie. Manifest licencji aplikacji jest dodatkowym kontraktem
wydania; aktualizacja pakietu produkcyjnego wymaga sprawdzenia obu tych źródeł.

`Release` nadal zawiera `KEEPCLIP_DEV`. Dopiero `PublicRelease=true` usuwa kod
deweloperski. Dlatego pełne QA osobno buduje i testuje oba warianty. Nie zastępuje
tego publikacją instalatora.

### Główne procesy

- **Biblioteka:** `frontend/app.js` odpytuje `POST /api/scan?automatic=true` co 2,5
  sekundy, gdy okno jest widoczne i nie ma otwartej konfiguracji. `LibraryScanService`
  obserwuje katalog przez `FileSystemWatcher`: pomija skan bez zmian i wykonuje
  pełną kontrolę co minutę, także przy utracie zdarzeń. Ręczny `POST /api/scan`
  zawsze skanuje. `Scanner` i `LibraryTraversal`
  przeglądają katalog, ograniczają czas/liczbę wpisów/głębokość, pomijają dowiązania
  i czekają na ustabilizowanie pliku. Zdarzenia podczas skanu i pliki jeszcze
  zapisywane pozostają do sprawdzenia w następnej próbie.
- **Klipy i pliki:** `ClipService`, repozytoria i `Trash` odpowiadają za metadane,
  foldery, wyszukiwanie, usuwanie i przenoszenie. Przeniesiony poza aplikacją plik
  jest rozpoznawany przez skan jako usunięcie starej ścieżki i dodanie nowej.
- **Wideo:** `MediaProcessor`, `MediaProcessRunner`, `AudioExtractor` i
  `PlaybackProxyService` przygotowują miniatury, wycinki, kompresję, naprawy i
  podglądy. Adaptery pozwalają testować błędy i anulowanie bez prawdziwego kodera.
  Żądania miniatur i worker korzystają z jednej kolejki; błędy mają 5 minut
  przerwy przed ponowieniem, a zmiana pliku resetuje tę przerwę. Podglądy mają
  jedną aktywną konwersję; `DELETE /api/playback/{id}` anuluje ją wraz z procesem
  FFmpeg. Cache podglądów jest sprzątany do 2 GiB, z ochroną właśnie utworzonego
  pliku i plików, których system nie pozwala usunąć.
- **Transkrypcja:** `TranscribeWorker` zarządza kolejką i anulowaniem;
  `Transcriber` dobiera model do pamięci GPU i obsługuje zejście na lżejszy model.
  Zapis segmentów i statusu odbywa się transakcyjnie.
- **ShadowPlay/AMD:** nagrania ShadowPlay są zwykłymi plikami biblioteki, z obsługą
  naprawy wadliwego początku. AMD występuje przez kodery AMF FFmpeg; NVIDIA przez
  NVENC. Nie ma odrębnego klienta API ShadowPlay ani AMD.
- **Powtórki i zasoby:** `ReplayService`, `ReplayWriter`, `AudioPump` i
  `ReplayCaptureCommand` zarządzają buforem nagrywania, procesem kodera, audio i
  zapisem wyniku. `ThumbnailWorker`, postęp pojedynczych operacji oraz monitor
  zasobów mają osobny stan i synchronizację.
- **Chmura:** `CloudService`, `CloudConnection`, `GoogleDriveClient` i
  `OAuthSession` odpowiadają za transfery, odczyt metadanych i sesję; testy
  sprawdzają m.in. błędy HTTP, zerwane transfery i zmianę sesji w trakcie żądania.

## Uruchamianie lokalne

Najprościej: otwórz folder repozytorium i kliknij dwukrotnie **`Testy.cmd`**.
Uruchomi pełne QA i po zakończeniu otworzy w przeglądarce `report.html`, również
przy nieudanych testach. **`Ostatni-raport.cmd`** otwiera ostatni przebieg bez
ponownego uruchamiania testów. Jeśli najnowszy przebieg jeszcze trwa lub raport
nie powstał, zgłasza ten stan zamiast pokazywać starszy zielony wynik.

Raport działa także po zwykłym otwarciu pliku HTML, bez serwera i bez internetu.
Pokazuje wynik całego przebiegu, kroki QA oraz pojedyncze testy .NET i Node.
Wyszukiwarka i filtry pomagają znaleźć błędy; rozwinięcie testu pokazuje komunikat,
stos wywołań i plik z linią, jeśli narzędzie podało tę informację. Lokalizacja
jest wskazówką diagnostyczną, nie automatycznym rozpoznaniem przyczyny błędu.
Kontrole PowerShell, które nie udostępniają pojedynczych wyników, pozostają
widoczne jako kroki z pełnym logiem.

Przycisk kopiowania promptu zbiera nieudane kontrole i dane potrzebne Codexowi
do diagnozy. Ten sam tekst jest w `repair-prompt.txt`; nic nie jest automatycznie
wysyłane do innego czatu. Gdy przeglądarka blokuje schowek, można zaznaczyć tekst
i skopiować go ręcznie. Raport zaznacza też niepełne lub uszkodzone wyniki.

Wymagane są Windows, .NET SDK zgodny z `global.json`, PowerShell oraz Node.js 22
(wersja używana w CI). Nie są potrzebne konto Google, token
API użytkownika, model Whisper, Docker ani instalator. Restore i audyt NuGet
wymagają dostępu do źródła pakietów i informacji o podatnościach.

Z katalogu głównego repozytorium:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/quality-check.ps1 -Mode Quick
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/quality-check.ps1 -Mode Full
```

Dodaj `-OpenReport`, aby raport otworzył się automatycznie. Przykład szybkiego testu:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/quality-check.ps1 -Mode Quick -OpenReport
```

`Quick` służy do iteracji po małej zmianie. `Full` jest kontrolą przed mergem i
zakończeniem pracy Codexa. Oba tryby zgłaszają niepowodzenie kodem innym niż zero.
Nie traktuj wyniku pojedynczego wybranego testu jako pełnego zaliczenia QA.

Przykład węższej iteracji po zmianie skanera:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/quality-check.ps1 -Mode Quick -Filter FullyQualifiedName~ScannerTests
```

`-Filter` działa wyłącznie z `Quick`. `Full` odrzuca filtr, aby nie dopuścić do
przypadkowego pominięcia części zestawu.

| Kontrola | Tryb | Cel |
| --- | --- | --- |
| Przywrócenie zależności z lockfile | Quick i Full | Wykrycie niezgodności pakietów i audyt podatności, także przechodnich |
| Build Release i analizatory | Quick i Full | Błędy kompilacji, istotne ostrzeżenia, problemy asynchroniczne i użycie zasobów |
| xUnit | Quick i Full | Logika, pliki, SQLite, usługi, zadania, prywatne HTTP API, procesy i transporty |
| Pokrycie | Full | Cobertura i czytelny raport dla kodu aplikacji, bez arbitralnego progu |
| Build/test PublicRelease | Full | Kompilacja i zachowanie po usunięciu kodu `KEEPCLIP_DEV` |
| Node i tłumaczenia | Quick i Full | Istniejące testy wyboru Dockera, powiązania demo i kompletności i18n |
| Testy konfiguracji OAuth | Full | Kontrola filtrowania konfiguracji pakowania na syntetycznych danych |
| Spójność zależności i licencji | Full | Zgodność pakietów, hashy materiałów i runtime z manifestem, bez publikacji |
| Testy mechanizmu QA | Full | Reakcja runnera na niepowodzenia i niekompletne wyniki |

Wyjście narzędzi i szczegółowe błędy są zapisywane w osobnym katalogu
`TestResults/quality/<identyfikator-uruchomienia>/`. Zachowaj ścieżkę wypisaną przez
skrypt; poprzedni raport nie jest dowodem wyniku bieżącej wersji kodu.
Raporty i logi są ignorowane przez Git.

`summary.md` i `summary.json` zawierają wynik kroków, błędy i wykonaną liczbę
testów. `report.html` pokazuje interaktywny przegląd; `report-data.json` zawiera
odczytane wyniki, a `repair-prompt.txt` tekst do diagnozy. TRX zapisuje szczegóły
xUnit, `node-tests.xml` wyniki Node w formacie JUnit. `coverage.html`,
`coverage-summary.md`/`.json` i `coverage.cobertura.xml` powstają w pełnym przebiegu.
Brak testów .NET, pominięte testy xUnit oraz brak świeżego raportu pokrycia oznaczają
niepowodzenie odpowiedniej kontroli. Kroki niewymagane są oznaczone
`not-requested`, a zablokowane poprzednią awarią jako `blocked`.

Build trafia do własnego podkatalogu przebiegu przez `--artifacts-path`. Nie
nadpisuje zwykłego `KeepClip/bin` i nie wymaga zamykania uruchomionej aplikacji
użytkownika. Niektóre niezależne kontrole mogą przejść mimo awarii wcześniejszego
kroku; cały przebieg nadal kończy się kodem 1.

### Testy wymagające narzędzi natywnych

Projekt już zawiera dwa jawnie włączane zestawy:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/quality-check.ps1 -Mode Full -IncludeFfmpeg
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/quality-check.ps1 -Mode Full -IncludeFfmpeg -IncludeDesktop
```

FFmpeg wymaga lokalnych `tools/bin/ffmpeg.exe` i `ffprobe.exe`; tworzy syntetyczne
audio/wideo w katalogu tymczasowym. Test Desktop wymaga dodatkowo WebView2 Runtime
i sesji pulpitu Windows. Sprawdza izolację poświadczenia w rzeczywistym WebView2,
a nie kompletny wygląd interfejsu. Brak narzędzia przy jawnym żądaniu testu jest
blokadą, a nie zaliczonym testem. QA nie uruchamia `setup.ps1` i niczego natywnego
nie pobiera automatycznie.

Testy licencji `tests/license-packaging.Tests.ps1` wymagają już przygotowanego
katalogu publikacji. Pozostają w procesie wydania i można uruchomić je jawnie
przeciwko własnemu artefaktowi. Zwykłe QA nie wykonuje `dotnet publish`, podpisywania,
Inno Setup, nagrywania pulpitu ani wdrożenia.

## Analiza statyczna i zależności

Analizatory Roslyn/.NET są dostarczane z SDK; ich konfiguracja jest wersjonowana
w `Directory.Build.props` i `.editorconfig`. Działają podczas kompilacji, więc
ten sam zestaw reguł obowiązuje lokalnie i w CI. Nie jest wymagany serwer SonarQube.
Nie wprowadzamy automatycznego formatowania całego repozytorium.

`QualityCheck=true` i parametry kompilacji runnera traktują ostrzeżenia jako
błędy. Obok reguł SDK jawnie włączone są m.in. poprawne użycie `ValueTask`
(`CA2012`), przekazywanie tokenu anulowania (`CA2016`), zachowanie stosu wyjątku
(`CA2200`), bezpieczny XML i kryptografia oraz zwalnianie zasobów w kodzie
aplikacji (`CA2000`, `CA2213`). Bieżący zestaw reguł jest w `.editorconfig`.

Audyt NuGet jest częścią restore. Znana podatność oraz brak możliwości wykonania
audytu nie mogą dawać zielonego pełnego wyniku. Nie wyłączaj audytu ani nie używaj
`--ignore-failed-sources` w celu ominięcia błędu. Zweryfikuj wersję SDK, połączenie
z NuGet i treść ostrzeżenia. Przy aktualizacji zależności świadomie odśwież lockfile,
przejrzyj zmiany i sprawdź manifest licencji; potem ponów `Full`.

Pełny restore omija cache HTTP, a ostrzeżenia `NU1900`–`NU1905` blokują przebieg.
Cache plików pakietów przyspiesza CI, lecz nie zastępuje aktualnego audytu.

## Pokrycie i wybór testów

Pomiar obejmuje aplikację `KeepClip.dll`, a nie kod testów ani bibliotek NuGet.
Nie wykluczamy GUI, P/Invoke ani trudnych ścieżek tylko po to, aby podnieść procent.
Nie ma arbitralnego minimalnego progu pokrycia. Raport służy do wyboru następnego
ryzyka do przetestowania; zaliczenie testów i audytu pozostaje niezależnym warunkiem.

Bez GUI można sprawdzać skanowanie, repozytoria, FTS, serwisy klipów, HTTP API,
stan kolejek, anulowanie, polecenia mediów, transporty chmurowe i ich błędy.
Rzeczywiste lokalne procesy testowe sprawdzają odczyt strumieni, timeout i zakończenie
procesu; nie są to prawdziwe konta ani usługi produkcyjne.

Najważniejsze obszary wymagające osobnej weryfikacji pozostają następujące:

| Obszar | Ograniczenie automatyzacji |
| --- | --- |
| WinForms/WebView2 | Zwykłe Full nie otwiera okna; test natywny nie obejmuje pełnej obsługi UI |
| GPU, NVENC/AMF/QSV, WASAPI | Atrapy i testy poleceń nie dowodzą działania urządzenia ani sterownika |
| Whisper | Dobór modeli i segmentacja są testowalne; prawdziwa inferencja wymaga dużego modelu i zasobów |
| Google Drive/OAuth | Atrapy testują kontrakty, błędy i wyścigi; nie sprawdzają aktualnej usługi Google na koncie |
| Instalator i aktualizacja | Full nie wykonuje instalacji, podpisywania ani publikacji |
| Frontend | Testy Node nie zastępują pełnego testu przeglądarkowego od skanu do odtworzenia |
| Obciążenie | Krótkie scenariusze współbieżne nie zastępują długiej sesji nagrywania ani profilowania pamięci |

## Dodawanie testów

1. Umieść test przy odpowiedniej warstwie w `tests/KeepClip.Tests`. Przed dodaniem
   przeczytaj istniejące testy i pomocnicze adaptery, żeby nie powielać scenariusza.
2. Używaj `TestEnvironment.NewTempDir` i `TestDb.Reset`; nie odwołuj się do
   użytkownikowego `data/`, folderu Wideo, tokenu Drive ani modelu Whisper.
3. Sprawdzaj kontrakt obserwowalny: stan bazy i pliku po niepowodzeniu, zwolniony
   zasób, zakończone zadanie, kompletny wynik. Nie sprawdzaj tylko tego, że metoda
   wywołała inną metodę w bieżącej implementacji.
4. W testach współbieżnych używaj sygnałów i limitów czasu zamiast przypadkowych
   opóźnień. Sprzątaj własne procesy, listenery, zmienne i statyczne adaptery w
   `finally`. Nie włączaj równoległego wykonywania całego zestawu: współdzieli
   statyczny stan aplikacji.
5. Awarie sieci i kodera odtwarzaj przez `HttpMessageHandler`, `CloudConnection`,
   `ClipOperations` lub właściwy adapter. Rzeczywisty FFmpeg uruchamiaj tylko
   w jawnie oznaczonych testach na wygenerowanych danych.
6. Przy naprawie regresji najpierw potwierdź błąd testem, następnie popraw kod
   i uruchom właściwy test oraz `Full`. Nie osłabiaj asercji, by pasowały do błędu.

`TestEnvironment` ustawia `KEEPCLIP_DATA_DIR`, `KEEPCLIP_SERVER_ONLY=1`, losowy
testowy `KEEPCLIP_API_TOKEN` i `KEEPCLIP_IDLE_EXIT=0` przed pierwszym użyciem
konfiguracji. Testy Credential Manager używają własnych losowych nazw wpisów,
a testy OAuth własnego listenera na `127.0.0.1`. Nie potrzebują prawdziwych tokenów.

## GitHub Actions

`.github/workflows/ci.yml` uruchamia wspólny runner QA na Windows przy PR, pushu
na główną gałąź `csharp` i ręcznie (`workflow_dispatch`). Używa przypiętego przez
`global.json` SDK, przygotowuje Node, cache pakietów zależny od lockfile i zapisuje
raporty również po niepowodzeniu. Uprawnienia ograniczają się do odczytu kodu.
Niezaufany PR nie otrzymuje sekretów ani uprawnień publikacji.

Artefakt `quality-reports` zawiera też raport HTML, jego dane i prompt. Po
pobraniu rozpakuj cały artefakt i otwórz `report.html`; względne odnośniki do
logów i coverage wymagają zachowania sąsiednich plików. CI nie otwiera przeglądarki.

`.github/workflows/release.yml` nadal odpowiada za FFmpeg ze źródeł, testy
natywne, pakowanie licencji, instalator i wydanie po tagu `v*`.
`.github/workflows/pages.yml` publikuje osobną stronę marketingową. QA ich nie
uruchamia i nie zastępuje. Ustawienie w GitHub obowiązkowego statusu CI przed
mergem wymaga osobnej konfiguracji ochrony gałęzi; sam plik workflow jej nie ustawia.

## Cykl pracy Codexa i diagnoza

Repozytoryjny `AGENTS.md` opisuje wymagania dla KeepClip, a
`.agents/skills/keepclip-quality/SKILL.md` kieruje do tego skryptu i dokumentu.
Umieszczenie skilla w repo odpowiada [oficjalnemu formatowi Codexa](https://learn.chatgpt.com/docs/build-skills),
a zakres instrukcji opisuje [dokumentacja AGENTS.md](https://learn.chatgpt.com/docs/agent-configuration/agents-md).
Nie instalujemy globalnego skilla ani automatyzacji wpływającej na inne sesje.

Codex pracuje w cyklu: zmiana → właściwa kontrola → odczyt logu → ustalenie
przyczyny → mała poprawka → ponowna kontrola. Po trzech kolejnych próbach bez
postępu zapisuje diagnozę i rzeczywistą blokadę, następnie sprawdza niezależne
części. Przed zakończeniem uruchamia pełny dostępny zestaw i podaje, czego nie
można było wykonać. Skrypt sam nie modyfikuje kodu; naprawy wykonuje agent w
aktywnej sesji, zgodnie z zakresem zadania.

| Objaw | Dalsza czynność |
| --- | --- |
| Niezgodny SDK | Sprawdź `global.json` i `dotnet --list-sdks`; nie zmieniaj TFM dla obejścia środowiska |
| Restore/audyt nie działa | Odczytaj log NuGet; sprawdź źródło, połączenie, lockfile i wskazane advisory |
| Błąd builda/analizatora | Zacznij od pierwszej diagnostyki z plikiem i linią; nie wyciszaj reguły bez usunięcia przyczyny |
| Zablokowany plik aplikacji | Nie zabijaj procesu użytkownika; użyj izolowanego builda albo zgłoś blokadę |
| Nieudany test | Odczytaj TRX i odtwórz konkretny scenariusz na danych testowych, potem pełny zestaw |
| Zero odkrytych testów | Sprawdź filtr i konfigurację; brak wykonania nie oznacza sukcesu |
| Brak coverage | Sprawdź uruchomienie kolektora i raport z bieżącego przebiegu; stary plik nie jest zamiennikiem |
| Brak FFmpeg/WebView2 | Zgłoś blokadę testu natywnego; nie pobieraj modelu ani nie uruchamiaj nagrywania |

Każda kontrola powinna pozostawić dokładne polecenie i wynik. Pełny raport
końcowy rozróżnia błędy produktu, ograniczenia środowiska i kontrole niewykonane.

Mechanikę odczytu wyników można sprawdzić oddzielnie:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/quality-runner.Tests.ps1
```

Testuje ona m.in. rzeczywisty proces kończący się błędem, brak programu/TRX,
zerową liczbę lub pominięcie testów, uszkodzony XML, agregację pokrycia i kodowanie
nazw plików w HTML. Wszystkie pliki powstają w osobnym katalogu tymczasowym.
To testy mechanizmu QA; dodatkowa kontrolowana regresja w teście aplikacji musi
zostać cofnięta przed końcowym pełnym przebiegiem.

## Dalsza rozbudowa

Pomiary nagrywania i próby GDI/Sandbox opisano osobno w
[raporcie nagrywania](recording-performance.md). Full wykonuje lekkie testy
narzędzi pomiaru; benchmark FFmpeg i nagrywanie planszy w VM pozostają jawnie
uruchamianymi eksperymentami, poza zwykłym QA.

Kolejne wartościowe kroki to test UI obejmujący automatyczne odświeżanie biblioteki,
dedykowany runner Windows ze sprawdzonym FFmpeg/WebView2, okresowa macierz GPU i
audio oraz dłuższy test zużycia pamięci. Testy prawdziwej chmury wymagają odrębnego
środowiska i konta testowego; nie należy dodawać ich do PR z forków.

## Weryfikacja wdrożenia QA

Weryfikację wykonano lokalnie 29.10.2026 na Windows, SDK 10.0.402 i Node 24.11.0.
Punkt odniesienia przed zmianami: 614/614 testów Release. Dodano 18 przypadków
xUnit dotyczących spójności skanowania i zwalniania zasobów klienta HTTP.

Końcowe polecenie, wykonane po usunięciu kontrolowanej regresji:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/quality-check.ps1 -Mode Full -IncludeFfmpeg -IncludeDesktop
```

Wynik: **PASS, kod 0**. Lokalny raport (ignorowany przez Git):
`TestResults/quality/20261029-164534-db37c892/summary.md`.

| Wykonana kontrola | Wynik |
| --- | --- |
| Build i analizatory Release, PublicRelease oraz obu wariantów natywnych | 0 błędów, 0 ostrzeżeń |
| xUnit Release | 632/632, bez pominięć |
| xUnit PublicRelease | 625/625, bez pominięć; 7 testów dotyczy kodu deweloperskiego |
| xUnit FFmpeg | 7/7 na syntetycznym audio/wideo |
| xUnit Desktop | 2/2 na lokalnych serwerach i testowym profilu WebView2 |
| Node | 36/36 |
| Pakowanie publicznej konfiguracji OAuth | 23 kontrole |
| Mechanizm QA | 22 kontrole; sprawdzone także osobno w PowerShell 5.1 i 7 |
| Restore, audyt NuGet i zgodność licencji | PASS; 19 zależności aplikacji, hashe i przypięty runtime |
| Pokrycie linii aplikacji | 3738/5036 = 74,23% |
| YAML workflow i konfiguracja wyzwalaczy/uprawnień/artefaktów | Sprawdzone lokalnie |
| Lokalny skill | Oficjalny `quick_validate.py`: `Skill is valid!` |

Pokrycie jest mierzone w zestawie Release bez GUI. Osobne wykonanie natywnych
testów nie jest doliczane do tego pomiaru. Raport wskazuje m.in. `AudioPump`,
`DesktopShell` i `ReplayToast` z 0% w tym zestawie, `Transcriber` z 22,22%
i `ReplayService` z 23,91%. Nie wykluczono ich z mianownika.

### Potwierdzone naprawy

- Test rollbacku SQLite najpierw wykazał utratę miniatury (7/8 nowych przypadków
  skanera przechodziło). `Scanner` usuwa teraz miniatury dopiero po zatwierdzeniu
  transakcji; po naprawie przeszedł cały zestaw skanera.
- Analizatory wskazały niezwalniane treści formularzy HTTP i enumerator urządzeń
  audio. Dodano jawne zwalnianie oraz 6 testów formularzy po sukcesie i błędzie.
- Uporządkowano zwalnianie timera, kontrolek, WebView2 i ikony/menu zasobnika.
  Oczekiwanie na zakończenie dekodera w cleanup jawnie używa `CancellationToken.None`,
  aby anulowanie operacji nie przerwało sprzątania jej procesu.
- Pierwszy pełny przebieg ujawnił duplikowane załączniki pokrycia VSTest. Runner
  rozpoznaje identyczne kopie po SHA256; różne raporty nadal oznaczają błąd.
  Oddzielono też ustawienia pokrycia, aby Quick i PublicRelease go nie zbierały.
- Sprawdzono błąd uruchomienia istniejącego, nieprawidłowego programu: poprzedni
  kod wyjścia 0 nie może spowodować fałszywego zaliczenia kolejnej kontroli.

### Próba wykrywania regresji

Tymczasowy `QualityGateRegressionProbe` zawierał test z `Assert.Fail`.
`Quick -Filter FullyQualifiedName~QualityGateRegressionProbe` poprawnie skompilował
projekty, wykonał dokładnie ten test, zapisał FAIL w TRX i zakończył QA kodem 1.
Próba: `TestResults/quality/20261029-164410-50cf84bf/`; potwierdzenie:
`TestResults/quality-regression-proof.json`. Plik testu usunięto w `finally`;
nie jest częścią końcowego kodu. Następujący po nim pełny przebieg przeszedł.

### Ograniczenia tej weryfikacji

GitHub Actions nie uruchamiano zdalnie: nie wykonano commita ani pushu. Lokalnie
wykonano ten sam runner, także pod PowerShell 7 używanym w CI. Nie publikowano,
nie podpisywano i nie instalowano aplikacji. Test pełnego pakowania licencji oraz
`ffmpeg-smoke.ps1` sprawdzający konkretny build FFmpeg pozostają w `release.yml`;
nie były częścią tego przebiegu. Nie wykonywano nagrywania sprzętowego, inferencji
Whisper ani połączeń z rzeczywistym kontem Google.

Kontrola tłumaczeń nadal zgłasza wcześniejsze ostrzeżenia o symbolach zastępczych
`selection.addN` i `toast.folderAdded`. Lokalna instalacja Sonara wypisuje brak
starego pliku `.sonarqube/bin/targets/SonarQube.Integration.targets`; QA nie
korzysta z tego serwera ani nie przedstawia tego przebiegu jako skanu Sonara.
Analizatory Roslyn działają i zakończyły się bez ostrzeżeń.

### Pliki wdrożenia

| Pliki | Zmiana |
| --- | --- |
| `scripts/quality-check.ps1`, `scripts/quality-common.ps1`, `scripts/check-dependencies.ps1` | Wspólny runner, raportowanie, kontrola zależności |
| `Directory.Build.props`, `.editorconfig`, `NuGet.Config` | Reguły analizy, audyt i źródło zależności |
| `KeepClip/packages.lock.json`, `tests/KeepClip.Tests/packages.lock.json` | Blokady wersji i zawartości pakietów |
| `tests/KeepClip.Tests/KeepClip.Tests.csproj` | Kolektor pokrycia z `PrivateAssets=all` |
| `tests/quality.runsettings`, `tests/quality.coverage.runsettings`, `tests/quality-runner.Tests.ps1` | Ustawienia i testy mechanizmu QA |
| `tests/KeepClip.Tests/Workers/ScannerConsistencyTests.cs`, `tests/KeepClip.Tests/Infrastructure/GoogleDriveClientTests.cs` | Testy regresji i zwalniania zasobów |
| `KeepClip/Workers/Scanner.cs` | Zachowanie miniatur po rollbacku |
| `KeepClip/Infrastructure/GoogleDriveClient.cs`, `AudioPump.cs`, `AudioExtractor.cs`, `DesktopShell.cs`, `ReplayToast.cs` | Poprawki wskazane przez analizatory |
| `.github/workflows/ci.yml` | Integracja pełnego QA bez publikacji |
| `AGENTS.md`, `.agents/skills/keepclip-quality/SKILL.md`, `README.md`, `docs/quality-assurance.md` | Instrukcje lokalne i dokumentacja |

Zachowano zastane zmiany użytkownika w `DesktopShell.cs` oraz
`tests/KeepClip.Tests/DesktopIntegration/AuthenticatedWebViewTests.cs`.
Workflow `release.yml` i `pages.yml` nie zostały zmienione.

## Rozszerzenie o raport graficzny — 29.10.2026

Dodano `Testy.cmd`, `Ostatni-raport.cmd`, `scripts/open-quality-report.ps1`,
generator `scripts/quality-report.ps1`, szablon `scripts/quality-report.template.html`
oraz testy `tests/quality-report.Tests.ps1` i `tests/quality-report.test.cjs`.
Runner i workflow CI zapisują teraz również pojedyncze wyniki Node w JUnit,
raport HTML, dane raportu i prompt do naprawy.

Końcowe `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/quality-check.ps1 -Mode Full`
zakończyło się **PASS, kod 0**. Raport:
`TestResults/quality/20261029-170541-adcbe1f0/report.html`.

- 632/632 testy Release, 625/625 PublicRelease, 50/50 Node — razem 1307
  pojedynczych wyników dostępnych w raporcie, bez pominięć.
- 100 kontroli generatora raportu i 14 testów JavaScript jego interakcji
  (te 14 jest częścią 50 testów Node), a także dotychczasowe kontrole QA/OAuth.
- Kompilacje i analizatory: 0 ostrzeżeń, 0 błędów; pokrycie linii: 74,23%.
- Kontrolowana regresja w osobnym przebiegu Quick zwróciła kod 1 i poprawnie
  pokazała 1 błąd .NET, 36 sukcesów Node, nazwę testu, źródłową linię 1 oraz
  prompt z błędem. Raport próby: `TestResults/quality/20261029-165951-c2149027/`.
  Tymczasowy `GraphicalQaRegressionProbe.cs` został usunięty przed pełnym QA.
- Parser sprawdzono w PowerShell 5.1 i 7. Testy wykrywają niespójne liczniki TRX,
  niepełne dane i nieznane statusy; takie wyniki nie mogą dawać zielonego sukcesu.

Testy JavaScript wykonują właściwy skrypt raportu na atrapie DOM i sprawdzają
filtry, wyszukiwanie, rozwijanie, kopiowanie oraz ręczny tryb przy blokadzie schowka.
Nie są testami renderowania przeglądarki. Automatyczny podgląd wizualny nie został
wykonany, ponieważ wbudowana przeglądarka narzędziowa blokuje adresy `file://`.
Wywołanie otwierające raport sprawdzono z atrapą startu przeglądarki; nie otwierano
w niej pliku w ramach tej walidacji. Nie uruchamiano zdalnie GitHub Actions.
