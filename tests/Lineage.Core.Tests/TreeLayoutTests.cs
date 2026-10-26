using System.Diagnostics;
using Lineage.Core.Charts;
using Lineage.Core.Model;

namespace Lineage.Core.Tests;

public class TreeLayoutTests
{
    // ---- invented families --------------------------------------------------------------------

    private static Person Add(FamilyTree t, string given, string surname, Sex sex)
    {
        var p = t.AddPerson(new Person { Sex = sex });
        p.Names.Add(new Name { Given = given, Surname = surname });
        return p;
    }

    private static Family Marry(FamilyTree t, Person? husband, Person? wife)
    {
        var f = t.AddFamily(new Family());
        if (husband != null) t.SetPartner(f, husband, second: false);
        if (wife != null) t.SetPartner(f, wife, second: true);
        return f;
    }

    private static List<Person> Children(FamilyTree t, Family f, int count, string surname)
    {
        var list = new List<Person>();
        for (var i = 0; i < count; i++)
        {
            var c = Add(t, $"Child{f.Id}-{i}", surname, i % 2 == 0 ? Sex.Male : Sex.Female);
            t.AddChild(f, c);
            list.Add(c);
        }
        return list;
    }

    /// <summary>Uneven branching: some childless, some with big families, every child married.</summary>
    private static (FamilyTree Tree, Person Root) Branching(int generations, Func<int, int, int> childrenOf)
    {
        var t = new FamilyTree();
        var root = Add(t, "Root", "Ashdown", Sex.Male);
        var level = new List<Person> { root };
        for (var g = 0; g < generations - 1; g++)
        {
            var next = new List<Person>();
            for (var i = 0; i < level.Count; i++)
            {
                var p = level[i];
                var spouse = Add(t, "Spouse", "Fairley", p.Sex == Sex.Male ? Sex.Female : Sex.Male);
                var f = p.Sex == Sex.Male ? Marry(t, p, spouse) : Marry(t, spouse, p);
                next.AddRange(Children(t, f, childrenOf(g, i), "Ashdown"));
            }
            level = next;
        }
        return (t, root);
    }

    private static void AssertNoOverlaps(TreeLayoutResult layout)
    {
        foreach (var row in layout.Nodes.GroupBy(n => n.Generation))
        {
            var sorted = row.OrderBy(n => n.X).ToList();
            for (var i = 1; i < sorted.Count; i++)
                Assert.True(sorted[i].X >= sorted[i - 1].Bounds.Right + layout.Options.SiblingGap - 1e-6,
                            $"boxes overlap in generation {row.Key}");
        }
        // Different rows never share vertical space either.
        foreach (var a in layout.Nodes.GroupBy(n => n.Generation))
            foreach (var b in layout.Nodes.GroupBy(n => n.Generation).Where(b => b.Key > a.Key))
                Assert.True(b.First().Y >= a.First().Bounds.Bottom);
        Assert.All(layout.Nodes, n => Assert.True(n.X >= 0 && n.Bounds.Right <= layout.Width + 1e-6 && n.Bounds.Bottom <= layout.Height + 1e-6));
    }

    // ---- descendants ----------------------------------------------------------------------------

    [Fact]
    public void NoBoxesOverlap()
    {
        // 0, 1, 4, 0, 2, 3, ... children: lopsided subtrees are where naive layouts collide.
        var (tree, root) = Branching(5, (g, i) => g == 0 ? 4 : (g * 7 + i * 3) % 5);
        var layout = TreeLayout.Descendants(tree, root.Id, 5);
        Assert.True(layout.Nodes.Count > 20);
        AssertNoOverlaps(layout);
    }

