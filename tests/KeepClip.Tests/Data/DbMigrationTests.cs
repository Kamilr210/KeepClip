using Microsoft.Data.Sqlite;

namespace KeepClip.Tests.Data;

public class DbMigrationTests
{
    [Fact]
    public void Migration_upgrades_a_legacy_database_without_losing_user_data()
    {
        using var con = LegacyDatabase();
        var name = "Robert'); DROP TABLE clips;--";
        con.Exec("INSERT INTO clips(id, filename) VALUES(1, $name)", ("$name", name));
        con.Exec("INSERT INTO folders(id, name) VALUES(2, 'Ulubione')");

        Db.Migrate(con);

        var clip = con.QueryOne("SELECT * FROM clips WHERE id=1")!;
        Assert.Equal(name, clip["filename"]);
        Assert.Equal(0L, clip["favorite"]);
        Assert.Equal("local", clip["storage"]);
        Assert.Null(clip["remote_id"]);
        Assert.Null(clip["remote_uploaded_at"]);
        Assert.Equal(0L, con.QueryOne("SELECT hidden_from_home FROM folders WHERE id=2")!["hidden_from_home"]);
    }

    [Fact]
    public void Repeated_migration_preserves_existing_favorites_cloud_metadata_and_visibility()
    {
        using var con = LegacyDatabase();
        Db.Migrate(con);
        con.Exec("INSERT INTO clips(id, filename, favorite, storage, remote_id, remote_uploaded_at) VALUES(1, 'a.mp4', 1, 'cloud', 'test-id', '2026-10-22')");
        con.Exec("INSERT INTO folders(id, name, hidden_from_home) VALUES(2, 'Prywatne', 1)");

        Db.Migrate(con);

        var clip = con.QueryOne("SELECT * FROM clips WHERE id=1")!;
        Assert.Equal(1L, clip["favorite"]);
        Assert.Equal("cloud", clip["storage"]);
        Assert.Equal("test-id", clip["remote_id"]);
        Assert.Equal("2026-10-22", clip["remote_uploaded_at"]);
        Assert.Equal(1L, con.QueryOne("SELECT hidden_from_home FROM folders WHERE id=2")!["hidden_from_home"]);
    }

    [Fact]
    public void Partially_upgraded_database_gets_only_the_missing_columns()
    {
        using var con = LegacyDatabase();
        con.Exec("ALTER TABLE clips ADD COLUMN FAVORITE INTEGER NOT NULL DEFAULT 0");
        con.Exec("INSERT INTO clips(id, filename, FAVORITE) VALUES(1, 'a.mp4', 1)");

        Db.Migrate(con);

        Assert.Equal(1, con.ScalarLong("SELECT favorite FROM clips WHERE id=1"));
        Assert.Equal("local", con.QueryOne("SELECT storage FROM clips WHERE id=1")!["storage"]);
        Assert.Equal(1, con.ScalarLong("SELECT count(*) FROM pragma_table_info('clips') WHERE lower(name)='favorite'"));
    }

    private static SqliteConnection LegacyDatabase()
    {
        var con = new SqliteConnection("Data Source=:memory:");
        con.Open();
        con.Exec("CREATE TABLE clips(id INTEGER PRIMARY KEY, filename TEXT); CREATE TABLE folders(id INTEGER PRIMARY KEY, name TEXT)");
        return con;
    }
}