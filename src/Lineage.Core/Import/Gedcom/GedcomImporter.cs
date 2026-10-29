using System.Text.RegularExpressions;
using Lineage.Core.Model;

namespace Lineage.Core.Import.Gedcom;

/// <summary>
/// Imports GEDCOM 5.5 / 5.5.1 (including AQ, PAF and Lineage custom tags) into a tree.
/// Numeric xrefs (@I12@, @F3@) become ids so RIN-style numbers survive; others are numbered.
/// </summary>
public sealed class GedcomImporter
{
    public static readonly HashSet<string> PersonEventTags = new()
    {
        "BIRT", "CHR", "DEAT", "BURI", "CREM", "ADOP", "BAPM", "BARM", "BASM", "BLES", "CHRA", "CONF",
        "FCOM", "ORDN", "NATU", "EMIG", "IMMI", "CENS", "PROB", "WILL", "GRAD", "RETI", "EVEN", "OCCU",
        "RESI", "EDUC", "RELI", "NATI", "CAST", "NCHI", "PROP", "SSN", "IDNO", "MILI", "DIV",
        "BAPL", "CONL", "WAC", "ENDL", "SLGC",
    };
    public static readonly HashSet<string> FamilyEventTags = new()
    {
        "MARR", "MARB", "MARC", "MARL", "MARS", "ENGA", "DIV", "DIVF", "ANUL", "CENS", "EVEN", "SLGS", "RESI",
    };

    private readonly FamilyTree _tree = new();
    private readonly Dictionary<string, long> _people = new(), _families = new(), _sources = new(),
                                              _repos = new(), _research = new();
    private readonly Dictionary<string, string> _notes = new();
    private long _nextP = 1, _nextF = 1, _nextS = 1, _nextR = 1, _nextT = 1;

    public static FamilyTree ImportFile(string path)
    {
        var tree = Import(GedcomNode.ReadFile(path));
        tree.Name = Path.GetFileNameWithoutExtension(path);
        return tree;
    }

    public static FamilyTree Import(string text) => new GedcomImporter().Run(GedcomNode.Parse(text));

    private FamilyTree Run(List<GedcomNode> records)
    {
        // Pass 1: assign ids for every record so forward references resolve.
        foreach (var r in records)
        {
            if (r.Xref is null) continue;
            switch (r.Tag)
            {
                case "INDI": _people[r.Xref] = Assign(r.Xref, _people, ref _nextP); break;
                case "FAM": _families[r.Xref] = Assign(r.Xref, _families, ref _nextF); break;
                case "SOUR": _sources[r.Xref] = Assign(r.Xref, _sources, ref _nextS); break;
                case "REPO": _repos[r.Xref] = Assign(r.Xref, _repos, ref _nextR); break;
                case "_TODO": _research[r.Xref] = Assign(r.Xref, _research, ref _nextT); break;
                case "NOTE": _notes[r.Xref] = r.Value; break;
            }
        }
        if (records.FirstOrDefault(r => r.Tag == "HEAD")?.First("_SHARE") is { } share && Guid.TryParse(share.Value.Trim(), out var shareId))
            _tree.SharedFrom = new ShareOrigin(shareId, share.Text("_FROM"));
        foreach (var r in records.Where(r => r.Tag == "REPO" && r.Xref != null)) ReadRepository(r);
        foreach (var r in records.Where(r => r.Tag == "SOUR" && r.Xref != null)) ReadSource(r);
        foreach (var r in records.Where(r => r.Tag == "_TODO" && r.Xref != null)) ReadResearch(r);
        foreach (var r in records.Where(r => r.Tag == "INDI" && r.Xref != null)) ReadPerson(r);
        foreach (var r in records.Where(r => r.Tag == "FAM" && r.Xref != null)) ReadFamily(r);
        // Person-side links (FAMC order, pedigree, sealings) need the families to exist.
        foreach (var r in records.Where(r => r.Tag == "INDI" && r.Xref != null))
            LinkPerson(r, _tree.People[_people[r.Xref!]]);
        // Make links two-sided even when the file only states one side (CHIL without FAMC, etc.).
        foreach (var f in _tree.Families.Values)
        {
            foreach (var id in f.PartnerIds)
                if (_tree.People.TryGetValue(id, out var p) && !p.SpouseFamilyIds.Contains(f.Id)) p.SpouseFamilyIds.Add(f.Id);
            foreach (var c in f.Children)
                if (_tree.People.TryGetValue(c.ChildId, out var p) && !p.ParentFamilyIds.Contains(f.Id)) p.ParentFamilyIds.Add(f.Id);
        }
        return _tree;
    }

