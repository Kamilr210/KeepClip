"""FastAPI app: scan, transcribe (SSE), search, video + thumbnail streaming."""
from __future__ import annotations

import asyncio
import json
import os
import re
import shutil
import sqlite3
import subprocess
import sys
import threading
import time
from datetime import datetime, timezone
from html import escape
from pathlib import Path
from typing import AsyncIterator, Optional

from fastapi import FastAPI, HTTPException, Query, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse, HTMLResponse, Response, StreamingResponse
from fastapi.staticfiles import StaticFiles
from send2trash import send2trash

from config import THUMBS_DIR
from db import get_conn, init_db
from media import (
    cut_clip,
    fix_broken_clip,
    make_thumbnail,
    probe_decode_errors,
    probe_duration,
    thumb_path,
)
from scanner import scan as scan_clips
import settings as user_settings
import gdrive

PROJECT_ROOT = Path(__file__).resolve().parent.parent
FRONTEND_DIR = PROJECT_ROOT / "frontend"

app = FastAPI(title="Klipy")
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


IDLE_TIMEOUT_SECONDS = 5 * 60  # auto-shutdown after this long with no heartbeat
HEARTBEAT_GRACE_AT_STARTUP = 60  # don't auto-shutdown for the first minute after boot
_last_heartbeat = time.time() + HEARTBEAT_GRACE_AT_STARTUP


def _touch_heartbeat() -> None:
    global _last_heartbeat
    _last_heartbeat = time.time()


def _idle_watcher() -> None:
    """Exit the process once we've gone IDLE_TIMEOUT_SECONDS with no page activity."""
    while True:
        time.sleep(30)
        # Never bail in the middle of a transcription run.
        if STATE.snapshot()["running"]:
            _touch_heartbeat()
            continue
        idle = time.time() - _last_heartbeat
        if idle > IDLE_TIMEOUT_SECONDS:
            print(f"[idle] no heartbeat for {int(idle)}s, exiting", file=sys.stderr, flush=True)
            os._exit(0)


@app.on_event("startup")
def _startup():
    init_db()
    threading.Thread(target=_idle_watcher, daemon=True).start()


# ---------- shared transcription progress state ----------
class TranscribeState:
    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.running = False
        self.cancel = False
        self.total = 0
        self.done = 0
        self.current: Optional[dict] = None
        self.error: Optional[str] = None
        self.finished_at: Optional[str] = None
        self.log: list[str] = []  # last few log lines

    def snapshot(self) -> dict:
        with self.lock:
            return {
                "running": self.running,
                "total": self.total,
                "done": self.done,
                "current": self.current,
                "error": self.error,
                "finished_at": self.finished_at,
                "log_tail": self.log[-8:],
            }


STATE = TranscribeState()


def _push_log(line: str) -> None:
    with STATE.lock:
        STATE.log.append(line)
        if len(STATE.log) > 100:
            STATE.log = STATE.log[-100:]


def _transcribe_worker(force: bool = False) -> None:
    from transcriber import transcribe

    try:
        with get_conn() as con:
            if force:
                rows = con.execute(
                    "SELECT id, filepath FROM clips ORDER BY mtime DESC"
                ).fetchall()
            else:
                rows = con.execute(
                    "SELECT id, filepath FROM clips WHERE transcribed_at IS NULL ORDER BY mtime DESC"
                ).fetchall()
        with STATE.lock:
            STATE.total = len(rows)
            STATE.done = 0
            STATE.error = None
            STATE.finished_at = None

        if not rows:
            _push_log("Brak nowych klipów do transkrypcji.")
            return

        for row in rows:
            if STATE.cancel:
                _push_log("Anulowano.")
                break
            cid = row["id"]
            fp = Path(row["filepath"])
            if not fp.exists():
                _push_log(f"[!] Brak pliku, pomijam: {fp.name}")
                with get_conn() as con:
                    con.execute("DELETE FROM clips WHERE id=?", (cid,))
                with STATE.lock:
                    STATE.done += 1
                continue

            with STATE.lock:
                STATE.current = {"id": cid, "filename": fp.name, "game": fp.parent.name}
            _push_log(f"-> {fp.parent.name} / {fp.name}")

            t0 = time.time()

            duration = probe_duration(fp)
            with get_conn() as con:
                if duration is not None:
                    con.execute("UPDATE clips SET duration=? WHERE id=?", (duration, cid))
                if not thumb_path(cid).exists():
                    if make_thumbnail(cid, fp):
                        con.execute("UPDATE clips SET has_thumb=1 WHERE id=?", (cid,))

            try:
                seg_count = 0
                with get_conn() as con:
                    con.execute("DELETE FROM segments WHERE clip_id=?", (cid,))
                    for start, end, text in transcribe(fp):
                        con.execute(
                            "INSERT INTO segments(clip_id, start_s, end_s, text) VALUES(?,?,?,?)",
                            (cid, start, end, text),
                        )
                        seg_count += 1
                    con.execute(
                        "UPDATE clips SET transcribed_at=?, language='pl' WHERE id=?",
                        (datetime.now(timezone.utc).isoformat(timespec="seconds"), cid),
                    )
                dt = time.time() - t0
                _push_log(f"   OK ({seg_count} segm., {dt:.1f}s)")
            except Exception as exc:
                _push_log(f"   BŁĄD: {exc}")
                with STATE.lock:
                    STATE.error = str(exc)

            with STATE.lock:
                STATE.done += 1
                STATE.current = None
    except Exception as exc:
        with STATE.lock:
            STATE.error = str(exc)
        _push_log(f"FATAL: {exc}")
    finally:
        with STATE.lock:
            STATE.running = False
            STATE.cancel = False
            STATE.finished_at = datetime.now(timezone.utc).isoformat(timespec="seconds")


# ---------- background thumbnail generation ----------
_thumb_thread: Optional[threading.Thread] = None
_thumb_lock = threading.Lock()


