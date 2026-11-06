using System.Text.Json;
using System.Text.Json.Serialization;
using Lineage.Core.Model;
using Microsoft.Data.Sqlite;

namespace Lineage.Core.Storage;

/// <summary>
/// The native .lineage file: a SQLite database. Each record type has its own table with
/// indexed columns for what people search and sort on (names, years); nested detail (events,
/// names, citations, media) is stored as JSON in the row. SQLite gives transactional saves,
/// so there is no "check/repair" step like AQ's linked-list file needs. A password-protected file
/// holds the same rows encrypted as one block instead (see <see cref="FileLock"/>).
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
        CREATE TABLE IF NOT EXISTS change (id INTEGER PRIMARY KEY, data TEXT NOT NULL);
        """;

    /// <summary>One record as the JSON stored in its row (also used for undo snapshots).</summary>
    public static string ToJson<T>(T entity) where T : Entity => JsonSerializer.Serialize(entity, Json);
    public static T FromJson<T>(string json) where T : Entity => JsonSerializer.Deserialize<T>(json, Json)!;

    /// <summary>A deep copy of any model object (an event, a citation…) through the same JSON form as the file.</summary>
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;

    public static void Save(FamilyTree tree, string path)
    {
        var tmp = path + ".saving";
        if (File.Exists(tmp)) Retry(() => File.Delete(tmp));
        if (tree.Lock is { } fileLock) File.WriteAllBytes(tmp, fileLock.Seal(Pack(tree)));
        else
        {
            using var db = Open(tmp);
            using var tx = db.BeginTransaction();
            foreach (var (key, value) in Meta(tree))
                Exec(db, tx, "INSERT INTO meta VALUES ($k, $v)", ("$k", key), ("$v", value));
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
            Bulk(db, tx, "INSERT INTO change (data) VALUES ($d)", tree.History, c => new object?[]
                { JsonSerializer.Serialize(c, Json) });
            tx.Commit();
            Exec(db, null, "PRAGMA journal_mode = DELETE");
        }
        SqliteConnection.ClearAllPools();
        // Never leaves a half-written file behind. A virus scanner or backup tool may hold the new file
        // for a moment right after it is written, so try a few times before giving up.
        Retry(() => File.Move(tmp, path, overwrite: true));
    }

    /// <summary>The tree-wide settings kept beside the records (the meta table).</summary>
    private static IEnumerable<(string Key, string Value)> Meta(FamilyTree tree)
    {
        yield return ("format", FormatVersion.ToString());
        yield return ("name", tree.Name);
        yield return ("not_duplicates", JsonSerializer.Serialize(tree.NotDuplicates.OrderBy(x => x).ToArray()));
        yield return ("home", tree.HomePersonId?.ToString() ?? "");
        yield return ("bookmarks", JsonSerializer.Serialize(tree.Bookmarks));
        yield return ("shares", JsonSerializer.Serialize(tree.Shares));
        yield return ("projects", tree.Projects.Count == 0 ? "" : JsonSerializer.Serialize(tree.Projects, Json));
        yield return ("shared_from", tree.SharedFrom is { } origin ? JsonSerializer.Serialize(origin) : "");
    }

    /// <summary>What a locked file encrypts: the same rows as the database, as compressed JSON.</summary>
    private sealed class Packed
    {
        public Dictionary<string, string> Meta { get; set; } = new();
        public List<string> Person { get; set; } = new();
        public List<string> Family { get; set; } = new();
        public List<string> Source { get; set; } = new();
        public List<string> Repository { get; set; } = new();
        public List<string> Research { get; set; } = new();
        public List<string> Change { get; set; } = new();
    }

    private static byte[] Pack(FamilyTree tree)
    {
        var packed = new Packed
        {
            Meta = Meta(tree).ToDictionary(m => m.Key, m => m.Value),
            Person = tree.People.Values.Select(p => JsonSerializer.Serialize(p, Json)).ToList(),
            Family = tree.Families.Values.Select(f => JsonSerializer.Serialize(f, Json)).ToList(),
            Source = tree.Sources.Values.Select(s => JsonSerializer.Serialize(s, Json)).ToList(),
            Repository = tree.Repositories.Values.Select(r => JsonSerializer.Serialize(r, Json)).ToList(),
            Research = tree.Research.Values.Select(r => JsonSerializer.Serialize(r, Json)).ToList(),
            Change = tree.History.Select(c => JsonSerializer.Serialize(c, Json)).ToList(),
        };
        using var output = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Fastest))
            JsonSerializer.Serialize(gzip, packed);
        return output.ToArray();
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

    /// <summary>
    /// Reads a .lineage file. A password-protected one needs <paramref name="password"/>
    /// (<see cref="PasswordNeededException"/> without it, <see cref="WrongPasswordException"/> when it's wrong);
    /// the tree then keeps the lock, so saving it keeps the password.
    /// </summary>
    public static FamilyTree Load(string path, string? password = null)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("No such Lineage Roots file", path);
        if (FileLock.IsLocked(path))
        {
            if (password == null) throw new PasswordNeededException();
            var (fileLock, plain) = FileLock.Open(File.ReadAllBytes(path), password);
            using var input = new System.IO.Compression.GZipStream(new MemoryStream(plain), System.IO.Compression.CompressionMode.Decompress);
            var packed = JsonSerializer.Deserialize<Packed>(input) ?? throw new InvalidDataException($"{path} is damaged.");
            var locked = Build(path, k => packed.Meta.TryGetValue(k, out var v) ? v : null,
                               packed.Person, packed.Family, packed.Source, packed.Repository, packed.Research, packed.Change);
            locked.Lock = fileLock;
            return locked;
        }
        using var db = Open(path, readOnly: true);
        return Build(path, k => Scalar(db, $"SELECT value FROM meta WHERE key='{k}'"),
                     Rows(db, "person"), Rows(db, "family"), Rows(db, "source"), Rows(db, "repository"), Rows(db, "research"),
                     HasTable(db, "change") ? Rows(db, "change") : Enumerable.Empty<string>());
    }

    private static FamilyTree Build(string path, Func<string, string?> meta, IEnumerable<string> people, IEnumerable<string> families,
                                    IEnumerable<string> sources, IEnumerable<string> repositories, IEnumerable<string> research,
                                    IEnumerable<string> changes)
    {
        var tree = new FamilyTree();
        var format = meta("format");
        if (format is null || int.Parse(format) > FormatVersion)
            throw new InvalidDataException($"{path} is not a Lineage Roots file this version can read (format {format}).");
        tree.Name = meta("name") ?? tree.Name;
        if (meta("not_duplicates") is { Length: > 0 } nd)
            tree.NotDuplicates.UnionWith(JsonSerializer.Deserialize<string[]>(nd) ?? Array.Empty<string>());
        if (long.TryParse(meta("home"), out var home)) tree.HomePersonId = home;
        if (meta("bookmarks") is { Length: > 0 } bm)
            tree.Bookmarks.AddRange(JsonSerializer.Deserialize<long[]>(bm) ?? Array.Empty<long>());
        if (meta("shares") is { Length: > 0 } sh)
            tree.Shares.AddRange(JsonSerializer.Deserialize<List<ShareRecord>>(sh) ?? new());
        if (meta("projects") is { Length: > 0 } pj)
            tree.Projects.AddRange(JsonSerializer.Deserialize<List<ClientProject>>(pj, Json) ?? new());
        if (meta("shared_from") is { Length: > 0 } sf)
            tree.SharedFrom = JsonSerializer.Deserialize<ShareOrigin>(sf);
        foreach (var j in people) tree.AddPerson(JsonSerializer.Deserialize<Person>(j, Json)!);
        foreach (var j in families) tree.AddFamily(JsonSerializer.Deserialize<Family>(j, Json)!);
        foreach (var j in sources) tree.AddSource(JsonSerializer.Deserialize<Source>(j, Json)!);
        foreach (var j in repositories) tree.AddRepository(JsonSerializer.Deserialize<Repository>(j, Json)!);
        foreach (var j in research) tree.AddResearch(JsonSerializer.Deserialize<ResearchItem>(j, Json)!);
        foreach (var j in changes) tree.History.Add(JsonSerializer.Deserialize<ChangeRecord>(j, Json)!);
        return tree;
    }

    private static bool HasTable(SqliteConnection db, string table)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n";
        cmd.Parameters.AddWithValue("$n", table);
        return cmd.ExecuteScalar() != null;
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

    private static IEnumerable<string> Rows(SqliteConnection db, string table)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT data FROM {table} ORDER BY id";
        using var r = cmd.ExecuteReader();
        while (r.Read()) yield return r.GetString(0);
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
