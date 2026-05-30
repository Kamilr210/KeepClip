"""SQLite layer for klipy. Stores clips and transcript segments with FTS5 full-text search."""
from __future__ import annotations

import sqlite3
from contextlib import contextmanager
from pathlib import Path
from typing import Iterator

DB_PATH = Path(__file__).parent.parent / "data" / "klipy.db"


SCHEMA = """
CREATE TABLE IF NOT EXISTS clips (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    game TEXT NOT NULL,
    filename TEXT NOT NULL,
    filepath TEXT NOT NULL UNIQUE,
    size_bytes INTEGER,
    mtime REAL,
    duration REAL,
    has_thumb INTEGER DEFAULT 0,
    transcribed_at TEXT,
    language TEXT,
    favorite INTEGER NOT NULL DEFAULT 0,
    storage TEXT NOT NULL DEFAULT 'local',
    remote_id TEXT,
    remote_uploaded_at TEXT
);

CREATE INDEX IF NOT EXISTS idx_clips_game ON clips(game);
CREATE INDEX IF NOT EXISTS idx_clips_transcribed ON clips(transcribed_at);

CREATE TABLE IF NOT EXISTS folders (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS folder_clips (
    folder_id INTEGER NOT NULL,
    clip_id INTEGER NOT NULL,
    added_at TEXT NOT NULL DEFAULT (datetime('now')),
    PRIMARY KEY (folder_id, clip_id),
    FOREIGN KEY (folder_id) REFERENCES folders(id) ON DELETE CASCADE,
    FOREIGN KEY (clip_id)   REFERENCES clips(id)   ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_folder_clips_clip ON folder_clips(clip_id);
CREATE INDEX IF NOT EXISTS idx_folder_clips_added ON folder_clips(folder_id, added_at DESC);

CREATE TABLE IF NOT EXISTS segments (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    clip_id INTEGER NOT NULL,
    start_s REAL NOT NULL,
    end_s REAL NOT NULL,
    text TEXT NOT NULL,
    FOREIGN KEY (clip_id) REFERENCES clips(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_segments_clip ON segments(clip_id);

CREATE VIRTUAL TABLE IF NOT EXISTS segments_fts USING fts5(
    text,
    content='segments',
    content_rowid='id',
    tokenize='unicode61 remove_diacritics 2'
);

CREATE TRIGGER IF NOT EXISTS segments_ai AFTER INSERT ON segments BEGIN
    INSERT INTO segments_fts(rowid, text) VALUES (new.id, new.text);
END;

CREATE TRIGGER IF NOT EXISTS segments_ad AFTER DELETE ON segments BEGIN
    INSERT INTO segments_fts(segments_fts, rowid, text) VALUES('delete', old.id, old.text);
END;

CREATE TRIGGER IF NOT EXISTS segments_au AFTER UPDATE ON segments BEGIN
    INSERT INTO segments_fts(segments_fts, rowid, text) VALUES('delete', old.id, old.text);
    INSERT INTO segments_fts(rowid, text) VALUES (new.id, new.text);
END;
"""


def _migrate(con: sqlite3.Connection) -> None:
    """Apply lightweight, idempotent schema migrations for existing DBs.

    CREATE TABLE IF NOT EXISTS never alters an existing table, so columns added
    after a user already has a DB must be patched in with ALTER TABLE.
    """
    cols = {row[1] for row in con.execute("PRAGMA table_info(clips)")}
    if "favorite" not in cols:
        con.execute("ALTER TABLE clips ADD COLUMN favorite INTEGER NOT NULL DEFAULT 0")
    # Cloud storage (Google Drive offload): where the clip's bytes live and, if in
    # the cloud, the remote object id. 'local' = file on disk at filepath.
    if "storage" not in cols:
        con.execute("ALTER TABLE clips ADD COLUMN storage TEXT NOT NULL DEFAULT 'local'")
    if "remote_id" not in cols:
        con.execute("ALTER TABLE clips ADD COLUMN remote_id TEXT")
    if "remote_uploaded_at" not in cols:
        con.execute("ALTER TABLE clips ADD COLUMN remote_uploaded_at TEXT")


def init_db() -> None:
    DB_PATH.parent.mkdir(parents=True, exist_ok=True)
    with sqlite3.connect(DB_PATH) as con:
        con.executescript(SCHEMA)
        _migrate(con)
        con.commit()


@contextmanager
def get_conn() -> Iterator[sqlite3.Connection]:
    con = sqlite3.connect(DB_PATH, timeout=30)
    con.row_factory = sqlite3.Row
    con.execute("PRAGMA foreign_keys = ON")
    try:
        yield con
        con.commit()
    finally:
        con.close()


if __name__ == "__main__":
    init_db()
    print(f"DB ready at {DB_PATH}")