def _thumbnail_worker() -> None:
    """Generate missing thumbnails AND probe missing durations for all clips.
    Cheap (~0.5s for thumbnail, ~0.1s for ffprobe). Runs in a daemon thread
    after every scan/config change."""
    while True:
        with get_conn() as con:
            row = con.execute(
                "SELECT id, filepath, duration FROM clips "
                "WHERE has_thumb=0 OR duration IS NULL LIMIT 1"
            ).fetchone()
        if not row:
            return
        cid = row["id"]
        fp = Path(row["filepath"])
        if not fp.exists():
            with get_conn() as con:
                con.execute("UPDATE clips SET has_thumb=2 WHERE id=?", (cid,))
            continue

        # Fill in duration if missing — needed for hover preview start offset
        new_duration = row["duration"]
        if new_duration is None:
            new_duration = probe_duration(fp)

        # Generate thumbnail if missing
        has_thumb_now = 1
        thumb_file = thumb_path(cid)
        if not thumb_file.exists():
            ok = make_thumbnail(cid, fp)
            has_thumb_now = 1 if ok else 2

        with get_conn() as con:
            con.execute(
                "UPDATE clips SET has_thumb=?, duration=COALESCE(?, duration) WHERE id=?",
                (has_thumb_now, new_duration, cid),
            )


def _ensure_thumbnail_worker() -> None:
    """Start the thumbnail worker if it isn't already running."""
    global _thumb_thread
    with _thumb_lock:
        if _thumb_thread and _thumb_thread.is_alive():
            return
        _thumb_thread = threading.Thread(target=_thumbnail_worker, daemon=True)
        _thumb_thread.start()


# ---------- API ----------
@app.get("/api/ping")
def api_ping():
    """Cheap liveness check used by klipy.cmd before opening the browser."""
    return {"ok": True}


@app.get("/api/config")
def api_get_config():
    _touch_heartbeat()
    root = user_settings.get_clips_root()
    configured = user_settings.is_configured()
    # Backward compat: pre-existing installs already have clips registered;
    # treat them as configured even without an explicit settings.json.
    if not configured:
        with get_conn() as con:
            n = con.execute("SELECT COUNT(*) AS n FROM clips").fetchone()["n"]
        if n > 0:
            user_settings.set_clips_root(str(root))
            configured = True
    return {
        "clips_root": str(root),
        "clips_root_exists": root.exists() and root.is_dir(),
        "configured": configured,
    }


class _ConfigPayload(__import__("pydantic").BaseModel):
    clips_root: str


@app.post("/api/config")
def api_set_config(payload: _ConfigPayload):
    _touch_heartbeat()
    if STATE.snapshot()["running"]:
        raise HTTPException(409, "Nie można zmieniać folderu w trakcie transkrypcji.")
    new_root = payload.clips_root.strip().strip('"').strip("'")
    if not new_root:
        raise HTTPException(400, "Ścieżka folderu jest wymagana.")
    p = Path(new_root)
    if not p.exists():
        raise HTTPException(400, f"Folder nie istnieje: {p}")
    if not p.is_dir():
        raise HTTPException(400, f"To nie jest folder: {p}")
    user_settings.set_clips_root(str(p))
    # Run a scan immediately so the new folder's contents show up
    scan_result = scan_clips()
    _ensure_thumbnail_worker()
    return {"ok": True, "clips_root": str(p), "scan": scan_result}


@app.post("/api/heartbeat")
def api_heartbeat():
    """Frontend sends this on a timer; while it keeps coming the server stays alive."""
    _touch_heartbeat()
    return {"ok": True}


@app.post("/api/scan")
def api_scan():
    _touch_heartbeat()
    result = scan_clips()
    _ensure_thumbnail_worker()
    return result


@app.post("/api/transcribe/start")
def api_start(force: bool = False):
    """Start a batch transcription run. By default only processes clips that
    haven't been transcribed yet. force=true re-does every clip in the DB."""
    _touch_heartbeat()
    with STATE.lock:
        if STATE.running:
            return {"started": False, "reason": "already_running"}
        STATE.running = True
        STATE.cancel = False
        STATE.error = None
        STATE.finished_at = None
        STATE.total = 0
        STATE.done = 0
    threading.Thread(target=_transcribe_worker, kwargs={"force": force}, daemon=True).start()
    return {"started": True, "force": force}


@app.post("/api/shutdown")
def api_shutdown():
    """Manual shutdown — used if you want to free RAM right now without waiting for timeout."""
    if STATE.snapshot()["running"]:
        return {"ok": False, "reason": "transcription_running"}
    threading.Timer(0.5, lambda: os._exit(0)).start()
    return {"ok": True}


@app.post("/api/transcribe/cancel")
def api_cancel():
    with STATE.lock:
        if not STATE.running:
            return {"cancelled": False, "reason": "not_running"}
        STATE.cancel = True
    return {"cancelled": True}


@app.get("/api/transcribe/status")
def api_status():
    return STATE.snapshot()


@app.get("/api/transcribe/stream")
async def api_stream(request: Request) -> StreamingResponse:
    async def gen() -> AsyncIterator[str]:
        last_payload = ""
        while True:
            if await request.is_disconnected():
                break
            snap = STATE.snapshot()
            payload = json.dumps(snap)
            if payload != last_payload:
                yield f"data: {payload}\n\n"
                last_payload = payload
            if not snap["running"] and snap.get("finished_at"):
                yield f"data: {payload}\n\n"
                break
            await asyncio.sleep(0.5)

    return StreamingResponse(gen(), media_type="text/event-stream")


