# FFmpeg 9.0.2: odpowiadające źródła w instalatorze

Stan: 2026-10-02. Zastany build o niepotwierdzonym pochodzeniu zastępuje kontrolowana kompilacja oficjalnego FFmpeg 9.0.2. Binaria są osobnymi programami CLI na GPL-3.0-or-later. Decyzja o licencji własnego kodu KeepClip pozostaje otwarta.

## Co otrzymuje odbiorca

Publikacja i instalator zawierają `licenses/ffmpeg-build/`: oryginalne archiwa źródłowe FFmpeg, x264, nv-codec-headers, AMF (nagłówki), Intel oneVPL oraz MinGW CRT/winpthreads z odpowiadającymi patchami/pakowaniem Debiana. Towarzyszą im oryginalne licencje, copyright i notices, skrypty budowy, Dockerfile, przypięty inwentarz wejść, konfiguracja kompilacji i lista pakietów toolchain. Nie stosujemy patchy do FFmpeg ani jego zależności aplikacyjnych. GCC-COPYRIGHT zawiera warunki runtime GCC i Runtime Library Exception. Sterowniki GPU oraz systemowe biblioteki Windows nie są dystrybuowane z FFmpeg.

`provenance.json` wiąże SHA-256 obu faktycznych EXE ze źródłami, konfiguracją, skryptami i notices. Niezależny plik `build/ffmpeg/sources.lock.json` w repo przypina SHA-256 wszystkich źródeł. Kontrola wymaga zgodności z tym inwentarzem i recepturą oraz obecności libx264, h264_nvenc, h264_amf, h264_qsv, AAC, gdigrab, ddagrab, dynaudnorm i scale_cuda. Dodatkowe niesystemowe zależności DLL wymagają przeglądu i przerywają pakowanie.

Źródła są dostarczane razem z binariami w instalatorze. CI przygotowuje ponadto `KeepClip-FFmpeg-Source.zip` obok `KeepClip-Setup.exe`. Release udostępnia oba pliki z tego samego buildu i JSON z hashami instalatora, archiwum źródeł i EXE FFmpeg. Ogólny link do upstream nie zastępuje tego zestawu. Nie składamy w imieniu autora oddzielnej oferty źródłowej.

## Budowanie lokalne i w CI

`setup.ps1 -Force` buduje pakiet z przypiętych źródeł przy użyciu Node 20+ i Docker z kontenerami Linux; dopiero po kontroli podmienia narzędzia. Gotowy pakiet można przekazać jako `-BuildArtifactDir`. Bez `-Force` poprawny istniejący zestaw pozostaje na dysku, a stary/nieznany jest odrzucany z instrukcją jawnej wymiany. Żaden pobrany instalator nie jest uruchamiany.

Workflow wydania kompiluje FFmpeg na Ubuntu, przekazuje binaria i źródła do joba Windows, po czym używa istniejącego procesu publish/Inno Setup. Zwykły `installer/build.ps1 -PublicRelease` działa dla poprawnego zestawu; `-ForDistribution` odrzuca brak źródeł, podmienione źródła lub binaria i zmianę receptury. Preprocesor Inno wywołuje tę samą kontrolę także przy bezpośrednim uruchomieniu ISCC. Tryb `-VerifyPackaging` zachowuje osobną nazwę artefaktu testowego.

Instrukcja odtworzenia z dostarczonych archiwów jest w `licenses/ffmpeg-build/README.md` oraz [build/ffmpeg/README.md](../build/ffmpeg/README.md). Kontener jest przypięty digestem; zapisywane są dokładne wersje narzędzi. Źródła i konfiguracja są odtwarzalne, lecz identyczności bitowej między różnymi toolchainami nie obiecujemy. Przy aktualizacji zmień źródła i hashe, odbuduj, sprawdź funkcje oraz zawartość instalatora.

## Zakres naprawy

Historyczne hashe lokalnego `N-124633-gc79dfd29e6-20260525` pozostają w `manifest.json/localFfmpegObservation` jako zapis audytu, a nie opis aktualnych narzędzi. Naprawa dotyczy przyszłych artefaktów. Nie dostarcza odpowiadających źródeł wcześniejszego publicznego instalatora v2.3.0 z Gyan 9.0.1; ten historyczny przypadek wymaga osobnego rozwiązania.

Punkty odniesienia: [GPLv3, sekcje 1 i 6](https://www.gnu.org/licenses/gpl-3.0.html), [FFmpeg licensing](https://ffmpeg.org/legal.html), [GCC Runtime Library Exception](https://www.gnu.org/licenses/gcc-exception-3.1.html). Nie przypisujemy cudzych komponentów copyrightowi autora KeepClip.
