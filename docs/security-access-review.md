# Przegląd zgłoszeń dostępu i odporności — audyt końcowy

Naprawiono w kodzie lokalnym wszystkie osiem zgłoszeń przekazanych przez użytkownika.
Audyt wykonano przed zatwierdzeniem i publikacją zmian, bez zmian na koncie Google. Wydanie
GitHub v2.4.1 nie zawiera jeszcze tych poprawek. Poniższe dowody dotyczą obecnego
kodu i lokalnych artefaktów kontrolnych, nie wyniku ponownego skanu usługi zewnętrznej.

## Wymagania i dowody dla każdego zgłoszenia

| Zgłoszenie | Implementacja | Dowód |
| --- | --- | --- |
| Broken auth, MediaEndpoints | PrivateApiMiddleware przed plikami statycznymi i wszystkimi endpointami; wymagany klucz właściciela, również dla mediów i miniatur | PrivateApiAuthenticationTests: anonimowe powierzchnie 401, zły klucz nie usuwa rekordu, uwierzytelniony Range daje 206; PrivateApiProcessTests sprawdza rzeczywisty proces i TCP |
| Authorization bypass, ReplayService | Ta sama bramka przed konfiguracją, zapisem i odczytem; odrzucenie niebezpiecznych listenerów przed bazą i usługami tła | Test anonimowego żądania replay nie zmienia ustawień; test middleware potwierdza zero wywołań handlera; testy procesu odrzucają wildcard/brak klucza/alternatywny Kestrel |
| Authorization bypass, ClipService | Brak dostępu do odczytu, modyfikacji i usuwania bez sesji; dodatkowe kontrole originu | Test usunięcia zachowuje rekord po 401; testy originów odrzucają żądania również z poprawnym kluczem; dotychczasowe uwierzytelnione testy biblioteki przechodzą |
| XSS, CloudService | Allowlista MIME, nosniff, CSP sandbox, brak przekazywania aktywnych dokumentów i treści błędów upstream | CloudTransferTests: HTML/XHTML/SVG/brak MIME/octet-stream są odrzucane; pięć typów wideo, Range i 416 zachowują poprawną obsługę; dodatkowo SVG awatara jest odrzucane |
| Sensitive data exposure, instalator OAuth | Publiczny klient installed.client_id, PKCE S256/state, brak client_secret w żądaniach i pakiecie; sanitizacja CI i kontrola Inno | GoogleDriveClientTests/OAuthSessionTests/OAuthConfigurationTests; 23 kontrole pakowania OAuth; rzeczywisty kompilator Inno odrzucił client_secret przed utworzeniem instalatora |
| Authorization bypass, CloudEndpoints | Uwierzytelnienie przed handlerami używającymi konta Google i plików; origin i klucz niezależnie wymagane | Testy middleware dla upload/download/connect/disconnect potwierdzają, że anonimowy klient nie uruchamia handlera; anonimowy status chmury w aplikacji daje 401 |
| DoS, Transcriber | Limit rzeczywistych bajtów PCM, buforowanie na dysku, ograniczona tablica float, deadline i anulowanie kończące proces | AudioExtractorTests: limit + 1 bajt, odczyty dzielące próbki, przerwane PCM, zakończenie własnego producenta; test anulowania workera zachowuje transkrypcję; FFmpeg poprawnie dekoduje własny FLAC |
| DoS, Scanner | Pomijanie reparse points, limity liczby wpisów, głębokości i czasu; brak zmian po niepełnym skanie | ScannerTests tworzą rzeczywiste junctions do korzenia i poza bibliotekę, sprawdzają limit wpisów/głębokości oraz anulowanie bez usunięcia rekordów |

## Sesja i granice dostępu

Desktop generuje klucz 256-bitowy przy każdym uruchomieniu. Trafia on w pamięci
procesu do natywnej obsługi WebView2 i jest dołączany wyłącznie do własnego originu
(schemat, host i port). Nie występuje w URL, HTML/JS, cookie, logach ani argumentach
procesu. Klucz innej instancji nie działa. Test rzeczywistego, niewidocznego WebView2
potwierdził działanie strony, obrazu, filmu, fetch i Range oraz brak Authorization
na drugim lokalnym serwerze. Nawigacja poza origin jest blokowana.

Tryb server-only wymaga jawnego KEEPCLIP_API_TOKEN; Bearer jest przeznaczony dla
klientów API, HTTP Basic dla przeglądarki. Zdalny dostęp wymaga dodatkowo
KEEPCLIP_ALLOW_REMOTE=1, konkretnego adresu HTTPS i certyfikatu. Wildcardy oraz
alternatywne Kestrel:Endpoints są odrzucane przed inicjalizacją bazy i replay.
Origin, Referer i Sec-Fetch-Site nie zastępują klucza. Dotyczy to także innego
lokalnego portu, który może należeć do obcego konta Windows.

