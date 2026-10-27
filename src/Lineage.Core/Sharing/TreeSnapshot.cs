using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Lineage.Core.Model;
using Lineage.Core.Storage;

namespace Lineage.Core.Sharing;

/// <summary>
/// A whole tree as one compressed string (people, families, sources and repositories, in the file's JSON form),
/// kept with a share as the record of exactly what was sent.
/// </summary>
public static class TreeSnapshot
{
    private sealed class Dto
    {
        public string Name { get; set; } = "";
        public List<string> People { get; set; } = new();
        public List<string> Families { get; set; } = new();
        public List<string> Sources { get; set; } = new();
        public List<string> Repositories { get; set; } = new();
    }

    public static string Save(FamilyTree tree)
    {
        var dto = new Dto
        {
            Name = tree.Name,
            People = tree.People.Values.Select(LineageStore.ToJson).ToList(),
            Families = tree.Families.Values.Select(LineageStore.ToJson).ToList(),
            Sources = tree.Sources.Values.Select(LineageStore.ToJson).ToList(),
            Repositories = tree.Repositories.Values.Select(LineageStore.ToJson).ToList(),
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto));
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize)) gzip.Write(bytes);
        return Convert.ToBase64String(output.ToArray());
    }

    public static FamilyTree Load(string snapshot)
    {
        using var input = new GZipStream(new MemoryStream(Convert.FromBase64String(snapshot)), CompressionMode.Decompress);
        using var reader = new StreamReader(input, Encoding.UTF8);
        var dto = JsonSerializer.Deserialize<Dto>(reader.ReadToEnd()) ?? new Dto();
        var tree = new FamilyTree { Name = dto.Name };
        foreach (var j in dto.People) tree.AddPerson(LineageStore.FromJson<Person>(j));
        foreach (var j in dto.Families) tree.AddFamily(LineageStore.FromJson<Family>(j));
        foreach (var j in dto.Sources) tree.AddSource(LineageStore.FromJson<Source>(j));
        foreach (var j in dto.Repositories) tree.AddRepository(LineageStore.FromJson<Repository>(j));
        return tree;
    }
}
