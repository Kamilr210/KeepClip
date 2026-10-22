# Rozbudowa testów — 22.10.2026

Poprzednie poprawki bezpieczeństwa zapisano w commicie `8b935ed`
„Usunięcie zagrożeń wskazanych przez Sonara i dodanie testów regresji”.
Poniższe testy, refaktoryzacje i poprawki są późniejszymi zmianami roboczymi.

## Wynik

| Pomiar | Przed rozbudową | Po rozbudowie |
| --- | ---: | ---: |
| Testy xUnit, Release | 215 | 470 |
| Pokrycie linii `KeepClip.dll`, Release | 42,88% | 68,68% |
| Pokrycie bloków `KeepClip.dll`, Release | 37,69% | 71,71% |

Dodano 255 przypadków testowych. Cały zestaw Release przechodzi, bez pominiętych
testów. PublicRelease przechodzi z 463 testami; różnicę stanowią testy kodu
deweloperskiego wyłączonego w tej konfiguracji. Przechodzi także 18 testów Node
wyboru Dockera i kontrola tłumaczeń. Kontrola tłumaczeń nadal zgłasza istniejące
ostrzeżenia placeholderów `selection.addN` i `toast.folderAdded`.

Pomiar uwzględnia cały skompilowany kod `KeepClip.dll`, również nowe adaptery
operacji zewnętrznych. Nie dodano wykluczeń klas, a kod testów nie powiększa
pokrycia aplikacji. `coverage.xml` jest gotowy do importu przez dotychczasową
konfigurację Sonara. Nie jest to pomiar JavaScriptu frontendu ani odświeżony
wynik całego projektu na serwerze SonarQube.

## Zakres

Wydzielono transport HTTP, sesję OAuth, wykonanie procesów, przetwarzanie mediów,
polecenie przechwytywania, składanie powtórki i sprawdzanie aktualizacji.
Dotychczasowe publiczne punkty wejścia aplikacji delegują do tej samej logiki,
którą testy wykonują z kontrolowanymi zależnościami. Operacje na bazie SQLite
i plikach są rzeczywiste, na osobnych danych testowych.

Poniższe wartości to agregaty pokrycia linii metod danego typu oraz jego typów
generowanych przez kompilator z raportu dotnet-coverage, nie osobne metryki Sonara:

| Moduł | Pokrycie |
| --- | ---: |
| GoogleDriveClient | 88,48% |
| OAuthSession | 92,76% |
| MediaProcessor | 87,54% |
| MediaProcessRunner | 97,06% |
| ClipService | 89,40% |
| CloudService | 84,55% |
| PlaybackProxyService | 90,52% |
| ReplayWriter | 90,77% |
| ReplayCaptureCommand | 100% |
| UpdateChecker | 97,87% |
| TranscribeWorker | 93% |
| ThumbnailWorker | 91,43% |
| CredentialStore | 91,18% |

Testy obejmują błędy HTTP, PKCE, callback OAuth i jego kodowanie HTML, rotację
i wygaśnięcie tokenów, rozłączenie podczas żądania, zmianę konta, stronicowanie,
przerwane transfery, spójność bazy, zachowanie plików, limity rozmiaru, ponawianie
kodowania, współbieżne zadania, anulowanie, cache aktualizacji, wybór modeli,
podział transkrypcji, skróty klawiaturowe oraz odpowiedzi i postęp operacji HTTP.

Testy sieciowe używają atrap HttpMessageHandler. Testy callbacku OAuth łączą się
wyłącznie z własnym listenerem na 127.0.0.1. Testy CredentialStore tworzą fikcyjne
wpisy `KeepClip.Tests:<losowy identyfikator>`, które usuwają i sprawdzają w finally.
Nie odczytują ani nie zmieniają tokenu Google Drive użytkownika. Testy procesów
uruchamiają krótkie własne polecenia, sprawdzając wyjście i zakończenie po timeout.
Testy nie nagrywają pulpitu ani mikrofonu i nie uruchamiają instalatora.

## Błędy ujawnione przez testy i poprawione w aplikacji

- Nieudane przeniesienie klipu do Kosza usuwało wpis i transkrypcję z biblioteki.
  Teraz operacja zwraca błąd i zachowuje dane.
- Błąd zapisu segmentu w transkrypcji zbiorczej pozostawiał niekompletny wynik.
  Wymiana segmentów i oznaczenie klipu są objęte jedną transakcją.
- Brak lub nieodczytywalność pierwszego klipu zatrzymywały kolejkę miniatur.
  Kolejka przechodzi teraz dalej i próbuje każdy kandydat najwyżej raz na przebieg.
- Anulowany podgląd mógł usunąć wynik nowego zadania. Zadania mają osobne pliki
  tymczasowe i publikują wynik dopiero po sprawdzeniu stanu anulowania.
- Powtórka mogła nadpisać plik utworzony podczas jej składania. Publikacja nie
  nadpisuje istniejącego pliku i ponownie dobiera nazwę po kolizji.
- Długie nazwy plików chmurowych traciły rozszerzenie. Rozszerzenie jest zachowane.
- Nieskończone liczby w metadanych chmury i postępie operacji trafiały do stanu
  aplikacji; są teraz odrzucane albo zastępowane poprawnymi wartościami.
