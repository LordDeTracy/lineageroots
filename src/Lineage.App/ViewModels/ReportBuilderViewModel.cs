using CommunityToolkit.Mvvm.ComponentModel;
using Lineage.Core.Export;
using Lineage.Core.Model;
using Lineage.Core.Reports;

namespace Lineage.App.ViewModels;

/// <summary>Choices for a report put together by the user: who is in it, what it says about each, and how it's laid out.</summary>
public sealed partial class ReportBuilderViewModel : ObservableObject
{
    private readonly FamilyTree _tree;

    public ReportBuilderViewModel(FamilyTree tree, Person root)
    {
        _tree = tree;
        Root = root;
        PeopleOptions = new[]
        {
            $"Just {root.DisplayName}",
            $"Ancestors of {root.DisplayName}",
            $"Descendants of {root.DisplayName}, with their husbands and wives",
            $"Ancestors and descendants of {root.DisplayName}",
            "Everyone in the tree",
        };
        Count();
    }

    public Person Root { get; }
    public string[] PeopleOptions { get; }
    public int[] GenerationOptions { get; } = { 2, 3, 4, 5, 6, 7, 8, 10, 12 };
    public string[] OrderOptions { get; } = { "Nearest generations first", "Alphabetically by surname" };

    [ObservableProperty] private string _title = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasGenerations))] private int _peopleIndex = 1;
    [ObservableProperty] private int _generations = 4;
    [ObservableProperty] private int _orderIndex;
    [ObservableProperty] private bool _story = true;
    [ObservableProperty] private bool _facts = true;
    [ObservableProperty] private bool _family = true;
    [ObservableProperty] private bool _notes = true;
    [ObservableProperty] private bool _photos;
    [ObservableProperty] private bool _sources = true;
    [ObservableProperty] private bool _index;
    [ObservableProperty] private bool _pagePerPerson;
    [ObservableProperty] private string _countText = "";

    /// <summary>"Just the person" and "everyone" have no generations to choose.</summary>
    public bool HasGenerations => PeopleIndex is 1 or 2 or 3;

    partial void OnPeopleIndexChanged(int value) => Count();
    partial void OnGenerationsChanged(int value) => Count();

    private void Count()
    {
        var n = Narrative.CustomPeople(_tree, Root, ToOptions(null)).Count;
        CountText = n == 1 ? "1 person" : $"{n:N0} people";
    }

    /// <summary>The choices for <see cref="Narrative.Custom"/>; "just the person" is their ancestors one generation deep.</summary>
    public CustomReportOptions ToOptions(Func<string, string?>? findMedia) => new()
    {
        Title = Title.Trim(),
        People = PeopleIndex switch { 0 or 1 => BranchKind.Ancestors, 2 => BranchKind.Descendants, 3 => BranchKind.AncestorsAndDescendants, _ => BranchKind.Everyone },
        Generations = PeopleIndex == 0 ? 1 : Generations,
        Order = OrderIndex == 1 ? CustomReportOrder.ByName : CustomReportOrder.ByGeneration,
        Story = Story, Facts = Facts, Family = Family, Notes = Notes, Photos = Photos,
        Sources = Sources, Index = Index, PagePerPerson = PagePerPerson,
        FindMedia = findMedia,
    };
}
