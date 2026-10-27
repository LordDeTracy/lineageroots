namespace Lineage.Core.Dna;

/// <summary>
/// A genetic relationship by the generations from each person up to the closest common ancestor(s).
/// <see cref="Up"/> ≥ <see cref="Down"/> (the relationship works the same both ways). <see cref="Down"/> is 0
/// for a direct line (parent, grandparent, …). <see cref="Half"/> means one shared ancestor rather than a
/// couple, as for half siblings; a direct line always goes through one person.
/// </summary>
public readonly record struct Kinship(int Up, int Down, bool Half)
{
    public static Kinship Of(int a, int b, bool half) => a >= b ? new(a, b, half || b == 0) : new(b, a, half || a == 0);

    public bool IsDirect => Down == 0;

    /// <summary>
    /// The expected share of the genome they have in common, as a power of ½ (0 = all of it, as for a parent).
    /// Full siblings (¾, sharing through both parents) are the one case that isn't a power of ½; they get −1.
    /// </summary>
    public int Degree => IsDirect ? Up - 1 : Up == 1 && Down == 1 && !Half ? -1 : Up + Down - (Half ? 1 : 2);

    public string Label
    {
        get
        {
            var half = Half && !IsDirect ? "half " : "";
            if (IsDirect)
                return Up switch
                {
                    1 => "parent or child",
                    2 => "grandparent or grandchild",
                    3 => "great-grandparent",
                    _ => $"{Ordinal(Up - 2)} great-grandparent",
                };
            if (Down == 1)
                return Up switch
                {
                    1 => half + "sibling",
                    2 => half + "aunt/uncle or niece/nephew",
                    3 => half + "great-aunt/uncle",
                    _ => $"{half}{Ordinal(Up - 2)} great-aunt/uncle",
                };
            var cousin = Ordinal(Down - 1);
            var removed = (Up - Down) switch { 0 => "", 1 => " once removed", 2 => " twice removed", var n => $" {n}× removed" };
            return $"{half}{cousin} cousin{removed}";
        }
    }

    private static string Ordinal(int n) => n switch
    {
        1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th",
    };

    public override string ToString() => Label;
}

/// <summary>
/// Simulates DNA passing down a family to see how much two relatives share. Each chromosome copy is a
/// mosaic of labelled pieces from the common ancestors; crossovers follow the gamma model of crossover
/// interference (ν = 4.3) on a sex-averaged genetic map, which reproduces the spread seen in real testing.
/// Shared DNA is counted the way testing companies report it: total centimorgans in segments of at least
/// <see cref="MinSegmentCm"/>, where the two people carry a copy of the same ancestral piece on either copy.
/// </summary>
public sealed class Inheritance
{
    public const double MinSegmentCm = 7;
    private const double InterferenceShape = 4.3;

    /// <summary>Sex-averaged lengths of chromosomes 1–22 in cM, scaled so the total matches what companies report for a parent.</summary>
    public static readonly double[] Chromosomes = Scale(new double[]
    {
        278, 263, 224, 214, 209, 194, 187, 170, 168, 181, 159, 175, 126, 120, 141, 134, 128, 126, 107, 108, 63, 72,
    }, 3485);

    public static double GenomeCm => Chromosomes.Sum();

    private static double[] Scale(double[] lengths, double total)
    {
        var factor = total / lengths.Sum();
        return lengths.Select(l => l * factor).ToArray();
    }

    private readonly Random _random;

    public Inheritance(int seed) => _random = new Random(seed);

    /// <summary>One copy of one chromosome: piece starts (cM) and the label of the ancestral piece from there on (0 = unrelated).</summary>
    private sealed class Copy
    {
        public readonly List<double> Starts = new();
        public readonly List<int> Labels = new();

        public static Copy Whole(int label)
        {
            var c = new Copy();
            c.Starts.Add(0);
            c.Labels.Add(label);
            return c;
        }

        public bool IsBlank => Labels.Count == 1 && Labels[0] == 0;

        public int LabelAt(double pos)
        {
            var i = Starts.BinarySearch(pos);
            if (i < 0) i = ~i - 1;
            return Labels[Math.Max(0, i)];
        }

        public void Add(double start, int label)
        {
            if (Labels.Count > 0 && Labels[^1] == label) return;
            if (Starts.Count > 0 && Starts[^1] == start) { Labels[^1] = label; return; }
            Starts.Add(start);
            Labels.Add(label);
        }
    }

    /// <summary>A person's two copies of every chromosome.</summary>
    private sealed record Genome(Copy[] A, Copy[] B);

    private static Genome Founder(int labelA, int labelB) =>
        new(Chromosomes.Select(_ => Copy.Whole(labelA)).ToArray(), Chromosomes.Select(_ => Copy.Whole(labelB)).ToArray());

    private static readonly Copy Blank = Copy.Whole(0);