- Spóźnione zakończenie zadania mogło nadpisać jego wcześniejszy wynik.
- Nieprawidłowe parametry wycinka docierały do kodera. Walidacja odbywa się przed
  utworzeniem katalogu wyjściowego i uruchomieniem kodowania.
- Cyfry w skrótach były interpretowane jako numery kodów klawiszy. `Alt+1`
  i `Ctrl+0` wskazują teraz właściwe klawisze; odrzucane są niepoprawne kombinacje.
- Zdjęcie bez Content-Length było pobierane w całości przed kontrolą rozmiaru.
  Odczyt kończy się najpóźniej po wykryciu przekroczenia 1 MiB.
- Krótkotrwały token OAuth był buforowany dłużej niż pozostawał ważny.
- Błędny OAuth state kończył poprawnie rozpoczętą próbę logowania; teraz jest
  odrzucany odpowiedzią 400, a poprawny callback nadal może zostać przyjęty.
- Trwające odświeżanie mogło odtworzyć token po rozłączeniu, a spóźniony profil
  zgłosić nieaktualny stan połączenia. Wyniki sprawdzają teraz wersję sesji.
- Po nowym logowaniu błąd pobrania profilu mógł pozostawić dane poprzedniego konta.
- Pusty URL instalatora był prezentowany jako dostępna aktualizacja. Wymagany
  jest poprawny bezwzględny URL HTTPS.
- Najmniejszy model Whispera oznaczony jako powodujący awarię był wybierany
  ponownie. Brak bezpiecznych kandydatów prowadzi teraz do kontrolowanego błędu.

Przypadki regresji najpierw wykazywały błędne zachowanie, a następnie przeszły po
poprawieniu kodu aplikacji. Współbieżność jest koordynowana zadaniami i sygnałami,
bez zgadywania czasu zakończenia przez krótki sleep.

## Odtworzenie weryfikacji

```powershell
dotnet test KeepClip.slnx -c Release --verbosity minimal
dotnet test KeepClip.slnx -c Release -p:PublicRelease=true --verbosity minimal
dotnet-coverage collect 'dotnet test KeepClip.slnx -c Release --verbosity quiet -m:1' -s tests/coverage.settings.xml -f xml -o coverage.xml
node --test --experimental-test-coverage tests/docker-cli.test.mjs
node tests/i18n-check.js
```

Lokalna kompilacja z analizatorem Sonara nie ma błędów. Pozostaje S1244 w PyFloat:
porównanie z Math.Floor sprawdza dokładną całkowitość liczby na potrzeby jej
formatowania, więc zastąpienie go tolerancją zmieniłoby znaczenie wyniku.
Nie wyciszano tej reguły. Zapisanie raportu coverage nie uruchamia pełnego skanu
ani nie zmienia statusów na serwerze SonarQube.

Pozostałe luki dotyczą głównie okna WinForms/WebView2, rzeczywistego przechwytywania
audio i obrazu, obsługi urządzeń/GPU, ładowania natywnego silnika Whisper oraz
uruchomienia instalatora i zamknięcia procesu. Testy poleceń kodowania nie zastępują
testów działania tych urządzeń. Dalsze pokrycie tych obszarów wymaga dedykowanych
testów sprzętowych lub interfejsu; nie usunięto ich z mianownika pomiaru.

## Insomnia — testy lokalne na własnych danych

Osobna baza na potrzeby ręcznego testu:

```powershell
$env:KEEPCLIP_DATA_DIR = Join-Path $env:TEMP ('KeepClip-manual-' + [guid]::NewGuid().ToString('N'))
dotnet run --project KeepClip --no-launch-profile -- server-only --urls http://127.0.0.1:8770
```

`POST http://127.0.0.1:8770/api/config`, JSON:

```json
{"clips_root":"C:\\Users\\Kamil\\Desktop\\klipy-csharp\\TestResults\\security-ffmpeg-smoke"}
```

Ten katalog zawiera lokalne nagrania syntetyczne przygotowane podczas poprzedniej
weryfikacji FFmpeg. Identyfikator testowego klipu można odczytać przez
`GET http://127.0.0.1:8770/api/clips` albo w testowej bazie `klipy.db`:

```sql
SELECT id, filename, filepath, duration
FROM clips
WHERE storage = 'local'
ORDER BY id;
```

`POST http://127.0.0.1:8770/api/clips/{id}/cut`, JSON:

```json
{"start":-1,"end":1,"target_size_mb":25}
```

Oczekiwane: 422 i brak nowego wycinka. Następnie
`GET http://127.0.0.1:8770/api/single-operation/status` powinien pokazać
`active: false`, `success: false` oraz opis błędu.

`POST http://127.0.0.1:8770/api/replay/config`, JSON:

```json
{"enabled":false,"background":false,"hotkey":"Alt+1"}
```

Oczekiwane: 200 i zapis poprawnego skrótu bez uruchomienia nagrywania.
`Ctrl++A` zamiast `Alt+1` powinno zwrócić 400 z `error: replayBadHotkey`.
