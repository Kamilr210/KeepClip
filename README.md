# KeepClip

Search your **NVIDIA ShadowPlay** game clips by what was *said* in them.

KeepClip transcribes your clips locally with Whisper, indexes every spoken line,
and lets you type a phrase (e.g. *"no way, did you see that"*) to instantly find
the clips where it was said — and jump straight to that moment in the video.

Everything runs **locally** on your machine. Your clips never leave your PC
unless you explicitly offload them to your own Google Drive.

> The user interface is in **Polish**.

## Features

- **Transcript search** — full-text search (SQLite FTS5) over every spoken line, with click-to-jump to the exact timestamp.
- **Local transcription** — [faster-whisper](https://github.com/SYSTRAN/faster-whisper) (`large-v3-turbo`) on your GPU (CUDA). Nothing is sent to any server.
- **Library management** — automatic folder scanning, thumbnails, favorites, and per-game grouping.
- **Clip cutting** — trim/compress a fragment of a clip; cuts are saved next to your game folders and picked up automatically.
- **Inline transcript editing** — fix a misheard line directly in the UI.
- **Optional cloud offload** — move clips to **your own** Google Drive to free local disk space, then pull them back ("zdejmij z chmury") on demand. Uses the `drive.file` scope, so the app only ever sees the files it created. OAuth tokens are stored in the OS keychain, never on disk.

## Tech stack

- **Backend** — FastAPI + SQLite (FTS5), served by uvicorn on `127.0.0.1:8765`
- **Frontend** — vanilla HTML / CSS / JS (no build step)
- **Media** — ffmpeg / ffprobe (portable, see setup below)
- **Transcription** — faster-whisper on CUDA

## Install (Windows) — easiest

Download **`KeepClip-Setup.exe`** from the
[Releases](https://github.com/Kamilr210/KeepClip/releases) page and run it.

The installer does everything for you: it installs Python if it's missing,
creates the environment, installs all dependencies, and downloads ffmpeg — then
adds a **KeepClip** shortcut to your desktop and Start menu. The first install
needs an internet connection and pulls down ~2 GB, so give it a few minutes.

> Want to set it up by hand (or you're not on Windows)? See **Manual setup** below.

## Requirements

- **Windows** (the launch scripts are `.bat` / `.vbs`)
- **Python 3.10+**
- **An NVIDIA GPU with CUDA** for transcription (the CUDA runtime libs are installed via pip). You can switch to CPU by setting `WHISPER_DEVICE = "cpu"` in `backend/config.py`, but it will be much slower.
- **ffmpeg** (downloaded separately — see step 3)

## Manual setup

### 1. Clone

```powershell
git clone https://github.com/Kamilr210/KeepClip.git
cd KeepClip
```

### 2. Create the Python environment

The launch scripts expect the virtualenv at `backend/.venv`:

```powershell
cd backend
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
cd ..
```

### 3. Add ffmpeg

KeepClip needs `ffmpeg.exe` and `ffprobe.exe`. They are **not** included in the
repo (each is ~200 MB). Download a static Windows build — for example from
[gyan.dev](https://www.gyan.dev/ffmpeg/builds/) or
[BtbN](https://github.com/BtbN/FFmpeg-Builds/releases) — and copy both
executables into:

```
tools/bin/ffmpeg.exe
tools/bin/ffprobe.exe
```

### 4. Run

```powershell
.\start.bat
```

Then open <http://127.0.0.1:8765> in your browser. On first launch the app asks
for the folder where your NVIDIA clips live (typically
`C:\Users\<you>\Videos\NVIDIA`).

Optional: run `install_shortcut.bat` to create a desktop shortcut that launches
KeepClip silently (no console window) and opens it in your browser.

## How to use

1. **Scan folder** — detects new clips in your configured folder.
2. **Transcribe new** — runs Whisper on each not-yet-transcribed clip on the GPU. A progress bar shows the current clip. The first clip is slower (the model loads).
3. **Search** — type a phrase; results appear instantly. Click a card to open the video at the moment the phrase was spoken.

## Optional: Google Drive offload

Cloud offload lets you move large clips off your local disk into Google Drive and
restore them later. **It is entirely optional** and uses *your own* Google
credentials — there is no shared server, and the app cannot see anything in your
Drive other than the files it creates.

To enable it, create your own OAuth client:

1. In the [Google Cloud Console](https://console.cloud.google.com/), create a project.
2. Enable the **Google Drive API**.
3. Configure the OAuth consent screen (External, add yourself as a test user).
4. Create an **OAuth client ID** of type **Web application**, and add this
   authorized redirect URI:
   ```
   http://127.0.0.1:8765/api/cloud/oauth2callback
   ```
5. Download the client JSON and save it as `data/google_client.json`.
6. Restart KeepClip and click **Połącz z Google Drive** in the Cloud view.

The resulting access/refresh tokens are stored in the Windows Credential Manager
(via [keyring](https://github.com/jaraco/keyring)), never in a file.

## Configuration

`backend/config.py`:

- `DEFAULT_CLIPS_ROOT` — fallback clips folder (you normally set this in-app instead)
- `WHISPER_MODEL` — e.g. `large-v3-turbo`, `large-v3`, `medium`, `small`
- `WHISPER_LANG` — transcription language (default `pl`)
- `WHISPER_DEVICE` — `cuda` or `cpu`

## Data & privacy

Everything personal lives in `data/` and is git-ignored:

- `data/klipy.db` — SQLite database (clips, segments, FTS index)
- `data/thumbs/` — JPEG thumbnails
- `data/settings.json` — your chosen clips folder
- `data/google_client.json` — your OAuth client (if you set up Drive)

None of this is included in the repository.

## License

MIT — see [LICENSE](LICENSE).
