# Materiały licencyjne zależności

`THIRD-PARTY-NOTICES.md` jest indeksem dla odbiorcy. `third-party/` zawiera oryginalne teksty z konkretnych pakietów lub wskazanych rewizji upstream. Nie tłumacz ani nie zastępuj ich własnym copyrightem. Ten katalog nie ustala licencji kodu KeepClip.

`manifest.json` opisuje 19 zależności aplikacji rozpoznanych przez restore, trzy pakiety runtime .NET i hashe natywnych DLL w publikacji win-x64. `Whisper.net.Runtime.Metal` jest zależnością przywracaną przez NuGet; jej pliki macOS nie są częścią instalatora Windows. Pakiety testowe i narzędzia SDK nie są licencjonowane jako składniki aplikacji.

## Pakowanie i kontrola

Każde `dotnet publish KeepClip/KeepClip.csproj` uruchamia `installer/stage-licenses.ps1`. Skrypt kopiuje ten katalog i indeks, zachowuje materiały archiwum FFmpeg, jeżeli są dostępne, oraz sprawdza wersje, hashe i kompletność notices przez `installer/verify-licenses.ps1`. Zmiana wersji pakietu, runtime lub natywnej DLL wymaga odświeżenia materiałów. Brak NOTICE albo inny tekst przerywa publikację lokalnego katalogu.

`installer/build.ps1` powtarza kontrolę także przy `-SkipPublish`. Inno Setup kopiuje cały katalog publikacji, w tym `licenses/` i `THIRD-PARTY-NOTICES.md`. Preprocesor `.iss` sprawdza obecność indeksu, inwentarza oraz licencji FFmpeg i silnika instalatora; wywołuje również kontrolę źródeł przy bezpośredniej kompilacji do dystrybucji. Licencję rzeczywiście użytego kompilatora Inno Setup skrypt dołącza dodatkowo jako `licenses/installer-engine/LICENSE.txt`.

Poprawny zestaw FFmpeg z kompletem przypiętych źródeł pozwala zbudować zwykły instalator. Brak źródeł albo podmiana wejść, skryptów lub EXE blokuje dystrybucję. Instalator zawiera pełny pakiet źródeł, a release dostarcza również osobny ZIP. Można sprawdzić pakowanie bez publikacji:

```powershell
powershell -ExecutionPolicy Bypass -File installer/build.ps1 -VerifyPackaging -OutputRoot "$env:TEMP\KeepClip-LicenseVerification"
powershell -ExecutionPolicy Bypass -File installer/verify-licenses.ps1 -PublishDir "$env:TEMP\KeepClip-LicenseVerification\publish" -RequireFfmpeg
powershell -ExecutionPolicy Bypass -File tests/license-packaging.Tests.ps1 -PublishDir "$env:TEMP\KeepClip-LicenseVerification\publish"
```

Wynik testowy ma nazwę `KeepClip-Packaging-Verification.exe` w osobnym katalogu `verification/`. Nie udostępniaj go użytkownikom. `-VerifyPackaging` nie łączy się z `-PublicRelease` ani z trybem GitHub Actions. Szczegóły i warunki odblokowania są w [FFMPEG-SOURCE.md](FFMPEG-SOURCE.md).

## Aktualizacja zależności

1. Zrób restore i nową publikację do osobnego katalogu. Porównaj `project.assets.json`, `KeepClip.deps.json`, pliki DLL oraz `tools/bin`. Stary katalog instalatora nie jest źródłem wersji.
2. Odczytaj `.nuspec` dokładnie przywróconych wersji i pobierz oryginalne LICENSE, NOTICE, THIRD-PARTY-NOTICES z tych pakietów lub ich wskazanych commitów. Zachowaj copyright i całą treść. Dla SQLitePCLRaw zachowaj również upstream `NOTICE.TXT`; opisuje on także opcjonalne warianty, a dołączony silnik `e_sqlite3` jest SQLite, nie SQLCipher.
3. Zaktualizuj `manifest.json`: wersje, hash treści NuGet z `project.assets.json` / `.nupkg.metadata`, hash archiwum `.nupkg`, źródła i SHA-256 materiałów oraz natywnych DLL. Hash treści NuGet i hash podpisanego archiwum to odrębne wartości.
4. Zweryfikuj źródła natywnych komponentów i ich dodatkowe notices. Obecny Whisper.net 1.9.1 wskazuje whisper.cpp `f24588a272ae8e23280d9c220536437164e6ed28` (1.8.5), z ggml i kodem llamafile. Vulkan korzysta z nagłówków SDK 1.4.321.1; zachowano teksty Khronos. Modeli nie dodawaj do instalatora: pobierane są później z `sandrohanea/whisper.net`, rewizja `v4`, z deklaracją MIT.
5. Uruchom testy aplikacji, testy pakowania i nowy build kontrolny. Odczytaj zawartość instalatora narzędziem ekstrakcji bez uruchamiania instalatora i porównaj hashe materiałów z publikacją.

Materiały anime.js, Three.js i Kenney pozostają w oryginalnych miejscach `keepClip-Web/KeepClipSite-main/`. Ta strona ma oddzielny workflow i nie trafia do publikacji desktopowej. Ten katalog nie potwierdza praw do innych grafik, postaci ani modeli strony, w tym Steve/Solar.
