"""ffmpeg/ffprobe helpers: video duration, thumbnails, and broken-clip repair."""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path
from typing import Optional

from config import FFMPEG, FFPROBE, THUMBS_DIR

THUMBS_DIR.mkdir(parents=True, exist_ok=True)
TMP_DIR = THUMBS_DIR.parent / "tmp"
TMP_DIR.mkdir(parents=True, exist_ok=True)

# When running under pythonw.exe, child console apps (ffmpeg/ffprobe) still get
# their own console allocated by default — which on Windows causes a brief CMD
# window flash for every subprocess call. CREATE_NO_WINDOW prevents that.
_NO_WIN = subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0


def _run(cmd: list[str], **kwargs):
    """Wrapper that runs subprocesses without spawning a visible console window."""
    return subprocess.run(cmd, creationflags=_NO_WIN, **kwargs)


def probe_duration(path: Path) -> Optional[float]:
    try:
        out = _run(
            [
                str(FFPROBE),
                "-v", "error",
                "-show_entries", "format=duration",
                "-of", "json",
                str(path),
            ],
            capture_output=True,
            text=True,
            timeout=30,
            check=True,
        )
        data = json.loads(out.stdout)
        return float(data["format"]["duration"])
    except Exception:
        return None


def thumb_path(clip_id: int) -> Path:
    return THUMBS_DIR / f"{clip_id}.jpg"


def probe_decode_errors(path: Path, seconds: float = 5.0) -> int:
    """Count H.264 decode errors in the first `seconds` of the file.

    NVIDIA ShadowPlay DVR clips often have broken NAL units at the start
    (the recording captured frames before a valid IDR). Browsers refuse to
    play those even though VLC tolerates them.
    """
    try:
        result = _run(
            [
                str(FFPROBE),
                "-v", "error",
                "-read_intervals", f"%+{seconds}",
                "-i", str(path),
            ],
            capture_output=True,
            text=True,
            timeout=60,
        )
    except Exception:
        return 999
    return result.stderr.count("\n") if result.stderr.strip() else 0


def _try_remux(src: Path, dst: Path, skip_seconds: float = 0.0) -> bool:
    """Stream-copy remux with optional input seek. Returns True on success."""
    if dst.exists():
        dst.unlink()
    cmd = [str(FFMPEG), "-hide_banner", "-y", "-loglevel", "error"]
    if skip_seconds > 0:
        cmd += ["-ss", f"{skip_seconds:.2f}"]
    cmd += [
        "-i", str(src),
        "-c", "copy",
        "-map", "0",
        "-ignore_unknown",
        "-movflags", "+faststart",
        "-avoid_negative_ts", "make_zero",
        str(dst),
    ]
    try:
        result = _run(cmd, capture_output=True, text=True, timeout=600)
    except Exception:
        return False
    return result.returncode == 0 and dst.exists() and dst.stat().st_size > 1024 * 1024


def fix_broken_clip(src: Path, tmp_out: Optional[Path] = None) -> tuple[bool, Optional[Path], str, float]:
    """Produce a browser-playable copy of a possibly-corrupt clip.

    Strategy: try plain remux first (handles moov-at-end / faststart),
    then progressively skip more of the broken intro until ffprobe reports
    no decode errors in the first few seconds.

    Returns (success, output_path, message, seconds_trimmed).
    """
    if tmp_out is None:
        tmp_out = TMP_DIR / f"fix_{src.stem}.mp4"

    attempts: list[tuple[str, float]] = [
        ("remux", 0.0),
        ("skip 1s", 1.0),
        ("skip 3s", 3.0),
        ("skip 7s", 7.0),
        ("skip 15s", 15.0),
        ("skip 25s", 25.0),
        ("skip 35s", 35.0),
        ("skip 60s", 60.0),
    ]

    for label, skip in attempts:
        if not _try_remux(src, tmp_out, skip_seconds=skip):
            continue
        errors = probe_decode_errors(tmp_out, seconds=3.0)
        if errors == 0:
            return True, tmp_out, f"OK ({label})", skip

    if tmp_out.exists():
        tmp_out.unlink()
    return False, None, "Nie udało się — nawet po pominięciu 60 s nadal są błędy dekodowania.", 0.0