    private static readonly Regex Numeric = new(@"^@[A-Za-z_]*?(\d+)@$", RegexOptions.Compiled);

    private static long Assign(string xref, Dictionary<string, long> map, ref long next)
    {
        var m = Numeric.Match(xref);
        if (m.Success && long.TryParse(m.Groups[1].Value, out var n) && n > 0 && !map.ContainsValue(n))
        {
            if (n >= next) next = n + 1;
            return n;
        }
        while (map.ContainsValue(next)) next++;
        return next++;
    }

    private string NoteValue(GedcomNode n) =>
        n.Value.StartsWith('@') && _notes.TryGetValue(n.Value.Trim(), out var shared) ? shared : n.Value;

    private string Notes(GedcomNode owner) => string.Join("\n\n", owner.All("NOTE").Select(NoteValue).Where(s => s.Length > 0));

    /// <summary>A record's _UID (32 hex digits, sometimes followed by a 4-digit checksum), if it has one.</summary>
    private static Guid? Uid(GedcomNode n)
    {
        var text = n.Text("_UID").Trim();
        return text.Length >= 32 && Guid.TryParse(text[..32], out var g) ? g : null;
    }

    private Citation ReadCitation(GedcomNode n)
    {
        var c = new Citation { Page = n.Text("PAGE"), Note = Notes(n) };
        if (_sources.TryGetValue(n.Value.Trim(), out var sid)) c.SourceId = sid;
        else if (n.Value.Length > 0)
        {   // inline source text: create a source for it
            var s = _tree.AddSource(new Source { Title = n.Value });
            c.SourceId = s.Id;
        }
        c.Text = n.First("DATA")?.Text("TEXT") ?? n.Text("TEXT");
        if (int.TryParse(n.Text("QUAY"), out var q)) c.Quality = q;
        c.Information = n.Text("_INFO").Trim().ToUpperInvariant() switch
        {
            "PRIMARY" => InformationKind.Primary, "SECONDARY" => InformationKind.Secondary, _ => InformationKind.Unknown,
        };
        c.Evidence = Enum.TryParse<EvidenceKind>(n.Text("_EVID").Trim(), ignoreCase: true, out var ev) ? ev : EvidenceKind.Unknown;
        return c;
    }

    private Event ReadEvent(GedcomNode n)
    {
        var e = new Event
        {
            Tag = n.Tag,
            Value = n.Value,
            Date = GenDate.Parse(n.Text("DATE")),
            Place = n.Text("PLAC"),
            Place2 = n.Text("_PLAC2"),
            Location = Map(n.First("PLAC")?.First("MAP")),
            TypeName = n.Text("TYPE").Trim(),
            Cause = n.Text("CAUS"),
            Temple = n.Text("TEMP"),
            Status = n.Text("STAT"),
            Description = n.Text("_Description2"),
            Note = Notes(n),
            Private = n.First("_CONF") != null || n.First("_Private") != null,
        };
        if (Uid(n) is { } eu) e.Id = eu;
        foreach (var s in n.All("SOUR")) e.Citations.Add(ReadCitation(s));
        foreach (var o in n.All("OBJE")) e.Media.Add(ReadMedia(o));
        e.Conclusion = n.Text("_PROOF").Trim().ToUpperInvariant() switch
        {
            "PROVEN" => FactConclusion.Proven, "DISPROVED" => FactConclusion.Disproved, _ => FactConclusion.NotDecided,
        };
        e.Reasoning = n.Text("_PROOFNOTE");
        foreach (var c in n.All("_CONFLICT"))
        {
            var sour = c.First("SOUR");
            e.Conflicts.Add(new EvidenceConflict
            {
                Claim = c.Value,
                SourceId = sour != null && _sources.TryGetValue(sour.Value.Trim(), out var sid) ? sid : null,
                Page = sour?.Text("PAGE") ?? "",
                Resolution = c.Text("_RESOLUTION"),
                Resolved = c.Text("_RESOLVED") == "Y",
            });
        }
        return e;
    }

