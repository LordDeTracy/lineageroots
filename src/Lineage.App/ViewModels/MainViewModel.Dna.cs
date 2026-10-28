using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Dna;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>One match in the tester's list.</summary>
public sealed record DnaMatchRow(DnaMatch Match, string Numbers, string Prediction, string Linked, string TreeText, bool Misfit)
{
    public string Name => Match.Name;
    public string Cm => DnaText.Cm(Match.SharedCm);
    public bool IsLinked => Linked.Length > 0;
    public bool HasTreeText => TreeText.Length > 0;
    public bool HasCompany => Match.Company.Length > 0;
}

/// <summary>A cluster's card: its colour, members and the ancestors linked matches point to.</summary>
public sealed record DnaClusterCard(int Number, string Members, string Ancestors, int Count, Avalonia.Media.IBrush Brush)
{
    public string Heading => $"Cluster {Number}  ·  {Count} matches";
    public bool HasAncestors => Ancestors.Length > 0;
}

/// <summary>An ancestor (or couple) in the chromosome painter's key: its colour, side and the matches painted for it.</summary>
public sealed record PaintGroupRow(PaintGroup Group, Avalonia.Media.IBrush Brush)
{
    public string Label => Group.Label;
    public string Side => Group.Side switch { PaintSide.Father => "Father's side", PaintSide.Mother => "Mother's side", _ => "Either side" };
    public string Detail => $"{Group.Segments:N0} segment{(Group.Segments == 1 ? "" : "s")}, {Group.Cm:0.#} cM · " + string.Join(", ", Group.Matches);
}

/// <summary>One hypothesis in "What are the odds", with its place in the ranking.</summary>
public sealed record WatoHypothesisRow(Hypothesis Hypothesis, string Probability, string Odds, double Share, string Details, bool IsBest)
{
    public string Label => Hypothesis.Label;
    public double BarWidth => Math.Max(2, Share * 260);
}

/// <summary>A tree person who shares DNA with the person being placed.</summary>
public sealed partial class WatoObservationRow : ObservableObject
{
    public WatoObservationRow(Observation observation, string source)
    {
        Observation = observation;
        Source = source;
    }

    public Observation Observation { get; }
    public string Name => Observation.Person.DisplayName;
    public string Cm => DnaText.Cm(Observation.Cm);
    public string Source { get; }
    [ObservableProperty] private bool _include = true;
}

/// <summary>Who "What are the odds" is placing: a tester (using their linked matches) or a match not yet in the tree.</summary>
public sealed record WatoSubject(string Text, Person? Tester, string? MatchName)
{
    public override string ToString() => Text;
}

/// <summary>A parent (or couple) to try in "What are the odds".</summary>
public sealed record ParentChoice(long Parent, long? OtherParent, string Text)
{
    public override string ToString() => Text;
}

/// <summary>A line of the shared cM calculator.</summary>
public sealed record CmGroupRow(CmGroup Group)
{
    public string Probability => DnaText.Percent(Group.Probability);
    public string Label => Group.Label;
    public string Range => $"Usually {Group.Range}, average {Group.AverageCm:N0} cM";
    public double BarWidth => Math.Max(2, Group.Probability * 260);
}

