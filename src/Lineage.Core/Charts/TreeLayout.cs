using Lineage.Core.Model;

namespace Lineage.Core.Charts;

/// <summary>An axis-aligned rectangle in chart units (device-independent pixels).</summary>
public readonly record struct LayoutBox(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;
    public bool Overlaps(LayoutBox other) => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}

/// <summary>One straight piece of a connector; connectors are drawn as elbows made of these.</summary>
public readonly record struct LayoutLine(double X1, double Y1, double X2, double Y2);

/// <summary>Who a slot in a box is, relative to the box: the descendant and their partners, or an ancestor couple.</summary>
public enum SlotRole { Person, Partner, Father, Mother }

/// <summary>
/// One person's place inside a box. Boxes hold a couple side by side: a descendant then each partner,
/// or an ancestor father then mother. <see cref="PersonId"/> is null for a partner nobody recorded.
/// </summary>
public sealed record NodeSlot(long? PersonId, SlotRole Role, long? FamilyId)
{
    /// <summary>The person has parents beyond the top of the chart.</summary>
    public bool MoreAncestors { get; internal set; }
}

/// <summary>A box in a laid-out tree: a person and the spouses shown beside them, with its position.</summary>
public sealed class TreeNode
{
    internal TreeNode(int generation, IReadOnlyList<NodeSlot> slots, double slotWidth, double height)
    {
        Generation = generation;
        Slots = slots;
        SlotWidth = slotWidth;
        Width = slotWidth * slots.Count;
        Height = height;
        SpouseIds = slots.Skip(1).Where(s => s.PersonId.HasValue).Select(s => s.PersonId!.Value).ToList();
    }

    /// <summary>0 is the root's row; descendants count up (1 = children), ancestors count down (-1 = parents).</summary>
    public int Generation { get; }

    /// <summary>Everyone in the box, left to right.</summary>
    public IReadOnlyList<NodeSlot> Slots { get; }

    /// <summary>The person the box is about: the descendant, or the first known parent of an ancestor couple.</summary>
    public long PersonId => Slots[0].PersonId!.Value;

    /// <summary>The known partners shown beside <see cref="PersonId"/>.</summary>
    public IReadOnlyList<long> SpouseIds { get; }

    public double X { get; internal set; }
    public double Y { get; internal set; }
    public double Width { get; }
    public double Height { get; }
    public double SlotWidth { get; }
    public LayoutBox Bounds => new(X, Y, Width, Height);
    public LayoutBox SlotBounds(int slot) => new(X + slot * SlotWidth, Y, SlotWidth, Height);

    /// <summary>The box this one hangs from: the parents' box above a descendant, the child's box below an ancestor couple.</summary>
    public TreeNode? Parent { get; internal set; }

    /// <summary>The family that links this box to <see cref="Parent"/>.</summary>
    public long? ViaFamilyId { get; internal set; }

    /// <summary>Where the connector from <see cref="Parent"/> meets this box (x).</summary>
    public double ConnectX { get; internal set; }

    /// <summary>Where that connector leaves <see cref="Parent"/> (x).</summary>
    public double ParentAnchorX { get; internal set; }

    /// <summary>The descendant has children beyond the bottom of the chart.</summary>
    public bool MoreDescendants { get; internal set; }

    /// <summary>An ancestor couple already shown elsewhere in the chart (pedigree collapse); not expanded again.</summary>
    public bool IsRepeat { get; internal set; }

    public int SlotAt(double x) => Math.Clamp((int)Math.Floor((x - X) / SlotWidth), 0, Slots.Count - 1);
}

/// <summary>Sizes and spacing for tree layouts, in device-independent pixels.</summary>
public sealed record TreeLayoutOptions
{
    /// <summary>Width of one person inside a box; a couple box is two of these.</summary>
    public double SlotWidth { get; init; } = 204;
    public double BoxHeight { get; init; } = 58;
    /// <summary>Between brothers and sisters.</summary>
    public double SiblingGap { get; init; } = 14;
    /// <summary>Between boxes with different parents (cousins, half-siblings from another marriage).</summary>
    public double CousinGap { get; init; } = 34;
    /// <summary>Vertical space between generations, where the connectors run.</summary>
    public double RowGap { get; init; } = 54;
    public double Padding { get; init; } = 24;

    public static TreeLayoutOptions Default { get; } = new();
}

/// <summary>A finished layout: boxes, connector segments and the overall size.</summary>
public sealed class TreeLayoutResult
{
    private readonly Dictionary<int, TreeNode[]> _rows;
    private readonly int _minGeneration;

