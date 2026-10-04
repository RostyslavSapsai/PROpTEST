using System.Text.Json;
using Microsoft.Data.Sqlite;
using PropTest.Core;

namespace PropTest.Infrastructure;

public sealed class SqliteRunRepository : IRunRepository
{
    readonly string connectionString;
    public string DatabasePath { get; }
    public SqliteRunRepository(string path)
    {
        DatabasePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, ForeignKeys = true }.ToString();
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS runs(id TEXT PRIMARY KEY, started TEXT NOT NULL, settings TEXT NOT NULL, source TEXT NOT NULL, outcome TEXT NOT NULL, version INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS samples(run_id TEXT NOT NULL REFERENCES runs(id), seq INTEGER NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(run_id,seq));
            UPDATE runs SET outcome='Interrupted' WHERE outcome='Running';
            """;
        cmd.ExecuteNonQuery();
    }
    SqliteConnection Open() { var c = new SqliteConnection(connectionString); c.Open(); return c; }
    public void Begin(RunInfo r)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO runs VALUES($id,$time,$settings,$source,'Running',1)";
        cmd.Parameters.AddWithValue("$id", r.Id.ToString()); cmd.Parameters.AddWithValue("$time", r.StartedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$settings", JsonSerializer.Serialize(r.Settings)); cmd.Parameters.AddWithValue("$source", r.Source); cmd.ExecuteNonQuery();
    }
    public void Append(Measurement s)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "INSERT INTO samples VALUES($id,$seq,$payload)";
        cmd.Parameters.AddWithValue("$id", s.RunId.ToString()); cmd.Parameters.AddWithValue("$seq", s.Sequence);
        cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(s)); cmd.ExecuteNonQuery();
    }
    public void Finish(Guid id, string outcome)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "UPDATE runs SET outcome=$outcome WHERE id=$id";
        cmd.Parameters.AddWithValue("$outcome", outcome); cmd.Parameters.AddWithValue("$id", id.ToString()); cmd.ExecuteNonQuery();
    }
    public IReadOnlyList<RunInfo> List()
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT r.id,started,settings,source,outcome,(SELECT count(*) FROM samples s WHERE s.run_id=r.id),version FROM runs r ORDER BY started DESC";
        using var reader = cmd.ExecuteReader(); var result = new List<RunInfo>();
        while (reader.Read())
        {
            if (reader.GetInt32(6) != 1) throw new InvalidDataException("Непідтримувана версія запису.");
            result.Add(new(Guid.Parse(reader.GetString(0)), DateTimeOffset.Parse(reader.GetString(1)), JsonSerializer.Deserialize<RunSettings>(reader.GetString(2))!, reader.GetString(3), reader.GetString(4), reader.GetInt32(5)));
        }
        return result;
    }
    public RunData Load(Guid id)
    {
        var info = List().Single(r => r.Id == id);
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT payload FROM samples WHERE run_id=$id ORDER BY seq";
        cmd.Parameters.AddWithValue("$id", id.ToString()); using var reader = cmd.ExecuteReader(); var samples = new List<Measurement>();
        while (reader.Read()) samples.Add(JsonSerializer.Deserialize<Measurement>(reader.GetString(0))!);
        return new(info, samples);
    }
}
