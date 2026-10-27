using CommunityToolkit.Mvvm.ComponentModel;
using Lineage.Core.Analysis;

namespace Lineage.App.ViewModels;

/// <summary>Evidence on the person page and the charts: how well proven each fact is.</summary>
public sealed partial class MainViewModel
{
    /// <summary>"3 strong · 1 weak · 2 without a source", for the life events heading.</summary>
    [ObservableProperty] private string _proofTally = "";

    /// <summary>Colour the charts' boxes by how well each person's birth and death are proven, instead of by sex.</summary>
    [ObservableProperty] private bool _chartsShowEvidence;

    private static string EvidenceTally(IReadOnlyList<ProofLevel> levels)
    {
        if (levels.Count == 0) return "";
        var parts = new List<string>();
        void Add(int n, string text) { if (n > 0) parts.Add($"{n} {text}"); }
        Add(levels.Count(l => l == ProofLevel.Proven), "proven");
        Add(levels.Count(l => l == ProofLevel.Strong), "strong");
        Add(levels.Count(l => l == ProofLevel.Good), "good");
        Add(levels.Count(l => l == ProofLevel.Weak), "weak");
        Add(levels.Count(l => l == ProofLevel.Conflicting), "conflicting");
        Add(levels.Count(l => l == ProofLevel.Unsourced), "without a source");
        return string.Join("  ·  ", parts);
    }
}