    [Fact]
    public void ParentsAreCentredOverTheirChildren()
    {
        var (tree, root) = Branching(4, (g, i) => 1 + (g + i) % 4);
        var layout = TreeLayout.Descendants(tree, root.Id, 4);
        foreach (var parent in layout.Nodes)
        {
            var kids = layout.Nodes.Where(n => n.Parent == parent).ToList();
            if (kids.Count == 0) continue;
            var middle = (kids.Min(k => k.ConnectX) + kids.Max(k => k.ConnectX)) / 2;
            Assert.Equal(parent.Bounds.CenterX, middle, 6);           // one marriage: hangs from the couple's middle
            Assert.All(kids, k => Assert.Equal(parent.Bounds.CenterX, k.ParentAnchorX, 6));
            Assert.All(kids, k => Assert.Equal(k.SlotBounds(0).CenterX, k.ConnectX, 6)); // lines reach the descendant, not the spouse
        }
    }

    [Fact]
    public void EachGenerationIsARow()
    {
        var (tree, root) = Branching(4, (_, _) => 2);
        var layout = TreeLayout.Descendants(tree, root.Id, 3);
        Assert.Equal(new[] { 0, 1, 2 }, layout.Nodes.Select(n => n.Generation).Distinct().Order());
        Assert.Equal(1 + 2 + 4, layout.Nodes.Count);
        foreach (var row in layout.Nodes.GroupBy(n => n.Generation))
            Assert.Single(row.Select(n => n.Y).Distinct());
        Assert.All(layout.Nodes.Where(n => n.Parent != null), n => Assert.Equal(n.Parent!.Generation + 1, n.Generation));
        Assert.All(layout.Nodes, n => Assert.Equal(2, n.Slots.Count));      // everyone married
        // The bottom row has more below it.
        Assert.All(layout.Nodes.Where(n => n.Generation == 2), n => Assert.True(n.MoreDescendants));
        Assert.Equal(root.Id, layout.Root!.PersonId);
        Assert.True(layout.Width > 4 * 2 * layout.Options.SlotWidth);
    }

    [Fact]
    public void ChildrenOfEachMarriageHangUnderTheRightCouple()
    {
        var t = new FamilyTree();
        var me = Add(t, "Walter", "Brennan", Sex.Male);
        var first = Add(t, "Agnes", "Holt", Sex.Female);
        var second = Add(t, "Ruth", "Carver", Sex.Female);
        var f1 = Marry(t, me, first);
        var f2 = Marry(t, me, second);
        var f3 = Marry(t, me, null);                      // a child with an unrecorded mother
        var a = Children(t, f1, 3, "Brennan");
        var b = Children(t, f2, 2, "Brennan");
        var c = Children(t, f3, 1, "Brennan");
        Children(t, Marry(t, a[1], Add(t, "Spouse", "Lane", Sex.Female)), 2, "Brennan");

        var layout = TreeLayout.Descendants(t, me.Id, 3);
        AssertNoOverlaps(layout);
        var rootNode = layout.Root!;
        Assert.Equal(new long?[] { me.Id, first.Id, second.Id, null }, rootNode.Slots.Select(s => s.PersonId));
        Assert.Equal(new[] { first.Id, second.Id }, rootNode.SpouseIds);

        var kids = layout.Nodes.Where(n => n.Parent == rootNode).OrderBy(n => n.X).ToList();
        Assert.Equal(a.Concat(b).Concat(c).Select(p => p.Id), kids.Select(k => k.PersonId));
        Assert.Equal(new[] { f1.Id, f1.Id, f1.Id, f2.Id, f2.Id, f3.Id }, kids.Select(k => k.ViaFamilyId!.Value));
        // Each marriage's line leaves from that partner's part of the couple box.
        for (var slot = 1; slot <= 3; slot++)
        {
            var family = rootNode.Slots[slot].FamilyId;
            var anchor = rootNode.SlotBounds(slot).CenterX;
            Assert.All(kids.Where(k => k.ViaFamilyId == family), k => Assert.Equal(anchor, k.ParentAnchorX, 6));
        }
        // Groups don't interleave, and they're further apart than siblings.
        var gapBetweenMarriages = kids[3].X - kids[2].Bounds.Right;
        Assert.True(gapBetweenMarriages >= layout.Options.CousinGap - 1e-6);
    }