    internal TreeLayoutResult(List<TreeNode> nodes, List<LayoutLine> lines, TreeNode? root, double width, double height,
                              int minGeneration, TreeLayoutOptions options)
    {
        Nodes = nodes;
        Lines = lines;
        Root = root;
        Width = width;
        Height = height;
        Options = options;
        _minGeneration = minGeneration;
        _rows = nodes.GroupBy(n => n.Generation).ToDictionary(g => g.Key, g => g.OrderBy(n => n.X).ToArray());
    }

    public IReadOnlyList<TreeNode> Nodes { get; }
    public IReadOnlyList<LayoutLine> Lines { get; }
    public TreeNode? Root { get; }
    public double Width { get; }
    public double Height { get; }
    public TreeLayoutOptions Options { get; }

    /// <summary>The box and slot under a point, found by row and binary search so it stays cheap for big trees.</summary>
    public (TreeNode Node, int Slot)? Find(double x, double y)
    {
        var rowHeight = Options.BoxHeight + Options.RowGap;
        var row = (int)Math.Floor((y - Options.Padding) / rowHeight);
        if (row < 0 || y - Options.Padding - row * rowHeight >= Options.BoxHeight) return null;
        if (!_rows.TryGetValue(_minGeneration + row, out var nodes)) return null;
        int lo = 0, hi = nodes.Length - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (nodes[mid].X <= x) { found = mid; lo = mid + 1; } else hi = mid - 1;
        }
        if (found < 0 || x >= nodes[found].Bounds.Right) return null;
        var node = nodes[found];
        return (node, node.SlotAt(x));
    }

    /// <summary>The person under a point, if any (partners count; unknown partners don't).</summary>
    public long? HitTest(double x, double y) => Find(x, y) is { } hit ? hit.Node.Slots[hit.Slot].PersonId : null;
}

/// <summary>
/// Lays out descendant and hourglass charts as tidy trees: every subtree gets exactly the width it needs,
/// parents sit centred over their children, children of each marriage hang under the right couple and
/// nothing overlaps. Works on contours (the left and right edge of a subtree at each depth), so it is
/// O(people × generations) and handles thousands of boxes instantly. Each person is expanded at most
/// once, so loops in bad data can't make it run forever.
/// </summary>
public static class TreeLayout
{
    /// <summary>
    /// The root, their partners, and their descendants. <paramref name="generations"/> counts the root's
    /// own row, like the pedigree chart: 1 is just the root couple, 3 adds children and grandchildren.
    /// </summary>
    public static TreeLayoutResult Descendants(FamilyTree tree, long rootId, int generations, TreeLayoutOptions? options = null) =>
        Build(tree, rootId, 1, generations, options ?? TreeLayoutOptions.Default);

    /// <summary>
    /// Ancestors above the root (a compact pedigree of couples) and descendants below, sharing the root's
    /// centre line. Both counts include the root's row: 3 and 3 show grandparents down to grandchildren.
    /// </summary>
    public static TreeLayoutResult Hourglass(FamilyTree tree, long rootId, int ancestorGenerations, int descendantGenerations,
                                             TreeLayoutOptions? options = null) =>
        Build(tree, rootId, Math.Max(1, ancestorGenerations), descendantGenerations, options ?? TreeLayoutOptions.Default);

    // ---- building -------------------------------------------------------------------------------

    /// <summary>A box while it is being laid out: its subtree contour and its offset from its parent.</summary>
    private sealed class Work
    {
        public required TreeNode Node;
        public readonly List<Work> Kids = new();
        public int Group;              // which anchor on the parent it hangs from (marriage or parent slot)
        public double ConnectOffset;   // where its connector meets it, relative to its own centre
        public double AnchorOffset;    // where its connector leaves the parent, relative to the parent's centre
        public double[] Left = Array.Empty<double>(), Right = Array.Empty<double>();
        public double Rel;             // centre relative to the parent's centre
        public double Centre;
    }

