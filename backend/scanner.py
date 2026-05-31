"""Walk the clips root, register new files in the DB, prune entries whose files vanished."""
from __future__ import annotations

from pathlib import Path

from config import VIDEO_EXTS
from db import get_conn, init_db
from media import thumb_path
from settings import get_clips_root


def scan() -> dict:
    """Find video files, insert new ones, remove DB rows for files that no longer exist.

    Returns counts: found (on disk), added (new in DB), removed (gone from disk).
    """
    init_db()
    found = 0
    added = 0
    removed = 0
    clips_root = get_clips_root()
    if not clips_root.exists():
        return {
            "found": 0, "added": 0, "removed": 0,
            "error": f"Folder z klipami nie istnieje: {clips_root}",
        }

    on_disk: set[str] = set()
    for path in clips_root.rglob("*"):
        if path.suffix.lower() not in VIDEO_EXTS or not path.is_file():
            continue
        on_disk.add(str(path))
    found = len(on_disk)

    with get_conn() as con:
        con.execute("UPDATE clips SET has_thumb=0 WHERE has_thumb=2")
        # remove rows for files that no longer exist
        existing_rows = list(con.execute("SELECT id, filepath, storage FROM clips"))
        dead_ids: list[int] = []
        existing_paths: set[str] = set()
        for row in existing_rows:
            # Cloud-offloaded clips intentionally have no local file (it was moved
            # to the Recycle Bin after upload). Never prune them, and reserve their
            # path so a stray reappearance can't trigger a duplicate UNIQUE insert.
            if row["storage"] == "cloud":
                existing_paths.add(row["filepath"])
                continue
            if row["filepath"] in on_disk:
                existing_paths.add(row["filepath"])
            else:
                dead_ids.append(row["id"])

        for cid in dead_ids:
            tp = thumb_path(cid)
            if tp.exists():
                try:
                    tp.unlink()
                except OSError:
                    pass
            con.execute("DELETE FROM clips WHERE id=?", (cid,))  # CASCADE removes segments
            removed += 1

        # insert new ones
        for spath in on_disk - existing_paths:
            path = Path(spath)
            game = path.parent.name if path.parent != clips_root else "_root"
            stat = path.stat()
            con.execute(
                "INSERT INTO clips(game, filename, filepath, size_bytes, mtime) VALUES(?,?,?,?,?)",
                (game, path.name, spath, stat.st_size, stat.st_mtime),
            )
            added += 1

    return {"found": found, "added": added, "removed": removed}


if __name__ == "__main__":
    print(scan())