    [Fact]
    public void ALoneParentWithoutPartnerNeedsNoPartnerSlot()
    {
        var t = new FamilyTree();
        var mum = Add(t, "Ivy", "Marsh", Sex.Female);
        Children(t, Marry(t, null, mum), 2, "Marsh");
        var layout = TreeLayout.Descendants(t, mum.Id, 2);
        Assert.Single(layout.Root!.Slots);
        Assert.Equal(3, layout.Nodes.Count);
        Assert.Equal(layout.Root.Bounds.CenterX, layout.Nodes.Where(n => n.Generation == 1).Average(n => n.ConnectX), 6);
    }

    [Fact]
    public void LoopsInTheDataDoNotHang()
    {
        // A is B's parent, B is C's parent, and C is recorded as A's parent: impossible but it happens.
        var t = new FamilyTree();
        var a = Add(t, "Ada", "Loop", Sex.Female);
        var b = Add(t, "Ben", "Loop", Sex.Male);
        var c = Add(t, "Cal", "Loop", Sex.Male);
        t.AddChild(Marry(t, null, a), b);
        t.AddChild(Marry(t, b, null), c);
        t.AddChild(Marry(t, c, null), a);
        // And someone married to their own child.
        var d = Add(t, "Dot", "Loop", Sex.Female);
        var fd = Marry(t, c, d);
        t.AddChild(fd, d);

        var sw = Stopwatch.StartNew();
        var down = TreeLayout.Descendants(t, a.Id, 50);
        var hour = TreeLayout.Hourglass(t, a.Id, 50, 50);
        Assert.True(sw.ElapsedMilliseconds < 1000);
        Assert.Equal(down.Nodes.Count, down.Nodes.Select(n => n.PersonId).Distinct().Count());   // each person expanded once
        Assert.True(down.Nodes.Count <= 4);
        Assert.True(hour.Nodes.Count < 20);
        AssertNoOverlaps(down);
        AssertNoOverlaps(hour);
    }

    [Fact]
    public void ThreeThousandPeopleLayOutQuickly()
    {
        // 3 children each for 8 generations: 3,280 descendants plus 1,093 spouses.
        var (tree, root) = Branching(8, (_, _) => 3);
        Assert.True(tree.People.Count > 4000);
        TreeLayout.Descendants(tree, root.Id, 3);                 // warm up the JIT
        var sw = Stopwatch.StartNew();
        var layout = TreeLayout.Descendants(tree, root.Id, 8);
        sw.Stop();
        Assert.Equal(3280, layout.Nodes.Count);
        Assert.True(sw.ElapsedMilliseconds < 500, $"took {sw.ElapsedMilliseconds} ms");
        AssertNoOverlaps(layout);
        // Hit testing finds every box by binary search.
        foreach (var n in layout.Nodes.Take(200))
            Assert.Equal(n.PersonId, layout.HitTest(n.SlotBounds(0).CenterX, n.Bounds.CenterY));
        Assert.Null(layout.HitTest(-5, -5));
    }

    // ---- hourglass ------------------------------------------------------------------------------

    private static (FamilyTree, Person Root, Person Father, Person Mother) Hourglass()
    {
        var t = new FamilyTree();
        var root = Add(t, "Edith", "Marlowe", Sex.Female);
        var father = Add(t, "Frank", "Marlowe", Sex.Male);
        var mother = Add(t, "Grace", "Penn", Sex.Female);
        t.AddChild(Marry(t, father, mother), root);
        var gf = Add(t, "Harold", "Marlowe", Sex.Male);
        var gm = Add(t, "Iris", "Webb", Sex.Female);
        t.AddChild(Marry(t, gf, gm), father);
        var mgf = Add(t, "Jack", "Penn", Sex.Male);
        t.AddChild(Marry(t, mgf, null), mother);
        t.AddChild(Marry(t, Add(t, "Old", "Penn", Sex.Male), null), mgf);   // beyond the chart's edge
        var husband = Add(t, "Leo", "Stone", Sex.Male);
        var f = Marry(t, husband, root);
        Children(t, f, 3, "Stone");
        return (t, root, father, mother);
    }

