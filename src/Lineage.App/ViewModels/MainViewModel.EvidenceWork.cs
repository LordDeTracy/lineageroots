using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>A fact in the evidence worklist.</summary>
public sealed record EvidenceWorkRow(FactWork Work)
{
    public string Who => Work.Who;
    public string What => Work.What;
    public string Level => Work.Evidence.Label;
    public string Gps => $"{Work.GpsMet} of 5";
    public bool IsConflict => Work.Kind == WorkKind.Conflict;
    public bool IsConcluded => Work.Kind == WorkKind.Concluded;
}

/// <summary>One record's say on the fact being analysed.</summary>
public sealed record ClaimRow(Claim Claim)
{
    public string Source => Claim.Source;
    public string Says => Claim.Says;
    public string Basis => Claim.Basis;
    public bool Disagrees => Claim.Disagrees;
    public string Rating
    {
        get
        {
            var parts = new[]
            {
                Claim.Kind switch { SourceKind.Original => "original", SourceKind.Derivative => "copy or index", SourceKind.Authored => "compiled work", _ => "" },
                Claim.Information switch { InformationKind.Primary => "first-hand", InformationKind.Secondary => "second-hand", _ => "" },
                Claim.Evidence switch { EvidenceKind.Direct => "direct", EvidenceKind.Indirect => "indirect", EvidenceKind.Negative => "negative", _ => "" },
            }.Where(p => p.Length > 0).ToList();
            return parts.Count == 0 ? "Not rated" : string.Join(" · ", parts);
        }
    }
}

/// <summary>One part of the Genealogical Proof Standard for the fact being analysed.</summary>
public sealed record ProofCheckRow(GpsCheck Check)
{
    public string Label => Check.Label;
    public string Detail => Check.Detail;
    public bool Met => Check.Met;
    public string Mark => Met ? "✓" : "○";
}

/// <summary>
/// The Evidence tab's Analysis view: a worklist of the tree's vital facts by what they need; for the one picked,
/// every record that speaks to it side by side, the proof standard's five parts, and the conclusion with its reasoning.
/// </summary>
public sealed partial class MainViewModel
{
    public static IReadOnlyList<string> EvidenceModes { get; } = new[] { "Sources", "Analysis" };
    public static IReadOnlyList<string> Conclusions { get; } = new[] { "Not decided", "Proven", "Disproved" };
    private const int MaxEvidenceRows = 400;

    public ObservableCollection<EvidenceWorkRow> EvidenceRows { get; } = new();
    public ObservableCollection<ClaimRow> EvidenceClaims { get; } = new();
    public ObservableCollection<ProofCheckRow> EvidenceGps { get; } = new();
    private List<FactWork> _evidenceAll = new();
    private bool _savingConclusion;

