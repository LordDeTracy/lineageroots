using CommunityToolkit.Mvvm.ComponentModel;
using Lineage.Core.Export;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>
/// Privacy choices before sharing a tree (GEDCOM or website): what to do with living people,
/// private events, notes and photos. Shows how many people are thought to be living.
/// </summary>
public sealed partial class ExportOptionsViewModel : ObservableObject
{
    public ExportOptionsViewModel(FamilyTree tree, string title, string action, Person? branchRoot = null, bool branchOnly = false)
    {
        Title = title;
        Action = action;
        _tree = tree;
        BranchRoot = branchRoot;
        // Splitting off a branch has no "whole tree" choice.
        BranchOptions = branchRoot == null ? Array.Empty<string>() : (branchOnly ? BranchWords.Skip(1) : BranchWords)
            .Select(w => w.Replace("{0}", branchRoot.DisplayName)).ToArray();
        _branchOffset = branchOnly ? 1 : 0;
        UpdateBranchCount();
        var year = DateTime.Today.Year;
        LivingCount = tree.People.Values.Count(p => Privacy.IsLiving(tree, p, 110, year));
    }

    private readonly FamilyTree _tree;
    private readonly int _branchOffset;

    public string Title { get; }
    public string Action { get; }

    // ---- which people ----------------------------------------------------------------------------

    private static readonly string[] BranchWords =
    {
        "The whole tree",
        "Ancestors of {0}",
        "Descendants of {0}, with their husbands and wives",
        "Ancestors and descendants of {0}",
    };

    /// <summary>The person a branch is taken from (the one showing); null hides the choice.</summary>
    public Person? BranchRoot { get; }
    public bool HasBranch => BranchRoot != null;
    public string[] BranchOptions { get; }
    [ObservableProperty] private int _branchIndex;
    [ObservableProperty] private string _branchCountText = "";
    partial void OnBranchIndexChanged(int value) => UpdateBranchCount();

    public BranchKind Branch => BranchRoot == null ? BranchKind.Everyone : (BranchKind)Math.Clamp(BranchIndex + _branchOffset, 0, 3);

    private void UpdateBranchCount()
    {
        if (BranchRoot == null) return;
        var n = Lineage.Core.Export.Branch.People(_tree, BranchRoot, Branch).Count;
        BranchCountText = n == 1 ? "1 person" : $"{n:N0} people";
    }

    /// <summary>The tree to export: the chosen branch (a copy), or the tree itself.</summary>
    public FamilyTree Apply(FamilyTree tree) =>
        BranchRoot == null || Branch == BranchKind.Everyone ? tree : Lineage.Core.Export.Branch.Extract(tree, Lineage.Core.Export.Branch.People(tree, BranchRoot, Branch));
    public int LivingCount { get; }
    public string LivingText => LivingCount switch
    {
        0 => "Nobody in this tree looks to be living.",
        1 => "1 person looks to be living (no death recorded and born within the last 110 years).",
        _ => $"{LivingCount:N0} people look to be living (no death recorded and born within the last 110 years).",
    };

    public string[] LivingOptions { get; } =
    {
        "Hide their details — show them as \"Living\" with their surname",
        "Leave them out completely",
        "Include everything (only for your own use)",
    };

    [ObservableProperty] private int _livingIndex;

    /// <summary>Set for a GEDCOM export: which version to write.</summary>
    public bool ShowGedcomVersion { get; init; }
    public string[] GedcomVersions { get; } =
        { "GEDCOM 5.5.1 — read by nearly every genealogy program", "GEDCOM 7.0 — the newest version, for programs that read it" };
    [ObservableProperty] private int _gedcomVersionIndex;
    public bool Gedcom7 => GedcomVersionIndex == 1;
    [ObservableProperty] private bool _excludePrivateEvents = true;
    [ObservableProperty] private bool _excludeNotes;
    [ObservableProperty] private bool _excludeMedia;

    public PrivacyOptions ToOptions() => new()
    {
        Living = LivingIndex switch { 1 => LivingMode.Exclude, 2 => LivingMode.Include, _ => LivingMode.Privatize },
        ExcludePrivateEvents = ExcludePrivateEvents,
        ExcludeNotes = ExcludeNotes,
        ExcludeMedia = ExcludeMedia,
    };

    /// <summary>True when nothing would be filtered, so the original tree can be exported as it is.</summary>
    public bool KeepsEverything => LivingIndex == 2 && !ExcludePrivateEvents && !ExcludeNotes && !ExcludeMedia;
}
