using Microsoft.Data.Sqlite;

namespace KeepClip.Data;

public static class Db
{
    public static string DbPath => Config.DbPath;

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
    remote_uploaded_at TEXT,
    demo_path TEXT
);

CREATE INDEX IF NOT EXISTS idx_clips_game ON clips(game);
CREATE INDEX IF NOT EXISTS idx_clips_transcribed ON clips(transcribed_at);

CREATE TABLE IF NOT EXISTS folders (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    hidden_from_home INTEGER NOT NULL DEFAULT 0
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
        con.Exec(@"
CREATE INDEX IF NOT EXISTS idx_clips_remote ON clips(remote_id) WHERE remote_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_clips_mtime ON clips(mtime DESC);
CREATE INDEX IF NOT EXISTS idx_clips_game_mtime ON clips(game, mtime DESC);
CREATE INDEX IF NOT EXISTS idx_clips_size ON clips(size_bytes DESC);
CREATE INDEX IF NOT EXISTS idx_clips_favorite_mtime ON clips(mtime DESC) WHERE favorite=1;
CREATE INDEX IF NOT EXISTS idx_clips_thumbnail_pending ON clips(id) WHERE storage!='cloud' AND (has_thumb!=1 OR duration IS NULL);
CREATE INDEX IF NOT EXISTS idx_segments_clip_start ON segments(clip_id, start_s);
");
    }

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

    internal static void Migrate(SqliteConnection con)
    {
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(clips)";
            using var r = cmd.ExecuteReader();
        while (r.Read()) cols.Add(r.GetString(1));
        }

        void AddColumn(string name, string ddl)
        {
            if (cols.Contains(name)) return;
            using var c = con.CreateCommand();
            c.CommandText = ddl;
            c.ExecuteNonQuery();
        }

        AddColumn("favorite", "ALTER TABLE clips ADD COLUMN favorite INTEGER NOT NULL DEFAULT 0");

        AddColumn("storage", "ALTER TABLE clips ADD COLUMN storage TEXT NOT NULL DEFAULT 'local'");
        AddColumn("remote_id", "ALTER TABLE clips ADD COLUMN remote_id TEXT");
        AddColumn("remote_uploaded_at", "ALTER TABLE clips ADD COLUMN remote_uploaded_at TEXT");
        AddColumn("demo_path", "ALTER TABLE clips ADD COLUMN demo_path TEXT");
        AddColumn("thumb_retry_at", "ALTER TABLE clips ADD COLUMN thumb_retry_at REAL");

        cols.Clear();
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(folders)";
            using var r = cmd.ExecuteReader();
            while (r.Read()) cols.Add(r.GetString(1));
        }
        if (!cols.Contains("hidden_from_home"))
            con.Exec("ALTER TABLE folders ADD COLUMN hidden_from_home INTEGER NOT NULL DEFAULT 0");
    }
}
