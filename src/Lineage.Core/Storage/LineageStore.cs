using System.Text.Json;
using System.Text.Json.Serialization;
using Lineage.Core.Model;
using Microsoft.Data.Sqlite;

namespace Lineage.Core.Storage;

/// <summary>
/// The native .lineage file: a SQLite database. Each record type has its own table with
/// indexed columns for what people search and sort on (names, years); nested detail (events,
/// names, citations, media) is stored as JSON in the row. SQLite gives transactional saves,
/// so there is no "check/repair" step like AQ's linked-list file needs.
/// </summary>
public static class LineageStore
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() },
        // Model collections are get-only, so they must be populated in place. Only collections:
        // populating a value object (e.g. a GenDate default) would mutate the shared instance.
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                info =>
                {
                    foreach (var p in info.Properties)
                        if (p.Set == null && typeof(System.Collections.IList).IsAssignableFrom(p.PropertyType))
                            p.ObjectCreationHandling = JsonObjectCreationHandling.Populate;
                },
            },
        },
    };

    private const string Schema = """
        PRAGMA journal_mode = WAL;
        CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS person (
            id INTEGER PRIMARY KEY, uid TEXT NOT NULL, surname TEXT, given TEXT,
            sex TEXT, birth_year INTEGER, death_year INTEGER, data TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS person_name ON person (surname COLLATE NOCASE, given COLLATE NOCASE);
        CREATE TABLE IF NOT EXISTS family (
            id INTEGER PRIMARY KEY, uid TEXT NOT NULL, partner1 INTEGER, partner2 INTEGER, data TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS family_p1 ON family (partner1);
        CREATE INDEX IF NOT EXISTS family_p2 ON family (partner2);
        CREATE TABLE IF NOT EXISTS source (id INTEGER PRIMARY KEY, uid TEXT NOT NULL, title TEXT, data TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS repository (id INTEGER PRIMARY KEY, uid TEXT NOT NULL, name TEXT, data TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS research (id INTEGER PRIMARY KEY, uid TEXT NOT NULL, data TEXT NOT NULL);
        """;

    /// <summary>One record as the JSON stored in its row (also used for undo snapshots).</summary>
    public static string ToJson<T>(T entity) where T : Entity => JsonSerializer.Serialize(entity, Json);
    public static T FromJson<T>(string json) where T : Entity => JsonSerializer.Deserialize<T>(json, Json)!;

    public static void Save(FamilyTree tree, string path)
    {
        var tmp = path + ".saving";
        if (File.Exists(tmp)) Retry(() => File.Delete(tmp));
        using (var db = Open(tmp))
        {
            using var tx = db.BeginTransaction();
            Exec(db, tx, "INSERT INTO meta VALUES ('format', $v), ('name', $n), ('not_duplicates', $nd), ('home', $h), ('bookmarks', $b)",
                 ("$v", FormatVersion.ToString()), ("$n", tree.Name),
                 ("$nd", JsonSerializer.Serialize(tree.NotDuplicates.Order().ToArray())),
                 ("$h", tree.HomePersonId?.ToString() ?? ""),
                 ("$b", JsonSerializer.Serialize(tree.Bookmarks)));
            Bulk(db, tx, "INSERT INTO person VALUES ($id,$uid,$s,$g,$sex,$by,$dy,$d)", tree.People.Values, p => new object?[]
            {
                p.Id, p.Uid.ToString(), p.PrimaryName.Surname, p.PrimaryName.Given, p.Sex.ToString(),
                p.Birth?.Date.Year is > 0 and var by ? by : null, p.Death?.Date.Year is > 0 and var dy ? dy : null,
                JsonSerializer.Serialize(p, Json),
            });
            Bulk(db, tx, "INSERT INTO family VALUES ($id,$uid,$a,$b,$d)", tree.Families.Values, f => new object?[]
                { f.Id, f.Uid.ToString(), f.Partner1Id, f.Partner2Id, JsonSerializer.Serialize(f, Json) });
            Bulk(db, tx, "INSERT INTO source VALUES ($id,$uid,$t,$d)", tree.Sources.Values, s => new object?[]
                { s.Id, s.Uid.ToString(), s.Title, JsonSerializer.Serialize(s, Json) });
            Bulk(db, tx, "INSERT INTO repository VALUES ($id,$uid,$n,$d)", tree.Repositories.Values, r => new object?[]
                { r.Id, r.Uid.ToString(), r.Name, JsonSerializer.Serialize(r, Json) });
            Bulk(db, tx, "INSERT INTO research VALUES ($id,$uid,$d)", tree.Research.Values, r => new object?[]
                { r.Id, r.Uid.ToString(), JsonSerializer.Serialize(r, Json) });
            tx.Commit();
            Exec(db, null, "PRAGMA journal_mode = DELETE");
        }
        SqliteConnection.ClearAllPools();
        // Never leaves a half-written file behind. A virus scanner or backup tool may hold the new file
        // for a moment right after it is written, so try a few times before giving up.
        Retry(() => File.Move(tmp, path, overwrite: true));
    }

    private static void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { action(); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 8)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    public static FamilyTree Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("No such Lineage Roots file", path);
        using var db = Open(path, readOnly: true);
        var tree = new FamilyTree();
        var format = Scalar(db, "SELECT value FROM meta WHERE key='format'");
        if (format is null || int.Parse(format) > FormatVersion)
            throw new InvalidDataException($"{path} is not a Lineage Roots file this version can read (format {format}).");
        tree.Name = Scalar(db, "SELECT value FROM meta WHERE key='name'") ?? tree.Name;
        if (Scalar(db, "SELECT value FROM meta WHERE key='not_duplicates'") is { } nd)
            tree.NotDuplicates.UnionWith(JsonSerializer.Deserialize<string[]>(nd) ?? Array.Empty<string>());
        if (long.TryParse(Scalar(db, "SELECT value FROM meta WHERE key='home'"), out var home)) tree.HomePersonId = home;
        if (Scalar(db, "SELECT value FROM meta WHERE key='bookmarks'") is { Length: > 0 } bm)
            tree.Bookmarks.AddRange(JsonSerializer.Deserialize<long[]>(bm) ?? Array.Empty<long>());
        foreach (var p in Rows<Person>(db, "person")) tree.AddPerson(p);
        foreach (var f in Rows<Family>(db, "family")) tree.AddFamily(f);
        foreach (var s in Rows<Source>(db, "source")) tree.AddSource(s);
        foreach (var r in Rows<Repository>(db, "repository")) tree.AddRepository(r);
        foreach (var r in Rows<ResearchItem>(db, "research")) tree.AddResearch(r);
        return tree;
    }

    private static SqliteConnection Open(string path, bool readOnly = false)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
        var db = new SqliteConnection(cs);
        db.Open();
        if (!readOnly) Exec(db, null, Schema);
        return db;
    }

    private static IEnumerable<T> Rows<T>(SqliteConnection db, string table)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT data FROM {table} ORDER BY id";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            yield return JsonSerializer.Deserialize<T>(r.GetString(0), Json)!;
    }

    private static void Bulk<T>(SqliteConnection db, SqliteTransaction tx, string sql, IEnumerable<T> items,
                                Func<T, object?[]> values)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        var names = System.Text.RegularExpressions.Regex.Matches(sql, @"\$\w+").Select(m => m.Value).ToArray();
        var ps = names.Select(n => cmd.Parameters.Add(new SqliteParameter { ParameterName = n })).ToArray();
        cmd.Prepare();
        foreach (var item in items)
        {
            var v = values(item);
            for (var i = 0; i < ps.Length; i++) ps[i].Value = v[i] ?? DBNull.Value;
            cmd.ExecuteNonQuery();
        }
    }

    private static void Exec(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object)[] args)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private static string? Scalar(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        try { return cmd.ExecuteScalar() as string; }
        catch (SqliteException) { return null; }
    }
}
