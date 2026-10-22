# Przegląd bezpieczeństwa Sonara — 22.10.2026

Projekt `KeepClip`, branch roboczy `csharp`. Odczytano wszystkie 29 otwartych
zgłoszeń z filtrem Security w lokalnym SonarQube. Analiza widoczna na serwerze
pochodziła z 18.10.2026. Ocenę oparto także na aktualnym kodzie i testach lokalnych.

## Ocena zgłoszeń

| Miejsce | Liczba | Ocena i wykonana zmiana |
| --- | ---: | --- |
| `KeepClip/Endpoints/MediaEndpoints.cs`, S4036 | 1 | Poprawiono. Eksplorator jest wybierany przez pełną ścieżkę systemowego katalogu Windows. Ścieżka otwieranego pliku jest normalizowana i przekazywana jako osobny argument bez powłoki. |
| `SearchEndpoints.cs`, `ClipService.cs`, `CloudService.cs`, S6444 | 3 | Poprawiono. Wyszukiwanie i rozpoznawanie nazw mają limit czasu i silnik `NonBacktracking`. Zapytanie ma limit 512 znaków i 32 słów; jego odrzucenie zwraca 422. Nazwy z chmury przekraczające limit lokalnej nazwy pliku korzystają z nazwy zastępczej. Proste zastępowanie niedozwolonych znaków nazwy wycinka nie używa już regexa. |
| `KeepClip/Data/Db.cs`, S2077 | 1 | Nie znaleziono wejścia użytkownika w DDL migracji. Dodatkowo zastąpiono składanie SQL kompletnymi stałymi poleceniami, co usuwa potrzebę interpolacji. Parametr SQL nie może zastąpić deklaracji kolumny. Testy sprawdzają migrację starszej i częściowo zmigrowanej bazy oraz zachowanie danych. |
| `build/ffmpeg/build.mjs`, wyszukiwanie programu w PATH | 3 | Poprawiono. Wszystkie wywołania Dockera używają raz ustalonej pełnej ścieżki ze standardowego katalogu instalacji. Dla instalacji niestandardowej służy jawne `KEEPCLIP_DOCKER`; ścieżki względne są odrzucane. Helper trafia również do pakietu odpowiadających źródeł i podlega kontroli hashy. |
| `keepClip-Web/KeepClipSite-main/hero3d.js`, Math.random | 20 | Bez zmiany. Losowość ustala parametry łusek, pyłu, błysku, czasu animacji i drżenia kamery. Nie tworzy tokenów, kluczy, uprawnień ani identyfikatorów bezpieczeństwa. Generator kryptograficzny nie jest tu potrzebny. |
| `build/ffmpeg/Dockerfile`, użytkownik root | 1 | Bez zmiany. Jest to obraz do budowy przypiętych i sprawdzonych hashami źródeł FFmpeg. Skrypt tworzy kontener tylko do eksportu `/out` przez `docker cp`; nie uruchamia go jako usługi ani nie montuje katalogów hosta. Root jest używany m.in. do instalacji toolchainu w obrazie. Ocenę należy ponowić przy dodaniu uruchamiania kontenera lub montowania zasobów hosta. |

Zmiany obejmują 8 zgłoszeń; pozostałe 21 nie wymaga zmiany w obecnym zastosowaniu.
Nie wyciszano reguł ani nie zmieniano statusów zgłoszeń na serwerze.

