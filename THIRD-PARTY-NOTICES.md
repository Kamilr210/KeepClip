# Third-party licenses and notices / Licencje i informacje o zależnościach

Materiały dotyczą cudzych komponentów używanych przez KeepClip. Prawa i warunki każdego komponentu należą do jego autorów; nie są objęte copyrightem autora KeepClip. Ten dokument nie nadaje licencji własnemu kodowi KeepClip.

Stan weryfikacji: 2026-10-02. Oryginalne, nieprzetłumaczone teksty są w [licenses/third-party/](licenses/third-party/). Dokładne rewizje, źródła tekstów, hashe i zakres zawiera [licenses/manifest.json](licenses/manifest.json).

**FFmpeg 9.0.2 jest budowany z przypiętych źródeł.** Komplet źródeł, zależności, skryptów, konfiguracji, copyright i notices trafia do `licenses/ffmpeg-build/` w instalatorze oraz osobnego archiwum źródłowego wydania. Szczegóły: [licenses/FFMPEG-SOURCE.md](licenses/FFMPEG-SOURCE.md).

## Pakiety aplikacji

| Pakiet | Wersja | Licencja | Copyright z metadanych pakietu | Teksty |
|---|---|---|---|---|
| Microsoft.Data.Sqlite | 10.0.10 | MIT | © Microsoft Corporation. All rights reserved. | [LICENSE.txt](licenses/third-party/Microsoft.Data.Sqlite/10.0.10/LICENSE.txt) |
| Microsoft.Data.Sqlite.Core | 10.0.10 | MIT | © Microsoft Corporation. All rights reserved. | [LICENSE.txt](licenses/third-party/Microsoft.Data.Sqlite/10.0.10/LICENSE.txt) |
| Microsoft.Extensions.AI.Abstractions | 10.2.0 | MIT | © Microsoft Corporation. All rights reserved. | [LICENSE](licenses/third-party/Microsoft.Extensions.AI.Abstractions/10.2.0/LICENSE) |
| Microsoft.Web.WebView2 | 1.0.3967.48 | BSD-3-Clause | © Microsoft Corporation. All rights reserved. | [LICENSE.txt](licenses/third-party/webview2/1.0.3967.48/LICENSE.txt), [NOTICE.txt](licenses/third-party/webview2/1.0.3967.48/NOTICE.txt) |
| NAudio | 2.2.1 | MIT | © Mark Heath 2023 | [LICENSE.txt](licenses/third-party/naudio/2.2.1/LICENSE.txt) |
| NAudio.Asio | 2.2.1 | MIT | © Mark Heath 2023 | [LICENSE.txt](licenses/third-party/naudio/2.2.1/LICENSE.txt) |
| NAudio.Core | 2.2.1 | MIT | © Mark Heath 2023 | [LICENSE.txt](licenses/third-party/naudio/2.2.1/LICENSE.txt) |
| NAudio.Midi | 2.2.1 | MIT | © Mark Heath 2023 | [LICENSE.txt](licenses/third-party/naudio/2.2.1/LICENSE.txt) |
| NAudio.Wasapi | 2.2.1 | MIT | © Mark Heath 2023 | [LICENSE.txt](licenses/third-party/naudio/2.2.1/LICENSE.txt) |
| NAudio.WinForms | 2.2.1 | MIT | © Mark Heath 2023 | [LICENSE.txt](licenses/third-party/naudio/2.2.1/LICENSE.txt) |
| NAudio.WinMM | 2.2.1 | MIT | © Mark Heath 2023 | [LICENSE.txt](licenses/third-party/naudio/2.2.1/LICENSE.txt) |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.12 | Apache-2.0 | Copyright 2014-2024 SourceGear, LLC | [LICENSE.TXT](licenses/third-party/SQLitePCLRaw/2.1.12/LICENSE.TXT), [NOTICE.TXT](licenses/third-party/SQLitePCLRaw/2.1.12/NOTICE.TXT) |
| SQLitePCLRaw.core | 2.1.12 | Apache-2.0 | Copyright 2014-2024 SourceGear, LLC | [LICENSE.TXT](licenses/third-party/SQLitePCLRaw/2.1.12/LICENSE.TXT), [NOTICE.TXT](licenses/third-party/SQLitePCLRaw/2.1.12/NOTICE.TXT) |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.12 | Apache-2.0 | Copyright 2014-2024 SourceGear, LLC | [LICENSE.TXT](licenses/third-party/SQLitePCLRaw/2.1.12/LICENSE.TXT), [NOTICE.TXT](licenses/third-party/SQLitePCLRaw/2.1.12/NOTICE.TXT) |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.12 | Apache-2.0 | Copyright 2014-2024 SourceGear, LLC | [LICENSE.TXT](licenses/third-party/SQLitePCLRaw/2.1.12/LICENSE.TXT), [NOTICE.TXT](licenses/third-party/SQLitePCLRaw/2.1.12/NOTICE.TXT) |
| Whisper.net | 1.9.1 | MIT | Oryginalny LICENSE wskazany obok | [LICENSE](licenses/third-party/whisper.net/1.9.1/LICENSE), [LICENSE](licenses/third-party/whisper.cpp/1.8.5/LICENSE), [llamafile-MIT.txt](licenses/third-party/whisper.cpp/1.8.5/llamafile-MIT.txt) |
| Whisper.net.Runtime | 1.9.1 | MIT | Oryginalny LICENSE wskazany obok | [LICENSE](licenses/third-party/whisper.net/1.9.1/LICENSE), [LICENSE](licenses/third-party/whisper.cpp/1.8.5/LICENSE), [llamafile-MIT.txt](licenses/third-party/whisper.cpp/1.8.5/llamafile-MIT.txt) |
| Whisper.net.Runtime.Metal | 1.9.1 | MIT | Oryginalny LICENSE wskazany obok | [LICENSE](licenses/third-party/whisper.net/1.9.1/LICENSE), [LICENSE](licenses/third-party/whisper.cpp/1.8.5/LICENSE), [llamafile-MIT.txt](licenses/third-party/whisper.cpp/1.8.5/llamafile-MIT.txt) |
| Whisper.net.Runtime.Vulkan | 1.9.1 | MIT | Oryginalny LICENSE wskazany obok | [LICENSE](licenses/third-party/whisper.net/1.9.1/LICENSE), [LICENSE](licenses/third-party/whisper.cpp/1.8.5/LICENSE), [llamafile-MIT.txt](licenses/third-party/whisper.cpp/1.8.5/llamafile-MIT.txt), [LICENSE.md](licenses/third-party/Vulkan-Headers/1.4.321/LICENSE.md), [Apache-2.0.txt](licenses/third-party/Vulkan-Headers/1.4.321/Apache-2.0.txt), [MIT.txt](licenses/third-party/Vulkan-Headers/1.4.321/MIT.txt) |

