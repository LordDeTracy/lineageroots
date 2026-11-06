using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lineage.Core.Model;

namespace Lineage.Core.Reports;

/// <summary>
/// The wording of life stories in one language. Each kind of sentence is a template with gaps, so a language can put
/// the words in its own order ("Er wurde am 14. März 1842 in Harrow geboren"): {S} the person (a name or a pronoun),
/// {N} the same in the plain form where {S} is possessive, {W} when and where, {P} parents, {X} a spouse, and so on.
/// A template can have forms for a man (.m), a woman (.f), someone whose sex isn't known (.u), and "they" (.they);
/// the plain id is used when there's no special form. English is built in; other languages come from
/// Reports/Story/xx.json, and anything a language leaves out is written in English.
/// </summary>
public sealed class StoryLanguage
{
    private readonly Dictionary<string, string> _t;
    private readonly StoryLanguage? _fallback;

    private StoryLanguage(string code, Dictionary<string, string> templates, StoryLanguage? fallback)
    {
        Code = code;
        _t = templates;
        _fallback = fallback;
    }

    public string Code { get; }

    /// <summary>Translates other words a story uses (event names); the program sets it to its own translator.</summary>
    public Func<string, string> Words { get; set; } = s => s;

    public static StoryLanguage English { get; } = new("en", EnglishTemplates(), null);

    /// <summary>The language stories are written in; the program sets it from Settings.</summary>
    public static StoryLanguage Current { get; set; } = English;

    /// <summary>The wording for a language code ("sv", "de"…); English when there's none.</summary>
    public static StoryLanguage For(string code)
    {
        if (code is "en" or "") return English;
        try
        {
            using var stream = typeof(StoryLanguage).Assembly.GetManifestResourceStream($"Lineage.Core.Reports.Story.{code}.json");
            if (stream == null) return English;
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
            return new StoryLanguage(code, map, English);
        }
        catch (JsonException) { return English; }
    }

    /// <summary>The template for <paramref name="id"/> in the given form ("m", "f", "u" or "they").</summary>
    public string Template(string id, string form = "")
    {
        for (var lang = this; lang != null; lang = lang._fallback)
        {
            if (form.Length > 0 && lang._t.TryGetValue($"{id}.{form}", out var t)) return t;
            if (form == "they" && lang._t.TryGetValue($"{id}.u", out t)) return t;
            if (lang._t.TryGetValue(id, out t)) return t;
        }
        return "";
    }

    /// <summary>Whether this language has its own wording for <paramref name="id"/>.</summary>
    public bool Has(string id) => _t.ContainsKey(id);

    /// <summary>A template with its {0} filled in.</summary>
    public string Format(string id, string value, string form = "") => Template(id, form).Replace("{0}", value);

    public static string Form(Sex sex) => sex switch { Sex.Male => "m", Sex.Female => "f", _ => "u" };

    public string Subject(Sex sex) => Template("subject", Form(sex));
    public string PossessivePronoun(Sex sex) => Template("possessive", Form(sex));
    public string Possessive(string name) =>
        Code == "en" ? ReportText.Possessive(name) : Format(name.EndsWith('s') ? "possessive.name.s" : "possessive.name", name);

    public string Ordinal(int n) => n is >= 1 and <= 10 ? Template($"ordinal.{n}") : Code == "en" ? ReportText.Ordinal(n) : n.ToString(CultureInfo.InvariantCulture) + ".";

    /// <summary>An occupation after "worked": " as a stonemason".</summary>
    public string As(string job) => Format("work.as", Code == "en" ? ReportText.WithArticle(job) : job);

    /// <summary>An event's name inside a sentence ("his religion was recorded as…").</summary>
    public string Noun(string englishLabel)
    {
        var word = Code == "en" ? englishLabel : Words(englishLabel);
        return Code == "de" ? word : ReportText.LowerFirst(word);
    }

    // ---- dates and ages ----------------------------------------------------------------------------

    /// <summary>A date inside a sentence: "on 14 March 1842", "am 14. März 1842", "в 1842 г.".</summary>
    public string DatePhrase(GenDate d)
    {
        if (Code == "en") return ReportText.DatePhrase(d);
        if (d.IsEmpty) return "";
        if (d.Kind == DateKind.Text) return char.IsDigit(d.Text[0]) ? Format("date.on", d.Text) : d.Text;
        if (d.Kind == DateKind.Simple && d.Qualifier == DateQualifier.None)
            return d.Day > 0 ? Format("date.on", Core(d)) : d.Month > 0 ? Fill(Template("date.in.month"), d) : Fill(Template("date.in.year"), d);
        return Date(d);
    }

