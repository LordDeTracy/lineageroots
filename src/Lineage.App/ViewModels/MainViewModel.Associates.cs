using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>One friend, associate or neighbour on the person page.</summary>
public sealed record AssociateRow(long PersonId, string Name, string Lifespan, string How, int Year, bool CanRemove)
{
    /// <summary>"Neighbour", "Witness at the couple’s marriage · 1868".</summary>
    public string Line => Year > 0 ? $"{How} · {Year}" : How;
}

public partial class MainViewModel
{
    /// <summary>The person's FAN club: friends, associates, neighbours, and the people at their events.</summary>
    public ObservableCollection<AssociateRow> AssociateRows { get; } = new();
    public bool HasAssociates => AssociateRows.Count > 0;

    private void ShowAssociates(Person p)
    {
        AssociateRows.Clear();
        foreach (var a in Associates.Of(_tree, p))
            AssociateRows.Add(new AssociateRow(a.Other.Id, a.Other.DisplayName, a.Other.Lifespan, a.How, a.Year,
                                               CanRemove: !a.Theirs && p.Associations.Any(x => x.PersonId == a.Other.Id && Relation(x) == a.How)));
        OnPropertyChanged(nameof(HasAssociates));
    }

    public static string[] RelationOptions { get; } =
        { "Neighbour", "Friend", "Business partner", "Employer", "Employee", "Fellow soldier", "Fellow passenger", "Lodger", "Servant", "Church member", "Witness", "Godparent" };

    /// <summary>Adds a friend, associate or neighbour: someone already in the tree, and how they were connected.</summary>
    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private async Task AddAssociateAsync()
    {
        if (_current is not { } p) return;
        var people = _tree.People.Values.Where(x => x.Id != p.Id)
            .GroupBy(EventEditorViewModel.PersonLabel).ToDictionary(g => g.Key, g => g.First());
        var name = await _dialogs.PromptAsync("Add a friend, associate or neighbour",
            $"Who was connected to {p.DisplayName}? Add them to the tree first if they aren't in it yet.", "",
            people.Keys.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList());
        if (name == null) return;
        var other = people.GetValueOrDefault(name.Trim()) ?? people.Values.FirstOrDefault(x => x.DisplayName == name.Trim());
        if (other == null)
        {
            await _dialogs.ShowMessageAsync("Add a friend, associate or neighbour", $"There's nobody called \"{name.Trim()}\" in the tree.");
            return;
        }
        var relation = await _dialogs.PromptAsync("How were they connected?", $"How was {other.DisplayName} connected to {p.DisplayName}?", "Neighbour", RelationOptions);
        if (relation == null) return;
        Change("add associate", new[] { p }, () => p.Associations.Add(new Association { PersonId = other.Id, Relation = relation.Trim() }));
        Status = $"{other.DisplayName} added as {(relation.Trim().Length > 0 ? relation.Trim().ToLowerInvariant() : "an associate")} of {p.DisplayName}.";
    }

    [RelayCommand]
    private void RemoveAssociate(AssociateRow row)
    {
        if (_current is not { } p) return;
        var link = p.Associations.FirstOrDefault(x => x.PersonId == row.PersonId && Relation(x) == row.How);
        if (link == null) return;
        Change("remove associate", new[] { p }, () => p.Associations.Remove(link));
    }

    /// <summary>How an association is worded in the list (the same as <see cref="Associates.Of"/> words it).</summary>
    private static string Relation(Association a) => a.Relation.Trim().Length > 0 ? a.Relation.Trim() : "Associate";

    [RelayCommand]
    private void OpenAssociate(AssociateRow row) => OpenPerson(row.PersonId);
}
