using Microsoft.Data.Sqlite;

namespace KeepClip;

/// <summary>
/// SQLite layer for KeepClip. Stores clips and transcript segments with FTS5
/// full-text search. Schema, indexes, and triggers are a 1:1 port of <c>db.py</c>
/// so an existing Python-created <c>klipy.db</c> opens unchanged.
/// </summary>
public static class Db
{
    public static string DbPath => Config.DbPath;

    // Verbatim port of the Python SCHEMA. The FTS5 virtual table uses the same
    // unicode61/remove_diacritics tokenizer, and the AI/AD/AU triggers keep the
    // external-content FTS index in sync with the segments table.
    private const string Schema = @"
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
";

    /// <summary>Create the DB file (if missing), apply the schema, and run migrations.</summary>
    public static void InitDb()
    {
        Directory.CreateDirectory(Config.DataDir);
        using var con = Open();
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = Schema;
            cmd.ExecuteNonQuery();
        }
        Migrate(con);
    }

    /// <summary>
    /// Open a connection with foreign keys enforced and a generous busy timeout,
    /// matching the Python <c>get_conn()</c> contract (PRAGMA foreign_keys=ON, timeout=30).
    /// </summary>
    public static SqliteConnection Open()
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 30,
        };
        var con = new SqliteConnection(csb.ConnectionString);
        con.Open();
        using (var pragma = con.CreateCommand())
        {
            pragma.CommandText = "PRAGMA busy_timeout=30000;";
            pragma.ExecuteNonQuery();
        }
        return con;
    }

    /// <summary>
    /// Apply lightweight, idempotent column migrations for older DBs.
    /// CREATE TABLE IF NOT EXISTS never alters an existing table, so columns
    /// added after a user already has a DB must be patched in with ALTER TABLE.
    /// </summary>
    private static void Migrate(SqliteConnection con)
    {
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(clips)";
            using var r = cmd.ExecuteReader();
            while (r.Read()) cols.Add(r.GetString(1)); // column 1 = name
        }

        void AddColumn(string name, string ddl)
        {
            if (cols.Contains(name)) return;
            using var c = con.CreateCommand();
            c.CommandText = $"ALTER TABLE clips ADD COLUMN {ddl}";
            c.ExecuteNonQuery();
        }

        AddColumn("favorite", "favorite INTEGER NOT NULL DEFAULT 0");
        // Cloud storage (Google Drive offload): where the clip's bytes live and,
        // if in the cloud, the remote object id. 'local' = file on disk at filepath.
        AddColumn("storage", "storage TEXT NOT NULL DEFAULT 'local'");
        AddColumn("remote_id", "remote_id TEXT");
        AddColumn("remote_uploaded_at", "remote_uploaded_at TEXT");
    }
}
