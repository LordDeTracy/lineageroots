using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

internal static class Display
{
    /// <summary>Up to two initials for an avatar: given name, then surname.</summary>
    public static string Initials(Person p)
    {
        var n = p.PrimaryName;
        var g = n.Given.Trim();
        var s = n.Surname.Trim();
        var first = g.Length > 0 ? char.ToUpperInvariant(g[0]).ToString() : "";
        var last = s.Length > 0 ? char.ToUpperInvariant(s[0]).ToString() : "";
        var text = first + last;
        return text.Length > 0 ? text : "?";
    }

    public static string BirthPlace(Person p) => FirstPart(p.Birth?.Place ?? "");

    /// <summary>"Springfield, Ohio, USA" → "Springfield, Ohio" — enough to recognise a place in a list.</summary>
    public static string FirstPart(string place)
    {
        var parts = place.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 2 ? string.Join(", ", parts) : $"{parts[0]}, {parts[1]}";
    }

    private static readonly Dictionary<string, string> Words = new()
    {
        ["ABT"] = "about", ["BEF"] = "before", ["AFT"] = "after", ["CAL"] = "calculated", ["EST"] = "estimated",
        ["FROM"] = "from", ["TO"] = "to", ["BET"] = "between", ["AND"] = "and", ["INT"] = "",
        ["JAN"] = "Jan", ["FEB"] = "Feb", ["MAR"] = "Mar", ["APR"] = "Apr", ["MAY"] = "May", ["JUN"] = "Jun",
        ["JUL"] = "Jul", ["AUG"] = "Aug", ["SEP"] = "Sep", ["OCT"] = "Oct", ["NOV"] = "Nov", ["DEC"] = "Dec",
    };

    /// <summary>"ABT 12 MAR 1850" → "about 12 Mar 1850".</summary>
    public static string Date(GenDate d)
    {
        if (d.IsEmpty) return "";
        if (d.Kind == DateKind.Text) return d.Text;
        var words = d.ToGedcom().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .Select(w => Words.TryGetValue(w, out var nice) ? nice : w).Where(w => w.Length > 0);
        return string.Join(' ', words);
    }

    /// <summary>Shows how a typed date will be understood, e.g. "about 1905" or "kept as text".</summary>
    public static string DateHint(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var d = GenDate.Parse(text);
        return d.Kind == DateKind.Text ? "Not a recognised date — it will be kept as text" : "Understood as " + Date(d);
    }

    /// <summary>The colour-coding swatches (AQ's eight colours).</summary>
    public static readonly string[] Colours = { "Blue", "Green", "Red", "Yellow", "Gray", "Purple", "Orange", "Beige" };

    public static Avalonia.Media.IBrush? ColourBrush(string name) => name switch
    {
        "Blue" => Brush(0x3B82C4), "Green" => Brush(0x3F9A6B), "Red" => Brush(0xC8483F), "Yellow" => Brush(0xD9A520),
        "Gray" or "Grey" => Brush(0x8A8F96), "Purple" => Brush(0x8B5CB8), "Orange" => Brush(0xD9772B), "Beige" => Brush(0xC9B48F),
        _ => null,
    };

    private static Avalonia.Media.IBrush Brush(uint rgb) => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromUInt32(0xFF000000 | rgb));

    public static string Join(params string[] bits) => string.Join("  ·  ", bits.Where(b => b.Length > 0));
}

/// <summary>One line in the people list.</summary>
public sealed class PersonRow
{
    public PersonRow(Person p)
    {
        Id = p.Id;
        var n = p.PrimaryName;
        Name = n.Surname.Length > 0 ? $"{n.Surname}, {n.Given}".TrimEnd(',', ' ') : n.Given.Length > 0 ? n.Given : "(no name)";
        Lifespan = p.Lifespan;
        Subtitle = Display.Join(Lifespan, Display.BirthPlace(p));
        Initials = Display.Initials(p);
        IsMale = p.Sex == Sex.Male;
        IsFemale = p.Sex == Sex.Female;
        SortKey = n.SortKey;
        ColourBrush = Display.ColourBrush(p.Colour);
    }