    private static TreeLayoutResult Build(FamilyTree tree, long rootId, int ancestorGenerations, int descendantGenerations, TreeLayoutOptions o)
    {
        if (tree.Person(rootId) is not { } rootPerson)
            return new TreeLayoutResult(new(), new(), null, 2 * o.Padding, 2 * o.Padding, 0, o);

        var downOrder = DescendantTree(tree, rootPerson, Math.Max(1, descendantGenerations), o);
        var upOrder = ancestorGenerations > 1 ? AncestorTree(tree, downOrder[0].Node, ancestorGenerations, o) : new List<Work>();

        Tidy(downOrder, o);
        Tidy(upOrder, o);

        // Positions: both trees share the root's centre; rows stack from the oldest generation down.
        // upOrder[0] is the root again (as seen from above), so it is left out of the node list.
        var all = downOrder.Concat(upOrder.Skip(1)).ToList();
        var minGen = all.Min(w => w.Node.Generation);
        var maxGen = all.Max(w => w.Node.Generation);
        var minX = all.Min(w => w.Centre - w.Node.Width / 2);
        var maxX = all.Max(w => w.Centre + w.Node.Width / 2);
        var shift = o.Padding - minX;
        foreach (var w in downOrder.Concat(upOrder))
        {
            w.Centre += shift;
            w.Node.X = w.Centre - w.Node.Width / 2;
            w.Node.Y = o.Padding + (w.Node.Generation - minGen) * (o.BoxHeight + o.RowGap);
        }
        foreach (var w in downOrder.Concat(upOrder))
            foreach (var k in w.Kids)
            {
                k.Node.ConnectX = k.Centre + k.ConnectOffset;
                k.Node.ParentAnchorX = w.Centre + k.AnchorOffset;
            }

        var lines = new List<LayoutLine>();
        foreach (var w in downOrder) DescendantLines(w, o, lines);
        foreach (var w in upOrder) AncestorLines(w, o, lines);

        var nodes = all.Select(w => w.Node).ToList();
        var width = maxX - minX + 2 * o.Padding;
        var height = 2 * o.Padding + (maxGen - minGen + 1) * o.BoxHeight + (maxGen - minGen) * o.RowGap;
        return new TreeLayoutResult(nodes, lines, downOrder[0].Node, width, height, minGen, o);
    }

    /// <summary>Breadth-first (parents before children), so a person reachable two ways is placed at the shallower spot.</summary>
    private static List<Work> DescendantTree(FamilyTree tree, Person root, int generations, TreeLayoutOptions o)
    {
        var placed = new HashSet<long> { root.Id };
        var expanded = new HashSet<long>();
        var order = new List<Work> { DescendantWork(tree, root, 0, o) };
        for (var i = 0; i < order.Count; i++)
        {
            var w = order[i];
            var person = tree.Person(w.Node.PersonId)!;
            var families = ShownFamilies(tree, person);
            if (w.Node.Generation >= generations - 1)
            {
                w.Node.MoreDescendants = families.Any(f => f.Family.Children.Count > 0);
                continue;
            }
            for (var k = 0; k < families.Count; k++)
            {
                var f = families[k].Family;
                if (!expanded.Add(f.Id)) continue;
                foreach (var child in tree.ChildrenOf(f))
                {
                    if (!placed.Add(child.Id)) continue;
                    var kid = DescendantWork(tree, child, w.Node.Generation + 1, o);
                    kid.Group = k;
                    kid.AnchorOffset = FamilyAnchor(w.Node, families.Count, k);
                    kid.Node.Parent = w.Node;
                    kid.Node.ViaFamilyId = f.Id;
                    w.Kids.Add(kid);
                    order.Add(kid);
                }
            }
        }
        return order;
    }

    private static Work DescendantWork(FamilyTree tree, Person p, int generation, TreeLayoutOptions o)
    {
        var families = ShownFamilies(tree, p);
        var slots = new List<NodeSlot> { new(p.Id, SlotRole.Person, null) };
        // A lone family with nobody recorded as partner needs no partner slot: children hang from the person.
        if (!(families.Count == 1 && families[0].SpouseId == null))
            slots.AddRange(families.Select(f => new NodeSlot(f.SpouseId, SlotRole.Partner, f.Family.Id)));
        var node = new TreeNode(generation, slots, o.SlotWidth, o.BoxHeight);
        return new Work { Node = node, ConnectOffset = SlotCentre(node, 0) };
    }

    /// <summary>The person's marriages worth showing: those with a partner or with children.</summary>
    private static List<(Family Family, long? SpouseId)> ShownFamilies(FamilyTree tree, Person p)
    {
        var result = new List<(Family, long?)>();
        foreach (var id in p.SpouseFamilyIds.Distinct())
        {
            if (tree.Family(id) is not { } f) continue;
            var other = f.PartnerIds.Where(x => x != p.Id).Select(x => tree.Person(x)).FirstOrDefault(x => x != null);
            if (other == null && f.Children.Count == 0) continue;
            result.Add((f, other?.Id));
        }
        return result;
    }

    private static double SlotCentre(TreeNode node, int slot) => -node.Width / 2 + (slot + 0.5) * node.SlotWidth;