Ochrona regexów jest zgodna z zaleceniami
[Microsoft dotyczącymi limitów i NonBacktracking](https://learn.microsoft.com/en-us/dotnet/standard/base-types/backtracking-in-regular-expressions).

## Testy i pomiar

- Punkt wyjścia: 161 testów xUnit przechodziło.
- Trzy nowe przypadki walidacji wyszukiwania najpierw nie przeszły na starym kodzie,
  a po poprawce aplikacji przechodzą bez zmiany oczekiwań.
- Końcowy zestaw: 215 testów xUnit, w tym testy SQL/FTS injection, granic zapytań,
  migracji, wycofania nieudanej wymiany transkrypcji, plików i zakresów HTTP.
- 18 testów Node sprawdza wybór Dockera, niebezpieczne ścieżki, błędy dostępu oraz
  zachowanie przy braku programu. Testy dodano do CI i workflow wydania.
- Wszystkie dane testowe powstają w osobnych bazach i katalogach tymczasowych.
  Testy jednostkowe Eksploratora sprawdzają przygotowane polecenie bez otwierania okien.
- Lokalny build z analizatorem Sonara 10.35 i profilem projektu nie zgłasza już
  S2077, S4036 ani S6444. Pozostaje 8 ostrzeżeń Sonara innych reguł
  (S6966, S8949, S1244); nie są to zgłoszenia z ocenianej listy Security.
- Kontrola tłumaczeń przechodzi; zgłasza dwa istniejące ostrzeżenia dotyczące
  placeholderów `selection.addN` i `toast.folderAdded`.
- Testowy pakiet przeszedł 17 kontroli licencji i kompletności źródeł. FFmpeg
  przeszedł 9 kontroli na syntetycznym obrazie i audio, w tym nagrywanie CPU,
  dzielenie, remuksowanie powtórki, wycinanie, audio do transkrypcji i miniaturę.

| Mierzony obszar | Pokrycie linii |
| --- | ---: |
| Przygotowanie zapytania FTS | 100% |
| Obsługa żądania wyszukiwania | 91,67% |
| Migracje bazy wraz z dodawaniem kolumn | 100% |
| Czyszczenie niedozwolonych znaków nazw plików | 100% |
| Rozpoznawanie gry z danych chmurowych | 86,67% |
| Przygotowanie polecenia Eksploratora | 85,71% |
| Wybór Dockera | 100% (także gałęzie i funkcje) |
| Cała biblioteka aplikacji `KeepClip.dll`, Release, ostatni pomiar | 42,88% |

Wynik całej aplikacji nie jest jeszcze wysoki. Nie wykluczono z pomiaru
nieprzetestowanych części aplikacji ani nie doliczono pokrycia samych testów.
Pomiar .NET nie obejmuje JavaScriptu frontendu, więc nie jest bezpośrednio
porównywalny ze starym wynikiem 17,8% całego projektu w SonarQube.
Nie wymuszano timeoutów zmianą prywatnych pól ani nie uruchamiano prawdziwego
Eksploratora tylko dla poprawienia procentu pokrycia.

Polecenia do odtworzenia wyników:

```powershell
dotnet test KeepClip.slnx -c Release --verbosity minimal
dotnet-coverage collect 'dotnet test KeepClip.slnx -c Release --verbosity quiet -m:1' -s tests/coverage.settings.xml -f xml -o coverage.xml
node --test --experimental-test-coverage tests/docker-cli.test.mjs
node tests/i18n-check.js
```

Raport `coverage.xml` jest w formacie już skonfigurowanym w SonarQube
(`sonar.cs.vscoveragexml.reportsPaths`). Odświeżenie serwera wymaga ponownego
uruchomienia właściwego skanowania projektu; lokalna weryfikacja C# nie wysyła
wyników na serwer ani nie zastępuje pełnej analizy JavaScriptu.

## Sprawdzenie w Insomnii

Uruchomienie serwera ze stałym lokalnym adresem:

```powershell
dotnet run --project KeepClip --no-launch-profile -- server-only --urls http://127.0.0.1:8770
```

- `GET http://127.0.0.1:8770/api/search?q=headshot` — 200, lista wyników może być pusta.
- `GET http://127.0.0.1:8770/api/search?q=1+2+3+4+5+6+7+8+9+10+11+12+13+14+15+16+17+18+19+20+21+22+23+24+25+26+27+28+29+30+31+32+33`
  — 422 i `error: searchQueryTooComplex` (33 słowa).
- `GET http://127.0.0.1:8770/api/search?q=` — 422; wyszukiwanie nie wymaga body.
- `POST http://127.0.0.1:8770/api/show-in-explorer`, `Content-Type: application/json`:

```json
{"path":"C:\\KeepClip-test"}
```

Po utworzeniu własnego katalogu `C:\KeepClip-test` oczekiwane jest 200 i otwarcie
systemowego Eksploratora. Nieistniejąca ścieżka zwraca 404. Ten test nie wymaga
rekordów w bazie ani SQL.

## Pakowanie FFmpeg

Zmiana skryptów wymaga odtworzenia pakietu odpowiadających źródeł i jego hashy.
Stare `tools/ffmpeg-notices` są celowo odrzucane przez walidator, zamiast trafiać
do nowego instalatora. Do odświeżenia właściwych narzędzi służy `setup.ps1 -Force`.
W trakcie prac użyto oddzielnego artefaktu w `TestResults`, lokalnych przypiętych
źródeł i cache Dockera; nie nadpisywano katalogu `tools` użytkownika.
Gotowy lokalny artefakt można wykorzystać bez ponownej kompilacji FFmpeg:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File setup.ps1 -BuildArtifactDir TestResults/security-ffmpeg-final -Force
```