@app.get("/api/stats")
def api_stats():
    with get_conn() as con:
        clips = con.execute("SELECT COUNT(*) AS n FROM clips").fetchone()["n"]
        done = con.execute(
            "SELECT COUNT(*) AS n FROM clips WHERE transcribed_at IS NOT NULL"
        ).fetchone()["n"]
        segs = con.execute("SELECT COUNT(*) AS n FROM segments").fetchone()["n"]
        total_bytes = con.execute(
            "SELECT COALESCE(SUM(size_bytes), 0) AS s FROM clips"
        ).fetchone()["s"]
        # Split by where the bytes actually live so the sidebar can show local vs
        # cloud separately (NULL storage == legacy local rows).
        local_bytes = con.execute(
            "SELECT COALESCE(SUM(size_bytes), 0) AS s FROM clips "
            "WHERE storage IS NULL OR storage != 'cloud'"
        ).fetchone()["s"]
        cloud_bytes = con.execute(
            "SELECT COALESCE(SUM(size_bytes), 0) AS s FROM clips WHERE storage = 'cloud'"
        ).fetchone()["s"]
        favorites = con.execute(
            "SELECT COUNT(*) AS n FROM clips WHERE favorite = 1"
        ).fetchone()["n"]
        games = [
            dict(row)
            for row in con.execute(
                """SELECT game, COUNT(*) AS clips,
                          SUM(CASE WHEN transcribed_at IS NOT NULL THEN 1 ELSE 0 END) AS done
                   FROM clips GROUP BY game ORDER BY game"""
            )
        ]
    # Free/total space on the volume that holds the clips folder.
    disk_total = disk_free = None
    try:
        root = user_settings.get_clips_root()
        if root and Path(root).exists():
            du = shutil.disk_usage(str(root))
            disk_total, disk_free = du.total, du.free
    except Exception:
        pass
    return {
        "clips": clips,
        "transcribed": done,
        "segments": segs,
        "total_bytes": total_bytes,
        "local_bytes": local_bytes,
        "cloud_bytes": cloud_bytes,
        "disk_total": disk_total,
        "disk_free": disk_free,
        "favorites": favorites,
        "games": games,
    }


_SORT_CLAUSES = {
    "newest":   "mtime DESC",
    "oldest":   "mtime ASC",
    "largest":  "size_bytes DESC",
    "smallest": "size_bytes ASC",
}


@app.get("/api/clips")
def api_clips(
    game: Optional[str] = None,
    sort: str = "newest",
    limit: int = 200,
    favorite: Optional[int] = None,
):
    order = _SORT_CLAUSES.get(sort, _SORT_CLAUSES["newest"])
    q = (
        "SELECT id, game, filename, duration, size_bytes, mtime, has_thumb, "
        "transcribed_at, favorite, storage "
        "FROM clips"
    )
    where: list[str] = []
    args: list = []
    if game:
        where.append("game = ?")
        args.append(game)
    if favorite:
        where.append("favorite = 1")
    if where:
        q += " WHERE " + " AND ".join(where)
    q += f" ORDER BY {order} LIMIT ?"
    args.append(limit)
    with get_conn() as con:
        return [dict(r) for r in con.execute(q, args)]


@app.post("/api/clips/{clip_id}/favorite")
def api_toggle_favorite(clip_id: int):
    """Flip the favorite flag for a clip and return the new state."""
    _touch_heartbeat()
    with get_conn() as con:
        row = con.execute(
            "SELECT favorite FROM clips WHERE id = ?", (clip_id,)
        ).fetchone()
        if not row:
            raise HTTPException(404, "Klip nie istnieje.")
        new_state = 0 if row["favorite"] else 1
        con.execute(
            "UPDATE clips SET favorite = ? WHERE id = ?", (new_state, clip_id)
        )
    return {"ok": True, "id": clip_id, "favorite": bool(new_state)}


_FTS_SAFE = re.compile(r"[\wÀ-ſ]+", re.UNICODE)


def _to_fts_query(q: str) -> str:
    """Turn 'nie no co ty robisz' into 'nie no co ty robisz' (AND'ed prefix tokens),
    so substring/inflection matches work even with diacritic-insensitive tokenizer."""
    tokens = _FTS_SAFE.findall(q.lower())
    if not tokens:
        return ""
    return " AND ".join(f'"{t}"*' for t in tokens)


_SEARCH_SORTS = {
    "relevance": "score ASC",
    "newest":    "c.mtime DESC, score ASC",
    "oldest":    "c.mtime ASC, score ASC",
    "largest":   "c.size_bytes DESC, score ASC",
    "smallest":  "c.size_bytes ASC, score ASC",
}


@app.get("/api/search")
def api_search(
    q: str = Query(..., min_length=1),
    sort: str = "relevance",
    limit: int = 100,
):
    fts_q = _to_fts_query(q)
    if not fts_q:
        return {"query": q, "results": []}
    order = _SEARCH_SORTS.get(sort, _SEARCH_SORTS["relevance"])
    with get_conn() as con:
        rows = con.execute(
            f"""
            SELECT s.id AS segment_id, s.start_s, s.end_s, s.text,
                   c.id AS clip_id, c.game, c.filename, c.duration,
                   c.size_bytes, c.mtime, c.has_thumb, c.favorite, c.storage,
                   snippet(segments_fts, 0, '<mark>', '</mark>', '...', 12) AS snippet,
                   bm25(segments_fts) AS score
            FROM segments_fts
            JOIN segments s ON s.id = segments_fts.rowid
            JOIN clips c ON c.id = s.clip_id
            WHERE segments_fts MATCH ?
            ORDER BY {order}
            LIMIT ?
            """,
            (fts_q, limit),
        ).fetchall()
    return {"query": q, "fts": fts_q, "results": [dict(r) for r in rows]}


# ---------- folders ----------
class _FolderCreate(__import__("pydantic").BaseModel):
    name: str


class _FolderRename(__import__("pydantic").BaseModel):
    name: str


@app.get("/api/folders")
def api_list_folders():
    """List all folders with clip count and a few sample clip IDs for the collage thumbnail."""
    _touch_heartbeat()
    with get_conn() as con:
        folders = [dict(r) for r in con.execute(
            """
            SELECT f.id, f.name, f.created_at,
                   (SELECT COUNT(*) FROM folder_clips fc WHERE fc.folder_id = f.id) AS clip_count
            FROM folders f
            ORDER BY f.name COLLATE NOCASE
            """
        )]
        for f in folders:
            f["sample_clip_ids"] = [r["clip_id"] for r in con.execute(
                """SELECT clip_id FROM folder_clips
                   WHERE folder_id=? ORDER BY added_at DESC LIMIT 4""",
                (f["id"],),
            )]
    return folders


