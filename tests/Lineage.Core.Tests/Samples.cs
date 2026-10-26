namespace Lineage.Core.Tests;

/// <summary>
/// Locates the private sample databases (an AQ .aq file plus AQ's own GEDCOM export of it).
/// They are personal family data and are never committed; tests that need them are no-ops
/// when the folder is absent. Set LINEAGE_SAMPLES to point elsewhere.
/// </summary>
public static class Samples
{
    public static string? Dir { get; } = Find();

    private static string? Find()
    {
        var env = Environment.GetEnvironmentVariable("LINEAGE_SAMPLES");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "samples", "real");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Every sample in the folder: each "&lt;name&gt;_aqexport.ged" that has a matching .aq file. Names are
    /// discovered, never written into the tests, because sample file names can be family names.
    /// When there are none, a single placeholder keeps xUnit theories valid (and they do nothing).
    /// </summary>
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        var found = 0;
        if (Dir != null)
            foreach (var ged in Directory.GetFiles(Dir, "*_aqexport.ged").Order())
            {
                var name = Path.GetFileName(ged)[..^"_aqexport.ged".Length];
                if (Get(name) == null) continue;
                data.Add(name);
                found++;
            }
        if (found == 0) data.Add("(no samples)");
        return data;
    }

    /// <summary>(.aq path, AQ's GEDCOM export path) for a sample, or null when not available.</summary>
    public static (string Aq, string AqExport)? Get(string name)
    {
        if (Dir == null) return null;
        var aq = new[] { $"{name}.snapshot.aq", $"{name}.aq" }.Select(f => Path.Combine(Dir, f)).FirstOrDefault(File.Exists);
        var ged = Path.Combine(Dir, $"{name}_aqexport.ged");
        return aq != null && File.Exists(ged) ? (aq, ged) : null;
    }
}
