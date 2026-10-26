using CommunityToolkit.Mvvm.ComponentModel;
using Lineage.Core.Reports;

namespace Lineage.App.ViewModels;

/// <summary>Choices for a family history book: wording, how far back and forward, and extras.</summary>
public sealed partial class BookOptionsViewModel : ObservableObject
{
    public static readonly int[] Generations = { 2, 3, 4, 5, 6, 7, 8 };

    public BookOptionsViewModel(string personName, string author)
    {
        _bookTitle = $"The family of {personName}";
        _author = author;
    }

    public int[] GenerationOptions => Generations;
    public string[] FormatOptions { get; } = { "PDF (to print or share)", "Word (to edit further)" };

    [ObservableProperty] private string _bookTitle;
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _author;
    [ObservableProperty] private string _introduction = FamilyBookOptions.PlaceholderIntroduction;
    [ObservableProperty] private int _ancestorGenerations = 5;
    [ObservableProperty] private int _descendantGenerations = 4;
    [ObservableProperty] private bool _includeSources = true;
    [ObservableProperty] private bool _includeIndex = true;
    [ObservableProperty] private int _formatIndex;

    public bool AsWord => FormatIndex == 1;

    public FamilyBookOptions ToOptions() => new()
    {
        Title = BookTitle.Trim(), Subtitle = Subtitle.Trim(), Author = Author.Trim(), Introduction = Introduction,
        AncestorGenerations = AncestorGenerations, DescendantGenerations = DescendantGenerations,
        IncludeSources = IncludeSources, IncludeIndex = IncludeIndex,
    };
}
