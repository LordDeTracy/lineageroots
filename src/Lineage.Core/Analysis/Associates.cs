using Lineage.Core.Model;

namespace Lineage.Core.Analysis;

/// <summary>One link between a person and someone outside their family tree lines.</summary>
/// <param name="How">"Neighbour", "Witness at the marriage of George Whitfield and Mary Ann Cole"…</param>
/// <param name="Year">The year of the event behind it, or 0.</param>
/// <param name="Theirs">True when the link is recorded on the other person (or their event), not on this one.</param>
public sealed record Associate(Person Other, string How, int Year, bool Theirs);

/// <summary>
/// A person's FAN club — friends, associates and neighbours — gathered from both directions: the associations recorded
/// on either person, the people at this person's events, and the events this person attended for others.
/// </summary>
public static class Associates
{
    public static List<Associate> Of(FamilyTree tree, Person p)
    {
        var result = new List<Associate>();
        foreach (var a in p.Associations)
            if (tree.Person(a.PersonId) is { } other) result.Add(new Associate(other, Word(a.Relation, "Associate"), 0, false));
        foreach (var other in tree.People.Values)
            foreach (var a in other.Associations.Where(a => a.PersonId == p.Id))
                if (p.Associations.All(mine => mine.PersonId != other.Id))
                    result.Add(new Associate(other, Word(a.Relation, "Associate"), 0, true));

        // People at this person's events, and this person at other people's.
        foreach (var (e, owner) in Events(tree))
        {
            var mine = owner.Contains(p.Id);
            foreach (var r in e.Roles)
            {
                var year = e.Date.Kind == DateKind.Text ? 0 : e.Date.Year;
                if (mine && tree.Person(r.PersonId) is { } guest)
                    result.Add(new Associate(guest, $"{Word(r.Role, "Witness")} at {Possessive(owner)} {EventTypes.Label(e).ToLowerInvariant()}", year, false));
                else if (r.PersonId == p.Id && !mine)
                    result.Add(new Associate(tree.Person(owner[0])!, $"{p.DisplayName} was {Word(r.Role, "Witness").ToLowerInvariant()} at the {EventTypes.Label(e).ToLowerInvariant()} of {Names(owner, tree)}", year, true));
            }
        }
        return result.OrderBy(a => a.Year == 0 ? int.MaxValue : a.Year).ThenBy(a => a.Other.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Every event in the tree with the people it belongs to (one person, or a couple).</summary>
    private static IEnumerable<(Event Event, List<long> Owner)> Events(FamilyTree tree)
    {
        foreach (var person in tree.People.Values)
            foreach (var e in person.Events.Where(e => e.Roles.Count > 0)) yield return (e, new List<long> { person.Id });
        foreach (var f in tree.Families.Values)
        {
            var couple = f.PartnerIds.Where(id => tree.Person(id) != null).ToList();
            if (couple.Count == 0) continue;
            foreach (var e in f.Events.Where(e => e.Roles.Count > 0)) yield return (e, couple);
        }
    }

    private static string Possessive(List<long> owner) =>
        owner.Count == 1 ? "their" : $"the {(owner.Count == 2 ? "couple’s" : "family’s")}";

    private static string Names(List<long> ids, FamilyTree tree) =>
        string.Join(" and ", ids.Select(id => tree.Person(id)?.DisplayName).OfType<string>());

    private static string Word(string text, string otherwise) => text.Trim().Length > 0 ? text.Trim() : otherwise;
}