    /// <summary>Children of a single marriage hang from the middle of the couple; with several, from each partner.</summary>
    private static double FamilyAnchor(TreeNode node, int familyCount, int family) =>
        familyCount <= 1 ? 0 : SlotCentre(node, family + 1);

    /// <summary>Upwards from the root: each box is a couple (father, mother) above the child it belongs to.</summary>
    private static List<Work> AncestorTree(FamilyTree tree, TreeNode rootNode, int generations, TreeLayoutOptions o)
    {
        var top = -(generations - 1);
        var seenFamilies = new HashSet<long>();
        var order = new List<Work> { new() { Node = rootNode } };
        for (var i = 0; i < order.Count; i++)
        {
            var w = order[i];
            // The root's partners' ancestors aren't shown; an ancestor couple expands both partners.
            var count = w.Node.Generation == 0 ? 1 : w.Node.Slots.Count;
            for (var s = 0; s < count; s++)
            {
                var slot = w.Node.Slots[s];
                if (tree.Person(slot.PersonId) is not { } person) continue;
                var family = tree.Family(person.ParentFamilyIds.FirstOrDefault());
                var father = family != null ? tree.Person(family.Partner1Id) : null;
                var mother = family != null ? tree.Person(family.Partner2Id) : null;
                if (family == null || (father == null && mother == null)) continue;
                if (w.Node.Generation <= top)
                {
                    slot.MoreAncestors = true;
                    continue;
                }
                var slots = new List<NodeSlot>();
                if (father != null) slots.Add(new NodeSlot(father.Id, SlotRole.Father, family.Id));
                if (mother != null) slots.Add(new NodeSlot(mother.Id, SlotRole.Mother, family.Id));
                var node = new TreeNode(w.Node.Generation - 1, slots, o.SlotWidth, o.BoxHeight)
                {
                    Parent = w.Node,
                    ViaFamilyId = family.Id,
                };
                var kid = new Work { Node = node, Group = s, AnchorOffset = SlotCentre(w.Node, s) };
                w.Kids.Add(kid);
                // Pedigree collapse (cousins marrying) or a loop in the data: show the couple, don't climb again.
                if (seenFamilies.Add(family.Id)) order.Add(kid);
                else node.IsRepeat = true;
            }
        }
        // Repeated couples were never queued, so give them a place in the order for the contour pass.
        var queued = new HashSet<Work>(order);
        foreach (var w in order.ToList())
            foreach (var k in w.Kids)
                if (queued.Add(k)) order.Add(k);
        return order;
    }

    // ---- the tidy tree ----------------------------------------------------------------------------

    /// <summary>
    /// Bottom-up: pack each box's children side by side against their contours, once from the left and once
    /// from the right, and average the two so small subtrees between big ones spread out evenly. Then
    /// centre the parent over its children and record the subtree's contour. Top-down: absolute centres.
    /// </summary>
    private static void Tidy(List<Work> order, TreeLayoutOptions o)
    {
        if (order.Count == 0) return;
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var w = order[i];
            var half = w.Node.Width / 2;
            var kids = w.Kids;
            if (kids.Count == 0)
            {
                w.Left = new[] { -half };
                w.Right = new[] { half };
                continue;
            }

            var m = kids.Count;
            var fromLeft = new double[m];
            var accL = new List<double>(kids[0].Left);
            var accR = new List<double>(kids[0].Right);
            for (var k = 1; k < m; k++)
            {
                var kid = kids[k];
                var gap = kid.Group == kids[k - 1].Group ? o.SiblingGap : o.CousinGap;
                var s = double.NegativeInfinity;
                for (var d = 0; d < Math.Min(accR.Count, kid.Left.Length); d++)
                    s = Math.Max(s, accR[d] - kid.Left[d] + (d == 0 ? gap : o.CousinGap));
                fromLeft[k] = s;
                Merge(accL, accR, kid, s);
            }

            var fromRight = new double[m];
            accL = new List<double>(kids[m - 1].Left);
            accR = new List<double>(kids[m - 1].Right);
            for (var k = m - 2; k >= 0; k--)
            {
                var kid = kids[k];
                var gap = kid.Group == kids[k + 1].Group ? o.SiblingGap : o.CousinGap;
                var s = double.PositiveInfinity;
                for (var d = 0; d < Math.Min(accL.Count, kid.Right.Length); d++)
                    s = Math.Min(s, accL[d] - kid.Right[d] - (d == 0 ? gap : o.CousinGap));
                fromRight[k] = s;
                Merge(accL, accR, kid, s);
            }

            // Both packings keep every pair apart, so their average does too.
            var offsets = new double[m];
            for (var k = 0; k < m; k++) offsets[k] = (fromLeft[k] + fromRight[k] - fromRight[0]) / 2;

            // Centre over the children: each marriage's anchor as close as possible to the middle of its children.
            double sum = 0;
            var groups = 0;
            for (var a = 0; a < m;)
            {
                var b = a;
                while (b + 1 < m && kids[b + 1].Group == kids[a].Group) b++;
                var middle = (offsets[a] + kids[a].ConnectOffset + offsets[b] + kids[b].ConnectOffset) / 2;
                sum += middle - kids[a].AnchorOffset;
                groups++;
                a = b + 1;
            }
            var p = sum / groups;

            var depth = kids.Max(k => k.Left.Length);
            var left = new double[depth + 1];
            var right = new double[depth + 1];
            left[0] = -half;
            right[0] = half;
            for (var d = 1; d <= depth; d++) { left[d] = double.PositiveInfinity; right[d] = double.NegativeInfinity; }
            for (var k = 0; k < m; k++)
            {
                var kid = kids[k];
                kid.Rel = offsets[k] - p;
                for (var d = 0; d < kid.Left.Length; d++)
                {
                    left[d + 1] = Math.Min(left[d + 1], kid.Left[d] + kid.Rel);
                    right[d + 1] = Math.Max(right[d + 1], kid.Right[d] + kid.Rel);
                }
                // The children's contours are no longer needed; let them go for big trees.
                kid.Left = kid.Right = Array.Empty<double>();
            }
            w.Left = left;
            w.Right = right;
        }

