# KeepClip — инструкции для AI-агентов

Этот файл относится к репозиторию **KeepClip** и его подкаталогам.

## 1. Архитектура и стек (актуально для v2.5.x+, ветка `switch_to_wpf`)

- **Платформа:** Windows x64, **.NET 10** (`global.json`, `<RuntimeFrameworkVersion>10.0.14</RuntimeFrameworkVersion>`), нативное **WPF**-приложение (`KeepClip/KeepClip.csproj`, `net10.0-windows`).
- **WinForms:** `<UseWindowsForms>true</UseWindowsForms>` включён только ради `System.Windows.Forms.NotifyIcon` (системный трей). Веб-сервер ASP.NET Core, `Endpoints/`, `frontend/` и `WebView2` удалены начиная с v2.5.0.
- **Решение:** `KeepClip.slnx` (также есть корневой `..\KeepClip.slnx` для Visual Studio) включает:
  - `KeepClip/KeepClip.csproj` — основное WPF-приложение (`KEEPCLIP_DEV` активен, пока `PublicRelease != true`; одного `-c Release` недостаточно для отключения dev-инструментов).
  - `tests/KeepClip.Tests/KeepClip.Tests.csproj` — модульные и интеграционные тесты (`xUnit`).
- **Слои приложения (`KeepClip/`):**
  - `UI/` — XAML-окна и встроенные представления (`MainWindow`, `PlayerView`, `ReplayView`, `CutWindow`, `ConfigFolderDialog`, `PromptDialog`, `OnboardingWindow`, `DevWindows`, `ClipCardItem`).
  - `Themes/` — тёмная тема (`Theme.xaml`, `ThemeManager.cs`: `purple` / `green`).
  - `Localization/` — `Translations.cs` и `Loc.cs` (`pl`, `en`, `ru`, `uk`, live-переключение через `Loc.Instance`).
  - `Services/` — бизнес-логика (`ClipService`, `ClipOperations`, `ClipDemoService`, `CloudService`, `CloudConnection`, `PlaybackProxyService`, `OnboardingService`, `ReplayWriter`, `SearchService`, `UpdateChecker`, `UpdateService`).
  - `Data/` — SQLite без ORM (`Db`, `DbExtensions`, `ClipRepository`, `FolderRepository`, `SegmentRepository`, `StatsRepository`, FTS-поиск).
  - `Domain/` и `Application/` — модели предметной области и ответов (`Clip`, `Folder`, `Segment`, `SearchResult`, `Stats`, `Result<T>`, `ClipDemoResponse`, `OnboardingState`, `ResourceSnapshot` и др.).
  - `Workers/` — фоновые воркеры (`Scanner`, `ThumbnailWorker`, `TranscribeWorker` с `ThreadPriority.BelowNormal`).
  - `Infrastructure/` — системные интеграции, медиа и облако (`Media`, `MediaProcessor`, `MediaProcessRunner`, `AudioExtractor`, `AudioPump`, `Transcriber`, `CloudVideoProxy`, `GoogleDrive*`, `OAuth*`, `HotkeyManager`, `ReplayToast`, `ReplayCapture*`, `Trash`, `Strings`, `DisplayHelper`, `ExplorerLauncher`, `CrashLog`, `CredentialStore`, `DevResourceMonitor`).
- `keepClip-Web/` — отдельный веб-проект (лендинг/сайт), не часть десктопного WPF-приложения.

## 2. Внешние инструменты и модели (FFmpeg и Whisper)

