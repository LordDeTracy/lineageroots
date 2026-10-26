using System.Buffers.Binary;
using Lineage.Core.Model;

namespace Lineage.Core.Import.Aq;

/// <summary>
/// Reads an AQ 16 database into a <see cref="FamilyTree"/>. Read-only: the .aq file
/// is never modified. AQ record numbers (RIN/MRIN) become Lineage ids so users can keep them.
/// Field meanings were verified against AQ's own GEDCOM export of real databases
/// (see docs/AQ16-teardown.md, sections 3.6-3.7 and 6).
/// </summary>
public sealed class AqImporter
{
    private const int MaxChain = 100_000;
    private readonly AqFile _f;
    private readonly FamilyTree _tree = new();
    private Dictionary<long, (string Name, string Tag)>? _eventTypes;
    private Dictionary<int, string>? _tagNames;

    public Dictionary<int, int> DeletedSkipped { get; } = new();

    private AqImporter(AqFile f) => _f = f;

    public static FamilyTree Import(string path) => new AqImporter(AqFile.Open(path)).Run();
    public static FamilyTree Import(AqFile file) => new AqImporter(file).Run();

    private static readonly string[] Months = { "", "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
    private static readonly string[] DateKeywords =
    {
        "BIC", "Cancelled", "Child", "Cleared", "Completed", "Dead", "Deceased", "Done", "DNS", "DNS/CAN",
        "Infant", "Not Married", "Pre-1970", "Stillborn", "Uncleared", "BC", "Submitted", "Qualified",
        "DontDoIt", "In Process",
    };
    private static readonly string[] Colours = { "Blue", "Green", "Red", "Yellow", "Gray", "Purple", "Orange", "Beige" };

    private static readonly (string Tag, int Offset)[] PersonEventSlots =
        { ("BIRT", 0x018), ("CHR", 0x062), ("DEAT", 0x0AC), ("BURI", 0x0F6) };
    private static readonly (string Tag, int Offset)[] OrdinanceSlots =
        { ("BAPL", 0x140), ("CONL", 0x164), ("WAC", 0x188), ("ENDL", 0x1AC) };

    private FamilyTree Run()
    {
        _tree.Name = _f.DatabaseName.Length > 0 ? _f.DatabaseName : "Imported";
        ForEach(RecordType.Repository, (n, r) => _tree.AddRepository(ReadRepository(n, r)));
        var sources = new Dictionary<long, (int? Quality, Source S)>();
        ForEach(RecordType.Source, (n, r) => { var s = ReadSource(n, r); sources[n] = (s.Quality, s); _tree.AddSource(s); });
        ForEach(RecordType.Individual, (n, r) => _tree.AddPerson(ReadPerson(n, r)));
        ForEach(RecordType.Marriage, (n, r) => _tree.AddFamily(ReadFamily(n, r)));
        ForEach(RecordType.ResearchItem, (n, r) => _tree.AddResearch(ReadResearch(n, r)));
        LinkResearch();
        LinkFamilies();
        foreach (var (type, count) in DeletedSkipped)
            _tree.Warnings.Add($"Skipped {count} deleted AQ records of block type {type}.");
        return _tree;
    }

    // ---- primitives -------------------------------------------------------------------

    private static uint U32(byte[] r, int off) => BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(off, 4));
    private static ushort U16(byte[] r, int off) => BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(off, 2));

    private void ForEach(RecordType t, Action<long, byte[]> action)
    {
        var count = _f.HeaderU32(t.CountOffset);
        for (long n = 1; n <= count; n++)
        {
            var raw = Live(t, n);
            if (raw != null) action(n, raw);
        }
    }

    /// <summary>A live (non-blank, non-deleted) record, or null.</summary>
    private byte[]? Live(RecordType t, long n)
    {
        var raw = _f.Record(t, n);
        if (raw == null || raw.All(b => b == 0)) return null;
        if (t.IsDeleted(raw))
        {
            DeletedSkipped[t.BlockType] = DeletedSkipped.GetValueOrDefault(t.BlockType) + 1;
            return null;
        }
        return raw;
    }

    private string Text(byte[] raw, int off)
    {
        var ptr = U32(raw, off);
        if (ptr == 0) return "";
        try { return _f.HeapString(ptr); }
        catch (InvalidDataException) { _tree.Warnings.Add($"Bad string pointer 0x{ptr:X}"); return ""; }
    }

    /// <summary>Long text: a chain of 255-byte note chunks (+0x04 next, 238 bytes of text at +0x11).</summary>
    private string NoteText(byte[] raw, int off)
    {
        var n = (long)U32(raw, off);
        if (n == 0) return "";
        var bytes = new List<byte>();
        var seen = new HashSet<long>();
        while (n != 0 && seen.Add(n) && seen.Count < MaxChain)
        {
            var chunk = _f.Record(RecordType.Note, n);
            if (chunk == null) break;
            var text = chunk.AsSpan(0x11, 238);
            var end = text.IndexOf((byte)0);
            bytes.AddRange((end < 0 ? text : text[..end]).ToArray());
            n = U32(chunk, 0x04);
        }
        return AqFile.DecodeText(bytes.ToArray());
    }

    private string Place(byte[] raw, int off)
    {
        var parts = Enumerable.Range(0, 5).Select(i => Text(raw, off + 4 * i)).ToList();
        while (parts.Count > 0 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
        return string.Join(", ", parts);
    }

    /// <summary>Decoded 6-byte AQ date; ordinance keywords (Completed, ...) come back as status.</summary>
    private (GenDate Date, string Keyword) Date(byte[] raw, int off)
    {
        var word = U16(raw, off);
        if ((word & 1) != 0)
            return (GenDate.Parse(Text(raw, off + 2)), "");   // free text; parse BET..AND etc.
        int year = word >> 3, month = raw[off + 2] >> 3, day = raw[off + 3] >> 3;
        int day2 = raw[off + 4] >> 3, qual = raw[off + 4] & 7, span = raw[off + 5];
        if (year == 0 && month == 0 && day == 0) return (GenDate.Empty, "");
        if (month >= 13)
        {
            var kw = month - 13 < DateKeywords.Length ? DateKeywords[month - 13] : "";
            if (year == 0) return (GenDate.Empty, kw);
            month = 0;
        }
        // Dual day/year apply to qualified dates too (AQ exports "AFT 6 MAR 1271/1272").
        var core = new GenDate
        {
            Year = year, Month = month, Day = day,
            DualDay = qual == 7 ? 0 : day2,
            DualYear = qual != 7 && span > 0 ? year + span : 0,
        };
        return qual switch
        {
            7 => (core with { Kind = DateKind.Period, End = new GenDate { Year = year + span } }, ""),
            1 => (core with { Qualifier = DateQualifier.After }, ""),
            2 => (core with { Qualifier = DateQualifier.Before }, ""),
            3 => (core with { Qualifier = DateQualifier.About }, ""),
            4 => (core with { Qualifier = DateQualifier.Calculated }, ""),
            5 => (new GenDate { Kind = DateKind.Text, Text = "Sub " + core.ToGedcom() }, ""),
            6 => (core with { Qualifier = DateQualifier.Estimated }, ""),
            _ => (core, ""),
        };
    }

    private Event ReadEvent(string tag, byte[] raw, int off)
    {
        var (date, _) = Date(raw, off);
        var e = Event.Of(tag, date, Place(raw, off + 6));
        e.Citations.AddRange(Citations(U32(raw, off + 0x1E)));
        return e;
    }

    private Event ReadOrdinance(string tag, byte[] raw, int off)
    {
        var (date, kw) = Date(raw, off);
        return new Event { Tag = tag, Date = date, Status = kw.ToUpperInvariant(), Temple = Text(raw, off + 6) };
    }

    private static bool IsEmpty(Event e) =>
        e.Date.IsEmpty && e.Place.Length == 0 && e.Citations.Count == 0 && e.Temple.Length == 0 && e.Status.Length == 0;

    /// <summary>Citation links (T16) from a list head; +0x0C = citation, +0x10 = next.</summary>
    private IEnumerable<Citation> Citations(uint head)
    {
        var seen = new HashSet<long>();
        for (long link = head; link != 0 && seen.Add(link) && seen.Count < MaxChain;)
        {
            var lr = _f.Record(RecordType.CitationLink, link);
            if (lr == null) yield break;
            var c = ReadCitation(U32(lr, 0x0C));
            if (c != null) yield return c;
            link = U32(lr, 0x10);
        }
    }

    private Citation? ReadCitation(long n)
    {
        var raw = n == 0 ? null : Live(RecordType.Citation, n);
        if (raw == null) return null;
        var sourceId = U32(raw, 0x04);
        int? quality = raw[1] < 4 ? raw[1] : null;
        quality ??= _tree.Sources.TryGetValue(sourceId, out var s) ? s.Quality : null;
        var page = NoteText(raw, 0x14);
        return new Citation
        {
            SourceId = sourceId,
            Page = page.Length > 0 ? page : Text(raw, 0x1C),
            Text = NoteText(raw, 0x20),
            Note = NoteText(raw, 0x24),
            Quality = quality,
        };
    }

    private Dictionary<long, (string Name, string Tag)> EventTypes()
    {
        if (_eventTypes != null) return _eventTypes;
        _eventTypes = new();
        var count = _f.HeaderU32(RecordType.EventType.CountOffset);
        for (long n = 1; n <= count; n++)
        {
            var raw = _f.Record(RecordType.EventType, n);
            if (raw != null) _eventTypes[n] = (Text(raw, 0x00), Text(raw, 0x1C));
        }
        return _eventTypes;
    }

    /// <summary>User ("other") events: T12 chain, +0x00 = next.</summary>
    private IEnumerable<Event> UserEvents(uint head)
    {
        var seen = new HashSet<long>();
        for (long n = head; n != 0 && seen.Add(n) && seen.Count < MaxChain;)
        {
            var raw = _f.Record(RecordType.UserEvent, n);
            if (raw == null) yield break;
            var (name, tag) = EventTypes().GetValueOrDefault(U32(raw, 0x0C));
            var e = ReadEvent(tag.Length > 0 ? tag : "EVEN", raw, 0x1C);
            if (tag.Length == 0) e.TypeName = name.Trim();
            e.Value = Text(raw, 0x10);
            e.Description = string.Join("; ", new[] { Text(raw, 0x14), Text(raw, 0x18) }.Where(s => s.Length > 0));
            e.Place2 = Place(raw, 0x66 + 6);
            var (end, _) = Date(raw, 0x66);
            if (!end.IsEmpty && e.Date.Kind == DateKind.Simple)
                e.Date = e.Date with { Kind = DateKind.Period, End = end };
            yield return e;
            n = U32(raw, 0x00);
        }
    }

    private IEnumerable<MediaLink> Media(uint head)
    {
        var seen = new HashSet<long>();
        for (long n = head; n != 0 && seen.Add(n) && seen.Count < MaxChain;)
        {
            var raw = _f.Record(RecordType.Scrapbook, n);
            if (raw == null) yield break;
            var file = Text(raw, 0x0C);
            yield return new MediaLink
            {
                File = file,
                Title = Text(raw, 0x10),
                Note = NoteText(raw, 0x14),
                Format = Path.GetExtension(file).TrimStart('.').ToUpperInvariant(),
                MediaType = (char)raw[0x1C] switch
                {
                    'P' => "PHOTO", 'S' => "SOUND", 'V' => "VIDEO", 'D' => "DOCUMENT", 'O' => "OTHER", _ => "",
                },
                Primary = (U16(raw, 0x1E) & 1) != 0,
            };
            n = U32(raw, 0x04);
        }
    }

    // ---- records -----------------------------------------------------------------------

    private Person ReadPerson(long rin, byte[] raw)
    {
        var p = new Person
        {
            Id = rin,
            Sex = (char)raw[0] switch { 'M' => Sex.Male, 'F' => Sex.Female, _ => Sex.Unknown },
            Title = Text(raw, 0x014),
            Note = NoteText(raw, 0x208),
            UserId = Text(raw, 0x228),
            Description = Text(raw, 0x248),
        };
        var name = p.PrimaryName;
        name.Surname = Text(raw, 0x004).Trim();
        name.Given = string.Join(' ', new[] { Text(raw, 0x008), Text(raw, 0x00C), Text(raw, 0x010) }
                                      .Select(s => s.Trim()).Where(s => s.Length > 0));
        name.Prefix = Text(raw, 0x23C);
        name.Nickname = Text(raw, 0x234);
        foreach (var (type, off) in new[] { ("aka", 0x238), ("married", 0x244), ("phonetic", 0x276), ("romanized", 0x27A) })
        {
            var alt = Text(raw, off);
            if (alt.Length > 0) p.Names.Add(new Name { Given = alt, Type = type });
        }

        foreach (var (tag, off) in PersonEventSlots)
        {
            var e = ReadEvent(tag, raw, off);
            if (!IsEmpty(e)) p.Events.Add(e);
        }
        var cause = Text(raw, 0x240);
        if (cause.Length > 0)
        {
            var death = p.Find("DEAT") ?? AddEvent(p.Events, Event.Of("DEAT"));
            death.Cause = cause;
        }
        p.Events.AddRange(UserEvents(U32(raw, 0x20C)));
        foreach (var (tag, off) in OrdinanceSlots)
        {
            var e = ReadOrdinance(tag, raw, off);
            if (!IsEmpty(e)) p.Events.Add(e);
        }
        p.Citations.AddRange(Citations(U32(raw, 0x210)));
        p.Media.AddRange(Media(U32(raw, 0x22C)));

        var contact = Live(RecordType.Contact, U32(raw, 0x230));
        if (contact != null)
        {
            string T(int o) => Text(contact, o);
            var cityLine = string.Join(", ", new[] { T(0x10), T(0x14), T(0x18) }.Where(s => s.Length > 0));
            p.Address = string.Join("\n", new[] { T(0x04), T(0x08), T(0x0C), cityLine, T(0x1C) }.Where(s => s.Length > 0));
            p.Phone = T(0x20);
            p.Email = T(0x24);
            p.Url = T(0x28);
        }

        var dna = _f.Record(RecordType.Dna, U32(raw, 0x26A));
        if (dna != null && U32(raw, 0x26A) != 0) p.DnaTests.Add(ReadDna(dna));

        var fs = Live(RecordType.FamilySearch, U32(raw, 0x272));
        if (fs != null)
        {
            var pid = System.Text.Encoding.ASCII.GetString(fs, 0x14, 16).Split('\0')[0];
            p.FamilySearchId = pid.Length == 7 ? $"{pid[..4]}-{pid[4..]}" : pid;
        }

        byte b1 = raw[0x01], b2 = raw[0x261];
        foreach (var slot in new[] { b1 >> 5, b2 & 7, (b2 >> 3) & 7 })
            if (slot > 0) p.Tags.Add(TagNames().GetValueOrDefault(slot - 1, $"Tag {slot}"));
        if ((b1 & 3) == 3) p.Colour = Colours[(b1 >> 2) & 7];
        return p;
    }

    private static Event AddEvent(List<Event> list, Event e) { list.Add(e); return e; }

    private DnaTest ReadDna(byte[] raw)
    {
        var stamp = U32(raw, 0x18);
        var t = new DnaTest
        {
            Kind = (char)raw[4] switch { 'P' => "Y-DNA", 'M' => "mtDNA", _ => "" },
            Company = Text(raw, 0x10),
            Reference = Text(raw, 0x14),
            Date = stamp == 0 ? null : DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(stamp).LocalDateTime),
            Note = NoteText(raw, 0x1C),
            Haplogroup = Text(raw, 0x1A0),
            HaplogroupEstimated = (raw[5] & 1) != 0,
        };
        for (var i = 0; i < 64; i++)
        {
            var marker = Text(raw, 0x20 + 4 * i);
            if (marker.Length > 0) t.Markers.Add(new(marker, U16(raw, 0x120 + 2 * i)));
        }
        return t;
    }

    private Dictionary<int, string> TagNames()
    {
        if (_tagNames != null) return _tagNames;
        _tagNames = new();
        for (var i = 0; i < 64; i++)
        {
            var ptr = _f.HeaderU32(0x3A0 + 4 * i);
            if (ptr == 0) continue;
            try { _tagNames[i] = _f.HeapString(ptr); } catch (InvalidDataException) { break; }
        }
        return _tagNames;
    }

    private Family ReadFamily(long mrin, byte[] raw)
    {
        var f = new Family
        {
            Id = mrin,
            Status = (char)raw[0] switch
            {
                'M' => CoupleStatus.Married, 'V' => CoupleStatus.Divorced, 'N' => CoupleStatus.Unmarried,
                _ => CoupleStatus.Unspecified,
            },
            NoChildren = (raw[2] & 1) != 0,
            Note = NoteText(raw, 0x8A),
        };
        var a = U32(raw, 0x04);
        var b = U32(raw, 0x08);
        f.Partner1Id = a == 0 ? null : a;
        f.Partner2Id = b == 0 ? null : b;

        var marr = ReadEvent("MARR", raw, 0x10);
        if (!IsEmpty(marr) || f.Status is CoupleStatus.Married or CoupleStatus.Unmarried) f.Events.Add(marr);
        if (f.Status == CoupleStatus.Unmarried) marr.TypeName = "Unmarried";
        f.Events.AddRange(UserEvents(U32(raw, 0x86)));
        f.Events.RemoveAll(e => e.Tag == "DIV" && e.Value == "Y" && e.Date.IsEmpty && e.Place.Length == 0);
        // AQ can keep the status flag at "married" after a divorce event is recorded; a recorded
        // divorce wins (the GEDCOM reader applies the same rule, so both paths agree).
        if (f.Events.Any(e => e.Tag == "DIV")) f.Status = CoupleStatus.Divorced;
        var seal = ReadOrdinance("SLGS", raw, 0x5A);
        if (!IsEmpty(seal)) f.Events.Add(seal);
        f.Citations.AddRange(Citations(U32(raw, 0x8E)));
        f.Media.AddRange(Media(U32(raw, 0x9A)));

        // Children: circular family-link list from +0x0C (link +0x08 = next, +0x10 = child)
        long head = U32(raw, 0x0C), link = head;
        var seen = new HashSet<long>();
        while (link != 0 && seen.Add(link) && seen.Count < MaxChain)
        {
            var lr = Live(RecordType.FamilyLink, link);
            if (lr == null) break;
            var child = U32(lr, 0x10);
            if (child != 0)
            {
                var c = new ChildLink
                {
                    ChildId = child,
                    ToFather = Relation((char)lr[0]),
                    ToMother = Relation((char)lr[1]),
                    Status = (char)lr[2] switch { 'c' => RelationStatus.Challenged, 'd' => RelationStatus.Disproved, _ => RelationStatus.Verified },
                };
                var sealing = ReadOrdinance("SLGC", lr, 0x14);
                if (!IsEmpty(sealing)) c.Sealing = sealing;
                f.Children.Add(c);
            }
            link = U32(lr, 0x08);
            if (link == head) break;
        }
        return f;
    }

    private static ParentRelation Relation(char c) => c switch
    {
        'a' => ParentRelation.Adopted, 'p' => ParentRelation.Step, 'f' => ParentRelation.Foster,
        'g' => ParentRelation.Guardian, 's' => ParentRelation.Sealing, _ => ParentRelation.Birth,
    };

    private Source ReadSource(long n, byte[] raw)
    {
        var freeform = raw[0x79] != 0;
        var title = Text(raw, 0x14);
        var code = System.Text.Encoding.ASCII.GetString(raw, 0x6C, 2).TrimEnd('\0');
        return new Source
        {
            Id = n,
            Title = title.Length > 0 || !freeform ? title : NoteText(raw, 0x24),
            ShortTitle = Text(raw, 0x00),
            Author = Text(raw, 0x10),
            Publication = Text(raw, 0x18),
            CallNumber = Text(raw, 0x0C),
            Text = NoteText(raw, 0x1C),
            Note = NoteText(raw, 0x20),
            Type = AqSourceTypes.Names.GetValueOrDefault(code, code),
            RepositoryId = U32(raw, 0x08) is var r and > 0 ? r : null,
            Quality = raw[0x74] < 4 ? raw[0x74] : null,
        };
    }

    private Repository ReadRepository(long n, byte[] raw) => new()
    {
        Id = n,
        Name = Text(raw, 0x00),
        Address = string.Join("\n", new[] { 0x04, 0x08, 0x0C, 0x10 }.Select(o => Text(raw, o)).Where(s => s.Length > 0)),
        Phone = Text(raw, 0x14),
        Note = NoteText(raw, 0x18),
    };

    private ResearchItem ReadResearch(long n, byte[] raw)
    {
        var cat = Live(RecordType.Category, U32(raw, 0x28));
        return new ResearchItem
        {
            Id = n,
            Description = Text(raw, 0x10),
            Priority = raw[2],
            Done = raw[3] != 0,
            Date = Date(raw, 0x1A).Date,
            Category = cat != null ? Text(cat, 0x08) : "",
            Note = string.Join("\n", new[] { NoteText(raw, 0x30), NoteText(raw, 0x34) }.Where(s => s.Length > 0)),
            RepositoryId = U32(raw, 0x08) is var r and > 0 ? r : null,
            SourceId = U32(raw, 0x0C) is var s and > 0 ? s : null,
        };
    }

    // ---- cross links --------------------------------------------------------------------

    private void LinkResearch()
    {
        var count = _f.HeaderU32(RecordType.ResearchXref.CountOffset);
        for (long n = 1; n <= count; n++)
        {
            var raw = Live(RecordType.ResearchXref, n);
            if (raw == null) continue;
            long owner = U32(raw, 0x04), item = U32(raw, 0x10);
            if (!_tree.Research.ContainsKey(item)) continue;
            if ((char)raw[0] == 'I' && _tree.People.TryGetValue(owner, out var p)) p.ResearchIds.Add(item);
            if ((char)raw[0] == 'M' && _tree.Families.TryGetValue(owner, out var f)) f.ResearchIds.Add(item);
        }
    }

    /// <summary>Membership from the marriage side; order (and primary parents) from each person's chains.</summary>
    private void LinkFamilies()
    {
        foreach (var f in _tree.Families.Values)
        {
            foreach (var id in f.PartnerIds)
                if (_tree.People.TryGetValue(id, out var p)) p.SpouseFamilyIds.Add(f.Id);
                else _tree.Warnings.Add($"Family {f.Id} refers to missing person {id}.");
            foreach (var c in f.Children)
                if (_tree.People.TryGetValue(c.ChildId, out var p)) p.ParentFamilyIds.Add(f.Id);
                else _tree.Warnings.Add($"Family {f.Id} has missing child {c.ChildId}.");
        }
        foreach (var p in _tree.People.Values)
        {
            var raw = Live(RecordType.Individual, p.Id);
            if (raw == null) continue;
            if (p.ParentFamilyIds.Count > 1) Reorder(p.ParentFamilyIds, ParentChain(raw));
            if (p.SpouseFamilyIds.Count > 1) Reorder(p.SpouseFamilyIds, SpouseChain(p.Id, raw));
        }
    }

    private static void Reorder(List<long> ids, List<long> order)
    {
        var rank = order.Select((id, i) => (id, i)).GroupBy(x => x.id).ToDictionary(g => g.Key, g => g.First().i);
        var sorted = ids.OrderBy(id => rank.GetValueOrDefault(id, int.MaxValue)).ToList();
        ids.Clear();
        ids.AddRange(sorted);
    }

    private List<long> ParentChain(byte[] raw)
    {
        var out_ = new List<long>();
        long head = U32(raw, 0x204), link = head;
        var seen = new HashSet<long>();
        while (link != 0 && seen.Add(link) && seen.Count < MaxChain)
        {
            var lr = _f.Record(RecordType.FamilyLink, link);
            if (lr == null) break;
            out_.Add(U32(lr, 0x04));
            link = U32(lr, 0x0C);
            if (link == head) break;
        }
        return out_;
    }

    private List<long> SpouseChain(long rin, byte[] raw)
    {
        var out_ = new List<long>();
        var seen = new HashSet<long>();
        for (long m = U32(raw, 0x200); m != 0 && seen.Add(m) && seen.Count < MaxChain;)
        {
            var mr = _f.Record(RecordType.Marriage, m);
            if (mr == null) break;
            out_.Add(m);
            m = U32(mr, 0x04) == rin ? U32(mr, 0x7E) : U32(mr, 0x82);
        }
        return out_;
    }
}
