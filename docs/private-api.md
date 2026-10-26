# Prywatna sesja KeepClip

Wszystkie trasy HTTP, w tym `/`, pliki interfejsu, `/api/*`, `/video/*`,
`/playback/*` i miniatury, wymagają klucza właściciela. Dostęp do loopback nie
identyfikuje użytkownika Windows. Brak lub zły klucz daje 401; niedozwolony
host, origin albo dostęp sieciowy daje 403 przed wywołaniem endpointu.

## Okno desktopowe

Uruchomienie `KeepClip.exe` lub `dotnet run --project KeepClip` tworzy nowy
losowy klucz 256-bitowy. Jest przekazywany w pamięci procesu do natywnej części
WebView2 i dołączany do żądań wyłącznie do własnego originu, z uwzględnieniem
schematu, hosta i portu. Nie jest umieszczany w URL, HTML, JavaScripcie, cookie,
wierszu poleceń ani pliku konfiguracyjnym. Po restarcie poprzedni klucz nie działa.
Użytkownik okna desktopowego nie wpisuje żadnego dodatkowego hasła.

## Tryb serwerowy i Insomnia

Przed uruchomieniem serwera ustaw prywatny klucz w środowisku tego procesu:

```powershell
$bytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($bytes)
$rng.Dispose()
$env:KEEPCLIP_API_TOKEN = [BitConverter]::ToString($bytes).Replace('-', '')
dotnet run --project KeepClip --no-launch-profile -- server-only --urls http://127.0.0.1:8770
```

Nie zapisuj klucza w repozytorium ani nie przesyłaj go w adresie URL.
W Insomnii wybierz Bearer Token i użyj wartości `KEEPCLIP_API_TOKEN` z własnej
sesji PowerShell. Klucz ma 64 znaki szesnastkowe. Nie ma endpointu wydającego go
anonimowemu klientowi. Zwykła przeglądarka używa standardowego okna HTTP Basic:
nazwa użytkownika `keepclip`, hasło to ten sam klucz aplikacji (nie hasło Google).
Nagłówki przeglądarki muszą wskazywać własny origin; żądania cross-site i same-site
z innego portu są odrzucane nawet przy poprawnym kluczu.

Próby lokalne na własnych danych:

- `GET http://127.0.0.1:8770/api/ping`: bez klucza 401, z poprawnym kluczem 200.
- `GET http://127.0.0.1:8770/api/clips`: wymaga tego samego klucza.
- `GET http://127.0.0.1:8770/video/{id}` z `Range: bytes=0-31`: z poprawnym
  kluczem i plikiem co najmniej 32 bajtów odpowiedź 206; bez klucza 401.
- `POST http://127.0.0.1:8770/api/replay/config`, `Content-Type: application/json`:

```json
{"enabled":false,"background":false,"mic":false}
```

Bez klucza 401 i brak zmiany ustawień; z kluczem 200. Ten przypadek nie uruchamia
nagrywania. Identyfikator własnego pliku testowego można odczytać z testowej bazy:

```sql
SELECT id, filename, filepath FROM clips WHERE storage = 'local';
```

## Dostęp przez sieć

Domyślnie dozwolone jest wyłącznie nasłuchiwanie lokalne. Dostęp sieciowy wymaga
łącznie: `server-only`, poprawnego `KEEPCLIP_API_TOKEN`, `KEEPCLIP_ALLOW_REMOTE=1`
oraz konkretnego adresu HTTPS i skonfigurowanego certyfikatu Kestrel. Przykład
adresu: `--urls https://192.168.1.20:8770`. Wybierz rzeczywisty adres własnego
komputera i certyfikat prawidłowy dla tego adresu. Certyfikat ustaw przez
`Kestrel:Certificates:Default`, nie przez alternatywne `Kestrel:Endpoints`.
Wildcardy (`*`, `+`, `0.0.0.0`, `::`), endpointy nadpisujące `--urls` oraz
niezabezpieczone zdalne HTTP są odrzucane przed inicjalizacją bazy i replay.

Świadome udostępnienie klucza daje uprawnienia właściciela biblioteki, w tym do
zmian plików i nagrywania. Nagłówki `Origin`, `Referer` i `Sec-Fetch-Site` stanowią
dodatkową ochronę przeglądarki; nie zastępują uwierzytelnienia.

## Testy

Zwykłe testy używają losowego klucza testowego i rzeczywistego middleware.
Nie ma produkcyjnego wyłączenia uwierzytelnienia dla Development/TestServer.
Testy procesu startują osobny KeepClip z własnym katalogiem danych i wyłączonym
replay. Test desktopowy tworzy niewidoczne okno WebView2, syntetyczne wideo
i dwa własne serwery loopback; sprawdza uwierzytelnione odtwarzanie i brak wycieku
klucza do drugiego originu. Wymaga zainstalowanego WebView2 Runtime oraz FFmpeg:

```powershell
dotnet test KeepClip.slnx -c Release -p:RunDesktopTests=true --filter Category=Desktop
dotnet test KeepClip.slnx -c Release -p:RunFfmpegTests=true -p:RunDesktopTests=true
```