@app.post("/api/folders")
def api_create_folder(payload: _FolderCreate):
    _touch_heartbeat()
    name = payload.name.strip()
    if not name:
        raise HTTPException(400, "Nazwa folderu jest wymagana.")
    if len(name) > 100:
        raise HTTPException(400, "Nazwa folderu za długa (max 100 znaków).")
    with get_conn() as con:
        try:
            cur = con.execute("INSERT INTO folders(name) VALUES(?)", (name,))
        except sqlite3.IntegrityError:
            raise HTTPException(409, f'Folder o nazwie „{name}" już istnieje.')
        new_id = cur.lastrowid
        row = con.execute(
            "SELECT id, name, created_at FROM folders WHERE id=?", (new_id,)
        ).fetchone()
    return dict(row)


@app.patch("/api/folders/{folder_id}")
def api_rename_folder(folder_id: int, payload: _FolderRename):
    _touch_heartbeat()
    name = payload.name.strip()
    if not name:
        raise HTTPException(400, "Nazwa folderu jest wymagana.")
    with get_conn() as con:
        row = con.execute("SELECT id FROM folders WHERE id=?", (folder_id,)).fetchone()
        if not row:
            raise HTTPException(404, "Folder nie istnieje.")
        try:
            con.execute("UPDATE folders SET name=? WHERE id=?", (name, folder_id))
        except sqlite3.IntegrityError:
            raise HTTPException(409, f'Folder o nazwie „{name}" już istnieje.')
    return {"ok": True, "id": folder_id, "name": name}


@app.delete("/api/folders/{folder_id}")
def api_delete_folder(folder_id: int):
    _touch_heartbeat()
    with get_conn() as con:
        row = con.execute("SELECT name FROM folders WHERE id=?", (folder_id,)).fetchone()
        if not row:
            raise HTTPException(404, "Folder nie istnieje.")
        con.execute("DELETE FROM folders WHERE id=?", (folder_id,))
    return {"ok": True, "deleted_name": row["name"]}


@app.get("/api/folders/{folder_id}/clips")
def api_folder_clips(folder_id: int, sort: str = "newest", limit: int = 500):
    """Clips in a folder. Sort options match /api/clips."""
    _touch_heartbeat()
    order_map = {
        "newest":   "c.mtime DESC",
        "oldest":   "c.mtime ASC",
        "largest":  "c.size_bytes DESC",
        "smallest": "c.size_bytes ASC",
        "added":    "fc.added_at DESC",  # most recently added to folder
    }
    order = order_map.get(sort, order_map["added"])
    with get_conn() as con:
        folder = con.execute(
            "SELECT id, name, created_at FROM folders WHERE id=?", (folder_id,)
        ).fetchone()
        if not folder:
            raise HTTPException(404, "Folder nie istnieje.")
        rows = con.execute(
            f"""
            SELECT c.id, c.game, c.filename, c.duration, c.size_bytes, c.mtime,
                   c.has_thumb, c.transcribed_at, c.storage
            FROM folder_clips fc
            JOIN clips c ON c.id = fc.clip_id
            WHERE fc.folder_id = ?
            ORDER BY {order}
            LIMIT ?
            """,
            (folder_id, limit),
        ).fetchall()
    return {"folder": dict(folder), "clips": [dict(r) for r in rows]}


@app.post("/api/folders/{folder_id}/clips/{clip_id}")
def api_add_clip_to_folder(folder_id: int, clip_id: int):
    _touch_heartbeat()
    with get_conn() as con:
        if not con.execute("SELECT 1 FROM folders WHERE id=?", (folder_id,)).fetchone():
            raise HTTPException(404, "Folder nie istnieje.")
        if not con.execute("SELECT 1 FROM clips WHERE id=?", (clip_id,)).fetchone():
            raise HTTPException(404, "Klip nie istnieje.")
        con.execute(
            "INSERT OR IGNORE INTO folder_clips(folder_id, clip_id) VALUES(?,?)",
            (folder_id, clip_id),
        )
    return {"ok": True}


@app.delete("/api/folders/{folder_id}/clips/{clip_id}")
def api_remove_clip_from_folder(folder_id: int, clip_id: int):
    _touch_heartbeat()
    with get_conn() as con:
        con.execute(
            "DELETE FROM folder_clips WHERE folder_id=? AND clip_id=?",
            (folder_id, clip_id),
        )
    return {"ok": True}


@app.get("/api/clips/{clip_id}/folders")
def api_clip_folders(clip_id: int):
    """Which folders contain this clip — for showing checkbox state in the player UI."""
    _touch_heartbeat()
    with get_conn() as con:
        rows = con.execute(
            "SELECT folder_id FROM folder_clips WHERE clip_id=?", (clip_id,)
        ).fetchall()
    return {"folder_ids": [r["folder_id"] for r in rows]}


@app.get("/api/segments/{clip_id}")
def api_segments(clip_id: int):
    with get_conn() as con:
        clip = con.execute("SELECT * FROM clips WHERE id=?", (clip_id,)).fetchone()
        if not clip:
            raise HTTPException(404, "clip not found")
        segs = [
            dict(r)
            for r in con.execute(
                "SELECT id, start_s, end_s, text FROM segments WHERE clip_id=? ORDER BY start_s",
                (clip_id,),
            )
        ]
    return {"clip": dict(clip), "segments": segs}


class _SegmentEdit(__import__("pydantic").BaseModel):
    text: str


@app.patch("/api/segments/{segment_id}")
def api_edit_segment(segment_id: int, payload: _SegmentEdit):
    """Manually correct a single transcript line. Whisper isn't always accurate,
    so the user can fix the wording by hand. The FTS index is kept in sync
    automatically by the segments_au trigger, so search reflects the edit."""
    _touch_heartbeat()
    text = payload.text.strip()
    if not text:
        raise HTTPException(422, "Tekst nie może być pusty.")
    with get_conn() as con:
        row = con.execute(
            "SELECT id, clip_id, start_s, end_s FROM segments WHERE id=?", (segment_id,)
        ).fetchone()
        if not row:
            raise HTTPException(404, "Segment nie istnieje.")
        con.execute("UPDATE segments SET text=? WHERE id=?", (text, segment_id))
    return {"ok": True, "id": segment_id, "text": text}