    /// <summary>0: the sources list and a source's page; 1: the analysis workspace.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEvidenceAnalysis), nameof(IsSourcesList), nameof(HasSidebar))]
    private int _evidenceModeIndex;
    /// <summary>0 needs work, 1 conflicts, 2 no source, 3 weak, 4 ready to call proven, 5 concluded.</summary>
    [ObservableProperty] private int _evidenceFilterIndex;
    [ObservableProperty] private string _evidenceSummary = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelectedEvidence))] private EvidenceWorkRow? _selectedEvidence;
    [ObservableProperty] private string _evidenceQuestion = "";
    [ObservableProperty] private int _evidenceConclusionIndex;
    [ObservableProperty] private string _evidenceReasoning = "";

    public bool IsEvidenceAnalysis => Section == AppSection.Sources && EvidenceModeIndex == 1;
    public bool IsSourcesList => Section == AppSection.Sources && EvidenceModeIndex == 0;
    public bool HasSelectedEvidence => SelectedEvidence != null;
    /// <summary>The worklist being rebuilt; tests await it.</summary>
    internal Task EvidenceTask { get; private set; } = Task.CompletedTask;

    partial void OnEvidenceModeIndexChanged(int value)
    {
        if (IsEvidenceAnalysis) RebuildEvidence();
    }

    partial void OnEvidenceFilterIndexChanged(int value) => ApplyEvidenceFilter();

    private void RebuildEvidence() => EvidenceTask = RebuildEvidenceAsync();

    private async Task RebuildEvidenceAsync()
    {
        var tree = _tree;
        var all = await Task.Run(() => EvidenceWork.Worklist(tree));
        if (!ReferenceEquals(tree, _tree)) return;
        _evidenceAll = all;
        int Count(WorkKind k) => all.Count(w => w.Kind == k);
        EvidenceSummary = all.Count == 0
            ? "No births, deaths or marriages recorded yet."
            : $"{all.Count:N0} facts: {Count(WorkKind.Conflict):N0} with conflicts · {Count(WorkKind.Unsourced):N0} with no source · " +
              $"{Count(WorkKind.Weak):N0} weak · {Count(WorkKind.ReadyToProve):N0} ready to decide · {Count(WorkKind.Concluded):N0} concluded";
        ApplyEvidenceFilter();
    }

    private void ApplyEvidenceFilter()
    {
        var selected = SelectedEvidence?.Work.Event;
        var rows = _evidenceAll.Where(w => EvidenceFilterIndex switch
        {
            1 => w.Kind == WorkKind.Conflict,
            2 => w.Kind == WorkKind.Unsourced,
            3 => w.Kind == WorkKind.Weak,
            4 => w.Kind == WorkKind.ReadyToProve,
            5 => w.Kind == WorkKind.Concluded,
            _ => w.Kind is WorkKind.Conflict or WorkKind.Unsourced or WorkKind.Weak,
        }).Take(MaxEvidenceRows).Select(w => new EvidenceWorkRow(w)).ToList();
        EvidenceRows.Clear();
        foreach (var r in rows) EvidenceRows.Add(r);
        SelectedEvidence = rows.FirstOrDefault(r => ReferenceEquals(r.Work.Event, selected));
    }

    partial void OnSelectedEvidenceChanged(EvidenceWorkRow? value)
    {
        EvidenceClaims.Clear();
        EvidenceGps.Clear();
        if (value == null) { EvidenceQuestion = EvidenceReasoning = ""; return; }
        var (e, owner) = (value.Work.Event, value.Work.Owner);
        EvidenceQuestion = EvidenceWork.Question(_tree, e, owner);
        foreach (var c in EvidenceWork.Claims(_tree, e, owner)) EvidenceClaims.Add(new ClaimRow(c));
        foreach (var g in value.Work.Gps) EvidenceGps.Add(new ProofCheckRow(g));
        EvidenceConclusionIndex = (int)e.Conclusion;
        EvidenceReasoning = e.Reasoning;
    }

    /// <summary>Records the conclusion and the reasoning behind it on the fact (one undoable step).</summary>
    [RelayCommand]
    private void SaveConclusion()
    {
        if (SelectedEvidence?.Work is not { } work) return;
        var (e, owner) = (work.Event, work.Owner);
        var conclusion = (FactConclusion)Math.Clamp(EvidenceConclusionIndex, 0, 2);
        var reasoning = EvidenceReasoning.Trim();
        if (e.Conclusion == conclusion && e.Reasoning == reasoning) return;
        _savingConclusion = true;   // the worklist is updated in place below, not rebuilt
        try
        {
            Change("record a conclusion", new[] { owner }, () =>
            {
                e.Conclusion = conclusion;
                e.Reasoning = reasoning;
            });
        }
        finally { _savingConclusion = false; }
        // Keep the fact selected, re-assessed, even if it now belongs under another filter.
        var updated = EvidenceWork.Work(_tree, e, owner);
        var at = _evidenceAll.FindIndex(w => ReferenceEquals(w.Event, e));
        if (at >= 0) _evidenceAll[at] = updated;
        var row = new EvidenceWorkRow(updated);
        var shown = EvidenceRows.ToList().FindIndex(r => ReferenceEquals(r.Work.Event, e));
        if (shown >= 0) EvidenceRows[shown] = row;
        SelectedEvidence = row;
        Status = "Conclusion recorded";
    }

    [RelayCommand]
    private void OpenEvidencePerson()
    {
        var id = SelectedEvidence?.Work.Owner switch
        {
            Person p => p.Id,
            Family f => f.PartnerIds.FirstOrDefault(),
            _ => 0,
        };
        if (id != 0) OpenPerson(id);
    }
}
