using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Sharing;

namespace Lineage.App.ViewModels;

/// <summary>One change a relative made, with the choice to take it or not.</summary>
public sealed partial class ChangeItem : ObservableObject
{
    public ChangeItem(ShareChange change)
    {
        Change = change;
        // Conflicts would overwrite your own edit, so they start unticked.
        _isAccepted = !change.Conflict;
    }

    public ShareChange Change { get; }
    public string Who => Change.Who;
    public string What => Change.What;
    public string KindLabel => Change.KindLabel;
    public string Yours => Change.Yours;
    public string Theirs => Change.Theirs;
    public bool HasYours => Yours.Length > 0;
    public bool HasTheirs => Theirs.Length > 0;
    public bool IsConflict => Change.Conflict;
    public bool IsRemoval => Change.Kind == ChangeKind.Removed;
    public string YoursLabel => Change.Conflict ? "Yours (you changed this too)" : "Yours";
    [ObservableProperty] private bool _isAccepted;
}

/// <summary>Reviews a relative's changes one by one: tick the ones to take into your tree.</summary>
public sealed partial class ReviewChangesViewModel : ObservableObject
{
    public ReviewChangesViewModel(string with, IReadOnlyList<ShareChange> changes)
    {
        With = with.Length > 0 ? with : "your relative";
        foreach (var c in changes)
        {
            var item = new ChangeItem(c);
            item.PropertyChanged += (_, _) => Update();
            Items.Add(item);
        }
        var added = changes.Count(c => c.Kind == ChangeKind.Added);
        var changed = changes.Count(c => c.Kind == ChangeKind.Changed);
        var removed = changes.Count(c => c.Kind == ChangeKind.Removed);
        var conflicts = changes.Count(c => c.Conflict);
        Summary = $"{Plural(changes.Count, "change")} in {With}'s copy: {added} added, {changed} changed, {removed} removed."
                  + (conflicts > 0 ? $" {Plural(conflicts, "change")} would replace something you changed yourself since sending it — those start unticked." : "");
        Update();
    }

    public string With { get; }
    public string Title => $"Changes from {With}";
    public string Summary { get; }
    public ObservableCollection<ChangeItem> Items { get; } = new();
    [ObservableProperty] private string _applyText = "";
    [ObservableProperty] private bool _canApply;

    public IReadOnlyList<ShareChange> Accepted => Items.Where(i => i.IsAccepted).Select(i => i.Change).ToList();
    public IReadOnlyList<ShareChange> Refused => Items.Where(i => !i.IsAccepted).Select(i => i.Change).ToList();

    private void Update()
    {
        var n = Items.Count(i => i.IsAccepted);
        ApplyText = n == 0 ? "Take none" : $"Take {Plural(n, "change")}";
        CanApply = true;
    }

    [RelayCommand]
    private void AcceptAll()
    {
        foreach (var i in Items) i.IsAccepted = true;
    }

    [RelayCommand]
    private void RefuseAll()
    {
        foreach (var i in Items) i.IsAccepted = false;
    }

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n:N0} {word}s";
}