    public long Id { get; }
    public string Name { get; }
    public string Lifespan { get; }
    public string Subtitle { get; }
    public string Initials { get; }
    public bool IsMale { get; }
    public bool IsFemale { get; }
    public string SortKey { get; }
    public bool HasSubtitle => Subtitle.Length > 0;
    /// <summary>The search found them through their notes or event descriptions, not their name.</summary>
    public bool FoundInNotes { get; set; }
    /// <summary>The person's colour-coding dot, if they have one.</summary>
    public Avalonia.Media.IBrush? ColourBrush { get; }
    public bool HasColour => ColourBrush != null;

    /// <summary>What screen readers announce for the row.</summary>
    public override string ToString() => Lifespan.Length > 0 ? $"{Name}, {Lifespan}" : Name;
}

/// <summary>A clickable person card in the family chart.</summary>
public sealed class PersonCard
{
    public PersonCard(Person p, string role = "")
    {
        Id = p.Id;
        Name = p.DisplayName.Length > 0 ? p.DisplayName : "(no name)";
        Lifespan = p.Lifespan;
        Detail = Display.Join(Lifespan, Display.BirthPlace(p));
        Initials = Display.Initials(p);
        IsMale = p.Sex == Sex.Male;
        IsFemale = p.Sex == Sex.Female;
        Role = role.ToUpperInvariant();
        // Title and the latest occupation, to tell apart relatives with the same name ("Rev.  ·  Vicar").
        var occupation = p.Events.Where(e => e.Tag == "OCCU" && (e.Value.Trim().Length > 0 || e.Description.Trim().Length > 0))
            .OrderBy(e => e.Date.SortKey).LastOrDefault();
        Calling = Display.Join(p.Title.Trim(), occupation == null ? "" : occupation.Value.Trim().Length > 0 ? occupation.Value.Trim() : occupation.Description.Trim());
    }

    public long Id { get; }
    public string Calling { get; }
    public bool HasCalling => Calling.Length > 0;
    public string Name { get; }
    public string Lifespan { get; }
    public string Detail { get; }
    public string Initials { get; }
    public bool IsMale { get; }
    public bool IsFemale { get; }
    public string Role { get; }
    public bool HasRole => Role.Length > 0;
    public bool HasDetail => Detail.Length > 0;
    public string Spoken => Display.Join(Role, Name, Lifespan).Replace("  ·  ", ", ");
}

/// <summary>An "add a relative" placeholder shown where the chart has a gap.</summary>
public sealed record AddSlot(string Label);

/// <summary>One line of the life timeline; clicking it edits <see cref="Event"/> on <see cref="Owner"/>.</summary>
public sealed record EventRow(string Label, string Year, string Date, string Age, string Place, string Detail,
                              bool HasGps, Event Event, Entity Owner)
{
    /// <summary>How well proven the fact is (set when the row is built for a page that shows it).</summary>
    public Lineage.Core.Analysis.FactEvidence? Proof { get; init; }
    public bool HasProof => Proof != null;
    public string ProofLabel => Proof?.Label ?? "";
    public string ProofTip => Proof == null ? "" : string.Join("\n", Proof.Reasons.Prepend(Proof.Summary).Concat(Proof.Tips.Select(t => "Tip: " + t)));
    public bool IsProofStrong => Proof?.Level is Lineage.Core.Analysis.ProofLevel.Strong or Lineage.Core.Analysis.ProofLevel.Proven;
    public bool IsProofGood => Proof?.Level == Lineage.Core.Analysis.ProofLevel.Good;
    public bool IsProofWeak => Proof?.Level == Lineage.Core.Analysis.ProofLevel.Weak;
    public bool IsProofConflicting => Proof?.Level == Lineage.Core.Analysis.ProofLevel.Conflicting;
    public bool HasPlace => Place.Length > 0;
    public bool HasDetail => Detail.Length > 0;
    public bool HasAge => Age.Length > 0;
    public string Spoken => Display.Join(Label, Date, Place).Replace("  ·  ", ", ");
}

/// <summary>One family the current person belongs to as a partner.</summary>
public sealed class FamilyCard
{
    public required long FamilyId { get; init; }
    public PersonCard? Spouse { get; init; }
    public string Summary { get; init; } = "";
    public List<PersonCard> Children { get; init; } = new();
    public bool HasSpouse => Spouse != null;
    public bool HasChildren => Children.Count > 0;
    public bool HasSummary => Summary.Length > 0;
    public string ChildrenHeading => Children.Count switch { 0 => "No children recorded", 1 => "1 child", var n => $"{n} children" };
}