- `Config.AppRoot` вычисляется в `Config.FindAppRoot()` поиском `tools`, `Assets` или `KeepClip.slnx` вверх от `AppContext.BaseDirectory`. Так как `Assets/` копируется в выходной каталог сборки (`bin\...\Assets`), при обычном запуске `AppRoot` совпадает с папкой `KeepClip.exe`.
- **FFmpeg / FFprobe:** ожидаются в `<AppRoot>\tools\bin\ffmpeg.exe` и `<AppRoot>\tools\bin\ffprobe.exe`. При `dotnet publish` таргет `StageRuntimeAssets` копирует `tools\**\*` из корня репозитория в `$(PublishDir)tools\`, но при обычном `dotnet build` копирование `tools\` не выполняется.
- **Whisper (STT):** движок подключён через NuGet (`Whisper.net` 1.9.1 + `Whisper.net.Runtime.Vulkan` + `Whisper.net.Runtime` с fallback на CPU). Модели GGML хранятся в `<AppRoot>\models\ggml-<model>.bin` (`large-v3`, `large-v3-turbo`, `small`; можно переопределить через `KEEPCLIP_WHISPER_MODEL`) и автоматически скачиваются при первой транскрибации.

## 3. Технические правила работы с кодом

- **Конфликты имён WPF / WinForms:** всегда явно квалифицируйте неоднозначные типы (`System.Windows.Controls.ContextMenu`, `System.Windows.Controls.MenuItem`, `System.Windows.Controls.TextBox`, `System.Windows.Controls.CheckBox`, `System.Windows.Application`, `System.Windows.Clipboard`, `Microsoft.Win32.OpenFileDialog`, `Microsoft.Win32.SaveFileDialog`).
- **Тёмная тема WPF:** все стандартные контролы (включая `ComboBox`, `ContextMenu`, `MenuItem`, `ScrollBar`, `ToolTip`) должны использовать стили из `Themes/Theme.xaml`. Обёртки элементов `ComboBox` обязаны переопределять `ToString()`. Плейсхолдер для `DarkTextBox` передаётся через `Tag`.
- **Локализация:** UI использует `Loc.Instance` / `Loc.T("key")`, ошибки сервисов — `Strings.Get("key")`. Новые ключи UI добавляйте во все языки (`pl`, `en`, `ru`, `uk`).
- **Стиль и минимальность правок:** сохраняйте существующее форматирование и однострочные сигнатуры там, где они используются. Не добавляйте лишних изменений форматирования и не делайте широких рефакторингов без необходимости.
- **Changelog:** при заметных изменениях для версий `>= 2.5.0` обновляйте `changelog.md`.
- **Git и ветки:** перед правками проверьте текущую ветку и локальные изменения; сохраняйте пользовательские правки. На ветке `dev` не редактируйте файлы напрямую — используйте отдельную ветку. Не выполняйте `commit`, `push`, создание тегов и релизов без явного запроса пользователя.

## 4. Сборка и контроль качества (QA)

Из корня репозитория (`C:\Users\goman\source\repos\KeepClip\KeepClip`):

```powershell
# Сборка
dotnet build KeepClip/KeepClip.csproj --no-restore -v:m
dotnet build tests/KeepClip.Tests/KeepClip.Tests.csproj --no-restore -v:m

# Скрипты контроля качества
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/quality-check.ps1 -Mode Quick
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/quality-check.ps1 -Mode Full
```

- Подробности режимов QA, отчётов и диагностики — в `docs/quality-assurance.md` и локальном скилле `.agents/skills/keepclip-quality/SKILL.md`.
- После правок запускайте подходящие проверки; перед завершением задачи по коду или QA — `-Mode Full`.
- При сбое найдите причину по логу/TRX, исправьте минимальный участок и повторите проверку. После 3 попыток без прогресса зафиксируйте диагноз, команду и блокер. Не удаляйте тесты, не ослабляйте ассерты и не отключайте анализаторы ради «зелёного» результата.

## 5. Безопасность тестов

- `Config` инициализирует пути статически при первом обращении — тесты должны настраивать изоляцию (`KEEPCLIP_DATA_DIR`, `TestEnvironment.NewTempDir`) до обращения к статическому состоянию.
- Используйте временные БД и заглушки адаптеров (`ClipOperations`, `CloudConnection`, `MediaProcessRunner`, HTTP).
- Не читайте и не изменяйте пользовательские `data/`, `models/`, клипы, настройки и учётные данные Google Drive. Не подключайте реальный облачный аккаунт.
- Не запускайте запись экрана/микрофона, скачивание моделей Whisper, инсталлятор, `setup.ps1`, `dotnet publish` и release-workflow в рамках обычного QA.
- Не завершайте запущенный пользовательский процесс `KeepClip.exe` для разблокировки сборки — сообщите о блокировке файла или используйте изолированный каталог сборки.
- Тесты разделяют статические настройки и SQLite, поэтому выполняются последовательно.
