using CommunityToolkit.Mvvm.ComponentModel;
using Lineage.App.Reports;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>The kinds of chart the wall-chart designer can print.</summary>
public enum WallChartKind { Pedigree, Fan, Descendants, Hourglass }

/// <summary>
/// Choices for a printed wall chart: which chart and how far it reaches, the words on it, poster or pages,
/// paper, size and look. The window shows a live preview and the finished size as these change.
/// </summary>
public sealed partial class WallChartDesignerViewModel : ObservableObject
{
    public WallChartDesignerViewModel(FamilyTree tree, Person root, string treeName, WallChartKind kind, bool evidence)
    {
        Tree = tree;
        Root = root;
        _kindIndex = (int)kind;
        _title = Titles[(int)kind].Replace("{0}", root.DisplayName);
        _subtitle = treeName;
        _colourByEvidence = evidence;
    }

    public FamilyTree Tree { get; }
    public Person Root { get; }

    private static readonly string[] Titles = { "Ancestors of {0}", "Ancestors of {0}", "Descendants of {0}", "The family of {0}" };

    public string[] KindOptions { get; } = { "Pedigree (ancestors as a tree)", "Fan (ancestors in a half circle)", "Descendants", "Hourglass (ancestors and descendants)" };
    public int[] GenerationOptions { get; } = { 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    public string[] ModeOptions { get; } = { "One poster (for a print shop)", "Pages to print at home and tape together" };
    public string[] PaperOptions { get; } = { "Usual paper for this computer", "Letter", "A4" };
    public string[] ColourOptions { get; } = { "Light (best on paper)", "As on screen" };

    [ObservableProperty] private int _kindIndex;
    [ObservableProperty] private int _generations = 5;
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _subtitle;
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private int _modeIndex;
    [ObservableProperty] private int _paperIndex;
    /// <summary>Printed size in percent of the on-screen size (100 = names about 10 pt).</summary>
    [ObservableProperty] private double _sizePercent = 100;
    [ObservableProperty] private int _colourIndex;
    [ObservableProperty] private bool _colourByEvidence;
    [ObservableProperty] private bool _frame = true;
    /// <summary>What the PDF will be, e.g. "A 92 × 61 cm poster"; filled in by the window, which owns the chart.</summary>
    [ObservableProperty] private string _planText = "";

    public WallChartKind Kind => (WallChartKind)KindIndex;
    public bool IsTiled => ModeIndex == 1;
    public bool IsPoster => !IsTiled;

    partial void OnKindIndexChanged(int value)
    {
        // Keep a title the user hasn't changed in step with the chart.
        foreach (var t in Titles)
            if (Title == t.Replace("{0}", Root.DisplayName)) { Title = Titles[value].Replace("{0}", Root.DisplayName); break; }
    }

    partial void OnModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTiled));
        OnPropertyChanged(nameof(IsPoster));
    }

    public WallChartOptions ToOptions() => new()
    {
        Title = Title.Trim(), Subtitle = Subtitle.Trim(), Note = Note.Trim(),
        Mode = IsTiled ? WallChartMode.Tiled : WallChartMode.SinglePoster,
        Paper = (WallChartPaper)PaperIndex,
        Scale = Math.Clamp(SizePercent / 100, 0.3, 4),
        LightTheme = ColourIndex == 0,
        Frame = Frame && !IsTiled,
    };

    /// <summary>Describes a plan in words for the window.</summary>
    public static string Describe(WallChartPlan plan) => plan.Mode == WallChartMode.Tiled
        ? $"{plan.Pages} pages: {plan.Columns} across, {plan.Rows} down"
        : $"A {plan.WidthMm / 10:0} × {plan.HeightMm / 10:0} cm poster";
}
