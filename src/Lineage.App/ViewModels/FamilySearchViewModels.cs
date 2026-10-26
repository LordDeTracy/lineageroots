using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.App.Services;
using Lineage.Core.FamilySearch;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

public sealed partial class FamilySearchSettingsViewModel : ObservableObject
{
    public FamilySearchSettingsViewModel(AppSettings s)
    {
        _appKey = s.FamilySearchAppKey;
        _environmentIndex = s.FamilySearchIntegration ? 1 : 0;
        _port = s.FamilySearchPort.ToString();
    }

    [ObservableProperty] private string _appKey;
    [ObservableProperty] private int _environmentIndex;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RedirectUri))] private string _port;

    public string RedirectUri => $"http://127.0.0.1:{(int.TryParse(Port, out var p) ? p : 52817)}/familysearch/";

    public void Apply(AppSettings s)
    {
        s.FamilySearchAppKey = AppKey.Trim();
        s.FamilySearchIntegration = EnvironmentIndex == 1;
        if (int.TryParse(Port, out var p) && p is > 1024 and < 65536) s.FamilySearchPort = p;
    }

    [RelayCommand]
    private static void OpenDeveloperSite() => MainViewModel.OpenExternal("https://www.familysearch.org/developers/");
}

public sealed record FsResultRow(FsPerson Person)
{
    public string Name => Person.FullName.Length > 0 ? Person.FullName : $"{Person.Given} {Person.Surname}";
    public string Detail => Display.Join(Person.Lifespan,
        Person.Fact("BIRT") is { } b ? $"Born {Display.Join(b.Date, b.Place).Replace("  ·  ", ", ")}" : "",
        Person.Id);
}

/// <summary>Searches the FamilySearch Family Tree for the current person.</summary>
public sealed partial class FamilySearchSearchViewModel : ObservableObject
{
    private readonly FamilySearchApi _api;

    public FamilySearchSearchViewModel(FamilySearchApi api, Person p)
    {
        _api = api;
        Title = $"Find {p.DisplayName} on FamilySearch";
        _given = p.PrimaryName.Given;
        _surname = p.PrimaryName.Surname;
        _birthYear = p.Birth?.Date.Year is > 0 and var y ? y.ToString() : "";
        _birthPlace = p.Birth?.Place ?? "";
    }

    public string Title { get; }
    public ObservableCollection<FsResultRow> Results { get; } = new();
    [ObservableProperty] private string _given;
    [ObservableProperty] private string _surname;
    [ObservableProperty] private string _birthYear;
    [ObservableProperty] private string _birthPlace;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelection))] private FsResultRow? _selected;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _busy;
    public bool HasSelection => Selected != null;

    [RelayCommand]
    private async Task SearchAsync()
    {
        Busy = true;
        Message = "Searching FamilySearch…";
        Results.Clear();
        try
        {
            var found = await _api.SearchAsync(Given.Trim(), Surname.Trim(), int.TryParse(BirthYear, out var y) ? y : 0, BirthPlace.Trim());
            foreach (var r in found) Results.Add(new FsResultRow(r));
            Message = Results.Count == 0 ? "No matches. Try fewer details, such as just the name and birth year." : $"{Results.Count} possible matches, best first.";
            Selected = Results.FirstOrDefault();
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private void OpenSelected()
    {
        if (Selected != null) MainViewModel.OpenExternal(Selected.Person.Url);
    }
}

public sealed partial class FieldDiffRow : ObservableObject
{
    public FieldDiffRow(FieldDiff d)
    {
        Diff = d;
        // Suggest taking FamilySearch's value only where ours is empty.
        _use = d.Local.Length == 0 && d.Remote.Length > 0;
    }

    public FieldDiff Diff { get; }
    public bool CanUse => Diff.Remote.Length > 0 && Diff.Differs;
    [ObservableProperty] private bool _use;
}

/// <summary>Side-by-side comparison with FamilySearch; ticked rows are copied into the local person.</summary>
public sealed class FamilySearchCompareViewModel
{
    public FamilySearchCompareViewModel(Person local, FsPerson remote)
    {
        Remote = remote;
        Title = $"Compare {local.DisplayName} with FamilySearch";
        foreach (var d in FsSync.Compare(local, remote)) Rows.Add(new FieldDiffRow(d));
        Summary = Rows.Any(r => r.CanUse)
            ? "Tick what you'd like to copy from FamilySearch. Nothing is changed on FamilySearch."
            : "Your tree and FamilySearch agree on everything compared here.";
    }

    public string Title { get; }
    public string Summary { get; }
    public FsPerson Remote { get; }
    public ObservableCollection<FieldDiffRow> Rows { get; } = new();
    public IEnumerable<string> ChosenKeys => Rows.Where(r => r.Use && r.CanUse).Select(r => r.Diff.Key);
}