    /// <summary>A date on its own: "14 March 1842", "about 1850".</summary>
    public string Date(GenDate d)
    {
        if (Code == "en") return ReportText.Date(d);
        if (d.IsEmpty) return "";
        if (d.Kind == DateKind.Text) return d.Text;
        var core = Core(d);
        return d.Kind switch
        {
            DateKind.Range => d.End is { IsEmpty: false } e ? Template("date.between").Replace("{0}", core).Replace("{1}", Core(e)) : core,
            DateKind.Period => d.End is { IsEmpty: false } e ? Template("date.fromto").Replace("{0}", core).Replace("{1}", Core(e)) : Format("date.from", core),
            _ => d.Qualifier switch
            {
                DateQualifier.About or DateQualifier.Calculated or DateQualifier.Estimated => Format("date.about", core),
                DateQualifier.Before => Format("date.before", core),
                DateQualifier.After => Format("date.after", core),
                _ => core,
            },
        };
    }

    private string Core(GenDate d) =>
        Fill(Template(d.Day > 0 && d.Month > 0 ? "date.dmy" : d.Month is > 0 and <= 12 ? "date.my" : "date.y"), d);

    /// <summary>Fills {d} day, {m} month, {mg} month after a day, {mp} month after "in", {y} year.</summary>
    private string Fill(string template, GenDate d)
    {
        var month = d.Month is > 0 and <= 12 ? d.Month : 0;
        string M(string kind) => month == 0 ? "" : Template($"{kind}.{month}") is { Length: > 0 } m ? m : Template($"month.{month}");
        var year = d.Year > 0 ? d.Year.ToString(CultureInfo.InvariantCulture) : "";
        if (d.DualYear > 0) year += "/" + (d.DualYear % 100).ToString("00", CultureInfo.InvariantCulture);
        var day = d.DualDay > 0 ? $"{d.Day}/{d.DualDay}" : d.Day.ToString(CultureInfo.InvariantCulture);
        return template.Replace("{d}", day).Replace("{mg}", M("month.day")).Replace("{mp}", M("month.in"))
                       .Replace("{m}", M("month")).Replace("{y}", year).Trim();
    }

    private static readonly Regex AgeWords = new(@"^aged (about )?(\d+)( days?| months?)?$", RegexOptions.CultureInvariant);

    /// <summary>"aged 69", "69 Jahre alt", or "" when the dates don't allow it.</summary>
    public string Age(Event? birth, GenDate death)
    {
        var english = ReportText.Age(birth, death);
        if (Code == "en" || english.Length == 0) return english;
        var m = AgeWords.Match(english);
        if (!m.Success) return english;
        var n = m.Groups[2].Value;
        var unit = m.Groups[3].Value.Trim();
        var id = unit.StartsWith("day") ? "age.days" : unit.StartsWith("month") ? "age.months" : m.Groups[1].Success ? "age.about" : "age.years";
        // "one" is exactly 1; "ones" ends in 1 (21, 31, not 11) and "few" in 2–4 (not 12–14), for languages that count that way.
        var teen = n.Length > 1 && n[^2] == '1';
        var form = n == "1" ? "one" : !teen && n[^1] == '1' ? "ones" : !teen && n[^1] is '2' or '3' or '4' ? "few" : "";
        if (form == "one" && !Has($"{id}.one")) form = "ones";
        return (form.Length > 0 && Has($"{id}.{form}") ? Template(id, form) : Template(id)).Replace("{0}", n);
    }

    // ---- English -----------------------------------------------------------------------------------

