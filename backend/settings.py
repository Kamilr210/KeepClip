"""Persistent user-tunable settings, stored as JSON next to the SQLite DB.

We keep this separate from config.py (which has compile-time defaults and
constants) so the user can change the clips folder without editing source.
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from config import CUTS_SUBDIR, DATA_DIR, DEFAULT_CLIPS_ROOT

SETTINGS_PATH = DATA_DIR / "settings.json"


def load() -> dict[str, Any]:
    if not SETTINGS_PATH.exists():
        return {}
    try:
        return json.loads(SETTINGS_PATH.read_text(encoding="utf-8"))
    except Exception:
        return {}


def save(data: dict[str, Any]) -> None:
    SETTINGS_PATH.parent.mkdir(parents=True, exist_ok=True)
    SETTINGS_PATH.write_text(
        json.dumps(data, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )


def get_clips_root() -> Path:
    data = load()
    p = data.get("clips_root")
    if p:
        return Path(p)
    return DEFAULT_CLIPS_ROOT


def set_clips_root(path: str) -> None:
    data = load()
    data["clips_root"] = path
    data["configured"] = True
    save(data)


def is_configured() -> bool:
    return bool(load().get("configured"))


def get_cuts_root() -> Path:
    data = load()
    p = data.get("cuts_root")
    if p:
        return Path(p)
    # Default: a subfolder inside the clips root so cuts live next to the game
    # folders and are scanned/accessible in-app (not stranded on the Desktop).
    return get_clips_root() / CUTS_SUBDIR


def set_cuts_root(path: str) -> None:
    data = load()
    data["cuts_root"] = path
    save(data)