@app.post("/api/clips/{clip_id}/retranscribe")
def api_retranscribe_clip(clip_id: int):
    """Re-run Whisper on a single clip, replacing its existing segments.

    Useful after we improve transcription logic (e.g. better timestamping)
    or if the original transcription went wrong on a particular clip.
    """
    _touch_heartbeat()
    if STATE.snapshot()["running"]:
        raise HTTPException(409, "Batch transcription is currently running.")

    with get_conn() as con:
        row = con.execute("SELECT id, filepath FROM clips WHERE id=?", (clip_id,)).fetchone()
        if not row:
            raise HTTPException(404, "clip not found")
    fp = Path(row["filepath"])
    if not fp.exists():
        raise HTTPException(404, "file missing on disk")

    from transcriber import transcribe

    t0 = time.time()
    try:
        new_segments = list(transcribe(fp))
    except Exception as exc:
        raise HTTPException(500, f"Whisper failed: {exc}")

    with get_conn() as con:
        con.execute("DELETE FROM segments WHERE clip_id=?", (clip_id,))
        for start, end, text in new_segments:
            con.execute(
                "INSERT INTO segments(clip_id, start_s, end_s, text) VALUES(?,?,?,?)",
                (clip_id, start, end, text),
            )
        con.execute(
            "UPDATE clips SET transcribed_at=?, language=? WHERE id=?",
            (datetime.now(timezone.utc).isoformat(timespec="seconds"), "pl", clip_id),
        )

    return {
        "ok": True,
        "segments": len(new_segments),
        "seconds": round(time.time() - t0, 1),
    }


@app.post("/api/clips/{clip_id}/fix")
def api_fix_clip(clip_id: int):
    """Fix a broken NVIDIA DVR-style clip so it plays in browsers.

    Sends original to Recycle Bin, replaces it with the fixed version,
    regenerates thumbnail, updates DB metadata.
    """
    _touch_heartbeat()
    with get_conn() as con:
        row = con.execute("SELECT filepath, filename FROM clips WHERE id=?", (clip_id,)).fetchone()
        if not row:
            raise HTTPException(404, "clip not found")
    src = Path(row["filepath"])
    if not src.exists():
        raise HTTPException(404, "file missing on disk")

    # Capture original mtime so the recording date stays correct after we
    # replace the file with the ffmpeg-produced (newly written) one.
    original_mtime = src.stat().st_mtime

    ok, fixed_path, message, trimmed = fix_broken_clip(src)
    if not ok or fixed_path is None:
        raise HTTPException(422, message)

    # Move original to Recycle Bin, slot fixed file into its place
    try:
        send2trash(str(src))
    except Exception as exc:
        # Clean up tmp file so we don't strand it
        if fixed_path.exists():
            fixed_path.unlink()
        raise HTTPException(500, f"Nie udało się przenieść oryginału do Kosza: {exc}")

    try:
        fixed_path.replace(src)
    except OSError as exc:
        raise HTTPException(500, f"Nie udało się przenieść naprawionego pliku: {exc}")

    # Restore the original mtime so UI keeps showing the real recording date
    try:
        os.utime(src, (src.stat().st_atime, original_mtime))
    except OSError:
        pass

    # Refresh metadata + thumbnail
    new_duration = probe_duration(src)
    tp = thumb_path(clip_id)
    if tp.exists():
        try:
            tp.unlink()
        except OSError:
            pass
    has_thumb = make_thumbnail(clip_id, src)

    # Shift existing transcript segments by -trimmed so timestamps line up with
    # the trimmed file. Drop anything that landed entirely in the cut-off intro.
    shifted = 0
    dropped = 0
    if trimmed > 0:
        with get_conn() as con:
            seg_rows = list(
                con.execute(
                    "SELECT id, start_s, end_s FROM segments WHERE clip_id=?",
                    (clip_id,),
                )
            )
            for s in seg_rows:
                new_start = s["start_s"] - trimmed
                new_end = s["end_s"] - trimmed
                if new_end <= 0:
                    con.execute("DELETE FROM segments WHERE id=?", (s["id"],))
                    dropped += 1
                else:
                    con.execute(
                        "UPDATE segments SET start_s=?, end_s=? WHERE id=?",
                        (max(0.0, new_start), new_end, s["id"]),
                    )
                    shifted += 1

    with get_conn() as con:
        stat = src.stat()
        con.execute(
            "UPDATE clips SET size_bytes=?, mtime=?, duration=?, has_thumb=? WHERE id=?",
            (stat.st_size, original_mtime, new_duration, 1 if has_thumb else 0, clip_id),
        )

    return {
        "ok": True,
        "message": message,
        "trimmed_seconds": trimmed,
        "new_duration": new_duration,
        "new_size_bytes": src.stat().st_size,
        "segments_shifted": shifted,
        "segments_dropped": dropped,
    }


class _CutPayload(__import__("pydantic").BaseModel):
    start: float
    end: float
    target_size_mb: Optional[float] = None