    private static Dictionary<string, string> EnglishTemplates() => new()
    {
        ["subject.m"] = "he", ["subject.f"] = "she", ["subject.u"] = "they",
        ["possessive.m"] = "his", ["possessive.f"] = "her", ["possessive.u"] = "their",
        ["ordinal.1"] = "first", ["ordinal.2"] = "second", ["ordinal.3"] = "third", ["ordinal.4"] = "fourth", ["ordinal.5"] = "fifth",
        ["ordinal.6"] = "sixth", ["ordinal.7"] = "seventh", ["ordinal.8"] = "eighth", ["ordinal.9"] = "ninth", ["ordinal.10"] = "tenth",
        ["and"] = " and ",
        ["place.in"] = " in {0}", ["place.from"] = " from {0}", ["place.to"] = " to {0}",

        ["born"] = "{S} was born{W}", ["born.they"] = "{S} were born{W}",
        ["born.parents.m"] = "{S} was born{W}, the son of {P}", ["born.parents.f"] = "{S} was born{W}, the daughter of {P}",
        ["born.parents.u"] = "{S} was born{W}, the child of {P}", ["born.parents.they"] = "{S} were born{W}, the child of {P}",
        ["child.m"] = "{S} was the son of {P}", ["child.f"] = "{S} was the daughter of {P}",
        ["child.u"] = "{S} was the child of {P}", ["child.they"] = "{S} were the child of {P}",
        ["christened"] = "{S} was christened{W}", ["christened.they"] = "{S} were christened{W}",
        ["baptized"] = "{S} was baptized{W}", ["baptized.they"] = "{S} were baptized{W}",

        ["partners"] = "{S} and {X} were partners",
        ["married"] = "{S} married{O} {X}{XP}{K}{W}",
        ["married.alone"] = "{S} married{O}{W}",
        ["married.nth"] = ", {0},",
        ["spouse.parents.m"] = ", the son of {P}", ["spouse.parents.f"] = ", the daughter of {P}", ["spouse.parents.u"] = ", the child of {P}",
        ["divorced"] = "They divorced{W}",
        ["annulled"] = "The marriage was annulled{W}",

        ["clause.born"] = " was born{W}", ["clause.christened"] = " was christened{W}",
        ["clause.died"] = " died{W}{A}", ["clause.buried"] = " was buried{W}",
        ["clause.and"] = " and",

        ["work"] = "{S} worked{J}", ["work.also"] = "{S} also worked{J}", ["work.as"] = " as {0}",
        ["lived"] = "{S} lived in {L}",
        ["census"] = "{S} was recorded in the census in {L}", ["census.they"] = "{S} were recorded in the census in {L}",

        ["ev.ADOP"] = "{S} was adopted{W}", ["ev.ADOP.they"] = "{S} were adopted{W}",
        ["ev.CONF"] = "{S} was confirmed{W}", ["ev.CONF.they"] = "{S} were confirmed{W}",
        ["ev.ORDN"] = "{S} was ordained{W}", ["ev.ORDN.they"] = "{S} were ordained{W}",
        ["ev.BLES"] = "{S} was blessed{W}", ["ev.BLES.they"] = "{S} were blessed{W}",
        ["ev.CHRA"] = "{S} was christened as an adult{W}", ["ev.CHRA.they"] = "{S} were christened as an adult{W}",
        ["ev.NATU"] = "{S} was naturalized{W}", ["ev.NATU.they"] = "{S} were naturalized{W}",
        ["ev.EDUC"] = "{S} was educated{W}", ["ev.EDUC.they"] = "{S} were educated{W}",
        ["ev.EDUC.v"] = "{S} was educated at {V}{W}", ["ev.EDUC.v.they"] = "{S} were educated at {V}{W}",
        ["ev.DSCR.v"] = "{S} was described as {V}{W}", ["ev.DSCR.v.they"] = "{S} were described as {V}{W}",
        ["ev.FCOM.m"] = "{S} made his first communion{W}", ["ev.FCOM.f"] = "{S} made her first communion{W}", ["ev.FCOM.u"] = "{S} made their first communion{W}",
        ["ev.BARM.m"] = "{S} celebrated his bar mitzvah{W}", ["ev.BARM.f"] = "{S} celebrated her bar mitzvah{W}", ["ev.BARM.u"] = "{S} celebrated their bar mitzvah{W}",
        ["ev.BASM.m"] = "{S} celebrated his bat mitzvah{W}", ["ev.BASM.f"] = "{S} celebrated her bat mitzvah{W}", ["ev.BASM.u"] = "{S} celebrated their bat mitzvah{W}",
        ["ev.WILL.m"] = "{S} made his will{W}", ["ev.WILL.f"] = "{S} made her will{W}", ["ev.WILL.u"] = "{S} made their will{W}",
        ["ev.EMIG"] = "{S} emigrated{W}", ["ev.IMMI"] = "{S} immigrated{W}",
        ["ev.GRAD"] = "{S} graduated{W}", ["ev.GRAD.v"] = "{S} graduated from {V}{W}",
        ["ev.RETI"] = "{S} retired{W}",
        ["ev.MILI"] = "{S} served in the military{W}", ["ev.MILI.v"] = "{S} served in the military ({V}){W}",
        ["ev.PROP"] = "{S} owned property{W}", ["ev.PROP.v"] = "{S} owned property ({V}){W}",
        ["ev.NCHI.v"] = "{S} had {V} children{W}",
        ["ev.recorded"] = "{S} {L} was recorded{V}{W}",
        ["ev.recorded.as"] = "{S} {L} was recorded as {V}{W}",
        ["ev.PROB"] = "{S} will was proved{V}{W}",
        ["value"] = " ({0})",

        ["died"] = "{S} died{C}{W}{A}{B}",
        ["died.of"] = " of {0}",
        ["died.age"] = ", {0}",
        ["died.buried"] = ", and was buried{W}", ["died.buried.they"] = ", and were buried{W}",
        ["died.cremated"] = ", and was cremated{W}", ["died.cremated.they"] = ", and were cremated{W}",
        ["buried"] = "{S} was buried{W}", ["buried.they"] = "{S} were buried{W}",
        ["cremated"] = "{S} was cremated{W}", ["cremated.they"] = "{S} were cremated{W}",
        ["name"] = "{S}",
    };
}
