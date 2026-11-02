using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Lineage.Core.Model;
using Lineage.Core.Reports;

namespace Lineage.Core.Export;

/// <summary>Choices for <see cref="PhoneViewer.Build"/>.</summary>
public sealed class PhoneViewerOptions
{
    /// <summary>The name shown at the top, usually the tree's name.</summary>
    public string Title { get; set; } = "";
    /// <summary>Who the viewer opens on (the home person); null opens the list of names.</summary>
    public long? HomePersonId { get; set; }
    public bool IncludeNotes { get; set; } = true;
}

/// <summary>
/// The family as one web page for a phone: the whole tree is written into the page, so it opens from a downloaded file,
/// an email attachment or a cloud drive with no internet and nothing to install. Read only: search by name, a page for
/// each person (life events, family, notes) and their ancestors three generations back. Filter the tree for privacy
/// before building it; everything passed in ends up in the file.
/// </summary>
public static class PhoneViewer
{
    /// <summary>The page. <paramref name="translate"/> turns its English labels into the program's language.</summary>
    public static string Build(FamilyTree tree, PhoneViewerOptions options, Func<string, string>? translate = null)
    {
        var t = translate ?? (s => s);
        var people = tree.People.Values.Select(p => Entry(tree, p, options, t)).ToList();
        var data = new
        {
            title = options.Title.Length > 0 ? options.Title : t("Family tree"),
            home = options.HomePersonId is { } h && tree.Person(h) != null ? h : (long?)null,
            made = DateTime.Today.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
            people,
            words = Words.ToDictionary(w => w, t),
        };
        // The default encoder escapes <, >, & and quotes, so nothing in a name or note can break out of the script.
        var json = JsonSerializer.Serialize(data);
        return Template.Replace("/*DATA*/", json).Replace("<!--TITLE-->", WebUtility.HtmlEncode(data.title));
    }

    /// <summary>The labels the page shows, in English; each is passed through the translation when the page is built.</summary>
    internal static readonly string[] Words =
    {
        "Family tree", "Search names", "person", "people", "Home person", "Parents", "Brothers and sisters", "Children", "Child",
        "Family", "With", "Married", "Life events", "Notes", "Ancestors", "Also known as", "No one matches that name.",
        "All names", "Made with Lineage Roots on", "Unknown", "Show more", "Father", "Mother", "Grandfather", "Grandmother",
    };

    private static object Entry(FamilyTree tree, Person p, PhoneViewerOptions options, Func<string, string> t)
    {
        var events = StoryWriter.Visible(p.Events).Select(e => (Event: e, With: (Person?)null)).ToList();
        foreach (var (family, spouse, _) in tree.FamiliesOf(p))
            events.AddRange(StoryWriter.Visible(family.Events).Select(e => (Event: e, With: spouse)));
        var timeline = events.Select((x, i) => (x, i))
            .OrderBy(y => y.x.Event.Date.SortKey == 0 ? int.MaxValue : y.x.Event.Date.SortKey).ThenBy(y => y.i)
            .Select(y => new
            {
                l = t(EventTypes.Label(y.x.Event)),
                d = ReportText.Date(y.x.Event.Date),
                p = y.x.Event.Place.Trim(),
                v = StoryWriter.Value(y.x.Event),
                w = y.x.With?.Id,
            }).ToList();
        var families = tree.FamiliesOf(p).Select(f => new
        {
            s = f.Spouse?.Id,
            m = StoryWriter.Visible(f.Family.Events).FirstOrDefault(e => e.Tag == "MARR") is { } m
                ? string.Join(" · ", new[] { ReportText.Date(m.Date), m.Place.Trim() }.Where(x => x.Length > 0)) : "",
            c = f.Children.Select(c => c.Id).ToArray(),
        }).ToList();
        var siblings = p.ParentFamilyIds.Select(id => tree.Family(id)).OfType<Family>().SelectMany(tree.ChildrenOf)
            .Where(c => c.Id != p.Id).Distinct()
            .OrderBy(c => c.Birth is { } b && b.Date.SortKey > 0 ? b.Date.SortKey : int.MaxValue).Select(c => c.Id).ToArray();
        return new
        {
            i = p.Id,
            n = p.DisplayName,
            k = SortKey(p),
            y = p.Lifespan,
            x = p.Sex switch { Sex.Male => "m", Sex.Female => "f", _ => "" },
            a = p.Names.Skip(1).Select(n => n.Full).Where(n => n.Length > 0 && n != p.DisplayName).Distinct().ToArray(),
            o = Calling(p),
            b = StoryWriter.Find(p, "BIRT")?.Place.Trim() ?? StoryWriter.Find(p, "CHR")?.Place.Trim() ?? "",
            r = tree.ParentsOf(p).Distinct().Select(x => x.Id).ToArray(),
            f = families,
            g = siblings,
            e = timeline,
            t = options.IncludeNotes ? NoteFormat.Html(p.Note) : "",   // formatted and already escaped
        };
    }

