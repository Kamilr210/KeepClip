# KeepClip — instrukcje lokalne

Ten plik dotyczy wyłącznie tego repozytorium i jego podkatalogów. Nie zmieniaj
globalnych instrukcji ani ustawień Codexa w ramach pracy nad KeepClip. Architektura
MusicGo.Newsroom nie ma zastosowania do tego projektu.

## Architektura i środowisko

- Windows x64, .NET 10 (`global.json`), ASP.NET Core w procesie WinForms/WebView2.
- `KeepClip.slnx`: aplikacja `KeepClip/KeepClip.csproj` i xUnit
  `tests/KeepClip.Tests/KeepClip.Tests.csproj`, oba `net10.0-windows`.
- `Endpoints/` → `Services/` → `Data/` (SQLite, bez ORM); `Domain/` to modele,
  `Application/` zawiera modele odpowiedzi, `Workers/` obsługuje kolejki,
  `Infrastructure/` integracje systemowe, media i chmurę.
- Frontend to statyczne HTML/CSS/JS w `frontend/`; `keepClip-Web/` jest osobną
  stroną. Skanowanie biblioteki zleca frontend przez `POST /api/scan`.
- `PublicRelease=true` wyłącza `KEEPCLIP_DEV`; samo `-c Release` tego nie robi.
- `Config` ustala katalogi przy pierwszym użyciu. Testy muszą ustawić izolację
  przed dostępem do statycznego stanu aplikacji.

## Praca z kodem

Zachowuj formatowanie i jednoliniowe sygnatury/wywołania tam, gdzie istnieją.
Nie dodawaj zmian formatowania niezwiązanych z zadaniem ani końcowej pustej linii
po ostatnim `}`. Dobieraj metodę HTTP endpointu zgodnie z istniejącym kontraktem.
Nie rób szerokich refaktoryzacji dla samego pokrycia testami.

Sprawdź gałąź i lokalne zmiany przed edycją. Zachowuj zmiany użytkownika. Na
gałęzi `dev` nie zmieniaj plików: zaproponuj nazwę zadania/gałęzi. Dla nowego
zadania używaj osobnej gałęzi lub uzgodnionego worktree. Nie wykonuj commitów,
pushów, tagów ani publikacji bez wyraźnej prośby. Propozycje nazw commitów podawaj
po polsku, rzeczownikowo, np. „Dodanie testów skanowania katalogów”.

## Kontrola jakości

Z katalogu głównego repozytorium:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/quality-check.ps1 -Mode Quick
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/quality-check.ps1 -Mode Full
```

Po zmianie uruchom adekwatne kontrole; przed zakończeniem pracy nad kodem lub QA
uruchom `Full`. Warianty natywne, raporty i diagnoza są w
[docs/quality-assurance.md](docs/quality-assurance.md). Lokalny skill
`.agents/skills/keepclip-quality/SKILL.md` pomaga wybrać i zdiagnozować kontrolę.

Po niepowodzeniu ustal przyczynę z logu/TRX, popraw najmniejszy odpowiedni zakres
i ponów kontrolę. Po trzech kolejnych próbach bez postępu zapisz diagnozę,
dokładne polecenie i blokadę, a następnie wykonaj niezależne kontrole. Nie usuwaj
testów, nie osłabiaj asercji, nie wyłączaj analizatorów ani audytu zależności dla
uzyskania zielonego wyniku. Nie przedstawiaj niewykonanej kontroli jako zaliczonej.

## Bezpieczeństwo testów

- Używaj `TestEnvironment.NewTempDir`, testowej bazy i atrap istniejących
  adapterów (`ClipOperations`, `CloudConnection`, `MediaProcessRunner`, HTTP).
- Nie czytaj ani nie zmieniaj użytkownikowych `data/`, `models/`, klipów,
  poświadczeń Drive i ustawień. Nie podłączaj prawdziwego konta chmurowego.
- Testy API działają przez `WebApplicationFactory`; lokalne procesy i listenery
  używają wyłącznie danych testowych oraz pętli zwrotnej.
- Nie uruchamiaj nagrywania ekranu/mikrofonu, pobierania modeli, instalatora,
  `setup.ps1`, `dotnet publish` ani workflow wydania jako zwykłego QA.
- Nie zabijaj działającej aplikacji użytkownika, żeby odblokować build. Zgłoś
  blokadę pliku i użyj odizolowanego katalogu builda, jeśli jest dostępny.
- Testy dzielą statyczne ustawienia i SQLite, więc pozostają sekwencyjne.
  Współbieżność testuj wewnątrz pojedynczego testu, z sygnałami i limitem czasu.

W raporcie końcowym podaj wykonane polecenia, rzeczywiste wyniki i ograniczenia.
Jeśli użytkownik potrzebuje ręcznego testu API, podaj metodę i pełny lokalny URL,
body oraz SQL do znalezienia wymaganych identyfikatorów w testowej bazie.
