using System.Text.Json.Serialization;

namespace Lineage.Core.Model;

public enum Sex { Unknown, Male, Female }

/// <summary>How a child relates to one parent.</summary>
public enum ParentRelation { Birth, Adopted, Step, Foster, Guardian, Sealing }

public enum RelationStatus { Verified, Challenged, Disproved }

public enum CoupleStatus { Unspecified, Married, Divorced, Unmarried }

/// <summary>Base for every stored record. Ids are stable within a tree and persist to storage.</summary>
public abstract class Entity
{
    public long Id { get; set; }
    /// <summary>Global unique id (GEDCOM _UID); generated when missing.</summary>
    public Guid Uid { get; set; } = Guid.NewGuid();
}

/// <summary>What kind of record a source is (evidence analysis): the original, a copy or index of it, or someone's compiled work.</summary>
public enum SourceKind { Unknown, Original, Derivative, Authored }

/// <summary>Whether the informant knew the fact first-hand (primary) or second-hand (secondary).</summary>
public enum InformationKind { Unknown, Primary, Secondary }

/// <summary>Whether the citation answers the question outright (direct), only together with other facts (indirect), or by an absence (negative).</summary>
public enum EvidenceKind { Unknown, Direct, Indirect, Negative }

/// <summary>The researcher's conclusion about a fact.</summary>
public enum FactConclusion { NotDecided, Proven, Disproved }

public sealed class Citation
{
    public long SourceId { get; set; }
    public string Page { get; set; } = "";
    public string Text { get; set; } = "";
    public string Note { get; set; } = "";
    public int? Quality { get; set; }   // GEDCOM QUAY 0-3
    public InformationKind Information { get; set; }
    public EvidenceKind Evidence { get; set; }
}

/// <summary>
/// A record that disagrees with a fact ("the 1851 census gives her age as 30, so born about 1821"), and how the
/// disagreement was settled. Open conflicts count against the fact until resolved.
/// </summary>
public sealed class EvidenceConflict
{
    public string Claim { get; set; } = "";
    public long? SourceId { get; set; }
    public string Page { get; set; } = "";
    public string Resolution { get; set; } = "";
    public bool Resolved { get; set; }
}

/// <summary>
/// Any fact or event: BIRT, DEAT, MARR, OCCU, RESI, LDS ordinances (BAPL, ENDL, SLGC, ...) or a
/// custom EVEN with a <see cref="TypeName"/>. One general list replaces AQ's fixed slots.
/// </summary>
public sealed class Event
{
    /// <summary>Permanent identity, so the same fact can be recognised in a copy of the tree (when merging a relative's changes).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tag { get; set; } = "EVEN";
    public string TypeName { get; set; } = "";     // for EVEN / custom types
    public string Value { get; set; } = "";        // e.g. occupation text, "Y" for DIV
    public GenDate Date { get; set; } = GenDate.Empty;
    public string Place { get; set; } = "";
    public string Place2 { get; set; } = "";       // "to" place of a period event
    public GeoPoint? Location { get; set; }        // GPS of Place (GEDCOM PLAC.MAP)
    public string Description { get; set; } = "";
    public string Cause { get; set; } = "";
    public string Temple { get; set; } = "";       // LDS ordinances
    public string Status { get; set; } = "";       // LDS ordinance status (COMPLETED, ...)
    public string Note { get; set; } = "";
    public bool Private { get; set; }
    public List<Citation> Citations { get; } = new();
    /// <summary>Photos and scans of this event: a wedding photo, a certificate, a gravestone.</summary>
    public List<MediaLink> Media { get; } = new();
    /// <summary>The researcher's verdict on this fact, and the reasoning behind it (the proof argument).</summary>
    public FactConclusion Conclusion { get; set; }
    public string Reasoning { get; set; } = "";
    /// <summary>Records that disagree with this fact.</summary>
    public List<EvidenceConflict> Conflicts { get; } = new();

    public static Event Of(string tag, GenDate? date = null, string place = "") =>
        new() { Tag = tag, Date = date ?? GenDate.Empty, Place = place };
}

