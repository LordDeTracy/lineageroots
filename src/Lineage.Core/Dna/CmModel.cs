using System.Globalization;
using System.Text;

namespace Lineage.Core.Dna;

/// <summary>The relationships with about the same expected sharing, with how likely they are together.</summary>
public sealed record CmGroup(int Degree, IReadOnlyList<Kinship> Members, double Probability, double AverageCm, double LowCm, double HighCm)
{
    public string Label => string.Join(", ", Members.Select(m => m.Label).Distinct());
    /// <summary>Typical range: 1st to 99th percentile.</summary>
    public string Range => $"{LowCm:N0}–{HighCm:N0} cM";
}

/// <summary>
/// How much DNA relatives share, from <see cref="Inheritance"/> simulations stored in <see cref="CmModelData"/>
/// (regenerate with <see cref="Generate"/>). For each relationship it holds the spread of total shared cM on a
/// log scale, smoothed a little for the measuring error of real tests, plus the chance of sharing nothing
/// detectable. Probabilities treat every listed relationship as equally likely before the DNA is seen.
/// </summary>
public static class CmModel
{
    public const int Bins = 120;
    public const double LogMin = 1.9459101090932196;   // ln 7 (the smallest segment counted)
    public const double LogMax = 8.2160880986323;      // ln 3700
    private const double BinWidth = (LogMax - LogMin) / Bins;
    private const double SmoothingLog = 0.08;          // ~8%: different companies and tests disagree by about this much

    /// <summary>Every relationship the model knows: direct lines up to 4th great-grandparent, and full and half relatives up to 5th cousins.</summary>
    public static readonly IReadOnlyList<Kinship> Relationships = BuildList();

    private static List<Kinship> BuildList()
    {
        var list = new List<Kinship>();
        for (var up = 1; up <= 6; up++) list.Add(new Kinship(up, 0, true));
        for (var down = 1; down <= 6; down++)
            for (var up = down; up <= 6; up++)
            {
                list.Add(new Kinship(up, down, false));
                list.Add(new Kinship(up, down, true));
            }
        return list;
    }

    private sealed record Distribution(double[] Density, double NoMatch, double Average, double Low, double High);

    private static readonly Lazy<Dictionary<Kinship, Distribution>> Model = new(Load);

    // ---- queries ----

    /// <summary>How likely this much sharing is for this relationship (a density per cM; for 0 cM, the chance of no match at all).</summary>
    public static double Likelihood(Kinship k, double cm)
    {
        var d = Model.Value[Nearest(k)];
        if (cm < Inheritance.MinSegmentCm) return Math.Max(d.NoMatch, 1e-4);
        var t = (Math.Log(cm) - LogMin) / BinWidth - 0.5;
        double density;
        if (t <= 0) density = d.Density[0];
        else if (t >= Bins - 1) density = d.Density[Bins - 1];
        else
        {
            var i = (int)t;
            density = d.Density[i] + (d.Density[i + 1] - d.Density[i]) * (t - i);
        }
        // Per log-unit to per cM.
        return Math.Max(density / cm, 1e-12);
    }

    /// <summary>The chance two people with this relationship share no detectable DNA.</summary>
    public static double NoMatchChance(Kinship k) => Model.Value[Nearest(k)].NoMatch;

    public static double AverageCm(Kinship k) => Model.Value[Nearest(k)].Average;

    /// <summary>The relationships that fit this amount of shared DNA, grouped, most likely first.</summary>
    public static List<CmGroup> Predict(double cm)
    {
        var weights = Relationships.Select(k => (k, w: Likelihood(k, cm))).ToList();
        var total = weights.Sum(x => x.w);
        return weights
            .GroupBy(x => x.k.Degree)
            .Select(g =>
            {
                // Cousins first (by far the most common matches), then aunts and uncles, then direct lines.
                var members = g.Select(x => x.k).OrderBy(k => k.IsDirect ? 2 : k.Down == 1 && k.Up > 1 ? 1 : 0)
                               .ThenBy(k => k.Half).ThenBy(k => k.Up - k.Down).ToList();
                var dists = members.Select(m => Model.Value[m]).ToList();
                return new CmGroup(g.Key, members, g.Sum(x => x.w) / total,
                                   dists.Average(d => d.Average), dists.Min(d => d.Low), dists.Max(d => d.High));
            })
            .OrderByDescending(g => g.Probability)
            .ToList();
    }

