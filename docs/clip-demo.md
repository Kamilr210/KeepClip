# Powiązanie klipu z demo CS2

W odtwarzaczu znajduje się sekcja **Demo CS2**. W aplikacji Windows przycisk
**Wybierz plik** otwiera systemowe okno wyboru `.dem`. Można też wkleić pełną
ścieżkę, co działa również w trybie przeglądarkowym. **Przypisz/Zmień** zapisuje
powiązanie, **Pokaż w folderze** zaznacza plik w Eksploratorze, a **Odłącz** usuwa
wyłącznie powiązanie.

KeepClip zapisuje tylko ścieżkę w kolumnie `clips.demo_path`. Demo pozostaje
w folderze CS2 i można nadal odtwarzać je w grze. Nie jest kopiowane, przenoszone,
wysyłane do Google Drive ani usuwane przy odłączeniu lub usunięciu klipu.
Jeden plik można przypisać do wielu klipów; każdy klip ma jedno powiązanie.
Powiązanie pozostaje lokalne także wtedy, gdy sam film jest w chmurze.

Po przeniesieniu lub usunięciu pliku panel pokazuje „Plik niedostępny”. Zapisana
ścieżka pozostaje widoczna i można wskazać nową lokalizację. Nowa kolumna jest
dodawana automatycznie także do istniejących baz, bez zmiany poprzednich danych.

## Walidacja i zabezpieczenia

- Przyjmowane są pełne ścieżki do `.dem` na dysku Windows. Ścieżki względne,
  UNC, urządzenia, adresy `file://`, alternatywne strumienie NTFS i niepoprawne
  znaki są odrzucane przed odczytem pliku.
- Plik musi istnieć, być czytelny, mieć przynajmniej 16 bajtów i rozpoczynać się
  sygnaturą `PBDEMS2\0`. Rozpoznanie opiera się na nagłówku
  [kontenera Source 2](https://github.com/pnxenopoulos/pbdems2).
  To kontrola nagłówka, nie pełne sprawdzenie odtwarzalności ani zgodności wersji
  gry. Archiwa `.dem.zip`, `.dem.bz2` i `.dem.zst` trzeba najpierw rozpakować.
- Odczytywany jest nagłówek, a nie cała zawartość dużego demo. Nie jest uruchamiany
  parser dema ani plik wykonywalny wskazany przez użytkownika.
- Nieudane przypisanie albo anulowany odczyt zachowują poprzednie powiązanie.
  Zapis SQL jest parametryzowany.
- Endpointy powiązania przyjmują tylko połączenia loopback i lokalny Host.
  Odrzucają obcy Origin oraz `Sec-Fetch-Site: cross-site`.
- Mostek WebView2 obsługuje żądania systemowego okna wyboru tylko z originu
  aplikacji. Nazwa i ścieżka demo są wyświetlane jako tekst.
- Odpowiedź lub wynik wyboru pliku dotyczący wcześniej otwartego klipu nie
  zmienia panelu nowo otwartego klipu.

## Insomnia

Dla stałego portu uruchom serwer lokalny:

```powershell
dotnet run --project KeepClip --no-launch-profile -- server-only --urls http://127.0.0.1:8770
```

Do osobnych testów można przed uruchomieniem ustawić `KEEPCLIP_DATA_DIR` na własny
katalog testowy i skonfigurować folder z syntetycznymi klipami. Wszystkie opisane
wywołania dotyczą lokalnego serwera.

Identyfikator klipu: `GET http://127.0.0.1:8770/api/clips` albo SQL w używanej
bazie `klipy.db`:

```sql
SELECT id, filename, filepath, storage, demo_path
FROM clips
ORDER BY id DESC
LIMIT 20;
```

Odczyt powiązania:

```http
GET http://127.0.0.1:8770/api/clips/{id}/demo
```

Przypisanie lub zastąpienie całego powiązania — `Content-Type: application/json`:

```http
PUT http://127.0.0.1:8770/api/clips/{id}/demo
```

```json
{
  "path": "D:\\SteamLibrary\\steamapps\\common\\Counter-Strike Global Offensive\\game\\csgo\\faceit_match.dem"
}
```

Ścieżkę przykładową zastąp rzeczywistą lokalizacją rozpakowanego demo. Odpowiedź
200 zawiera `clip_id`, `path`, `filename`, `available` i `size_bytes`.
Ponowne wysłanie tego samego PUT jest bezpieczne i nie kopiuje pliku.

Odłączenie — bez body:

```http
DELETE http://127.0.0.1:8770/api/clips/{id}/demo
```

Odpowiedź 200 ma `path: null`, `filename: null`, `available: false` i
`size_bytes: null`. Ponowne odłączenie również zwraca 200. Plik `.dem` pozostaje
na dysku.

| Przypadek | Status |
| --- | --- |
| Odczyt, poprawne przypisanie, odłączenie | 200 |
| Pusta/nieprawidłowa ścieżka, złe rozszerzenie | 400 |
| Obcy Host/Origin, połączenie spoza loopback | 403 |
| Nieistniejący klip lub brak wskazanego pliku podczas przypisania | 404 |
| Zły nagłówek, zbyt krótki lub zablokowany plik | 422 |
| Wcześniej przypisany plik zniknął — GET | 200, `available: false` |

## Testy

Końcowy pełny przebieg: **521 testów Release**, **514 PublicRelease** oraz
**36 testów Node** (w tym 18 nowego panelu) — wszystkie przechodzą.
Serwis `ClipDemoService` ma 88,37% pokrycia linii metod, handlery GET/PUT/DELETE
100%, a wspólny walidator originu 100%. Lokalna analiza Sonara nie zgłasza nowych
ostrzeżeń; pozostaje wcześniejsze S1244 w formatowaniu liczb w `MediaProcessor`.

Testy C# używają własnych plików z syntetycznym nagłówkiem Source 2 oraz osobnej
bazy SQLite. Obejmują przypisanie, zamianę, odłączenie, usunięcie klipu,
współdzielenie demo, brak i blokadę pliku, błędny format, anulowanie, migrację
i ochronę originu. Nie uruchamiają gry ani nie zmieniają prawdziwych dem użytkownika.

18 testów JavaScript sprawdza panel, błędy, wybór pliku, Enter, literalne
wyświetlanie nazwy oraz spóźnione odpowiedzi. Pokrycie `clip-demo.js`: 100% linii
i funkcji, 94,74% gałęzi. Testy są dodane do CI i workflow wydania.

Interfejs sprawdzono także w przeglądarce na osobnym lokalnym serwerze: przypisanie,
odświeżenie, odłączenie i zachowanie pliku. Zweryfikowano układ przy rozmiarze
900×600 oraz standardowym widoku. Systemowy dialog wyboru jest dostępny w oknie
Windows; przeglądarka korzysta z wklejanej ścieżki.

```powershell
dotnet test KeepClip.slnx -c Release
dotnet test KeepClip.slnx -c Release -p:PublicRelease=true
node --test --experimental-test-coverage tests/clip-demo.test.cjs
node tests/i18n-check.js
```