    /// <summary>Title and latest occupation ("Rev. · Vicar"), which tell apart relatives with the same name.</summary>
    private static string Calling(Person p)
    {
        var job = p.Events.Where(e => e.Tag == "OCCU" && (e.Value.Trim().Length > 0 || e.Description.Trim().Length > 0))
            .OrderBy(e => e.Date.SortKey).LastOrDefault();
        var work = job == null ? "" : job.Value.Trim().Length > 0 ? job.Value.Trim() : job.Description.Trim();
        return string.Join(" · ", new[] { p.Title.Trim(), work }.Where(x => x.Length > 0));
    }

    /// <summary>Surname first, so the list reads like an index.</summary>
    private static string SortKey(Person p) =>
        $"{p.PrimaryName.Surname.Trim()} {p.PrimaryName.Given.Trim()}".Trim().ToLowerInvariant();

    private const string Template = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="color-scheme" content="light dark">
<title><!--TITLE--></title>
<style>
:root { --bg:#F6F3EE; --card:#FFFFFF; --ink:#22252A; --muted:#6B6F76; --line:#E4DED4; --accent:#2F6F5E; --soft:#E3EFEA; --m:#3D6FA8; --f:#A8476B; }
@media (prefers-color-scheme: dark) { :root { --bg:#16181B; --card:#1F2226; --ink:#E8E6E1; --muted:#9A9EA5; --line:#30343A; --accent:#7CC3A8; --soft:#21332D; --m:#7FA8D8; --f:#D88AA7; } }
* { box-sizing:border-box; }
body { margin:0; background:var(--bg); color:var(--ink); font:16px/1.45 -apple-system, "Segoe UI", Roboto, system-ui, sans-serif; -webkit-text-size-adjust:100%; }
header { position:sticky; top:0; z-index:2; background:var(--bg); padding:calc(10px + env(safe-area-inset-top)) 16px 10px; border-bottom:1px solid var(--line); }
header .row { display:flex; align-items:center; gap:10px; }
header h1 { font-size:17px; margin:0; flex:1; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
button, .btn { font:inherit; color:var(--accent); background:none; border:1px solid var(--line); border-radius:10px; padding:6px 12px; cursor:pointer; text-decoration:none; }
input[type=search] { width:100%; margin-top:10px; font:inherit; padding:10px 12px; border-radius:12px; border:1px solid var(--line); background:var(--card); color:var(--ink); }
main { padding:12px 16px calc(24px + env(safe-area-inset-bottom)); max-width:720px; margin:0 auto; }
.card { background:var(--card); border:1px solid var(--line); border-radius:16px; padding:14px 16px; margin:0 0 12px; }
h2 { font-size:12px; letter-spacing:.08em; text-transform:uppercase; color:var(--muted); margin:0 0 8px; }
h3 { font-size:15px; margin:12px 0 4px; }
.name { font-size:24px; font-weight:700; margin:0; line-height:1.2; }
.muted { color:var(--muted); }
.small { font-size:13px; }
ul.people { list-style:none; margin:0; padding:0; }
ul.people li a, a.person { display:flex; align-items:center; gap:10px; padding:9px 0; color:var(--ink); text-decoration:none; border-bottom:1px solid var(--line); }
ul.people li:last-child a { border-bottom:none; }
.dot { width:34px; height:34px; border-radius:50%; background:var(--soft); color:var(--accent); display:flex; align-items:center; justify-content:center; font-size:13px; font-weight:700; flex:none; }
.dot.m { color:var(--m); } .dot.f { color:var(--f); }
.who { display:flex; flex-direction:column; min-width:0; }
.who b { font-weight:600; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
ol.events { list-style:none; margin:0; padding:0; }
ol.events li { padding:8px 0; border-bottom:1px solid var(--line); }
ol.events li:last-child { border-bottom:none; }
.note p { margin:0 0 8px; } .note ul { margin:0 0 8px; padding-left:20px; } .note h3 { font-size:15px; margin:10px 0 4px; }
.tree { display:grid; grid-template-columns:1fr 1fr; gap:8px; }
.tree a { display:block; padding:8px 10px; border-radius:12px; background:var(--soft); color:var(--ink); text-decoration:none; font-size:14px; }
.tree a.none { opacity:.45; pointer-events:none; }
.tree .gen { grid-column:1 / -1; font-size:12px; color:var(--muted); margin-top:4px; }
a { color:var(--accent); }
footer { text-align:center; font-size:12px; color:var(--muted); margin-top:16px; }
</style>
</head>
<body>
<header><div class="row"><button id="all" hidden>☰</button><h1 id="title"></h1><button id="home" hidden>⌂</button></div>
<input id="q" type="search" autocomplete="off"></header>
<main id="main"></main>
<script>
const D = /*DATA*/;
const W = s => D.words[s] || s;
const byId = new Map(D.people.map(p => [p.i, p]));
const sorted = D.people.slice().sort((a, b) => a.k.localeCompare(b.k));
const $ = id => document.getElementById(id);
const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const initials = p => (p.n.match(/\b\p{L}/gu) || ["?"]).slice(0, 2).join("").toUpperCase();
function row(p) {
  if (!p) return "";
  const sub = [p.y, p.b].filter(Boolean).join(" · ");
  return `<li><a href="#p${p.i}"><span class="dot ${p.x}">${esc(initials(p))}</span><span class="who"><b>${esc(p.n)}</b>${sub ? `<span class="muted small">${esc(sub)}</span>` : ""}</span></a></li>`;
}
function list(ids) { return `<ul class="people">${ids.map(id => row(byId.get(id))).join("")}</ul>`; }
let shown = 200;
function showList() {
  const q = $("q").value.trim().toLowerCase();
  const words = q.split(/\s+/).filter(Boolean);
  const hits = words.length ? sorted.filter(p => words.every(w => p.n.toLowerCase().includes(w) || p.a.some(a => a.toLowerCase().includes(w)) || (p.y || "").includes(w))) : sorted;
  let html = `<p class="muted small">${hits.length.toLocaleString()} ${esc(W(hits.length === 1 ? "person" : "people"))}</p>`;
  if (!hits.length) html += `<div class="card">${esc(W("No one matches that name."))}</div>`;
  else html += `<div class="card">${list(hits.slice(0, shown).map(p => p.i))}${hits.length > shown ? `<p><button id="more">${esc(W("Show more"))}</button></p>` : ""}</div>`;
  html += `<footer>${esc(W("Made with Lineage Roots on"))} ${esc(D.made)}</footer>`;
  $("main").innerHTML = html;
  const more = $("more"); if (more) more.onclick = () => { shown += 400; showList(); };
}
function box(id, label) {
  const p = byId.get(id);
  return p ? `<a href="#p${p.i}"><span class="muted small">${esc(label)}</span><br><b>${esc(p.n)}</b><br><span class="muted small">${esc(p.y)}</span></a>`
           : `<a class="none"><span class="muted small">${esc(label)}</span><br>${esc(W("Unknown"))}</a>`;
}
function parentsOf(id) { const p = byId.get(id); return p ? p.r : []; }
function father(id) { return parentsOf(id).find(x => byId.get(x)?.x === "m") ?? parentsOf(id)[0]; }
function mother(id) { return parentsOf(id).find(x => x !== father(id)); }
function showPerson(p) {
  let h = `<div class="card"><p class="name">${esc(p.n)}</p>`;
  if (p.y) h += `<p class="muted" style="margin:4px 0 0">${esc(p.y)}</p>`;
  if (p.o) h += `<p style="margin:4px 0 0">${esc(p.o)}</p>`;
  if (p.a.length) h += `<p class="muted small" style="margin:6px 0 0">${esc(W("Also known as"))} ${esc(p.a.join(", "))}</p>`;
  if (D.home === p.i) h += `<p class="small" style="margin:6px 0 0;color:var(--accent)">${esc(W("Home person"))}</p>`;
  h += `</div>`;
  if (p.e.length) {
    h += `<div class="card"><h2>${esc(W("Life events"))}</h2><ol class="events">`;
    for (const e of p.e) {
      const w = e.w != null ? byId.get(e.w) : null;
      h += `<li><b>${esc(e.l)}</b>${w ? ` · ${esc(W("With"))} <a href="#p${w.i}">${esc(w.n)}</a>` : ""}`;
      const where = [e.d, e.p].filter(Boolean).join(" · ");
      if (where) h += `<div class="muted small">${esc(where)}</div>`;
      if (e.v) h += `<div class="small">${esc(e.v)}</div>`;
      h += `</li>`;
    }
    h += `</ol></div>`;
  }
  if (p.r.length || p.f.length || p.g.length) {
    h += `<div class="card"><h2>${esc(W("Family"))}</h2>`;
    if (p.r.length) h += `<h3>${esc(W("Parents"))}</h3>${list(p.r)}`;
    for (const f of p.f) {
      const s = f.s != null ? byId.get(f.s) : null;
      h += `<h3>${s ? `${esc(W("With"))} ${esc(s.n)}` : esc(W("Family"))}</h3>`;
      if (s) h += list([s.i]);
      if (f.m) h += `<p class="muted small" style="margin:4px 0">${esc(W("Married"))} ${esc(f.m)}</p>`;
      if (f.c.length) h += `<p class="muted small" style="margin:8px 0 0">${esc(W(f.c.length === 1 ? "Child" : "Children"))}</p>${list(f.c)}`;
    }
    if (p.g.length) h += `<h3>${esc(W("Brothers and sisters"))}</h3>${list(p.g)}`;
    h += `</div>`;
  }
  if (p.r.length) {
    const f = father(p.i), m = mother(p.i);
    h += `<div class="card"><h2>${esc(W("Ancestors"))}</h2><div class="tree">`;
    h += box(f, W("Father")) + box(m, W("Mother"));
    h += `<div class="gen"></div>`;
    h += box(father(f), W("Grandfather")) + box(mother(f), W("Grandmother")) + box(father(m), W("Grandfather")) + box(mother(m), W("Grandmother"));
    h += `</div></div>`;
  }
  if (p.t) h += `<div class="card"><h2>${esc(W("Notes"))}</h2><div class="note">${p.t}</div></div>`;
  $("main").innerHTML = h;
  window.scrollTo(0, 0);
}
function route() {
  const m = location.hash.match(/^#p(-?\d+)$/);
  const p = m ? byId.get(Number(m[1])) : null;
  $("q").hidden = !!p; $("all").hidden = !p; $("home").hidden = D.home == null || (p && p.i === D.home);
  if (p) { $("title").textContent = p.n; document.title = p.n; showPerson(p); }
  else { $("title").textContent = D.title; document.title = D.title; showList(); }
}
$("q").placeholder = W("Search names");
$("q").addEventListener("input", () => { shown = 200; showList(); });
// The phone's own back button goes back; this one always leads to the list of names.
$("all").onclick = () => location.hash = "#all";
$("all").title = W("All names");
$("home").onclick = () => location.hash = "#p" + D.home;
$("home").title = W("Home person");
window.addEventListener("hashchange", route);
if (!location.hash && D.home != null) location.replace("#p" + D.home);
route();
</script>
</body>
</html>
""";
}
