using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lineage.App.ViewModels;

/// <summary>A keyboard shortcut and what it does, for the tutorial's last page.</summary>
public sealed record Shortcut(string Keys, string What);

/// <summary>One page of the tutorial.</summary>
public sealed record TutorialPage(string Title, string Body);

/// <summary>
/// The tour shown when the program starts (until "Don't show this again" is ticked) and on F1: the main ideas, a page
/// at a time, ending with the keyboard shortcuts.
/// </summary>
public sealed partial class TutorialViewModel : ObservableObject
{
    public static IReadOnlyList<Shortcut> Shortcuts { get; } = new Shortcut[]
    {
        new("Ctrl+K", "Quick search: jump to anyone, any place or source, or run a command"),
        new("Ctrl+F", "Search the people list"),
        new("Alt+1 … Alt+8", "Home, People, Evidence, Places, Research, DNA, Charts, Reports"),
        new("Ctrl+1 / 6 / 7", "The person's page, map and timeline"),
        new("Ctrl+2 … 5, 8, 9, 0", "Charts: pedigree, fan, descendants, hourglass, lifelines, relationship, statistics"),
        new("Alt+← / Alt+→", "Back and forward"),
        new("Ctrl+H", "Go to the home person"),
        new("Ctrl+E", "Edit the person showing"),
        new("Ctrl+Shift+A", "Add a person"),
        new("Ctrl+T", "Transcribe a record (baptism, marriage, burial, will, deed)"),
        new("Ctrl+D", "Bookmark the person showing"),
        new("Ctrl+R", "How are they related?"),
        new("Ctrl+P", "Reports and books"),
        new("Ctrl+Z / Ctrl+Y", "Undo and redo"),
        new("Ctrl+Shift+H", "Change history: who changed what, and undo from any point"),
        new("Ctrl+S", "Save now (it also saves by itself)"),
        new("Ctrl+N / Ctrl+O / Ctrl+I", "New tree, open a tree, import AQ or GEDCOM"),
        new("F1", "This tutorial"),
    };

    public static IReadOnlyList<TutorialPage> Pages { get; } = new TutorialPage[]
    {
        new("Welcome to Lineage Roots",
            "This short tour shows the main ideas. Use Next and Back, or close it at any time; F1 brings it back.\n\n" +
            "Bring in your family from AQ or any GEDCOM file with Import, or start a new tree. Your original file is never " +
            "changed: Lineage Roots keeps its own copy in Documents\\Lineage Roots, and saves it by itself after every change."),
        new("People and their families",
            "The People tab lists everyone; click a name to see their page — facts, family, notes, photos and sources. " +
            "Search by name, or by words in their notes, and use Filters for places, dates and years lived.\n\n" +
            "Set yourself as the home person (in the person's ⋯ menu) and everyone shows how they're related to you."),
        new("Adding and editing",
            "Edit a person with the Edit button (Ctrl+E); add parents, spouses and children from their family cards, or someone " +
            "new with Add person.\n\nTo enter a record all at once, use Transcribe a record (Ctrl+T) for baptisms, marriages, " +
            "burials, wills and deeds, or Enter household on a census in the Timeline. Ctrl+Z undoes anything."),
        new("Evidence and research",
            "The Evidence tab holds your sources. Its Analysis view lists every birth, marriage and death with how well it's " +
            "proven, lays the records side by side and checks them against the Genealogical Proof Standard.\n\n" +
            "The Research tab keeps your to-do list and a log of searches — including those that found nothing — and finds " +
            "problems in the tree."),
        new("Charts, maps and reports",
            "The Charts tab draws pedigrees, fans, descendants, lifelines, relationship paths and statistics; Save prints " +
            "them or makes a wall chart. The Places tab maps where the family lived.\n\n" +
            "The Reports tab makes a family history book, client research reports and other documents as PDF or Word."),
        new("Sharing",
            "Share a copy with a relative and merge their changes back (⋯ menu), or share on this network at a genealogy " +
            "night: others nearby get a copy with your PIN and your say-so, phones see a family website — no internet needed."),
        new("Keyboard shortcuts", ""),
    };

    public TutorialViewModel(bool showAtStart) => _dontShowAgain = !showAtStart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Page), nameof(IsFirst), nameof(IsLast), nameof(IsShortcuts), nameof(Position), nameof(NextText))]
    private int _index;
    [ObservableProperty] private bool _dontShowAgain;

    public TutorialPage Page => Pages[Index];
    public bool IsFirst => Index == 0;
    public bool IsLast => Index == Pages.Count - 1;
    public bool IsShortcuts => IsLast;
    public string Position => $"{Index + 1} of {Pages.Count}";
    public string NextText => IsLast ? "Done" : "Next";

    /// <summary>Raised when the tour is finished (Done on the last page).</summary>
    public event Action? Finished;

    [RelayCommand]
    private void Next()
    {
        if (IsLast) Finished?.Invoke();
        else Index++;
    }

    [RelayCommand]
    private void Back()
    {
        if (!IsFirst) Index--;
    }

    /// <summary>Opens straight on the shortcuts page.</summary>
    public void ShowShortcuts() => Index = Pages.Count - 1;
}