    /// <summary>The copy a parent passes on: their two copies spliced at this meiosis's crossovers.</summary>
    private Copy[] Gamete(Genome parent)
    {
        var result = new Copy[Chromosomes.Length];
        for (var c = 0; c < Chromosomes.Length; c++)
        {
            var a = parent.A[c];
            var b = parent.B[c];
            if (a.IsBlank && b.IsBlank) { result[c] = Blank; continue; }
            var crossovers = Crossovers(Chromosomes[c]);
            var fromA = _random.Next(2) == 0;
            var copy = new Copy();
            var start = 0.0;
            foreach (var x in crossovers.Append(Chromosomes[c]))
            {
                Splice(fromA ? a : b, start, x, copy);
                start = x;
                fromA = !fromA;
            }
            result[c] = copy;
        }
        return result;
    }

    private static void Splice(Copy source, double from, double to, Copy into)
    {
        var i = source.Starts.BinarySearch(from);
        if (i < 0) i = ~i - 1;
        i = Math.Max(0, i);
        into.Add(from, source.Labels[i]);
        for (i++; i < source.Starts.Count && source.Starts[i] < to; i++) into.Add(source.Starts[i], source.Labels[i]);
    }

    /// <summary>
    /// Crossover positions (cM) on a chromosome of this length. Chiasmata form a stationary gamma renewal process
    /// (mean spacing 50 cM); each chiasma is a crossover in a given gamete with probability ½.
    /// </summary>
    private List<double> Crossovers(double lengthCm)
    {
        var list = new List<double>();
        const double scale = 50.0 / InterferenceShape;
        // Stationary start: a length-biased gap with a uniform position inside it.
        var pos = _random.NextDouble() * Gamma(InterferenceShape + 1) * scale;
        while (pos < lengthCm)
        {
            if (_random.Next(2) == 0) list.Add(pos);
            pos += Gamma(InterferenceShape) * scale;
        }
        return list;
    }

    /// <summary>Gamma(shape, 1) by Marsaglia and Tsang (shape ≥ 1).</summary>
    private double Gamma(double shape)
    {
        var d = shape - 1.0 / 3;
        var c = 1 / Math.Sqrt(9 * d);
        while (true)
        {
            double x, v;
            do
            {
                x = Normal();
                v = 1 + c * x;
            } while (v <= 0);
            v = v * v * v;
            var u = _random.NextDouble();
            if (u < 1 - 0.0331 * x * x * x * x || Math.Log(u) < 0.5 * x * x + d * (1 - v + Math.Log(v))) return d * v;
        }
    }

    private double Normal()
    {
        var u1 = 1.0 - _random.NextDouble();
        var u2 = _random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    /// <summary>A child of this parent and an unrelated partner.</summary>
    private Genome Descend(Genome parent) => new(Gamete(parent), Chromosomes.Select(_ => Blank).ToArray());

    /// <summary>
    /// Someone <paramref name="generations"/> below the common ancestor (labels 1, 2) and, for a full line,
    /// the ancestor's partner (labels 3, 4).
    /// </summary>
    private Genome Line(Genome ancestor, Genome? partner, int generations)
    {
        var g = partner == null ? Descend(ancestor) : new Genome(Gamete(ancestor), Gamete(partner));
        for (var i = 1; i < generations; i++) g = Descend(g);
        return g;
    }

    /// <summary>The total shared cM for one simulated pair of relatives.</summary>
    public double Simulate(Kinship k)
    {
        var ancestor = Founder(1, 2);
        var partner = k.Half ? null : Founder(3, 4);
        var x = Line(ancestor, partner, k.Up);
        var y = k.IsDirect ? ancestor : Line(ancestor, partner, k.Down);
        return Shared(x, y);
    }

    private static double Shared(Genome x, Genome y)
    {
        var total = 0.0;
        var points = new List<double>();
        for (var c = 0; c < Chromosomes.Length; c++)
        {
            points.Clear();
            points.AddRange(x.A[c].Starts);
            points.AddRange(x.B[c].Starts);
            points.AddRange(y.A[c].Starts);
            points.AddRange(y.B[c].Starts);
            points.Sort();
            var runStart = -1.0;
            for (var i = 0; i < points.Count; i++)
            {
                var at = points[i];
                if (i > 0 && at == points[i - 1]) continue;
                var shared = Match(x.A[c].LabelAt(at), x.B[c].LabelAt(at), y.A[c].LabelAt(at), y.B[c].LabelAt(at));
                if (shared && runStart < 0) runStart = at;
                else if (!shared && runStart >= 0)
                {
                    if (at - runStart >= MinSegmentCm) total += at - runStart;
                    runStart = -1;
                }
            }
            if (runStart >= 0 && Chromosomes[c] - runStart >= MinSegmentCm) total += Chromosomes[c] - runStart;
        }
        return total;
    }

    private static bool Match(int xa, int xb, int ya, int yb) =>
        (xa != 0 && (xa == ya || xa == yb)) || (xb != 0 && (xb == ya || xb == yb));
}
