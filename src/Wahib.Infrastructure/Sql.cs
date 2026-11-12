using Microsoft.Data.Sqlite;
namespace Wahib.Infrastructure;

internal static class Sql
{
    public static SqliteConnection Connect(string path, bool readOnly = false)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 10
        };
        var db = new SqliteConnection(builder.ToString());
        db.Open();
        Exec(db,null,"PRAGMA foreign_keys=ON;");
        Exec(db,null,"PRAGMA busy_timeout=10000;");
        if (!readOnly) Exec(db,null,"PRAGMA journal_mode=WAL;");
        return db;
    }
    public static int Exec(SqliteConnection db, SqliteTransaction? tx, string sql, params (string,object?)[] p)
    {
        using var cmd = Make(db,tx,sql,p);
        return cmd.ExecuteNonQuery();
    }
    public static object? Scalar(SqliteConnection db,SqliteTransaction? tx,string sql, params (string,object?)[] p)
    {
        using var cmd = Make(db,tx,sql,p);
        var v = cmd.ExecuteScalar();
        return v == DBNull.Value ? null : v;
    }
    public static long Number(SqliteConnection db,SqliteTransaction? tx,string sql, params (string,object?)[] p) =>
        Convert.ToInt64(Scalar(db,tx,sql,p) ?? 0L);
    public static string Text(SqliteConnection db,SqliteTransaction? tx,string sql, params (string,object?)[] p) =>
        Convert.ToString(Scalar(db,tx,sql,p)) ?? "";
    public static List<DbRow> Query(SqliteConnection db,SqliteTransaction? tx,string sql, params (string,object?)[] p)
    {
        using var cmd = Make(db,tx,sql,p);
        using var reader=cmd.ExecuteReader();
        var result = new List<DbRow>();
        while(reader.Read())
        {
            var fields=new Dictionary<string,object?>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<reader.FieldCount;i++) fields[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);
            result.Add(new DbRow(fields));
        }
        return result;
    }
    private static SqliteCommand Make(SqliteConnection db,SqliteTransaction? tx,string sql,(string,object?)[] p)
    {
        var cmd=db.CreateCommand(); cmd.CommandText=sql; cmd.Transaction=tx;
        foreach(var (name,val) in p) cmd.Parameters.AddWithValue(name, val ?? DBNull.Value);
        return cmd;
    }
}
internal sealed class DbRow(Dictionary<string,object?> cells)
{
    public string Str(string key) => Convert.ToString(cells[key]) ?? "";
    public long Long(string key) => Convert.ToInt64(cells[key] ?? 0);
    public long? NullableLong(string key) => cells[key] is null ? null : Long(key);
    public bool Bool(string key) => Long(key)!=0;
}
