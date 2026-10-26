using System.Text.Json;
using Lineage.Core.Model;

namespace Lineage.Core.FamilySearch;

public sealed record FsFact(string Tag, string Date, string Place);

/// <summary>A person from the FamilySearch Family Tree, reduced to what Lineage Roots uses.</summary>
public sealed record FsPerson(string Id, string Given, string Surname, string FullName, Sex Sex, bool Living,
                              IReadOnlyList<FsFact> Facts, string Lifespan, int? AscendancyNumber, double? Score = null)
{
    public FsFact? Fact(string tag) => Facts.FirstOrDefault(f => f.Tag == tag);
    public string Url => FamilySearchApi.PersonPageUrl(Id);
}

/// <summary>Reads the GEDCOM X JSON that the FamilySearch API returns.</summary>
public static class GedcomX
{
    private static readonly Dictionary<string, string> FactTags = new()
    {
        ["Birth"] = "BIRT", ["Christening"] = "CHR", ["Baptism"] = "BAPM", ["Death"] = "DEAT", ["Burial"] = "BURI",
        ["Cremation"] = "CREM", ["Marriage"] = "MARR", ["Residence"] = "RESI", ["Occupation"] = "OCCU",
        ["Immigration"] = "IMMI", ["Emigration"] = "EMIG", ["Naturalization"] = "NATU", ["Census"] = "CENS",
        ["MilitaryService"] = "MILI", ["Religion"] = "RELI", ["Education"] = "EDUC",
    };

    /// <summary>Persons from a response such as /platform/tree/persons/{id} or /platform/tree/ancestry.</summary>
    public static List<FsPerson> Persons(JsonElement root) =>
        root.TryGetProperty("persons", out var persons) ? persons.EnumerateArray().Select(Person).ToList() : new();

    /// <summary>Results of /platform/tree/search (an Atom feed whose entries each hold a GEDCOM X document).</summary>
    public static List<FsPerson> SearchResults(JsonElement root)
    {
        var results = new List<FsPerson>();
        if (!root.TryGetProperty("entries", out var entries)) return results;
        foreach (var entry in entries.EnumerateArray())
        {
            var id = Str(entry, "id");
            double? score = entry.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : null;
            if (!entry.TryGetProperty("content", out var content) || !content.TryGetProperty("gedcomx", out var gx)) continue;
            var people = Persons(gx);
            var match = people.FirstOrDefault(p => p.Id == id) ?? people.FirstOrDefault();
            if (match != null) results.Add(match with { Score = score });
        }
        return results;
    }

    public static FsPerson Person(JsonElement p)
    {
        var id = Str(p, "id");
        string given = "", surname = "", full = "";
        if (p.TryGetProperty("names", out var names) && names.GetArrayLength() > 0)
        {
            // The preferred name, or else the first.
            var name = names.EnumerateArray().FirstOrDefault(n => n.TryGetProperty("preferred", out var pr) && pr.ValueKind == JsonValueKind.True);
            if (name.ValueKind == JsonValueKind.Undefined) name = names[0];
            if (name.TryGetProperty("nameForms", out var forms) && forms.GetArrayLength() > 0)
            {
                var form = forms[0];
                full = Str(form, "fullText");
                if (form.TryGetProperty("parts", out var parts))
                    foreach (var part in parts.EnumerateArray())
                    {
                        var type = Str(part, "type");
                        if (type.EndsWith("/Given")) given = Str(part, "value");
                        else if (type.EndsWith("/Surname")) surname = Str(part, "value");
                    }
            }
        }
        var display = p.TryGetProperty("display", out var d) ? d : default;
        if (full.Length == 0 && display.ValueKind == JsonValueKind.Object) full = Str(display, "name");
        if (given.Length == 0 && surname.Length == 0) (given, surname) = SplitName(full);

        var sexText = p.TryGetProperty("gender", out var g) ? Str(g, "type") : display.ValueKind == JsonValueKind.Object ? Str(display, "gender") : "";
        var sex = sexText.EndsWith("Male", StringComparison.OrdinalIgnoreCase) && !sexText.EndsWith("Female", StringComparison.OrdinalIgnoreCase) ? Sex.Male
                : sexText.EndsWith("Female", StringComparison.OrdinalIgnoreCase) ? Sex.Female : Sex.Unknown;

        var facts = new List<FsFact>();
        if (p.TryGetProperty("facts", out var fs))
            foreach (var f in fs.EnumerateArray())
            {
                var type = Str(f, "type");
                var key = type[(type.LastIndexOf('/') + 1)..];
                if (!FactTags.TryGetValue(key, out var tag)) continue;
                var date = f.TryGetProperty("date", out var dt) ? First(Str(dt, "original"), Str(dt, "formal").TrimStart('+')) : "";
                var place = f.TryGetProperty("place", out var pl) ? Str(pl, "original") : "";
                facts.Add(new FsFact(tag, date, place));
            }
        // Older or summary responses only carry the "display" block.
        if (display.ValueKind == JsonValueKind.Object)
        {
            if (!facts.Any(f => f.Tag == "BIRT") && (Str(display, "birthDate").Length > 0 || Str(display, "birthPlace").Length > 0))
                facts.Add(new FsFact("BIRT", Str(display, "birthDate"), Str(display, "birthPlace")));
            if (!facts.Any(f => f.Tag == "DEAT") && (Str(display, "deathDate").Length > 0 || Str(display, "deathPlace").Length > 0))
                facts.Add(new FsFact("DEAT", Str(display, "deathDate"), Str(display, "deathPlace")));
        }
        int? asc = display.ValueKind == JsonValueKind.Object && int.TryParse(Str(display, "ascendancyNumber"), out var n) ? n : null;
        var living = p.TryGetProperty("living", out var lv) && lv.ValueKind == JsonValueKind.True;
        var lifespan = display.ValueKind == JsonValueKind.Object ? Str(display, "lifespan") : "";
        return new FsPerson(id, given, surname, full, sex, living, facts, lifespan, asc);
    }

    private static (string, string) SplitName(string full)
    {
        var words = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= 1 ? (full, "") : (string.Join(' ', words[..^1]), words[^1]);
    }

    private static string First(string a, string b) => a.Length > 0 ? a : b;

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
