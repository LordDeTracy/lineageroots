using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Reports;

namespace Lineage.App.ViewModels;

/// <summary>Choices for a family history book: wording, how far back and forward, and extras.</summary>
public sealed partial class BookOptionsViewModel : ObservableObject
{
    /// <summary>0 leaves that part of the book out.</summary>
    public static readonly int[] Generations = { 0, 2, 3, 4, 5, 6, 7, 8 };

    public BookOptionsViewModel(string personName, string author)
    {
        _bookTitle = $"The family of {personName}";
        _author = author;
    }

    public string[] GenerationOptions { get; } = Generations.Select(g => g == 0 ? "Leave out" : g.ToString()).ToArray();

    public int AncestorIndex
    {
        get => Array.IndexOf(Generations, AncestorGenerations);
        set { if (value >= 0) AncestorGenerations = Generations[value]; }
    }

    public int DescendantIndex
    {
        get => Array.IndexOf(Generations, DescendantGenerations);
        set { if (value >= 0) DescendantGenerations = Generations[value]; }
    }

    /// <summary>Chapters written by hand (stories, memories), placed after the introduction.</summary>
    public ObservableCollection<ChapterRow> Chapters { get; } = new();

    [RelayCommand]
    private void AddChapter() => Chapters.Add(new ChapterRow(this));

    internal void Remove(ChapterRow row) => Chapters.Remove(row);
    public string[] FormatOptions { get; } = { "PDF (to print or share)", "Word (to edit further)" };

    [ObservableProperty] private string _bookTitle;
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _author;
    [ObservableProperty] private string _introduction = FamilyBookOptions.PlaceholderIntroduction;
    [ObservableProperty] private int _ancestorGenerations = 5;
    [ObservableProperty] private int _descendantGenerations = 4;
    [ObservableProperty] private bool _includeSources = true;
    [ObservableProperty] private bool _includeIndex = true;
    [ObservableProperty] private bool _includePhotos = true;
    [ObservableProperty] private int _formatIndex;

    public bool AsWord => FormatIndex == 1;

    /// <summary>Both parts left out would make an empty book.</summary>
    public bool HasContent => AncestorGenerations > 0 || DescendantGenerations > 0 || Chapters.Any(c => c.Text.Trim().Length > 0);

    public FamilyBookOptions ToOptions()
    {
        var o = new FamilyBookOptions
        {
            Title = BookTitle.Trim(), Subtitle = Subtitle.Trim(), Author = Author.Trim(), Introduction = Introduction,
            AncestorGenerations = AncestorGenerations, DescendantGenerations = DescendantGenerations,
            IncludeSources = IncludeSources, IncludeIndex = IncludeIndex, IncludePhotos = IncludePhotos,
        };
        o.Chapters.AddRange(Chapters.Select(c => new BookChapter(c.Title, c.Text)));
        return o;
    }
}

/// <summary>A chapter the compiler writes in the book dialog.</summary>
public sealed partial class ChapterRow : ObservableObject
{
    private readonly BookOptionsViewModel _owner;
    public ChapterRow(BookOptionsViewModel owner) => _owner = owner;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _text = "";

    [RelayCommand]
    private void Remove() => _owner.Remove(this);
}