    [Fact]
    public void HourglassPutsAncestorsAboveAndDescendantsBelow()
    {
        var (t, root, father, mother) = Hourglass();
        var layout = TreeLayout.Hourglass(t, root.Id, 3, 2);
        AssertNoOverlaps(layout);
        var me = layout.Root!;
        Assert.Equal(0, me.Generation);
        Assert.Equal(new[] { -2, -1, 0, 1 }, layout.Nodes.Select(n => n.Generation).Distinct().Order());
        Assert.All(layout.Nodes.Where(n => n.Generation < 0), n => Assert.True(n.Y < me.Y));
        Assert.All(layout.Nodes.Where(n => n.Generation > 0), n => Assert.True(n.Y > me.Y));

        // The parents' couple box sits right over the root (not over the root's husband).
        var parents = Assert.Single(layout.Nodes, n => n.Generation == -1);
        Assert.Equal(new long?[] { father.Id, mother.Id }, parents.Slots.Select(s => s.PersonId));
        Assert.Equal(me.SlotBounds(0).CenterX, parents.Bounds.CenterX, 6);
        Assert.Equal(me.SlotBounds(0).CenterX, parents.ParentAnchorX, 6);

        // Grandparents: one couple over the father, a lone grandfather over the mother.
        var grand = layout.Nodes.Where(n => n.Generation == -2).OrderBy(n => n.X).ToList();
        Assert.Equal(2, grand.Count);
        Assert.Equal(2, grand[0].Slots.Count);
        Assert.Single(grand[1].Slots);
        Assert.True(grand[1].Slots[0].MoreAncestors);
        Assert.False(grand[0].Slots[0].MoreAncestors);
        Assert.True(grand[0].Bounds.CenterX < parents.SlotBounds(0).CenterX + 1e-6);
        Assert.True(grand[1].Bounds.CenterX > parents.SlotBounds(1).CenterX - 1e-6);

        // Descendants hang from the root couple as in the descendant chart.
        var kids = layout.Nodes.Where(n => n.Generation == 1).ToList();
        Assert.Equal(3, kids.Count);
        Assert.Equal(me.Bounds.CenterX, (kids.Min(k => k.ConnectX) + kids.Max(k => k.ConnectX)) / 2, 6);
        Assert.Equal(layout.Nodes.Count, layout.Nodes.Distinct().Count());
        Assert.True(layout.Lines.Count > 10);
    }

    [Fact]
    public void PedigreeCollapseShowsTheCoupleTwiceWithoutClimbingAgain()
    {
        // Second cousins married: both sides share the same great-grandparents.
        var t = new FamilyTree();
        var gg1 = Add(t, "Amos", "Reed", Sex.Male);
        var gg2 = Add(t, "Bea", "Reed", Sex.Female);
        var ggFamily = Marry(t, gg1, gg2);
        var s1 = Add(t, "Carl", "Reed", Sex.Male);
        var s2 = Add(t, "Dora", "Reed", Sex.Female);
        t.AddChild(ggFamily, s1);
        t.AddChild(ggFamily, s2);
        var root = Add(t, "Eve", "Reed", Sex.Female);
        t.AddChild(Marry(t, s1, s2), root);    // (brother and sister in this invented data: worst case)
        var layout = TreeLayout.Hourglass(t, root.Id, 4, 1);
        var top = layout.Nodes.Where(n => n.Generation == -2).ToList();
        Assert.Equal(2, top.Count);
        Assert.Single(top, n => n.IsRepeat);
        AssertNoOverlaps(layout);
    }

    [Fact]
    public void UnknownRootGivesAnEmptyLayout()
    {
        var layout = TreeLayout.Descendants(new FamilyTree(), 42, 3);
        Assert.Empty(layout.Nodes);
        Assert.Null(layout.Root);
        Assert.Null(layout.HitTest(10, 10));
    }
}