Whisper.net.Runtime i Whisper.net.Runtime.Vulkan dostarczają natywne pliki także jako zawartość NuGet; nie wszystkie pojawiają się jako odrębne pozycje w KeepClip.deps.json. Inwentarz obejmuje również restore. Whisper.net.Runtime.Metal jest przywracanym pakietem przechodnim; pliki macOS nie są instalowane w wydaniu Windows. Pakiety testowe nie są dołączane do aplikacji.

## .NET w publikacji samowystarczalnej

| Pakiet runtime | Wersja | Licencja i notices |
|---|---|---|
| Microsoft.NETCore.App.Runtime.win-x64 | 10.0.12 | [LICENSE.TXT](licenses/third-party/Microsoft.NETCore.App.Runtime.win-x64/10.0.12/LICENSE.TXT), [THIRD-PARTY-NOTICES.TXT](licenses/third-party/Microsoft.NETCore.App.Runtime.win-x64/10.0.12/THIRD-PARTY-NOTICES.TXT) |
| Microsoft.WindowsDesktop.App.Runtime.win-x64 | 10.0.12 | [LICENSE](licenses/third-party/Microsoft.WindowsDesktop.App.Runtime.win-x64/10.0.12/LICENSE) |
| Microsoft.AspNetCore.App.Runtime.win-x64 | 10.0.12 | [LICENSE.txt](licenses/third-party/Microsoft.AspNetCore.App.Runtime.win-x64/10.0.12/LICENSE.txt), [THIRD-PARTY-NOTICES.TXT](licenses/third-party/Microsoft.AspNetCore.App.Runtime.win-x64/10.0.12/THIRD-PARTY-NOTICES.TXT) |

Zachowano pełne materiały z dokładnych runtime packs, w tym notices komponentów używanych przez .NET. Copyright (c) .NET Foundation and Contributors; pozostałe copyrighty i licencje są w oryginalnych plikach. Nie przypisujemy wszystkich tych komponentów autorowi aplikacji.

## Natywne biblioteki i składniki instalacji

