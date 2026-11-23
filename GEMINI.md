# GEMINI.md — Руководство по проекту KeepClip для AI-агентов

Этот документ описывает архитектуру, структуру, соглашения и важные нюансы **KeepClip** (начиная с версии **2.5.0**, после перевода на WPF).

---

## 1. Обзор проекта

**KeepClip** — десктопное приложение для Windows x64 (.NET 10, WPF) для просмотра, организации и поиска игровых клипов:
- Автоматическое сканирование папки с клипами (ShadowPlay, SteelSeries Moments, AMD и др.) и генерация миниатюр (`FFmpeg` / `FFprobe`).
- Распознавание речи (**Whisper.net**, Vulkan GPU с fallback на CPU) и полнотекстовый поиск по транскрипциям (SQLite FTS).
- Встроенный видеоплеер с YouTube-подобной полосой перемотки, субтитрами (CC), редактированием/удалением сегментов, экспортом `.txt`, вырезкой и сжатием фрагментов под лимит размера (Discord и др.), ремонтом повреждённых клипов и привязкой CS2 `.dem` файлов.
- **Мгновенный повтор (Instant Replay)** — циклическая запись экрана и звука (WASAPI через `NAudio` + `FFmpeg`) по глобальной горячей клавише (`HotkeyManager`), сворачивание в системный трей (`NotifyIcon`) и всплывающие уведомления (`ReplayToast`).
- Опциональная интеграция с **Google Drive** (OAuth 2.0 PKCE, загрузка/скачивание клипов и папок, потоковое воспроизведение через `CloudVideoProxy`).

---

## 2. Структура решения и файлов

Корневое решение для Visual Studio: `C:\Users\goman\source\repos\KeepClip\KeepClip.slnx` (ссылается на проекты внутри папки `KeepClip/`).
Репозиторий находится в `C:\Users\goman\source\repos\KeepClip\KeepClip\`:

- `KeepClip/KeepClip.csproj` — основное WPF-приложение (`Microsoft.NET.Sdk`, `net10.0-windows`, `<UseWPF>true</UseWPF>`, `<UseWindowsForms>true</UseWindowsForms>` для `NotifyIcon`).
  - Константа `KEEPCLIP_DEV` определена, пока `PublicRelease != true` (включает `DevLog` и `DevResourceMonitor`).
- `tests/KeepClip.Tests/KeepClip.Tests.csproj` — модульные и интеграционные тесты (`xUnit`).
- `changelog.md` — журнал изменений, ведётся начиная с версии **2.5.0**.
- `AGENTS.md` — локальные правила репозитория (QA, безопасность тестов, ветки).

### Структура исходников `KeepClip/`

| Каталог / Файл | Назначение |
|---|---|
| `App.xaml` / `App.xaml.cs` | Точка входа WPF, `Mutex` одиночного экземпляра, инициализация БД и папок, `NotifyIcon` в трее, общие экземпляры репозиториев и сервисов (`App.Clips`, `App.Segments`, `App.Folders`, `App.Stats`, `App.Playback`, `App.ClipService`, `App.ClipDemoService`, `App.CloudProxy`). |
| `Config.cs` | Пути (`AppRoot`, `DataDir`, `ThumbsDir`, `TmpDir`, `PlaybackDir`, `ToolsBin`, `AssetsDir`, `DbPath`, `ModelsDir`), поддерживаемые языки (`pl`, `en`, `ru`, `uk`), расширения видео, настройки Whisper. |
| `Settings.cs` | Потокобезопасное чтение/запись `data/settings.json`. |
| `ReplayService.cs` | Управление фоновым буфером мгновенного повтора (`FFmpeg` + `AudioPump`). |
| `TranscribeState.cs` | Состояние и прогресс пакетной транскрибации. |
| `DevLog.cs` | Кольцевой буфер логов (только в `#if KEEPCLIP_DEV`). |
| `UI/` | Весь интерфейс на WPF XAML + C#: |
| `UI/MainWindow.xaml(.cs)` | Главное безрамочное окно (`WindowChrome`), анимированные фоновые сферы, боковая панель с логотипом `KEEPCLIP` в самом верху, 5 вкладок (`Klipy`, `Foldery`, `Ulubione`, `Chmura`, `Ustawienia`), встроенные оверлеи `PlayerOverlay` и `ReplayOverlay`. |
| `UI/PlayerView.xaml(.cs)` | Встроенный плеер (`MediaElement`, YouTube-слайдер перемотки `YouTubeSlider`, горизонтальная панель кнопок без скролла, CS2 Demo панель, список сегментов). Открывается на всю область окна. |
| `UI/ReplayView.xaml(.cs)` | Встроенный оверлей настроек мгновенного повтора (включение, длительность, FPS, качество, захват хоткея, выбор аудиоустройств, «Сохранить сейчас»). |
| `UI/CutWindow.xaml(.cs)` | Модальное окно вырезки фрагмента с предпросмотром, ползунками начала/конца и пресетами размера (20–500 MB / свой размер). |
| `UI/ConfigFolderDialog.xaml(.cs)` | Диалог первичного выбора или смены папки с клипами (`OpenFolderDialog`). |
| `UI/PromptDialog.xaml(.cs)` | Универсальное тёмное модальное окно подтверждения удаления и ввода текста (создание/переименование папок, правка сегмента). |
| `UI/OnboardingWindow.xaml(.cs)` | Пошаговое окно обучения (`OnboardingService`). |
| `UI/DevWindows.cs` | `LogsWindow` и `ResourcesWindow` (`#if KEEPCLIP_DEV`) с кастомными тёмными заголовками. |
| `UI/ClipCardItem.cs` | Модель карточки клипа/результата поиска для привязки в `ItemsControl`. |
| `Themes/` | `Theme.xaml` (тёмная палитра, шаблоны `DarkButton`, `DarkTextBox` с плейсхолдером через `Tag`, `DarkComboBox`, `ComboBoxItem`, `ContextMenu`, `MenuItem`, `ScrollBar`, `SwitchCheckBox`) и `ThemeManager.cs` (`purple` / `green`). |
| `Localization/` | `Translations.cs` (словарь переводов `pl`, `ru`, `en`, `uk`) и `Loc.cs` (`Loc.Instance` с `INotifyPropertyChanged` для живого переключения языка, `Loc.T()`, `Loc.Format()`, `Loc.ClipWord()`, очистка старых HTML-тегов `<b>`, `<br>`, `<code>`). |
| `Data/` | SQLite без ORM: `Db.cs`, `DbExtensions.cs`, `ClipRepository.cs`, `FolderRepository.cs`, `SegmentRepository.cs`, `StatsRepository.cs`. |
| `Domain/` & `Application/` | Модели: `Clip`, `Folder`, `Segment`, `SearchResult`, `Stats`, `Result<T>`, `ClipDemoResponse`, `OnboardingState`, `ResourceSnapshot` и др. |
| `Services/` | Бизнес-логика: `ClipService`, `ClipOperations`, `ClipDemoService`, `CloudService`, `CloudConnection`, `PlaybackProxyService`, `OnboardingService`, `ReplayWriter`, `SearchService` (`ToFtsQuery`), `UpdateChecker`, `UpdateService`. |
| `Workers/` | Фоновые задачи: `Scanner.cs`, `ThumbnailWorker.cs`, `TranscribeWorker.cs` (работает в потоке `ThreadPriority.BelowNormal`). |
| `Infrastructure/` | Системные интеграции: `HotkeyManager` (WPF `HwndSource`), `ReplayToast` (прозрачное WPF-окно без активации), `CloudVideoProxy` (`HttpListener` на loopback), `GoogleDrive*`, `OAuth*`, `Media*`, `Audio*`, `Transcriber`, `Trash`, `Strings`, `DisplayHelper`, `ExplorerLauncher`, `CrashLog`, `CredentialStore`, `DevResourceMonitor`. |
| `Assets/` | Иконки (`Assets/icons/`) и звуки (`Assets/sounds/`), копируются в выходной каталог. |