Nie tworzono dodatkowego konta systemowego. Kontrolę dostępu sprawdzono przez
niezależnego anonimowego klienta TCP, inne klucze instancji, rzeczywisty proces
serwera i prywatny kanał native WebView2. Nie ma produkcyjnego obejścia dla
Development ani TestServer. Instrukcja uruchomienia i dokładne testy Insomnia:
[private-api.md](private-api.md).

## Limity zasobów

- Skanowanie: 100 000 wszystkich wpisów (nie tylko filmów), głębokość 64,
  30 sekund z oczekiwaniem na blokadę. Kontrola czasu/anulowania odbywa się między
  operacjami systemu plików; same synchroniczne wywołania systemowe nie są przerywane.
  Błąd enumeracji nie prowadzi do usunięcia biblioteki na podstawie częściowej listy.
- Audio: maksymalnie 28 800 000 próbek mono 16 kHz, czyli 30 minut. PCM zajmuje
  maksymalnie 57,6 MB w pliku tymczasowym, a końcowa tablica do 115,2 MB w pamięci.
  Limit jest sprawdzany podczas odczytu, bez zaufania do metadanych czasu.
  Nie powstaje transkrypcja uciętej treści udająca poprawny wynik. Dekodowanie ma
  limit pięciu minut; przekroczenie budżetu i anulowanie kończą drzewo procesu.
  Plik tymczasowy jest usuwany. Token anulowania dociera z API i workera do
  dekodowania oraz przetwarzania Whisper.

## OAuth i instalator

Aplikacja odczytuje wyłącznie identyfikator z sekcji installed. Stare pole
client_secret jest ignorowane, a klient webowy lub konfiguracja bez typu są
odrzucane. Wymiana kodu i odświeżanie nie wysyłają client_secret. Zachowano
PKCE S256, losowy state, kodowanie pól i Windows CredentialStore dla tokenów
użytkownika. Dokumentacja Google opisuje klienta Desktop jako publicznego
oraz client_secret jako opcjonalny dla tych operacji:
[Google OAuth for Desktop Apps](https://developers.google.com/identity/protocols/oauth2/native-app).
Testy OAuth korzystają wyłącznie z lokalnych atrap HTTP i własnego callbacku;
nie wykonywano logowania ani rotacji rzeczywistych poświadczeń użytkownika.

CI dekoduje wejściowy JSON w katalogu tymczasowym, wybiera tylko installed.client_id
przez installer/prepare-public-oauth.ps1 i usuwa surowy plik w finally. Build tworzy
minimalny plik w publish/data. Inno Setup wyklucza cały katalog data z ogólnej listy
plików i osobno dołącza wyłącznie sprawdzony publiczny google_client.json. Nie czyta
już surowego embed/google_client.json. Kontrola działa także przy bezpośrednim ISCC.

Zbudowano osobną publikację PublicRelease i instalator kontrolny z bieżącego kodu,
zweryfikowanych narzędzi FFmpeg oraz fikcyjnego klienta OAuth. Nie uruchamiano
instalatora. Log kompilatora potwierdza dołączenie minimalnego pliku i pominięcie
prywatnego pliku testowego. Próba ponownej kompilacji z client_secret została
odrzucona, zanim powstał EXE. Kod sprawdzany w osobnym katalogu porównano z aktualną
implementacją. Nie nadpisywano narzędzi ani danych aplikacji użytkownika.

## Wyniki końcowe i artefakty

- Release: 622/622, PublicRelease: 615/615, zero pominiętych testów;
  obejmują FFmpeg, WebView2, rzeczywisty proces serwera, bazy i pliki testowe.
- OAuth: 23 kontrole PowerShell; licencje/źródła/pakowanie: 17 kontroli.
- Przed poprawkami odtworzono sześć błędów proxy, dwanaście błędów dostępu API
  i cztery błędy obsługi publicznego OAuth. Poprawiono kod, zachowując wymagania testów.
- Raporty: tests/KeepClip.Tests/TestResults/security-final-release.trx oraz
  security-final-public.trx; lokalne logi: TestResults/security-public-package-build.log,
  security-installer-compile.log, security-invalid-installer.log.
- Pakiet kontrolny: TestResults/security-public-package/dist/verification/
  KeepClip-Packaging-Verification.exe. Zawiera fikcyjne dane OAuth — nie publikować.
- Prywatne start-dev.ps1, start-dev.vbs i skrót pozostają lokalnie, wykluczone z Gita.
  Zachowano wcześniejsze niecommitowane zmiany organizacji wydań.

Polecenia weryfikacji:

```powershell
dotnet test KeepClip.slnx -c Release -p:RunFfmpegTests=true -p:RunDesktopTests=true
dotnet test KeepClip.slnx -c Release -p:PublicRelease=true -p:RunFfmpegTests=true -p:RunDesktopTests=true
powershell -NoProfile -ExecutionPolicy Bypass -File tests/oauth-packaging.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/license-packaging.Tests.ps1 -PublishDir TestResults/security-public-package/publish
```