@app.post("/api/clips/{clip_id}/cut")
def api_cut_clip(clip_id: int, payload: _CutPayload):
    """Cut a section of a clip into a standalone file, optionally compressed."""
    _touch_heartbeat()
    with get_conn() as con:
        row = con.execute(
            "SELECT filepath, filename, game FROM clips WHERE id=?", (clip_id,)
        ).fetchone()
        if not row:
            raise HTTPException(404, "Klip nie istnieje.")
    src = Path(row["filepath"])
    if not src.exists():
        raise HTTPException(404, "Plik źródłowy nie istnieje na dysku.")

    cuts_root = user_settings.get_cuts_root()
    cuts_root.mkdir(parents=True, exist_ok=True)

    # Build output filename: <stem>_cut_<start>-<end>[_<sizeMB>MB].mp4
    stem = src.stem
    s_lbl = f"{int(payload.start):d}"
    e_lbl = f"{int(payload.end):d}"
    size_lbl = f"_{int(payload.target_size_mb)}MB" if payload.target_size_mb else ""
    out_name = f"{stem}_cut_{s_lbl}-{e_lbl}{size_lbl}.mp4"
    # Sanitize - Windows doesn't like certain chars
    out_name = re.sub(r'[<>:"/\\|?*]', "_", out_name)
    output = cuts_root / out_name

    # Avoid overwriting an existing cut
    i = 2
    base = output.stem
    while output.exists():
        output = cuts_root / f"{base}_{i}.mp4"
        i += 1

    ok, msg, stats = cut_clip(
        src=src, output=output,
        start=payload.start, end=payload.end,
        target_size_mb=payload.target_size_mb,
    )
    if not ok:
        raise HTTPException(422, msg)

    # If the cut landed inside the clips root, register it right away so it's
    # accessible in-app without waiting for a manual rescan. (We skip this when
    # the user pointed cuts_root outside the library, since the scanner would
    # then prune the orphan row on its next run.)
    new_clip_id = None
    clips_root = user_settings.get_clips_root()
    try:
        inside_library = output.resolve().is_relative_to(clips_root.resolve())
    except (OSError, ValueError):
        inside_library = False
    if inside_library:
        out_stat = output.stat()
        game = output.parent.name if output.parent != clips_root else "_root"
        with get_conn() as con:
            cur = con.execute(
                "INSERT INTO clips(game, filename, filepath, size_bytes, mtime) VALUES(?,?,?,?,?)",
                (game, output.name, str(output), out_stat.st_size, out_stat.st_mtime),
            )
            new_clip_id = cur.lastrowid

    return {
        "ok": True,
        "output_path": str(output),
        "output_name": output.name,
        "cuts_root": str(cuts_root),
        "clip_id": new_clip_id,
        "in_library": new_clip_id is not None,
        **stats,
    }


class _ShowPath(__import__("pydantic").BaseModel):
    path: str


@app.post("/api/show-in-explorer")
def api_show_in_explorer(payload: _ShowPath):
    """Open Windows Explorer with the given file highlighted."""
    _touch_heartbeat()
    p = Path(payload.path)
    if not p.exists():
        raise HTTPException(404, "Ścieżka nie istnieje.")
    try:
        if p.is_file():
            # explorer /select highlights the file inside its folder
            subprocess.Popen(["explorer", f"/select,{p}"])
        else:
            subprocess.Popen(["explorer", str(p)])
    except Exception as exc:
        raise HTTPException(500, f"Nie udało się otworzyć Eksploratora: {exc}")
    return {"ok": True}


def _send_to_trash_with_retry(path: str, attempts: int = 6, delay: float = 0.25) -> Optional[str]:
    """Move a file to the Recycle Bin, retrying briefly on a sharing violation.

    Right after the player stops streaming a clip, Windows can keep the file
    handle open for a moment, which makes send2trash fail with WinError 32
    ("used by another process"). A few short retries let that handle close.
    Returns None on success, or the last error string on failure.
    """
    last_error = None
    for i in range(attempts):
        try:
            send2trash(path)
            return None
        except Exception as exc:
            last_error = str(exc)
            if i < attempts - 1:
                time.sleep(delay)
    return last_error


@app.delete("/api/clips/{clip_id}")
def api_delete_clip(clip_id: int, delete_file: bool = True):
    """Remove a clip from the DB. If delete_file is true, move the source file to
    the Windows Recycle Bin (recoverable). A cloud copy is sent to Drive trash
    (also recoverable). Thumbnail is always deleted."""
    with get_conn() as con:
        row = con.execute(
            "SELECT filepath, filename, storage, remote_id FROM clips WHERE id=?",
            (clip_id,),
        ).fetchone()
        if not row:
            raise HTTPException(404, "clip not found")
        filepath = Path(row["filepath"])
        filename = row["filename"]
        storage = row["storage"]
        remote_id = row["remote_id"]

    trash_error = None
    if delete_file and filepath.exists():
        trash_error = _send_to_trash_with_retry(str(filepath))

    tp = thumb_path(clip_id)
    if tp.exists():
        try:
            tp.unlink()
        except OSError:
            pass

    with get_conn() as con:
        con.execute("DELETE FROM clips WHERE id=?", (clip_id,))  # CASCADE -> segments

    # Best-effort: also move the Drive copy to trash (recoverable). Never let a
    # cloud hiccup block the local delete the user asked for.
    if storage == "cloud" and remote_id and gdrive.is_connected():
        try:
            gdrive.delete(remote_id, permanent=False)
        except Exception:
            pass

    return {
        "deleted_clip_id": clip_id,
        "filename": filename,
        "file_sent_to_trash": delete_file and trash_error is None,
        "trash_error": trash_error,
    }


# ---------- cloud (Google Drive) ----------
# Guard against a clip being uploaded twice at once (double-click / retry / two
# tabs). Without it both requests read storage='local' and each uploads, leaving
# duplicate files in Drive. We serialize per clip id with an in-progress set.
_uploads_in_progress: set[int] = set()
_uploads_lock = threading.Lock()


def _offload_clip(clip_id: int) -> dict:
    """Upload one clip to Drive, mark it cloud in the DB, then Recycle-Bin the local
    file. Idempotent and double-submit-safe: a concurrent call for the same clip
    raises 409, and an already-uploaded clip returns early. May raise HTTPException
    or gdrive.GDriveError (the caller maps the latter to a 502)."""
    with _uploads_lock:
        if clip_id in _uploads_in_progress:
            raise HTTPException(409, "Ten klip jest właśnie wysyłany.")
        _uploads_in_progress.add(clip_id)
    try:
        with get_conn() as con:
            row = con.execute(
                "SELECT filepath, filename, storage, remote_id FROM clips WHERE id=?",
                (clip_id,),
            ).fetchone()
        if not row:
            raise HTTPException(404, "clip not found")
        if row["storage"] == "cloud" and row["remote_id"]:
            return {"clip_id": clip_id, "storage": "cloud", "already": True}

        path = Path(row["filepath"])
        if not path.exists():
            raise HTTPException(404, "file missing on disk")

        remote_id = gdrive.upload(path, row["filename"])  # may raise GDriveError

        # Mark cloud BEFORE deleting the local file so a crash can't lose the only copy.
        with get_conn() as con:
            con.execute(
                "UPDATE clips SET storage='cloud', remote_id=?, "
                "remote_uploaded_at=datetime('now') WHERE id=?",
                (remote_id, clip_id),
            )
        trash_error = _send_to_trash_with_retry(str(path))
        return {
            "clip_id": clip_id,
            "storage": "cloud",
            "remote_id": remote_id,
            "local_freed": trash_error is None,
            "trash_error": trash_error,
        }
    finally:
        with _uploads_lock:
            _uploads_in_progress.discard(clip_id)