def cut_clip(
    src: Path,
    output: Path,
    start: float,
    end: float,
    target_size_mb: Optional[float] = None,
) -> tuple[bool, str, dict]:
    """Cut a section of a video, optionally compressing to fit a target file size.

    Always re-encodes with h264_nvenc so the cut is frame-accurate (no keyframe
    misalignment artifacts at the start that you get with stream copy + seek).

    If target_size_mb is given, computes a video bitrate that — combined with
    fixed 128 kbps AAC audio — should land near the target. Otherwise uses
    constant-quality mode at CQ 19 (visually lossless on 1080p gaming footage).

    Returns (ok, message, stats) where stats has duration_s, size_bytes, bitrate_kbps.
    """
    duration = end - start
    if duration <= 0.1:
        return False, "Koniec musi być co najmniej 0.1s po początku.", {}
    if duration > 60 * 30:
        return False, "Maksymalnie 30 minut.", {}
    if not src.exists():
        return False, f"Plik źródłowy nie istnieje: {src}", {}

    output.parent.mkdir(parents=True, exist_ok=True)

    audio_kbps = 128
    cmd = [
        str(FFMPEG), "-y", "-hide_banner", "-loglevel", "error",
        # input seek BEFORE -i is fast and accurate when re-encoding
        "-ss", f"{start:.3f}", "-to", f"{end:.3f}",
        "-i", str(src),
        "-c:v", "h264_nvenc", "-preset", "p5", "-tune", "hq",
    ]

    if target_size_mb is not None and target_size_mb > 0:
        target_bytes = target_size_mb * 1024 * 1024
        overhead_bytes = 50_000  # container + index overhead estimate
        audio_bytes = (audio_kbps * 1000 / 8) * duration
        video_bytes_budget = target_bytes - audio_bytes - overhead_bytes
        if video_bytes_budget <= 100_000:
            return False, (
                f"Docelowy rozmiar {target_size_mb} MB jest za mały dla "
                f"{duration:.1f}s nagrania."
            ), {}
        video_kbps = max(200, int((video_bytes_budget * 8) / duration / 1000))
        cmd += [
            "-rc", "vbr",
            "-b:v", f"{video_kbps}k",
            "-maxrate", f"{int(video_kbps * 1.3)}k",
            "-bufsize", f"{video_kbps * 2}k",
        ]
    else:
        # Constant-quality: visually lossless on 1080p gaming, file size depends on motion
        cmd += ["-rc", "vbr", "-cq", "19", "-b:v", "0"]

    cmd += [
        "-c:a", "aac", "-b:a", f"{audio_kbps}k",
        "-movflags", "+faststart",
        str(output),
    ]

    try:
        result = _run(cmd, capture_output=True, timeout=600, text=True)
    except Exception as exc:
        return False, f"ffmpeg padło: {exc}", {}

    if result.returncode != 0 or not output.exists():
        msg = result.stderr.strip().splitlines()[-1] if result.stderr else "nieznany"
        return False, f"ffmpeg zakończył się błędem: {msg}", {}

    stat = output.stat()
    return True, "ok", {
        "duration_s": duration,
        "size_bytes": stat.st_size,
        "size_mb": round(stat.st_size / 1024 / 1024, 2),
    }


def make_thumbnail(clip_id: int, video: Path, at_seconds: float = 5.0) -> bool:
    """Extract one frame as a 1280px-wide JPEG. Returns True on success.

    Source clips are typically 1080p+ (e.g. 1920x1200), and the big mosaic cards
    can render fairly large once Windows display scaling is factored in — a 720px
    thumb looked soft there. 1280px stays comfortably above the on-screen size on
    any realistic monitor while remaining a small file; -q:v 2 is the highest
    JPEG quality so detailed game frames don't pick up compression artifacts.
    """
    out = thumb_path(clip_id)
    duration = probe_duration(video) or 10.0
    timestamp = min(at_seconds, max(0.0, duration / 2))
    try:
        _run(
            [
                str(FFMPEG),
                "-y",
                "-ss", f"{timestamp:.2f}",
                "-i", str(video),
                "-frames:v", "1",
                "-vf", "scale=1280:-2",
                "-q:v", "2",
                str(out),
            ],
            capture_output=True,
            timeout=30,
            check=True,
        )
        return out.exists()
    except Exception:
        return False