---

## 3. Важные технические нюансы для агентов

1. **WPF + WinForms (`UseWPF` + `UseWindowsForms`)**
   - `UseWindowsForms` включён только ради `System.Windows.Forms.NotifyIcon`.
   - Из-за пересечения пространств имён в C#-файлах **всегда явно квалифицируйте** неоднозначные типы: `System.Windows.Controls.ContextMenu`, `System.Windows.Controls.MenuItem`, `System.Windows.Controls.TextBox`, `System.Windows.Controls.CheckBox`, `Microsoft.Win32.OpenFileDialog`, `Microsoft.Win32.SaveFileDialog`, `System.Windows.Application`, `System.Windows.Clipboard`.
2. **Тёмная тема и шаблоны контролов**
   - Стандартные стили Aero2 в WPF рисуют белые `ComboBox`, `ContextMenu`, `MenuItem` и `ScrollBar`. Все они переопределены в `Themes/Theme.xaml`.
   - Для `ComboBox` объекты-обёртки (`ChoiceItem`, `ComboChoice`, `AudioChoice`) **должны переопределять `ToString()`**, чтобы отображаемый текст корректно выводился в `SelectionBoxItem`.
   - Для `DarkTextBox` текст-подсказка (placeholder) задаётся через свойство `Tag`.
3. **Локализация**
   - Пользовательский UI использует `KeepClip.Localization.Loc` (`{Binding [key], Source={x:Static loc:Loc.Instance}}` в XAML и `Loc.T("key")` в C#).
   - Сообщения ошибок сервисов/бэкенда используют `KeepClip.Infrastructure.Strings.Get("key")`.
   - Все ключи UI должны существовать в `Localization/Translations.cs` или в `Extra` внутри `Localization/Loc.cs`.
4. **Неблокирующий UI при транскрибации и генерации миниатюр**
   - `TranscribeWorker` должен работать с пониженным приоритетом (`ThreadPriority.BelowNormal`), чтобы не вешать UI-поток.
   - `MainWindow` периодически опрашивает готовность миниатюр (`_thumbPoll`) после `ThumbnailWorker.Ensure()`, чтобы карточки обновлялись автоматически.
5. **Тесты и сборка**
   - Сборка приложения: `dotnet build KeepClip/KeepClip.csproj --no-restore -v:m`
   - Сборка тестов: `dotnet build tests/KeepClip.Tests/KeepClip.Tests.csproj --no-restore -v:m`
   - В `.csproj` зафиксирован `<RuntimeFrameworkVersion>10.0.14</RuntimeFrameworkVersion>` (должен совпадать с `licenses/manifest.json` для релизной сборки). Для запуска `dotnet test` на машине должен быть установлен соответствующий runtime `.NET 10.0.14` (или выше с совместимым roll-forward).
   - При внесении любых изменений для версий **>= 2.5.0** обновляйте `changelog.md`.