    private MediaLink ReadMedia(GedcomNode n)
    {
        var m = new MediaLink
        {
            File = n.Text("FILE"),
            Title = n.Text("TITL"),
            Format = (n.Text("FORM").Length > 0 ? n.Text("FORM") : n.First("FILE")?.Text("FORM") ?? "").ToUpperInvariant(),
            MediaType = n.Text("_TYPE"),
            Note = Notes(n),
            Primary = n.Text("_PRIM") == "Y",
            Location = Map(n.First("_MAP")),
            Taken = n.Text("_DATE"),
        };
        // People tagged in the photo (Lineage custom tags, written by GedcomExporter.WriteMedia):
        //   n+1 _REGION / n+2 _PERSON @I12@ / n+2 _RECT x y width height (0..1 of the picture).
        foreach (var r in n.All("_REGION"))
            if (_people.TryGetValue(r.Text("_PERSON").Trim(), out var pid) && ReadRect(r.Text("_RECT")) is { } rect)
                m.Regions.Add(new PhotoRegion { PersonId = pid, X = rect[0], Y = rect[1], Width = rect[2], Height = rect[3] });
        return m;
    }

    private static double[]? ReadRect(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) return null;
        var values = new double[4];
        for (var i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out values[i])) return null;
        return values;
    }

    private static GeoPoint? Map(GedcomNode? map) =>
        map == null ? null : GeoPoint.FromGedcom(map.Text("LATI"), map.Text("LONG"));

    private void ReadPerson(GedcomNode r)
    {
        var p = new Person { Id = _people[r.Xref!] };
        p.Names.Clear();
        foreach (var nm in r.All("NAME"))
        {
            var name = ParseName(nm.Value);
            if (nm.Text("GIVN").Length > 0) name.Given = nm.Text("GIVN");
            if (nm.Text("SURN").Length > 0) name.Surname = nm.Text("SURN");
            name.Prefix = nm.Text("NPFX");
            name.Suffix = nm.Text("NSFX");
            name.Nickname = nm.Text("NICK");
            name.Type = nm.Text("TYPE");
            p.Names.Add(name);
            foreach (var (tag, type) in new[] { ("_AKA", "aka"), ("_MARNM", "married"), ("FONE", "phonetic"), ("ROMN", "romanized") })
                foreach (var alt in nm.All(tag)) p.Names.Add(new Name { Given = alt.Value, Type = type });
        }
        if (p.Names.Count == 0) p.Names.Add(new Name());
        p.Sex = r.Text("SEX").Trim().ToUpperInvariant() switch { "M" => Sex.Male, "F" => Sex.Female, _ => Sex.Unknown };
        p.Title = r.Text("TITL");
        p.Description = r.Text("DSCR");
        p.UserId = r.Text("REFN");
        p.FamilySearchId = r.Text("_FSFTID");
        p.Colour = r.Text("_COLOR");
        p.Note = Notes(r);
        if (Guid.TryParse(r.Text("_UID").Length >= 32 ? r.Text("_UID")[..32] : "", out var uid)) p.Uid = uid;
        var addr = r.First("ADDR");
        if (addr != null)
            p.Address = string.Join("\n", new[] { addr.Value, addr.Text("ADR1"), addr.Text("ADR2"),
                string.Join(", ", new[] { addr.Text("CITY"), addr.Text("STAE"), addr.Text("POST") }.Where(s => s.Length > 0)),
                addr.Text("CTRY") }.Where(s => s.Length > 0));
        p.Phone = r.Text("PHON");
        p.Email = r.Text("_EMAIL").Length > 0 ? r.Text("_EMAIL") : r.Text("EMAIL");
        p.Url = r.Text("_URL").Length > 0 ? r.Text("_URL") : r.Text("WWW");
        foreach (var t in r.All("_TAG")) p.Tags.Add(t.Value);

        foreach (var c in r.Children)
        {
            if (c.Tag == "SLGC" && c.Text("FAMC").Length > 0) continue;   // goes on the child link
            if (PersonEventTags.Contains(c.Tag)) p.Events.Add(ReadEvent(c));
        }
        foreach (var s in r.All("SOUR")) p.Citations.Add(ReadCitation(s));
        foreach (var o in r.All("OBJE")) p.Media.Add(ReadMedia(o));
        foreach (var t in r.All("_TODO"))
            if (_research.TryGetValue(t.Value.Trim(), out var rid)) p.ResearchIds.Add(rid);
        foreach (var d in r.All("_DNA")) p.DnaTests.Add(ReadDna(d));
        foreach (var m in r.All("_MATCH")) p.DnaMatches.Add(ReadMatch(m));
        _tree.AddPerson(p);
    }

    private DnaMatch ReadMatch(GedcomNode n)
    {
        static double Num(string s) =>
            double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        var m = new DnaMatch
        {
            Name = n.Value,
            SharedCm = Num(n.Text("_CM")),
            Segments = int.TryParse(n.Text("_SEGMENTS"), out var seg) ? seg : 0,
            LongestCm = Num(n.Text("_LONGEST")),
            Company = n.Text("_Company"),
            Reference = n.Text("REFN"),
            Note = n.Text("NOTE"),
        };
        if (Guid.TryParse(n.Text("_UID"), out var id)) m.Id = id;
        if (_people.TryGetValue(n.Text("_PERSON").Trim(), out var pid)) m.PersonId = pid;
        foreach (var s in n.All("_SHARED"))
            if (Guid.TryParse(s.Value, out var g)) m.SharedWith.Add(g);
        foreach (var s in n.All("_SEG"))
        {
            var f = s.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length >= 4 && int.TryParse(f[0], out var chr) && long.TryParse(f[1], out var start) && long.TryParse(f[2], out var end))
                m.ChromosomeSegments.Add(new DnaSegment
                {
                    Chromosome = chr, Start = start, End = end, Cm = Num(f[3]),
                    Snps = f.Length > 4 && int.TryParse(f[4], out var snps) ? snps : 0,
                });
        }
        return m;
    }

    private static DnaTest ReadDna(GedcomNode d)
    {
        var t = new DnaTest
        {
            Kind = d.Text("TYPE"),
            Company = d.Text("_Company"),
            Reference = d.Text("REFN"),
            Haplogroup = d.Text("_Haplogroup"),
            HaplogroupEstimated = d.First("_Haplogroup")?.Text("_Est") == "Y",
            Note = d.Text("NOTE"),
        };
        if (DateTime.TryParse(d.Text("DATE"), System.Globalization.CultureInfo.InvariantCulture,
                              System.Globalization.DateTimeStyles.None, out var dt))
            t.Date = DateOnly.FromDateTime(dt);
        foreach (var m in d.All("_Marker"))
            t.Markers.Add(new(m.Value, int.TryParse(m.Text("_Value"), out var v) ? v : 0));
        return t;
    }

    /// <summary>"John Henry /Doe/ Jr." -> given, surname, suffix.</summary>
    public static Name ParseName(string raw)
    {
        var s = raw.Trim();
        var a = s.IndexOf('/');
        var b = a >= 0 ? s.IndexOf('/', a + 1) : -1;
        if (a < 0) return new Name { Given = s };
        if (b < 0) return new Name { Given = s[..a].Trim(), Surname = s[(a + 1)..].Trim() };
        return new Name { Given = s[..a].Trim(), Surname = s[(a + 1)..b].Trim(), Suffix = s[(b + 1)..].Trim() };
    }

    private void ReadFamily(GedcomNode r)
    {
        var f = new Family { Id = _families[r.Xref!], Note = Notes(r) };
        if (Uid(r) is { } fu) f.Uid = fu;
        if (_people.TryGetValue(r.Text("HUSB").Trim(), out var h)) f.Partner1Id = h;
        if (_people.TryGetValue(r.Text("WIFE").Trim(), out var w)) f.Partner2Id = w;
        foreach (var c in r.Children)
        {
            if (FamilyEventTags.Contains(c.Tag)) f.Events.Add(ReadEvent(c));
            if (c.Tag == "CHIL" && _people.TryGetValue(c.Value.Trim(), out var cid))
                f.Children.Add(new ChildLink { ChildId = cid });
        }
        var marr = f.Marriage;
        // "1 DIV Y" with no details is a divorced-status marker, not an event.
        var divorced = f.Events.RemoveAll(e => e.Tag == "DIV" && e.Value == "Y" && e.Date.IsEmpty
                                             && e.Place.Length == 0 && e.Citations.Count == 0 && e.Media.Count == 0) > 0
                       || f.Events.Any(e => e.Tag == "DIV");
        f.Status = divorced ? CoupleStatus.Divorced
                 : marr?.TypeName.Equals("Unmarried", StringComparison.OrdinalIgnoreCase) == true ? CoupleStatus.Unmarried
                 : marr != null ? CoupleStatus.Married : CoupleStatus.Unspecified;
        f.NoChildren = r.First("_NONE") != null || r.Text("NCHI") == "0";
        f.Plural = r.Text("_PLURAL").StartsWith('Y');
        foreach (var s in r.All("SOUR")) f.Citations.Add(ReadCitation(s));
        foreach (var o in r.All("OBJE")) f.Media.Add(ReadMedia(o));
        foreach (var t in r.All("_TODO"))
            if (_research.TryGetValue(t.Value.Trim(), out var rid)) f.ResearchIds.Add(rid);
        _tree.AddFamily(f);
    }

    /// <summary>Second pass on people: FAMC/FAMS order and per-parent relationships.</summary>
    private void LinkPerson(GedcomNode r, Person p)
    {
        foreach (var famc in r.All("FAMC"))
        {
            if (!_families.TryGetValue(famc.Value.Trim(), out var fid) || !_tree.Families.TryGetValue(fid, out var f)) continue;
            if (!p.ParentFamilyIds.Contains(fid)) p.ParentFamilyIds.Add(fid);
            var link = f.Children.FirstOrDefault(c => c.ChildId == p.Id);
            if (link == null) { link = new ChildLink { ChildId = p.Id }; f.Children.Add(link); }
            var pedi = famc.Text("PEDI").ToLowerInvariant();
            if (pedi is "challenged" or "disproved")
                link.Status = pedi == "challenged" ? RelationStatus.Challenged : RelationStatus.Disproved;
            else if (pedi.Length > 0)
                link.ToFather = link.ToMother = Pedigree(pedi);
            var fa = famc.First("PEDI")?.Text("_HUSB") ?? "";
            var mo = famc.First("PEDI")?.Text("_WIFE") ?? "";
            if (fa.Length > 0) link.ToFather = Pedigree(fa);
            if (mo.Length > 0) link.ToMother = Pedigree(mo);
        }
        foreach (var fams in r.All("FAMS"))
            if (_families.TryGetValue(fams.Value.Trim(), out var fid) && !p.SpouseFamilyIds.Contains(fid))
                p.SpouseFamilyIds.Add(fid);
        foreach (var slgc in r.All("SLGC").Where(s => s.Text("FAMC").Length > 0))
        {
            var fid = _families.GetValueOrDefault(slgc.Text("FAMC").Trim());
            var link = _tree.Family(fid)?.Children.FirstOrDefault(c => c.ChildId == p.Id);
            if (link != null) link.Sealing = ReadEvent(slgc);
        }
    }

    private static ParentRelation Pedigree(string v) => v.ToLowerInvariant() switch
    {
        "adopted" => ParentRelation.Adopted, "step" => ParentRelation.Step, "foster" => ParentRelation.Foster,
        "guardian" => ParentRelation.Guardian, "sealing" => ParentRelation.Sealing, _ => ParentRelation.Birth,
    };

    private void ReadSource(GedcomNode r)
    {
        var s = new Source
        {
            Id = _sources[r.Xref!],
            Title = r.Text("TITL"),
            ShortTitle = r.Text("ABBR"),
            Author = r.Text("AUTH"),
            Publication = r.Text("PUBL"),
            Text = r.Text("TEXT"),
            Note = Notes(r),
            Type = r.Text("_TYPE"),
            CallNumber = r.Text("CALN").Length > 0 ? r.Text("CALN") : r.First("REPO")?.Text("CALN") ?? "",
        };
        if (int.TryParse(r.Text("QUAY"), out var q)) s.Quality = q;
        if (Uid(r) is { } su) s.Uid = su;
        s.Kind = Enum.TryParse<SourceKind>(r.Text("_SRCKIND").Trim(), ignoreCase: true, out var kind) ? kind : SourceKind.Unknown;
        if (_repos.TryGetValue(r.Text("REPO").Trim(), out var rid)) s.RepositoryId = rid;
        foreach (var o in r.All("OBJE")) s.Media.Add(ReadMedia(o));
        _tree.AddSource(s);
    }

    private void ReadRepository(GedcomNode r) => _tree.AddRepository(new Repository
    {
        Id = _repos[r.Xref!], Name = r.Text("NAME"), Address = r.Text("ADDR"), Phone = r.Text("PHON"), Note = Notes(r),
    });

    private void ReadResearch(GedcomNode r)
    {
        var t = new ResearchItem
        {
            Id = _research[r.Xref!],
            Description = r.Text("DESC"),
            Category = r.Text("_CAT"),
            Priority = int.TryParse(r.Text("_PRIORITY"), out var pr) ? pr : 0,
            Done = r.Text("STAT") is "1" or "Y",
            Date = GenDate.Parse(r.Text("DATE")),
            Note = string.Join("\n", new[] { Notes(r), r.Text("TEXT") }.Where(s => s.Length > 0)),
        };
        if (_repos.TryGetValue(r.Text("REPO").Trim(), out var rid)) t.RepositoryId = rid;
        if (_sources.TryGetValue(r.Text("SOUR").Trim(), out var sid)) t.SourceId = sid;
        t.Result = Enum.TryParse<SearchResult>(r.Text("_RESULT").Trim(), ignoreCase: true, out var result) ? result : SearchResult.None;
        t.HintKey = r.Text("_HINT").Trim();
        _tree.AddResearch(t);
    }
}
