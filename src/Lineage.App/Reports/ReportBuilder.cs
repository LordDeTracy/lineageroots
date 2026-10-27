using System.Globalization;
using Lineage.App.ViewModels;
using Lineage.Core.Model;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lineage.App.Reports;

/// <summary>
/// Printable PDF reports. Events marked private are left out. Paper size follows the computer's
/// region (Letter in the US and Canada, A4 elsewhere).
/// </summary>
public static class ReportBuilder
{
    private const string Ink = "#22252A", Muted = "#6F7378", Faint = "#9A9DA2", Line = "#E4DED5", Accent = "#2E6B58", Tint = "#F6F3EE";

    static ReportBuilder() => PdfFonts.Setup();

    private static PageSize Paper => RegionInfo.CurrentRegion.TwoLetterISORegionName is "US" or "CA" ? PageSizes.Letter : PageSizes.A4;

    // ---- family group sheet ---------------------------------------------------------------------

    public static void FamilyGroupSheet(FamilyTree tree, Person p, string path)
    {
        var families = tree.FamiliesOf(p).ToList();
        Build(path, $"Family group sheet — {p.DisplayName}", col =>
        {
            if (families.Count == 0)
            {
                PersonBlock(col, tree, p, Role(p, "Person"));
                col.Item().PaddingTop(10).Text("No spouse or children recorded.").FontColor(Muted);
                return;
            }
            var first = true;
            foreach (var (family, spouse, children) in families)
            {
                if (!first) col.Item().PageBreak();
                first = false;
                var (a, b) = family.Partner1Id == p.Id ? (p, spouse) : (spouse, p);
                if (a != null) PersonBlock(col, tree, a, Role(a, "Husband"));
                if (b != null) PersonBlock(col, tree, b, Role(b, "Wife"));

                var facts = family.Events.Where(e => !e.Private).ToList();
                if (facts.Count > 0 || family.Status != CoupleStatus.Unspecified)
                {
                    Heading(col, "Marriage");
                    foreach (var e in facts) FactRow(col, EventTypes.Label(e), e);
                    if (family.Status is CoupleStatus.Divorced) FactRow(col, "Status", "Divorced");
                }

                Heading(col, children.Count == 0 ? "Children" : $"Children ({children.Count})");
                if (children.Count == 0) col.Item().Text("None recorded.").FontColor(Muted);
                else col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.ConstantColumn(22); c.RelativeColumn(3); c.ConstantColumn(28); c.RelativeColumn(3); c.RelativeColumn(3); c.RelativeColumn(2.5f); });
                    foreach (var h in new[] { "", "Name", "Sex", "Born", "Died", "Spouse" })
                        t.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(4).Text(h).FontSize(8.5f).FontColor(Faint).SemiBold();
                    var n = 0;
                    foreach (var c in children)
                    {
                        n++;
                        var spouses = string.Join(", ", tree.FamiliesOf(c).Select(f => f.Spouse?.DisplayName).OfType<string>());
                        void Cell(string text, bool bold = false)
                        {
                            var cell = t.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(5).PaddingRight(6);
                            var span = cell.Text(text).FontSize(9.5f);
                            if (bold) span.SemiBold();
                        }
                        Cell(n.ToString());
                        Cell(c.DisplayName, bold: true);
                        Cell(c.Sex switch { Sex.Male => "M", Sex.Female => "F", _ => "" });
                        Cell(Fact(c.Birth));
                        Cell(Fact(c.Death));
                        Cell(spouses);
                    }
                });
                if (family.Note.Trim().Length > 0)
                {
                    Heading(col, "Notes");
                    col.Item().Text(family.Note.Trim()).FontSize(9.5f).LineHeight(1.35f);
                }
            }
        });
    }

    private static string Role(Person p, string fallback) => p.Sex switch { Sex.Male => "Husband", Sex.Female => "Wife", _ => fallback };

    private static void PersonBlock(ColumnDescriptor col, FamilyTree tree, Person p, string role)
    {
        col.Item().PaddingTop(14).Background(Tint).Padding(12).Column(c =>
        {
            c.Item().Text(role.ToUpperInvariant()).FontSize(8).FontColor(Faint).SemiBold().LetterSpacing(0.08f);
            c.Item().Text(p.DisplayName).FontSize(15).SemiBold();
            var (father, mother) = tree.PrimaryParents(p);
            var parents = string.Join(" and ", new[] { father?.DisplayName, mother?.DisplayName }.OfType<string>());
            if (parents.Length > 0) c.Item().Text($"Child of {parents}").FontColor(Muted).FontSize(9.5f);
        });
        foreach (var e in p.Events.Where(e => !e.Private).OrderBy(e => e.Date.SortKey == 0 ? int.MaxValue : e.Date.SortKey))
            FactRow(col, EventTypes.Label(e), e);
        if (p.Note.Trim().Length > 0)
            col.Item().PaddingTop(4).PaddingLeft(12).Text(Shorten(p.Note.Trim(), 900)).FontSize(9).FontColor(Muted).LineHeight(1.3f);
    }

    // ---- ancestors -------------------------------------------------------------------------------

    public static void Ancestors(FamilyTree tree, Person p, string path, int generations = 8)
    {
        var people = tree.Ancestors(p, generations);
        Build(path, $"Ancestors of {p.DisplayName}", col =>
        {
            col.Item().PaddingBottom(4).Text("Each person is numbered: a person's father is twice their number, their mother twice plus one.")
               .FontSize(9).FontColor(Muted);
            for (var gen = 1; gen <= generations; gen++)
            {
                var from = 1 << (gen - 1);
                var inGen = people.Where(kv => kv.Key >= from && kv.Key < from * 2).OrderBy(kv => kv.Key).ToList();
                if (inGen.Count == 0) break;
                Heading(col, gen switch
                {
                    1 => "Generation 1",
                    2 => "Generation 2 — parents",
                    3 => "Generation 3 — grandparents",
                    4 => "Generation 4 — great-grandparents",
                    _ => $"Generation {gen} — {Ordinal(gen - 3)} great-grandparents",
                });
                foreach (var (n, person) in inGen)
                    col.Item().PaddingVertical(4).Row(row =>
                    {
                        row.ConstantItem(36).Text(n.ToString()).FontColor(Accent).SemiBold();
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(person.DisplayName).SemiBold().FontSize(11);
                            foreach (var line in LifeLines(person)) c.Item().Text(line).FontSize(9.5f).FontColor(Muted);
                            var parentNumbers = new[] { 2 * n, 2 * n + 1 }.Where(people.ContainsKey).ToList();
                            if (parentNumbers.Count > 0)
                                c.Item().Text($"Parents: {string.Join(" and ", parentNumbers.Select(x => $"{people[x].DisplayName} ({x})"))}")
                                 .FontSize(9).FontColor(Faint);
                        });
                    });
            }
        });
    }

    // ---- descendants -----------------------------------------------------------------------------

    public static void Descendants(FamilyTree tree, Person p, string path, int generations = 6)
    {
        Build(path, $"Descendants of {p.DisplayName}", col =>
        {
            var seen = new HashSet<long>();
            void Write(Person person, int depth, string number)
            {
                if (!seen.Add(person.Id)) return; // a pedigree loop or a cousin marriage
                col.Item().PaddingLeft(depth * 18).PaddingTop(depth == 0 ? 0 : 5).Column(c =>
                {
                    c.Item().Text(t =>
                    {
                        t.Span(number + "  ").FontColor(Accent).SemiBold();
                        t.Span(person.DisplayName).SemiBold();
                        if (person.Lifespan.Length > 0) t.Span($"  ({person.Lifespan})").FontColor(Muted);
                    });
                    foreach (var line in LifeLines(person)) c.Item().PaddingLeft(14).Text(line).FontSize(9).FontColor(Muted);
                });
                var families = tree.FamiliesOf(person).ToList();
                var childNo = 0;
                foreach (var (family, spouse, children) in families)
                {
                    if (spouse != null)
                        col.Item().PaddingLeft(depth * 18 + 14).PaddingTop(2).Text(t =>
                        {
                            t.DefaultTextStyle(x => x.FontSize(9.5f));
                            t.Span("m. ").FontColor(Faint);
                            t.Span(spouse.DisplayName);
                            if (spouse.Lifespan.Length > 0) t.Span($"  ({spouse.Lifespan})").FontColor(Muted);
                            if (family.Marriage is { } m && !m.Private && !m.Date.IsEmpty) t.Span($"  —  married {Display.Date(m.Date)}").FontColor(Faint);
                        });
                    if (depth + 1 >= generations) continue;
                    foreach (var child in children) Write(child, depth + 1, $"{number}.{++childNo}");
                }
            }
            Write(p, 0, "1");
        });
    }

    // ---- shared ----------------------------------------------------------------------------------

    private static void Build(string path, string title, Action<ColumnDescriptor> body)
    {
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(Paper);
            page.Margin(44);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor(Ink));
            page.Header().PaddingBottom(14).Column(h =>
            {
                h.Item().Text(title).FontSize(19).SemiBold();
                h.Item().Text($"Prepared {DateTime.Now:d MMMM yyyy} with Lineage Roots").FontSize(8.5f).FontColor(Faint);
                h.Item().PaddingTop(8).LineHorizontal(1).LineColor(Line);
            });
            page.Content().Column(body);
            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(8).FontColor(Faint));
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        })).GeneratePdf(path);
    }

    private static void Heading(ColumnDescriptor col, string text) =>
        col.Item().PaddingTop(16).PaddingBottom(4).Text(text.ToUpperInvariant()).FontSize(8.5f).SemiBold().FontColor(Accent).LetterSpacing(0.08f);

    private static void FactRow(ColumnDescriptor col, string label, Event e)
    {
        var detail = Display.Join(Display.Date(e.Date), e.Place, e.Value == "Y" ? "" : e.Value, e.Cause.Length > 0 ? $"cause: {e.Cause}" : "");
        if (detail.Length == 0) detail = "Yes";
        FactRow(col, label, detail);
    }

    private static void FactRow(ColumnDescriptor col, string label, string text) =>
        col.Item().PaddingLeft(12).PaddingVertical(2).Row(r =>
        {
            r.ConstantItem(110).Text(label).FontColor(Muted).FontSize(9.5f);
            r.RelativeItem().Text(text.Replace("  ·  ", ", ")).FontSize(9.5f);
        });

    private static string Fact(Event? e) => e == null || e.Private ? "" : Display.Join(Display.Date(e.Date), Display.FirstPart(e.Place)).Replace("  ·  ", ", ");

    private static IEnumerable<string> LifeLines(Person p)
    {
        foreach (var e in new[] { p.Birth, p.Death })
            if (e is { Private: false } && (!e.Date.IsEmpty || e.Place.Length > 0))
                yield return $"{EventTypes.Label(e)}: {Display.Join(Display.Date(e.Date), e.Place).Replace("  ·  ", ", ")}";
    }

    private static string Ordinal(int n) => n switch { 1 => "2nd", 2 => "3rd", _ => $"{n + 1}th" };

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";
}