        order[0].Centre = 0;
        foreach (var w in order)
            foreach (var k in w.Kids)
                k.Centre = w.Centre + k.Rel;
    }

    private static void Merge(List<double> accL, List<double> accR, Work kid, double shift)
    {
        for (var d = 0; d < kid.Left.Length; d++)
        {
            if (d < accL.Count)
            {
                accL[d] = Math.Min(accL[d], kid.Left[d] + shift);
                accR[d] = Math.Max(accR[d], kid.Right[d] + shift);
            }
            else
            {
                accL.Add(kid.Left[d] + shift);
                accR.Add(kid.Right[d] + shift);
            }
        }
    }

    // ---- connectors -----------------------------------------------------------------------------

    /// <summary>Elbows down from each marriage to its children; several marriages get buses at staggered heights.</summary>
    private static void DescendantLines(Work w, TreeLayoutOptions o, List<LayoutLine> lines)
    {
        if (w.Kids.Count == 0) return;
        var bottom = w.Node.Y + w.Node.Height;
        var runs = new List<(int From, int To)>();
        for (var a = 0; a < w.Kids.Count;)
        {
            var b = a;
            while (b + 1 < w.Kids.Count && w.Kids[b + 1].Group == w.Kids[a].Group) b++;
            runs.Add((a, b));
            a = b + 1;
        }
        for (var r = 0; r < runs.Count; r++)
        {
            var (a, b) = runs[r];
            var ax = w.Centre + w.Kids[a].AnchorOffset;
            var bus = bottom + o.RowGap * (r + 1) / (runs.Count + 1);
            lines.Add(new LayoutLine(ax, bottom, ax, bus));
            var lo = ax;
            var hi = ax;
            for (var k = a; k <= b; k++)
            {
                var kid = w.Kids[k];
                var kx = kid.Centre + kid.ConnectOffset;
                lo = Math.Min(lo, kx);
                hi = Math.Max(hi, kx);
                lines.Add(new LayoutLine(kx, bus, kx, kid.Node.Y));
            }
            if (hi - lo > 0.01) lines.Add(new LayoutLine(lo, bus, hi, bus));
        }
    }

    /// <summary>Elbows up from a person to the middle of their parents' couple box.</summary>
    private static void AncestorLines(Work w, TreeLayoutOptions o, List<LayoutLine> lines)
    {
        var top = w.Node.Y;
        var bus = top - o.RowGap / 2;
        foreach (var kid in w.Kids)
        {
            var sx = w.Centre + kid.AnchorOffset;
            var kx = kid.Centre + kid.ConnectOffset;
            lines.Add(new LayoutLine(sx, top, sx, bus));
            if (Math.Abs(kx - sx) > 0.01) lines.Add(new LayoutLine(Math.Min(sx, kx), bus, Math.Max(sx, kx), bus));
            lines.Add(new LayoutLine(kx, bus, kx, kid.Node.Y + kid.Node.Height));
        }
    }
}