def _restore_clip(clip_id: int) -> dict:
    """Inverse of _offload_clip: download a cloud clip back to its original local
    path, mark it local, then move the Drive copy to Drive trash (recoverable).
    Shares the per-clip lock so an upload and a restore can't run at once. May
    raise HTTPException or gdrive.GDriveError (the caller maps the latter to 502)."""
    with _uploads_lock:
        if clip_id in _uploads_in_progress:
            raise HTTPException(409, "Ten klip jest właśnie przetwarzany.")
        _uploads_in_progress.add(clip_id)
    try:
        with get_conn() as con:
            row = con.execute(
                "SELECT filepath, filename, storage, remote_id FROM clips WHERE id=?",
                (clip_id,),
            ).fetchone()
        if not row:
            raise HTTPException(404, "clip not found")
        if row["storage"] != "cloud" or not row["remote_id"]:
            return {"clip_id": clip_id, "storage": "local", "already": True}

        dest = Path(row["filepath"])
        gdrive.download(row["remote_id"], dest)  # may raise GDriveError

        # File is back on disk → flip to local BEFORE removing the Drive copy, so a
        # crash here leaves a (redundant) Drive copy rather than no copy at all.
        with get_conn() as con:
            con.execute(
                "UPDATE clips SET storage='local', size_bytes=?, remote_id=NULL, "
                "remote_uploaded_at=NULL WHERE id=?",
                (dest.stat().st_size, clip_id),
            )
        drive_error = None
        try:
            gdrive.delete(row["remote_id"], permanent=False)  # to Drive trash
        except Exception as e:
            drive_error = str(e)
        return {
            "clip_id": clip_id,
            "storage": "local",
            "remote_removed": drive_error is None,
            "drive_error": drive_error,
        }
    finally:
        with _uploads_lock:
            _uploads_in_progress.discard(clip_id)


@app.get("/api/cloud/status")
def api_cloud_status():
    """Everything the Cloud view needs: whether a client.json was dropped in,
    whether an account is connected, and (if so) the account + quota."""
    return gdrive.status()


@app.post("/api/cloud/connect")
def api_cloud_connect():
    """Return the Google consent URL the frontend opens in a new tab."""
    try:
        return {"auth_url": gdrive.build_auth_url()}
    except gdrive.GDriveError as e:
        raise HTTPException(400, str(e))


@app.get("/api/cloud/oauth2callback", response_class=HTMLResponse)
def api_cloud_oauth_callback(code: Optional[str] = None, error: Optional[str] = None):
    """Google redirects the user's browser here after consent. We exchange the
    code for tokens (stored in the OS keychain) and return a tiny page that
    closes itself, since the real app is the other tab."""
    if error:
        return HTMLResponse(
            f"<h2>Połączenie anulowane</h2><p>{escape(error)}</p>"
            "<p>Możesz zamknąć tę kartę.</p>",
            status_code=400,
        )
    if not code:
        return HTMLResponse("<h2>Brak kodu autoryzacji.</h2>", status_code=400)
    try:
        gdrive.exchange_code(code)
    except Exception as e:
        return HTMLResponse(
            f"<h2>Nie udało się połączyć z Google Drive</h2><p>{escape(str(e))}</p>",
            status_code=400,
        )
    return HTMLResponse(
        """
        <!doctype html><html lang="pl"><head><meta charset="utf-8">
        <title>KeepClip — połączono</title>
        <style>
          body{margin:0;height:100vh;display:flex;align-items:center;justify-content:center;
               background:#12151d;color:#e8ebf2;font-family:system-ui,sans-serif}
          .box{text-align:center}
          .ok{font-size:46px;margin-bottom:12px}
          a{color:#7c5cff}
        </style></head>
        <body><div class="box">
          <div class="ok">✓</div>
          <h2>Połączono z Google Drive</h2>
          <p>Możesz zamknąć tę kartę i wrócić do KeepClip.</p>
        </div>
        <script>setTimeout(function(){window.close();}, 1500);</script>
        </body></html>
        """
    )


@app.post("/api/cloud/disconnect")
def api_cloud_disconnect():
    gdrive.disconnect()
    return {"connected": False}


@app.post("/api/clips/{clip_id}/upload")
def api_clip_upload(clip_id: int):
    """Offload one clip to Google Drive, then move the local file to the Recycle
    Bin. All the heavy lifting (plus the double-submit guard) lives in
    _offload_clip; here we just check the connection and map Drive errors to 502."""
    if not gdrive.is_connected():
        raise HTTPException(400, "Nie połączono z Google Drive.")
    try:
        return _offload_clip(clip_id)
    except gdrive.GDriveError as e:
        raise HTTPException(502, str(e))


@app.post("/api/clips/{clip_id}/download")
def api_clip_download(clip_id: int):
    """Bring a clip back from Drive to local disk and remove the Drive copy.
    Heavy lifting + double-submit guard live in _restore_clip."""
    if not gdrive.is_connected():
        raise HTTPException(400, "Nie połączono z Google Drive.")
    try:
        return _restore_clip(clip_id)
    except gdrive.GDriveError as e:
        raise HTTPException(502, str(e))


