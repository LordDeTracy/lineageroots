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
    public ExportOptionsViewModel(FamilyTree tree, string title, string action)
    {
        Title = title;
        Action = action;
        var year = DateTime.Today.Year;
        LivingCount = tree.People.Values.Count(p => Privacy.IsLiving(tree, p, 110, year));
    }

    public string Title { get; }
    public string Action { get; }
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
