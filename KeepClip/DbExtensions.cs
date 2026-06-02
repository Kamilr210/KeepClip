using Microsoft.Data.Sqlite;

namespace KeepClip;

/// <summary>
/// Tiny ergonomic helpers over <see cref="SqliteConnection"/> that mirror the way
/// the Python code uses <c>con.execute(...).fetchall()/fetchone()</c> and returns
/// <c>dict(row)</c>. Rows come back as ordered dictionaries keyed by column name
/// with DBNull mapped to null, so they serialize to the exact same JSON shapes.
/// </summary>
public static class DbExtensions
{
    public static SqliteCommand Cmd(this SqliteConnection con, string sql,
                                    params (string name, object? val)[] ps)
    {
        var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, val) in ps)
            cmd.Parameters.AddWithValue(name, val ?? DBNull.Value);
        return cmd;
    }

    public static List<Dictionary<string, object?>> Query(this SqliteConnection con, string sql,
                                                          params (string, object?)[] ps)
    {
        using var cmd = con.Cmd(sql, ps);
        using var r = cmd.ExecuteReader();
        var list = new List<Dictionary<string, object?>>();
        while (r.Read())
        {
            var d = new Dictionary<string, object?>(r.FieldCount);
            for (int i = 0; i < r.FieldCount; i++)
                d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            list.Add(d);
        }
        return list;
    }

    public static Dictionary<string, object?>? QueryOne(this SqliteConnection con, string sql,
                                                        params (string, object?)[] ps)
    {
        var rows = con.Query(sql, ps);
        return rows.Count > 0 ? rows[0] : null;
    }

    public static object? Scalar(this SqliteConnection con, string sql, params (string, object?)[] ps)
    {
        using var cmd = con.Cmd(sql, ps);
        var v = cmd.ExecuteScalar();
        return v is DBNull ? null : v;
    }

    public static long ScalarLong(this SqliteConnection con, string sql, params (string, object?)[] ps)
        => Convert.ToInt64(con.Scalar(sql, ps) ?? 0L);

    public static int Exec(this SqliteConnection con, string sql, params (string, object?)[] ps)
    {
        using var cmd = con.Cmd(sql, ps);
        return cmd.ExecuteNonQuery();
    }
}