@app.post("/api/folders/{folder_id}/upload")
def api_folder_upload(folder_id: int):
    """Offload every still-local clip in a folder to Drive. Returns a tally so the
    UI can report how many went up, how many were already there, and failures."""
    if not gdrive.is_connected():
        raise HTTPException(400, "Nie połączono z Google Drive.")
    with get_conn() as con:
        frow = con.execute("SELECT id FROM folders WHERE id=?", (folder_id,)).fetchone()
        if not frow:
            raise HTTPException(404, "folder not found")
        clip_ids = [
            r["clip_id"]
            for r in con.execute(
                "SELECT clip_id FROM folder_clips WHERE folder_id=? ORDER BY added_at",
                (folder_id,),
            ).fetchall()
        ]

    uploaded = skipped = failed = 0
    errors: list[str] = []
    for cid in clip_ids:
        try:
            res = _offload_clip(cid)
        except HTTPException as e:
            # 409 = already being uploaded by another request → treat as skipped;
            # anything else (404 missing file, etc.) is a real failure.
            if e.status_code == 409:
                skipped += 1
            else:
                failed += 1
                errors.append(f"#{cid}: {e.detail}")
            continue
        except Exception as e:
            failed += 1
            errors.append(f"#{cid}: {e}")
            continue
        if res.get("already"):
            skipped += 1
        else:
            uploaded += 1

    return {
        "folder_id": folder_id,
        "total": len(clip_ids),
        "uploaded": uploaded,
        "skipped": skipped,
        "failed": failed,
        "errors": errors[:10],
    }


# ---------- media streaming ----------
def _range_response(path: Path, request: Request) -> Response:
    """Serve a video with HTTP range support so the <video> element can seek."""
    file_size = path.stat().st_size
    range_header = request.headers.get("range")
    if not range_header:
        return FileResponse(path, media_type="video/mp4")

    # bytes=START-END
    m = re.match(r"bytes=(\d+)-(\d*)", range_header)
    if not m:
        raise HTTPException(416, "invalid range")
    start = int(m.group(1))
    end = int(m.group(2)) if m.group(2) else file_size - 1
    end = min(end, file_size - 1)
    length = end - start + 1

    def iterate():
        with open(path, "rb") as f:
            f.seek(start)
            remaining = length
            chunk = 1024 * 1024
            while remaining > 0:
                data = f.read(min(chunk, remaining))
                if not data:
                    break
                remaining -= len(data)
                yield data

    headers = {
        "Content-Range": f"bytes {start}-{end}/{file_size}",
        "Accept-Ranges": "bytes",
        "Content-Length": str(length),
        "Content-Type": "video/mp4",
    }
    return StreamingResponse(iterate(), status_code=206, headers=headers)


@app.get("/video/{clip_id}")
def serve_video(clip_id: int, request: Request):
    with get_conn() as con:
        row = con.execute(
            "SELECT filepath, storage FROM clips WHERE id=?", (clip_id,)
        ).fetchone()
    if not row:
        raise HTTPException(404, "clip not found")
    # Cloud clips have no local bytes — transparently hand off to the Drive proxy
    # so existing /video/{id} links keep working regardless of where it lives.
    if row["storage"] == "cloud":
        return serve_cloud_video(clip_id, request)
    path = Path(row["filepath"])
    if not path.exists():
        raise HTTPException(404, "file missing on disk")
    return _range_response(path, request)


@app.get("/cloud-video/{clip_id}")
def serve_cloud_video(clip_id: int, request: Request):
    """Stream a Drive-hosted clip through localhost. The browser's <video> can't
    attach an Authorization header, so we proxy: forward the Range header up to
    Drive, then pass Drive's bytes + range headers straight back. Status code is
    mirrored (206 for ranged, 200 otherwise) so seeking works."""
    with get_conn() as con:
        row = con.execute(
            "SELECT remote_id, storage FROM clips WHERE id=?", (clip_id,)
        ).fetchone()
    if not row:
        raise HTTPException(404, "clip not found")
    if row["storage"] != "cloud" or not row["remote_id"]:
        raise HTTPException(404, "clip is not in the cloud")
    if not gdrive.is_connected():
        raise HTTPException(400, "Nie połączono z Google Drive.")

    try:
        upstream = gdrive.open_range_stream(row["remote_id"], request.headers.get("range"))
    except gdrive.GDriveError as e:
        raise HTTPException(502, str(e))

    def body():
        try:
            for chunk in upstream.iter_content(chunk_size=1024 * 1024):
                if chunk:
                    yield chunk
        finally:
            upstream.close()

    passthrough = ("content-range", "content-length", "content-type", "accept-ranges")
    headers = {k: v for k, v in upstream.headers.items() if k.lower() in passthrough}
    headers.setdefault("Accept-Ranges", "bytes")
    headers.setdefault("Content-Type", "video/mp4")
    return StreamingResponse(body(), status_code=upstream.status_code, headers=headers)


# keepclip/backend/app.py

@app.get("/thumb/{clip_id}")
def serve_thumb(clip_id: int):
    p = thumb_path(clip_id)
    if not p.exists():
        # try to generate it lazily
        with get_conn() as con:
            # Вытягиваем has_thumb из БД
            row = con.execute("SELECT filepath, has_thumb FROM clips WHERE id=?", (clip_id,)).fetchone()
            
        # Если клип не найден или мы уже пытались и неудачно (has_thumb == 2) — сразу отдаем 404
        if not row or row["has_thumb"] == 2:
            raise HTTPException(404, "no thumbnail")
            
        if not make_thumbnail(clip_id, Path(row["filepath"])):
            # Запоминаем неудачу, чтобы не пытаться снова при каждом скролле
            with get_conn() as con:
                con.execute("UPDATE clips SET has_thumb=2 WHERE id=?", (clip_id,))
            raise HTTPException(404, "no thumbnail")
            
        with get_conn() as con:
            con.execute("UPDATE clips SET has_thumb=1 WHERE id=?", (clip_id,))
            
    return FileResponse(p, media_type="image/jpeg")


# ---------- frontend ----------
@app.get("/", response_class=HTMLResponse)
def index():
    idx = FRONTEND_DIR / "index.html"
    if not idx.exists():
        return HTMLResponse("<h1>frontend missing</h1>", status_code=500)
    html = idx.read_text(encoding="utf-8")
    # Cache-bust static assets by their mtime so an updated CSS/JS reaches the browser
    for name in ("styles.css", "app.js"):
        f = FRONTEND_DIR / name
        if f.exists():
            v = int(f.stat().st_mtime)
            html = html.replace(f"/static/{name}", f"/static/{name}?v={v}")
    return HTMLResponse(html)


app.mount("/static", StaticFiles(directory=FRONTEND_DIR), name="static")