public sealed class Name
{
    public string Given { get; set; } = "";
    public string Surname { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Suffix { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Type { get; set; } = "";   // "", "aka", "married", ...

    [JsonIgnore] public string Full => string.Join(' ', new[] { Prefix, Given, Surname, Suffix }.Where(s => s.Length > 0));
    [JsonIgnore] public string SortKey => $"{Surname}, {Given}".ToUpperInvariant();
    public override string ToString() => Full;
}

public sealed class MediaLink
{
    public string File { get; set; } = "";
    public string Title { get; set; } = "";
    public string Format { get; set; } = "";
    public string MediaType { get; set; } = "";   // PHOTO, SOUND, VIDEO, DOCUMENT, OTHER
    public string Note { get; set; } = "";
    public bool Primary { get; set; }              // the person's profile photo
    public GeoPoint? Location { get; set; }        // where it was taken (from the camera, or typed)
    public string Taken { get; set; } = "";        // when it was taken, as recorded by the camera
    /// <summary>File size in bytes when it was linked (0 if unknown); helps find the file again after it moves.</summary>
    public long Size { get; set; }
    /// <summary>People tagged in the photo, each with the rectangle around them.</summary>
    public List<PhotoRegion> Regions { get; } = new();

    /// <summary>A deep copy, so an editor can change it and the caller decides whether to keep the changes.</summary>
    public MediaLink Clone()
    {
        var copy = new MediaLink();
        copy.CopyFrom(this);
        return copy;
    }

    /// <summary>Overwrites this link with a deep copy of <paramref name="other"/>, keeping this object in its list.</summary>
    public void CopyFrom(MediaLink other)
    {
        File = other.File;
        Title = other.Title;
        Format = other.Format;
        MediaType = other.MediaType;
        Note = other.Note;
        Primary = other.Primary;
        Location = other.Location;
        Taken = other.Taken;
        Size = other.Size;
        Regions.Clear();
        Regions.AddRange(other.Regions.Select(r => r.Clone()));
    }
}

public sealed class DnaTest
{
    public string Kind { get; set; } = "";        // Y-DNA, mtDNA, autosomal
    public string Company { get; set; } = "";
    public string Reference { get; set; } = "";
    public DateOnly? Date { get; set; }
    public string Haplogroup { get; set; } = "";
    public bool HaplogroupEstimated { get; set; }
    public List<KeyValuePair<string, int>> Markers { get; } = new();
    public string Note { get; set; } = "";
}

/// <summary>
/// Someone the person shares DNA with, as listed by a testing company. <see cref="PersonId"/> links the match
/// to someone in this tree once they are identified. <see cref="SharedWith"/> holds the <see cref="Id"/>s of
/// the person's other matches that this match also shares DNA with ("shared matches"), for clustering.
/// </summary>
public sealed class DnaMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public long? PersonId { get; set; }
    public double SharedCm { get; set; }
    public int Segments { get; set; }
    public double LongestCm { get; set; }
    public string Company { get; set; } = "";
    public string Reference { get; set; } = "";   // the company's ID for the match, so a new download updates it
    public string Note { get; set; } = "";
    public List<Guid> SharedWith { get; } = new();
}

public sealed class Person : Entity
{
    public List<DnaTest> DnaTests { get; } = new();
    /// <summary>People this person shares DNA with (when this person has tested).</summary>
    public List<DnaMatch> DnaMatches { get; } = new();
    public List<Name> Names { get; } = new();
    public Sex Sex { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";   // physical description
    public string UserId { get; set; } = "";        // REFN
    public string FamilySearchId { get; set; } = "";
    public string Note { get; set; } = "";
    public string Email { get; set; } = "";
    public string Url { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Colour { get; set; } = "";
    public List<string> Tags { get; } = new();
    public List<Event> Events { get; } = new();
    public List<Citation> Citations { get; } = new();
    public List<MediaLink> Media { get; } = new();
    public List<long> ResearchIds { get; } = new();
    /// <summary>Research suggestions the user has set aside for this person (by suggestion key).</summary>
    public List<string> DismissedHints { get; } = new();
    /// <summary>Parent families in preference order; the first is the primary set of parents.</summary>
    public List<long> ParentFamilyIds { get; } = new();
    public List<long> SpouseFamilyIds { get; } = new();

    /// <summary>The first name; created on demand so a new person always has one.</summary>
    [JsonIgnore] public Name PrimaryName { get { if (Names.Count == 0) Names.Add(new Name()); return Names[0]; } }
    [JsonIgnore] public string DisplayName => PrimaryName.Full.Length > 0 ? PrimaryName.Full : "(no name)";
    public Event? Find(string tag) => Events.FirstOrDefault(e => e.Tag == tag);
    [JsonIgnore] public Event? Birth => Find("BIRT") ?? Find("CHR");
    [JsonIgnore] public Event? Death => Find("DEAT") ?? Find("BURI");

    /// <summary>"1901–1972" style lifespan text.</summary>
    [JsonIgnore] public string Lifespan
    {
        get
        {
            var b = Birth?.Date.Year ?? 0;
            var d = Death?.Date.Year ?? 0;
            return b == 0 && d == 0 ? "" : $"{(b > 0 ? b : "?")}–{(d > 0 ? d.ToString() : "")}";
        }
    }
}

public sealed class ChildLink
{
    public long ChildId { get; set; }
    public ParentRelation ToFather { get; set; } = ParentRelation.Birth;
    public ParentRelation ToMother { get; set; } = ParentRelation.Birth;
    public RelationStatus Status { get; set; } = RelationStatus.Verified;
    public Event? Sealing { get; set; }   // LDS sealing to parents
}

public sealed class Family : Entity
{
    public long? Partner1Id { get; set; }   // GEDCOM HUSB slot
    public long? Partner2Id { get; set; }   // GEDCOM WIFE slot
    public CoupleStatus Status { get; set; }
    public bool NoChildren { get; set; }
    public string Note { get; set; } = "";
    public List<ChildLink> Children { get; } = new();
    public List<Event> Events { get; } = new();
    public List<Citation> Citations { get; } = new();
    public List<MediaLink> Media { get; } = new();
    public List<long> ResearchIds { get; } = new();

    [JsonIgnore] public Event? Marriage => Events.FirstOrDefault(e => e.Tag == "MARR");
    [JsonIgnore] public IEnumerable<long> PartnerIds =>
        new[] { Partner1Id, Partner2Id }.Where(p => p.HasValue).Select(p => p!.Value);
}

public sealed class Source : Entity
{
    public string Title { get; set; } = "";
    public string ShortTitle { get; set; } = "";
    public string Author { get; set; } = "";
    public string Publication { get; set; } = "";
    public string CallNumber { get; set; } = "";
    public string Text { get; set; } = "";
    public string Note { get; set; } = "";
    public string Type { get; set; } = "";
    public long? RepositoryId { get; set; }
    public int? Quality { get; set; }
    public SourceKind Kind { get; set; }
    /// <summary>Scans or photos of the source itself: a register page, a certificate, a letter.</summary>
    public List<MediaLink> Media { get; } = new();
}

public sealed class Repository : Entity
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Note { get; set; } = "";
}

public sealed class ResearchItem : Entity
{
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public int Priority { get; set; }
    public bool Done { get; set; }
    public GenDate Date { get; set; } = GenDate.Empty;
    public string Note { get; set; } = "";
    public long? RepositoryId { get; set; }
    public long? SourceId { get; set; }
}