- **SQLite 3.53.3**: silnik e_sqlite3 z SQLitePCLRaw.lib.e_sqlite3 2.1.12, public domain. [Oficjalna deklaracja SQLite](https://www.sqlite.org/copyright.html). Apache-2.0 dotyczy SQLitePCLRaw; zachowano cały upstream [NOTICE.TXT](licenses/third-party/SQLitePCLRaw/2.1.12/NOTICE.TXT), obejmujący też opcjonalne warianty. Nie oznacza to dołączenia SQLCipher ani OpenSSL.
- **whisper.cpp 1.8.5 i ggml**: rewizja f24588a272ae8e23280d9c220536437164e6ed28 wskazana przez Whisper.net 1.9.1; [MIT](licenses/third-party/whisper.cpp/1.8.5/LICENSE), copyright (c) 2023-2026 The ggml authors. Zachowano także [MIT kodu llamafile](licenses/third-party/whisper.cpp/1.8.5/llamafile-MIT.txt), Copyright 2024 Mozilla Foundation.
- **Nagłówki Vulkan 1.4.321**, używane przez build Vulkan SDK 1.4.321.1: [oryginalne zasady](licenses/third-party/Vulkan-Headers/1.4.321/LICENSE.md), [Apache-2.0](licenses/third-party/Vulkan-Headers/1.4.321/Apache-2.0.txt), [MIT](licenses/third-party/Vulkan-Headers/1.4.321/MIT.txt); copyright Khronos Group zachowano w tych plikach. SDK i sterownik GPU nie są pakowane jako runtime aplikacji.
- **FFmpeg / ffprobe 9.0.2**: kontrolowany statyczny build, GPL-3.0-or-later; [GPLv3](licenses/third-party/ffmpeg/COPYING.GPLv3). Dokładne hashe EXE, źródeł i notices identyfikuje `licenses/ffmpeg-build/provenance.json`. Zależności: x264 `31e19f92f00c7003fa115047ce50978bc98c3a0d` (GPL-2.0-or-later), nv-codec-headers `n12.2.72.0` (MIT), AMF headers `v1.5.3` (MIT), Intel oneVPL dispatcher `v2.17.0` (MIT), MinGW CRT/winpthreads `10.0.0-3` (licencje w `MINGW-COPYRIGHT`) i runtime GCC 12 z Runtime Library Exception (`GCC-COPYRIGHT`). Oryginalne copyrighty i licencje tych komponentów oraz notices oneVPL/NVIDIA znajdują się obok źródeł w `licenses/ffmpeg-build/`. Sterowniki GPU i biblioteki systemu Windows są ładowane z systemu i nie są dołączane.
- **WebView2 Runtime / Evergreen bootstrapper**: odrębne warunki Microsoft; licencja BSD SDK nie nadaje licencji runtime. Runtime jest instalowany/pobierany przez bootstrapper i może się aktualizować. [Oficjalne pobieranie i warunki Microsoft](https://developer.microsoft.com/en-us/microsoft-edge/webview2/), [dokumentacja dystrybucji](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution). Hashe i informacje o bootstrapperze użytym w danym instalatorze są w licenses/installer-engine/webview2-bootstrapper.json, jeżeli został dołączony. Nie zastępuj EULA runtime tekstem BSD SDK.
- **Inno Setup 6.7.3**, użyty do bieżącej lokalnej weryfikacji instalatora: [oryginalna licencja](licenses/third-party/inno-setup/6.7.3/LICENSE.txt); Copyright (C) 1997-2026 Jordan Russell, Portions Copyright (C) 2000-2026 Martijn Laan. Build dodatkowo dołącza licencję faktycznie użytego kompilatora w licenses/installer-engine/LICENSE.txt i jego identyfikację. Inno Setup nie jest składnikiem na licencji MIT.

## Modele i osobna strona WWW

Model Whisper jest pobierany później z sandrohanea/whisper.net, rewizja v4; wagi nie są częścią instalatora. Zachowano [deklarację MIT model card](licenses/third-party/whisper-models/v4/MODEL-CARD.md) i [oryginalny LICENSE OpenAI Whisper](licenses/third-party/whisper-models/v4/LICENSE), Copyright (c) 2022 OpenAI. Źródło pobierania potwierdza downloader Whisper.net 1.9.1; hashe konkretnych pobieranych wag nie są obecnie przypięte przez aplikację.

Strona keepClip-Web jest publikowana oddzielnie. Istniejące notices pozostają w keepClip-Web/KeepClipSite-main/vendor/anime.LICENSE.md, vendor/three/LICENSE oraz models/LICENSE-kenney-blocky-characters.txt. Nie zmieniono grafik ani postaci i nie potwierdzono praw do innych modeli, w tym Steve/Solar.