    /// <summary>The probability of the group this relationship belongs to, given this much shared DNA (used to say whether a match fits the tree).</summary>
    public static double GroupProbability(Kinship k, double cm) =>
        Predict(cm).FirstOrDefault(g => g.Degree == Nearest(k).Degree)?.Probability ?? 0;

    /// <summary>A listed relationship standing in for this one (more distant ones borrow the closest with the same expected sharing).</summary>
    public static Kinship Nearest(Kinship k)
    {
        if (Model.Value.ContainsKey(k)) return k;
        var sameDegree = Relationships.Where(r => r.Degree == k.Degree).ToList();
        if (sameDegree.Count > 0)
            return sameDegree.OrderBy(r => r.IsDirect != k.IsDirect).ThenBy(r => Math.Abs(r.Up - k.Up)).First();
        return Relationships.MaxBy(r => r.Degree);
    }

    // ---- loading ----

    private static Dictionary<Kinship, Distribution> Load()
    {
        var result = new Dictionary<Kinship, Distribution>();
        var rows = CmModelData.Table.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var row in rows)
        {
            // up down half samples zero | counts...
            var parts = row.Split(' ');
            var k = new Kinship(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture), parts[2] == "1");
            var samples = double.Parse(parts[3], CultureInfo.InvariantCulture);
            var zero = double.Parse(parts[4], CultureInfo.InvariantCulture);
            var counts = parts[5].Split(',').Select(c => double.Parse(c, CultureInfo.InvariantCulture)).ToArray();
            result[k] = Build(counts, samples, zero);
        }
        return result;
    }

    private static Distribution Build(double[] counts, double samples, double zero)
    {
        // Smooth in log space, then express as density per log-unit.
        var sigmaBins = SmoothingLog / BinWidth;
        var reach = (int)Math.Ceiling(sigmaBins * 4);
        var density = new double[Bins];
        for (var i = 0; i < Bins; i++)
        {
            if (counts[i] == 0) continue;
            var spread = new double[2 * reach + 1];
            var sum = 0.0;
            for (var j = -reach; j <= reach; j++) sum += spread[j + reach] = Math.Exp(-0.5 * j * j / (sigmaBins * sigmaBins));
            for (var j = -reach; j <= reach; j++)
            {
                var b = Math.Clamp(i + j, 0, Bins - 1);
                density[b] += counts[i] / samples * spread[j + reach] / sum / BinWidth;
            }
        }

        double average = 0, matched = 0;
        for (var i = 0; i < Bins; i++)
        {
            var mid = Math.Exp(LogMin + (i + 0.5) * BinWidth);
            average += counts[i] * mid;
            matched += counts[i];
        }
        average = matched > 0 ? average / matched : 0;
        return new Distribution(density, zero / samples, average, Percentile(density, 0.01), Percentile(density, 0.99));
    }

    /// <summary>A percentile of the matched pairs, from the smoothed curve, interpolated within a bin.</summary>
    private static double Percentile(double[] density, double p)
    {
        var total = density.Sum();
        if (total == 0) return 0;
        var target = total * p;
        var seen = 0.0;
        for (var i = 0; i < Bins; i++)
        {
            if (seen + density[i] >= target)
                return Math.Exp(LogMin + (i + (target - seen) / density[i]) * BinWidth);
            seen += density[i];
        }
        return Math.Exp(LogMax);
    }

    // ---- generating the table ----

    /// <summary>
    /// Simulates every relationship and returns the table for <see cref="CmModelData"/>: one line per relationship,
    /// "up down half samples zero counts", counts being how many simulations fell in each log-spaced bin.
    /// </summary>
    public static string Generate(int samples, int seed)
    {
        var lines = new string[Relationships.Count];
        Parallel.For(0, Relationships.Count, i =>
        {
            var k = Relationships[i];
            var sim = new Inheritance(seed + i * 7919);
            var counts = new int[Bins];
            var zero = 0;
            for (var n = 0; n < samples; n++)
            {
                var cm = sim.Simulate(k);
                if (cm < Inheritance.MinSegmentCm) { zero++; continue; }
                var b = (int)((Math.Log(cm) - LogMin) / BinWidth);
                counts[Math.Clamp(b, 0, Bins - 1)]++;
            }
            lines[i] = $"{k.Up} {k.Down} {(k.Half ? 1 : 0)} {samples} {zero} {string.Join(',', counts)}";
        });
        var sb = new StringBuilder();
        foreach (var l in lines) sb.Append(l).Append('\n');
        return sb.ToString();
    }
}