/// <summary>The DNA section: a tester's matches, clusters of matches, "What are the odds", and the shared cM calculator.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<PersonChoice> DnaTesters { get; } = new();
    public ObservableCollection<DnaMatchRow> DnaMatchRows { get; } = new();
    public ObservableCollection<DnaClusterCard> DnaClusterCards { get; } = new();
    public ObservableCollection<WatoSubject> WatoSubjects { get; } = new();
    public ObservableCollection<WatoObservationRow> WatoObservations { get; } = new();
    public ObservableCollection<ParentChoice> WatoHypotheses { get; } = new();
    public ObservableCollection<WatoHypothesisRow> WatoResults { get; } = new();
    public ObservableCollection<CmGroupRow> CmCalculatorRows { get; } = new();
    public ObservableCollection<PaintGroupRow> PaintGroupRows { get; } = new();

    [ObservableProperty] private PersonChoice? _dnaTester;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDnaMatchesTab), nameof(IsDnaClustersTab), nameof(IsDnaOddsTab), nameof(IsDnaCalculatorTab), nameof(IsDnaChromosomesTab))]
    private int _dnaTabIndex;
    [ObservableProperty] private string _dnaFilter = "";
    [ObservableProperty] private string _dnaSummary = "";
    [ObservableProperty] private bool _hasDnaMatches;
    [ObservableProperty] private ClusterResult? _dnaClusters;
    [ObservableProperty] private string _clusterMinCm = Clusters.DefaultMinCm.ToString("0");
    [ObservableProperty] private string _clusterMaxCm = Clusters.DefaultMaxCm.ToString("0");
    [ObservableProperty] private string _clusterSummary = "";
    [ObservableProperty] private WatoSubject? _watoSubject;
    [ObservableProperty] private ParentChoice? _watoHypothesisToAdd;
    [ObservableProperty] private string _watoSummary = "";
    [ObservableProperty] private string _cmCalculatorText = "";
    [ObservableProperty] private string _cmCalculatorNote = "";
    [ObservableProperty] private Painting? _dnaPainting;
    [ObservableProperty] private string _paintingSummary = "";
    private IReadOnlyList<ParentChoice>? _parentChoices;

    public bool IsDnaMatchesTab => DnaTabIndex == 0;
    public bool IsDnaClustersTab => DnaTabIndex == 1;
    public bool IsDnaOddsTab => DnaTabIndex == 2;
    public bool IsDnaCalculatorTab => DnaTabIndex == 3;
    public bool IsDnaChromosomesTab => DnaTabIndex == 4;
    public bool HasPainting => DnaPainting is { Segments.Count: > 0 };
    public bool HasDnaTester => DnaTester != null;
    public bool HasClusters => DnaClusterCards.Count > 0;
    public bool HasWatoResults => WatoResults.Count > 0;
    public bool HasWatoObservations => WatoObservations.Count > 0;
    public bool HasCmCalculatorRows => CmCalculatorRows.Count > 0;

    /// <summary>Every person and couple, for "child of …" hypotheses (built when first needed).</summary>
    public IReadOnlyList<ParentChoice> ParentChoices => _parentChoices ??= BuildParentChoices();

    private Person? Tester => DnaTester != null ? _tree.Person(DnaTester.Id) : null;

    partial void OnDnaTesterChanged(PersonChoice? value)
    {
        OnPropertyChanged(nameof(HasDnaTester));
        RebuildDnaPage();
    }
    partial void OnDnaFilterChanged(string value) => RebuildDnaMatches();
    partial void OnDnaTabIndexChanged(int value) => RebuildDnaPage();
    partial void OnClusterMinCmChanged(string value) => RebuildClusters();
    partial void OnClusterMaxCmChanged(string value) => RebuildClusters();
    partial void OnWatoSubjectChanged(WatoSubject? value) => RebuildWatoObservations();
    partial void OnCmCalculatorTextChanged(string value) => RebuildCmCalculator();

    partial void OnWatoHypothesisToAddChanged(ParentChoice? value)
    {
        if (value == null) return;
        if (!WatoHypotheses.Contains(value)) WatoHypotheses.Add(value);
        WatoHypothesisToAdd = null;
        RebuildWato();
    }

    [RelayCommand]
    private void RemoveWatoHypothesis(ParentChoice choice)
    {
        WatoHypotheses.Remove(choice);
        RebuildWato();
    }

    /// <summary>Opens the DNA section with this person as the tester.</summary>
    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private void ShowDnaMatches()
    {
        if (_current is not { } p) return;
        Section = AppSection.Dna;
        RebuildDnaTesters(p.Id);
    }

    /// <summary>Called when the section opens and after every edit.</summary>
    private void RebuildDna()
    {
        _parentChoices = null;
        OnPropertyChanged(nameof(ParentChoices));
        RebuildDnaTesters(DnaTester?.Id);
    }

    private void RebuildDnaTesters(long? keep)
    {
        var testers = _tree.People.Values.Where(p => p.DnaMatches.Count > 0).OrderBy(p => p.PrimaryName.SortKey).ToList();
        foreach (var extra in new[] { keep, _current?.Id, _tree.HomePersonId })
            if (_tree.Person(extra) is { } p && !testers.Contains(p)) testers.Add(p);
        var choices = testers.Select(p => new PersonChoice(p.Id, p.DnaMatches.Count > 0
            ? $"{p.DisplayName} ({p.DnaMatches.Count:N0} match{(p.DnaMatches.Count == 1 ? "" : "es")})"
            : p.DisplayName)).ToList();
        DnaTesters.Clear();
        foreach (var c in choices) DnaTesters.Add(c);
        var pick = choices.FirstOrDefault(c => c.Id == keep) ?? choices.FirstOrDefault(c => _tree.Person(c.Id)?.DnaMatches.Count > 0) ?? choices.FirstOrDefault();
        if (pick == DnaTester) RebuildDnaPage();
        else DnaTester = pick;   // rebuilds through OnDnaTesterChanged
    }

    private void RebuildDnaPage()
    {
        RebuildDnaMatches();
        if (IsDnaClustersTab) RebuildClusters();
        if (IsDnaOddsTab) RebuildWatoSubjects();
        if (IsDnaCalculatorTab) RebuildCmCalculator();
        if (IsDnaChromosomesTab) RebuildPainting();
    }

    // ---- chromosome painter ----

    private void RebuildPainting()
    {
        PaintGroupRows.Clear();
        DnaPainting = Tester is { } tester ? ChromosomePainter.Paint(_tree, tester) : null;
        OnPropertyChanged(nameof(HasPainting));
        if (DnaPainting is not { } painting || Tester is not { } t) { PaintingSummary = ""; return; }
        foreach (var g in painting.Groups) PaintGroupRows.Add(new PaintGroupRow(g, ClusterBrush(g.Number)));
        if (!t.DnaMatches.Any(m => m.ChromosomeSegments.Count > 0))
        {
            PaintingSummary = "No segment data yet. Import a segment download (FamilyTreeDNA chromosome browser, MyHeritage shared segments, GEDmatch one-to-one) to paint where each match's DNA sits.";
            return;
        }
        var parts = new List<string>
        {
            $"Painted {Percent(painting.Coverage(PaintSide.Father))} of the father's side and " +
            $"{Percent(painting.Coverage(PaintSide.Mother))} of the mother's, from {painting.Segments.Count:N0} segment{(painting.Segments.Count == 1 ? "" : "s")}.",
        };
        if (painting.Unlinked > 0)
            parts.Add($"{painting.Unlinked:N0} match{(painting.Unlinked == 1 ? " has" : "es have")} segments but no link to the tree yet: link them on the Matches tab to paint them.");
        if (painting.Unrelated > 0)
            parts.Add($"{painting.Unrelated:N0} linked match{(painting.Unrelated == 1 ? " isn't" : "es aren't")} related by blood in the tree.");
        PaintingSummary = string.Join(" ", parts);
    }

    private static string Percent(double share) => share > 0 && share < 0.01 ? "under 1%" : $"{share:0%}";

    // ---- matches ----

    private void RebuildDnaMatches()
    {
        DnaMatchRows.Clear();
        if (Tester is not { } tester)
        {
            HasDnaMatches = false;
            DnaSummary = "";
            return;
        }
        var filter = DnaFilter.Trim();
        foreach (var m in tester.DnaMatches.OrderByDescending(m => m.SharedCm).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (filter.Length > 0 && !m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            DnaMatchRows.Add(ToRow(tester, m));
        }
        HasDnaMatches = tester.DnaMatches.Count > 0;
        var linked = tester.DnaMatches.Count(m => _tree.Person(m.PersonId) != null);
        var misfits = DnaMatchRows.Count(r => r.Misfit);
        DnaSummary = !HasDnaMatches ? ""
            : $"{tester.DnaMatches.Count:N0} match{(tester.DnaMatches.Count == 1 ? "" : "es")}  ·  {linked:N0} linked to the tree"
              + (misfits > 0 ? $"  ·  {misfits:N0} don't fit the tree" : "");
    }

    private DnaMatchRow ToRow(Person tester, DnaMatch m)
    {
        var numbers = Display.Join(
            m.Segments > 0 ? $"{m.Segments} segment{(m.Segments == 1 ? "" : "s")}" : "",
            m.LongestCm > 0 ? $"longest {DnaText.Cm(m.LongestCm)}" : "",
            m.SharedWith.Count > 0 ? $"{m.SharedWith.Count} shared match{(m.SharedWith.Count == 1 ? "" : "es")}" : "");
        var person = _tree.Person(m.PersonId);
        var link = person != null ? Placement.Link(_tree, tester, person) : null;
        var misfit = link != null && m.SharedCm > 0 && CmModel.GroupProbability(link.Kinship, m.SharedCm) < DnaText.FitThreshold;
        var treeText = person == null ? ""
            : link == null ? "not connected by birth in the tree"
            : misfit ? $"tree says {link.Kinship.Label} — unlikely with this much DNA"
            : $"tree says {link.Kinship.Label} — fits";
        return new DnaMatchRow(m, numbers, m.SharedCm > 0 ? DnaText.Prediction(m.SharedCm) : "", person?.DisplayName ?? "", treeText, misfit);
    }

    [RelayCommand]
    private async Task AddDnaMatchAsync()
    {
        if (Tester is not { } tester) return;
        await EditMatchAsync(tester, null);
    }

    [RelayCommand]
    private async Task EditDnaMatchAsync(DnaMatchRow row)
    {
        if (Tester is not { } tester) return;
        await EditMatchAsync(tester, row.Match);
    }

    [RelayCommand]
    private void OpenDnaMatchPerson(DnaMatchRow row)
    {
        if (_tree.Person(row.Match.PersonId) is { } p) OpenPerson(p.Id);
    }

    private async Task EditMatchAsync(Person tester, DnaMatch? existing)
    {
        var match = existing ?? new DnaMatch();
        var editor = new DnaMatchEditorViewModel(_tree, tester, match, existing == null);
        var result = await _dialogs.EditDnaMatchAsync(editor);
        if (result == EditorResult.Cancelled) return;
        if (result == EditorResult.Deleted && existing != null)
        {
            Change("delete DNA match", new[] { tester }, () =>
            {
                tester.DnaMatches.Remove(existing);
                foreach (var m in tester.DnaMatches) m.SharedWith.Remove(existing.Id);
            });
            return;
        }
        var shared = editor.SharedIds.ToHashSet();
        Change(existing == null ? "add DNA match" : "edit DNA match", new[] { tester }, () =>
        {
            editor.Apply(match);
            if (existing == null) tester.DnaMatches.Add(match);
            // Shared matches go both ways.
            match.SharedWith.Clear();
            match.SharedWith.AddRange(shared);
            foreach (var other in tester.DnaMatches)
            {
                if (other == match) continue;
                if (shared.Contains(other.Id)) { if (!other.SharedWith.Contains(match.Id)) other.SharedWith.Add(match.Id); }
                else other.SharedWith.Remove(match.Id);
            }
        });
    }

    [RelayCommand]
    private async Task ImportDnaMatchesAsync()
    {
        if (Tester is not { } tester) return;
        var path = await _dialogs.OpenFileAsync($"Import DNA matches for {tester.DisplayName}", ("Match or segment lists", "*.csv;*.txt;*.tsv"));
        if (path == null) return;
        string text;
        try { text = await File.ReadAllTextAsync(path); }
        catch (Exception ex) { await _dialogs.ShowMessageAsync("Couldn't read the file", ex.Message); return; }

        var matchName = "";
        if (SegmentImport.NeedsMatchName(text))
        {
            matchName = (await _dialogs.PromptAsync("Whose segments are these?",
                "This segment file doesn't name the match. Choose the match it compares with, or type a new name.",
                "", tester.DnaMatches.Select(m => m.Name).Order(StringComparer.OrdinalIgnoreCase).ToList()))?.Trim() ?? "";
            if (matchName.Length == 0) return;
        }
        var company = await _dialogs.PromptAsync("Which company is this list from?",
            "Optional — it keeps matches from different companies apart. Leave it blank for a shared-matches list.",
            "", DnaMatchEditorViewModel.Companies) ?? "";
        MatchImportResult? result = null;
        Change("import DNA matches", new[] { tester }, () => result = MatchImport.Import(tester, text, company.Trim(), matchName));
        var problems = result!.Problems.Count > 0 ? "\n\n" + string.Join("\n", result.Problems.Take(8)) + (result.Problems.Count > 8 ? "\n…" : "") : "";
        await _dialogs.ShowMessageAsync(result.Kind == "segments" ? "DNA segments imported" : "DNA matches imported", result.Summary + "." + problems);
        if (IsDnaChromosomesTab) RebuildPainting();
    }

    // ---- clusters ----

    private static readonly Avalonia.Media.IBrush[] ClusterBrushes = new[]
    {
        "#2E8B6E", "#C0563F", "#3B6FB6", "#B58A1E", "#8A4FB0", "#2F9BB0", "#B5487A", "#6A8F2B", "#D07A2E", "#5A5FB8",
    }.Select(c => (Avalonia.Media.IBrush)new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(c))).ToArray();

    public static Avalonia.Media.IBrush ClusterBrush(int number) => ClusterBrushes[(number - 1) % ClusterBrushes.Length];

    private void RebuildClusters()
    {
        DnaClusterCards.Clear();
        if (Tester is not { } tester)
        {
            DnaClusters = null;
            ClusterSummary = "";
            OnPropertyChanged(nameof(HasClusters));
            return;
        }
        var min = DnaMatchEditorViewModel.ParseNumber(ClusterMinCm) ?? Clusters.DefaultMinCm;
        var max = DnaMatchEditorViewModel.ParseNumber(ClusterMaxCm) ?? Clusters.DefaultMaxCm;
        var result = Clusters.Build(tester.DnaMatches, min, max);
        DnaClusters = result;
        foreach (var c in result.Clusters)
        {
            var ancestors = Placement.SharedAncestors(_tree, tester, c.Members);
            var linked = c.Members.Count(m => _tree.Person(m.PersonId) != null);
            var top = ancestors.Where(a => a.Count == ancestors[0].Count).Take(2)
                               .Select(a => _tree.Person(a.AncestorId)?.DisplayName).OfType<string>().ToList();
            var ancestorText = top.Count == 0 ? ""
                : $"Linked matches point to {string.Join(" and ", top)} ({ancestors[0].Count} of {linked} linked)";
            DnaClusterCards.Add(new DnaClusterCard(c.Number,
                string.Join(", ", c.Members.Take(12).Select(m => $"{m.Name} ({m.SharedCm:0} cM)")) + (c.Members.Count > 12 ? $", and {c.Members.Count - 12} more" : ""),
                ancestorText, c.Members.Count, ClusterBrush(c.Number)));
        }
        var shared = tester.DnaMatches.Any(m => m.SharedWith.Count > 0);
        ClusterSummary = result.Order.Count == 0
            ? $"No matches between {min:0} and {max:0} cM."
            : !shared
                ? "Clusters come from shared matches, and there aren't any yet. Import a shared-matches list, or tick the shared matches in each match."
                : $"{result.Order.Count:N0} matches between {min:0} and {max:0} cM: {result.Clusters.Count} cluster{(result.Clusters.Count == 1 ? "" : "s")}, " +
                  $"{result.Unclustered.Count:N0} not in a cluster." + (result.Dropped > 0 ? $" The {result.Dropped:N0} smallest were left out." : "");
        OnPropertyChanged(nameof(HasClusters));
    }

    // ---- what are the odds ----

    private void RebuildWatoSubjects()
    {
        var keep = WatoSubject;
        WatoSubjects.Clear();
        if (Tester is { } tester)
        {
            WatoSubjects.Add(new WatoSubject($"{tester.DisplayName} — using their matches linked to the tree", tester, null));
            foreach (var m in tester.DnaMatches.Where(m => _tree.Person(m.PersonId) == null).OrderByDescending(m => m.SharedCm))
                WatoSubjects.Add(new WatoSubject($"{m.Name} — a match of {tester.DisplayName} ({DnaText.Cm(m.SharedCm)})", null, m.Name));
        }
        var pick = WatoSubjects.FirstOrDefault(s => keep != null && s.Tester?.Id == keep.Tester?.Id && s.MatchName == keep.MatchName)
                   ?? WatoSubjects.FirstOrDefault();
        if (pick == WatoSubject) RebuildWatoObservations();
        else WatoSubject = pick;
    }

    private void RebuildWatoObservations()
    {
        WatoObservations.Clear();
        if (WatoSubject is { Tester: { } placing })
        {
            foreach (var m in placing.DnaMatches.OrderByDescending(m => m.SharedCm))
                if (_tree.Person(m.PersonId) is { } p && p.Id != placing.Id)
                    WatoObservations.Add(new WatoObservationRow(new Observation(p, m.SharedCm), $"match \"{m.Name}\""));
        }
        else if (WatoSubject is { MatchName: { } name })
        {
            // Everyone in the tree who tested and lists this match.
            foreach (var t in _tree.People.Values)
                foreach (var m in t.DnaMatches.Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
                    WatoObservations.Add(new WatoObservationRow(new Observation(t, m.SharedCm), $"{t.DisplayName}'s match list"));
        }
        foreach (var o in WatoObservations) o.PropertyChanged += (_, _) => RebuildWato();
        OnPropertyChanged(nameof(HasWatoObservations));
        RebuildWato();
    }

    private void RebuildWato()
    {
        WatoResults.Clear();
        var observations = WatoObservations.Where(o => o.Include).Select(o => o.Observation).ToList();
        var hypotheses = WatoHypotheses.Select(h => _tree.Person(h.Parent) is { } a ? new Hypothesis(a, _tree.Person(h.OtherParent)) : null)
                                       .OfType<Hypothesis>().ToList();
        if (observations.Count > 0 && hypotheses.Count > 0)
        {
            var results = Placement.Evaluate(_tree, observations, hypotheses);
            foreach (var r in results)
            {
                var details = string.Join("\n", r.Fits.Select(f =>
                    $"{f.Observation.Person.DisplayName}: would be {f.Relationship}, shares {DnaText.Cm(f.Observation.Cm)}"
                    + (f.Kinship is { } k && f.Observation.Cm > 0 ? $" (about {CmModel.AverageCm(k):N0} cM is usual)" : "")));
                WatoResults.Add(new WatoHypothesisRow(r.Hypothesis,
                    DnaText.Percent(r.Probability),
                    r == results[0] ? "Most likely" : r.TimesLessLikely > 1e6 ? "Over a million times less likely" : $"{r.TimesLessLikely.ToString(r.TimesLessLikely >= 10 ? "#,0" : "0.#")}× less likely",
                    r.Probability, details, r == results[0]));
            }
        }
        WatoSummary = observations.Count == 0
            ? "Nobody to compare with yet. Link the matches you've identified to people in the tree, or choose a match that several relatives share."
            : hypotheses.Count == 0
                ? "Add two or more places they might fit — for example a child of each couple they could descend from."
                : $"Compared with {observations.Count} relative{(observations.Count == 1 ? "" : "s")}. The odds assume one of these places is right and each was equally likely beforehand.";
        OnPropertyChanged(nameof(HasWatoResults));
    }

    private List<ParentChoice> BuildParentChoices()
    {
        var list = new List<ParentChoice>();
        foreach (var f in _tree.Families.Values)
            if (_tree.Person(f.Partner1Id) is { } a && _tree.Person(f.Partner2Id) is { } b)
                list.Add(new ParentChoice(a.Id, b.Id, $"Child of {a.DisplayName} and {b.DisplayName}"));
        foreach (var p in _tree.People.Values)
            list.Add(new ParentChoice(p.Id, null, $"Child of {p.DisplayName} (other parent unknown)"));
        return list.OrderBy(c => c.Text, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ---- calculator ----

    private void RebuildCmCalculator()
    {
        CmCalculatorRows.Clear();
        var cm = DnaMatchEditorViewModel.ParseNumber(CmCalculatorText);
        if (cm is > 0 and <= 4000)
            foreach (var g in CmModel.Predict(cm.Value).Where(g => g.Probability >= 0.005).Take(6))
                CmCalculatorRows.Add(new CmGroupRow(g));
        CmCalculatorNote = cm is null or 0 ? ""
            : cm > 4000 ? "That's more than a parent shares; check the number."
            : cm < 20 ? "Matches this small often come from much further back than 5th cousins, or aren't real matches at all."
            : "";
        OnPropertyChanged(nameof(HasCmCalculatorRows));
    }
}
