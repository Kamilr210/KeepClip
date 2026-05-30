"""Faster-whisper wrapper. Loads the model once, transcribes clips to (start, end, text) tuples."""
from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path
from typing import Iterable, Iterator, Optional, Union

import numpy as np

from config import (
    FFMPEG,
    TOOLS_BIN,
    WHISPER_COMPUTE_TYPE,
    WHISPER_DEVICE,
    WHISPER_LANG,
    WHISPER_MODEL,
    WHISPER_SPLIT_GAP_SECONDS,
)

_NO_WIN = subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0


def _load_audio_normalized(path: Path) -> Optional[np.ndarray]:
    """Decode audio via ffmpeg with `dynaudnorm` so quiet voices stand out against
    loud game SFX, then resample to mono 16 kHz float32 — Whisper's native format.
    Returns None on failure (caller should fall back to passing the file path).

    `dynaudnorm=f=150:g=15` does aggressive but smooth per-frame loudness
    normalization, which on our test clip went from 14 to 26 detected segments
    and unmasked an entire conversation Whisper was missing in the first minute.
    """
    try:
        result = subprocess.run(
            [
                str(FFMPEG), "-v", "error", "-i", str(path),
                "-af", "dynaudnorm=f=150:g=15",
                "-vn", "-ac", "1", "-ar", "16000",
                "-f", "s16le", "-",
            ],
            capture_output=True,
            timeout=300,
            creationflags=_NO_WIN,
        )
        if result.returncode != 0 or not result.stdout:
            print(f"[whisper] audio preprocessing failed: {result.stderr[:300]}", file=sys.stderr)
            return None
        return np.frombuffer(result.stdout, dtype=np.int16).astype(np.float32) / 32768.0
    except Exception as exc:
        print(f"[whisper] audio preprocessing error: {exc}", file=sys.stderr)
        return None


def _ensure_cuda_dlls_on_path() -> None:
    """nvidia-*-cu12 pip wheels ship DLLs under site-packages\\nvidia\\<lib>\\bin.
    ctranslate2 won't pick them up unless they're on the DLL search path."""
    import site

    candidates: list[Path] = []
    for sp in site.getsitepackages():
        nvidia = Path(sp) / "nvidia"
        if not nvidia.exists():
            continue
        for lib in nvidia.iterdir():
            bin_dir = lib / "bin"
            if bin_dir.is_dir():
                candidates.append(bin_dir)

    for d in candidates:
        os.add_dll_directory(str(d))
        os.environ["PATH"] = str(d) + os.pathsep + os.environ.get("PATH", "")


_ensure_cuda_dlls_on_path()
# ffmpeg next to us so faster-whisper / av can decode anything ffmpeg can
os.environ["PATH"] = str(TOOLS_BIN) + os.pathsep + os.environ.get("PATH", "")

_model = None


def _cuda_is_available() -> bool:
    """True if ctranslate2 can see at least one CUDA (NVIDIA) device.

    Lets us pick the backend automatically: machines without an NVIDIA GPU
    transcribe on the CPU instead of failing on a missing CUDA device.
    """
    try:
        import ctranslate2

        return ctranslate2.get_cuda_device_count() > 0
    except Exception:
        return False


def get_model():
    global _model
    if _model is not None:
        return _model
    from faster_whisper import WhisperModel

    device = (WHISPER_DEVICE or "auto").strip().lower()
    if device == "auto":
        device = "cuda" if _cuda_is_available() else "cpu"

    if device == "cuda":
        # Prefer the GPU, but degrade gracefully (compute type, then CPU).
        candidates = [
            ("cuda", WHISPER_COMPUTE_TYPE),
            ("cuda", "int8_float32"),
            ("cuda", "int8"),
            ("cpu", "int8"),
        ]
    else:
        # No NVIDIA GPU — run on the CPU (slower, but works everywhere).
        candidates = [
            ("cpu", "int8"),
            ("cpu", "float32"),
        ]

    last_exc = None
    seen: set[tuple[str, str]] = set()
    for dev, ctype in candidates:
        if (dev, ctype) in seen:
            continue
        seen.add((dev, ctype))
        try:
            _model = WhisperModel(WHISPER_MODEL, device=dev, compute_type=ctype)
            print(f"[whisper] loaded {WHISPER_MODEL} on {dev} ({ctype})", file=sys.stderr)
            return _model
        except Exception as exc:
            print(f"[whisper] {dev}/{ctype} failed: {exc}", file=sys.stderr)
            last_exc = exc
    raise RuntimeError(f"Could not load Whisper model: {last_exc}")


def _split_on_internal_gaps(seg, max_gap: float):
    """Split a Whisper segment wherever there's a long pause between words.

    Whisper sometimes packs unrelated utterances separated by many seconds of
    silence into a single segment (especially in gaming audio). With
    word_timestamps=True we can detect those gaps and split, so each emitted
    chunk has start/end tightly matching what was actually said.

    Yields (start, end, text) tuples.
    """
    if not seg.words:
        text = seg.text.strip()
        if text:
            yield float(seg.start), float(seg.end), text
        return

    chunk = [seg.words[0]]
    for w in seg.words[1:]:
        gap = w.start - chunk[-1].end
        if gap >= max_gap:
            text = "".join(ww.word for ww in chunk).strip()
            if text:
                yield float(chunk[0].start), float(chunk[-1].end), text
            chunk = [w]
        else:
            chunk.append(w)
    text = "".join(ww.word for ww in chunk).strip()
    if text:
        yield float(chunk[0].start), float(chunk[-1].end), text


def transcribe(path: Path) -> Iterator[tuple[float, float, str]]:
    """Yield (start_s, end_s, text) segments for the given clip.

    Preprocesses audio with dynaudnorm so quiet voices aren't masked by loud
    game SFX, uses word-level timestamps (via DTW) for accurate timing, and
    splits each segment on long internal pauses so click-to-jump lands on the
    real phrase.
    """
    model = get_model()

    # Preprocess audio (normalize loud SFX vs quiet voices). Fall back to raw
    # file if extraction fails for any reason.
    audio = _load_audio_normalized(path)
    audio_input: Union[str, np.ndarray] = audio if audio is not None else str(path)

    segments, _info = model.transcribe(
        audio_input,
        language=WHISPER_LANG,
        vad_filter=True,
        vad_parameters={"min_silence_duration_ms": 500},
        beam_size=5,
        condition_on_previous_text=False,  # avoids hallucinated loops on gaming audio
        word_timestamps=True,              # DTW-based per-word timing
        # The default no_speech_threshold (0.6) is too eager to drop quiet gaming
        # banter (close-talking mic, casual chat) as "non-speech". 0.85 keeps
        # marginal segments that turn out to be real speech.
        no_speech_threshold=0.85,
        # Prevent the model from looping the same trigram over and over when
        # it gets confused by music/SFX (e.g. "Dlaczego? Dlaczego? Dlaczego?").
        no_repeat_ngram_size=3,
    )
    for seg in segments:
        yield from _split_on_internal_gaps(seg, WHISPER_SPLIT_GAP_SECONDS)


if __name__ == "__main__":
    import argparse

    p = argparse.ArgumentParser()
    p.add_argument("path")
    args = p.parse_args()
    for start, end, text in transcribe(Path(args.path)):
        print(f"[{start:7.2f} -> {end:7.2f}] {text}")
