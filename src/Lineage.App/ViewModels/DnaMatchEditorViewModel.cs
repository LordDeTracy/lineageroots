using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Dna;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>One of the tester's other matches, ticked when this match shares DNA with them too.</summary>
public sealed partial class SharedMatchOption : ObservableObject
{
    public SharedMatchOption(Guid id, string text, bool isChecked)
    {
        Id = id;
        Text = text;
        _isChecked = isChecked;
    }

    public Guid Id { get; }
    public string Text { get; }
    [ObservableProperty] private bool _isChecked;
}

/// <summary>Adds or edits one DNA match: the numbers from the testing company, who it is in the tree, and shared matches.</summary>
public sealed partial class DnaMatchEditorViewModel : ObservableObject
{
    public static readonly string[] Companies = { "Ancestry", "23andMe", "MyHeritage", "FamilyTreeDNA", "Living DNA", "GEDmatch" };

    private readonly FamilyTree _tree;
    private readonly Person _tester;

    public DnaMatchEditorViewModel(FamilyTree tree, Person tester, DnaMatch match, bool isNew)
    {
        _tree = tree;
        _tester = tester;
        IsNew = isNew;
        Title = isNew ? $"Add a DNA match for {tester.DisplayName}" : $"DNA match of {tester.DisplayName}";
        _name = match.Name;
        _sharedCm = match.SharedCm > 0 ? Number(match.SharedCm) : "";
        _segments = match.Segments > 0 ? match.Segments.ToString(CultureInfo.CurrentCulture) : "";
        _longestCm = match.LongestCm > 0 ? Number(match.LongestCm) : "";
        _company = match.Company;
        _reference = match.Reference;
        _note = match.Note;
        AllPeople = tree.People.Values.Where(p => p.Id != tester.Id).OrderBy(p => p.PrimaryName.SortKey)
            .Select(p => new PersonChoice(p.Id, p.Lifespan.Length > 0 ? $"{p.DisplayName} ({p.Lifespan})" : p.DisplayName)).ToList();
        _linked = AllPeople.FirstOrDefault(p => p.Id == match.PersonId);
        foreach (var other in tester.DnaMatches.Where(m => m.Id != match.Id).OrderByDescending(m => m.SharedCm))
            SharedOptions.Add(new SharedMatchOption(other.Id, $"{other.Name}  ·  {Number(other.SharedCm)} cM",
                                                    match.SharedWith.Contains(other.Id) || other.SharedWith.Contains(match.Id)));
        UpdatePrediction();
    }

    public string Title { get; }
    public bool IsNew { get; }
    public bool CanDelete => !IsNew;
    public IReadOnlyList<string> CompanyOptions => Companies;
    public IReadOnlyList<PersonChoice> AllPeople { get; }
    public ObservableCollection<SharedMatchOption> SharedOptions { get; } = new();
    public bool HasSharedOptions => SharedOptions.Count > 0;

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _sharedCm;
    [ObservableProperty] private string _segments;
    [ObservableProperty] private string _longestCm;
    [ObservableProperty] private string _company;
    [ObservableProperty] private string _reference;
    [ObservableProperty] private string _note;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsLinked))] private PersonChoice? _linked;
    [ObservableProperty] private string _prediction = "";
    [ObservableProperty] private string _treeRelationship = "";
    [ObservableProperty] private bool _fitsTree = true;

    public bool IsLinked => Linked != null;
    public bool HasTreeRelationship => TreeRelationship.Length > 0;

    partial void OnSharedCmChanged(string value) => UpdatePrediction();
    partial void OnLinkedChanged(PersonChoice? value) => UpdatePrediction();

    [RelayCommand] private void Unlink() => Linked = null;

    public static double? ParseNumber(string text) =>
        double.TryParse(text.Replace("cM", "", StringComparison.OrdinalIgnoreCase).Trim(), NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.CurrentCulture, out var v) && v >= 0 ? v
        : double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0 ? v : null;

    private static string Number(double v) => v.ToString("0.#", CultureInfo.CurrentCulture);

    private void UpdatePrediction()
    {
        var cm = ParseNumber(SharedCm);
        Prediction = cm is > 0 ? DnaText.Prediction(cm.Value) : "";
        var person = Linked != null ? _tree.Person(Linked.Id) : null;
        var link = person != null ? Placement.Link(_tree, _tester, person) : null;
        TreeRelationship = person == null ? ""
            : link == null ? "The tree doesn't connect them by birth."
            : $"In the tree: {link.Kinship.Label}.";
        FitsTree = link == null || cm is not > 0 || CmModel.GroupProbability(link.Kinship, cm.Value) >= DnaText.FitThreshold;
        if (link != null && cm is > 0)
            TreeRelationship += FitsTree ? " The DNA fits." : " That's unlikely with this much DNA — check the tree or the match.";
        OnPropertyChanged(nameof(HasTreeRelationship));
    }

    public bool IsValid => Name.Trim().Length > 0 && ParseNumber(SharedCm) is not null;

    public IReadOnlyList<Guid> SharedIds => SharedOptions.Where(o => o.IsChecked).Select(o => o.Id).ToList();

    public void Apply(DnaMatch match)
    {
        match.Name = Name.Trim();
        match.SharedCm = ParseNumber(SharedCm) ?? 0;
        match.Segments = (int)Math.Round(ParseNumber(Segments) ?? 0);
        match.LongestCm = ParseNumber(LongestCm) ?? 0;
        match.Company = Company.Trim();
        match.Reference = Reference.Trim();
        match.Note = Note;
        match.PersonId = Linked?.Id;
    }
}

/// <summary>Wording shared by the DNA page and the match editor.</summary>
public static class DnaText
{
    /// <summary>Below this the tree's relationship is called unlikely for the shared cM.</summary>
    public const double FitThreshold = 0.02;

    /// <summary>"Most likely 1st cousin or similar (99%)".</summary>
    public static string Prediction(double cm)
    {
        var best = CmModel.Predict(cm)[0];
        var names = best.Members.Take(2).Select(m => m.Label).ToList();
        var label = best.Members.Count > 2 ? string.Join(", ", names) + ", or similar" : string.Join(" or ", names);
        return $"Most likely {label} ({Percent(best.Probability)})";
    }

    /// <summary>"82%", or "&gt;99%" and "&lt;1%" at the ends (the model is never that certain).</summary>
    public static string Percent(double p) => p >= 0.995 ? ">99%" : p < 0.005 ? "<1%" : p.ToString("P0");

    public static string Cm(double cm) => $"{cm.ToString("#,0.#", CultureInfo.CurrentCulture)} cM";
}
