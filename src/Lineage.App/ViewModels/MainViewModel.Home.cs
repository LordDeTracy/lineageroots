using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lineage.App.ViewModels;

/// <summary>Bookmarks and the home person.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<PersonCard> BookmarkCards { get; } = new();
    [ObservableProperty] private bool _isBookmarked;
    [ObservableProperty] private bool _isHomePerson;
    [ObservableProperty] private PersonCard? _homePersonCard;
    public bool HasBookmarks => BookmarkCards.Count > 0;
    public bool HasHomePerson => HomePersonCard != null;
    partial void OnHomePersonCardChanged(PersonCard? value) => OnPropertyChanged(nameof(HasHomePerson));

    private void RefreshBookmarkState()
    {
        IsBookmarked = _current != null && _tree.Bookmarks.Contains(_current.Id);
        IsHomePerson = _current != null && _tree.HomePersonId == _current.Id;
        BookmarkCards.Clear();
        foreach (var id in _tree.Bookmarks)
            if (_tree.Person(id) is { } p) BookmarkCards.Add(new PersonCard(p));
        HomePersonCard = _tree.Person(_tree.HomePersonId) is { } home ? new PersonCard(home, "Home person") : null;
        OnPropertyChanged(nameof(HasBookmarks));
    }

    /// <summary>Stars or un-stars the current person. Saved with the tree; not part of undo.</summary>
    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private void ToggleBookmark()
    {
        if (_current is not { } p) return;
        if (!_tree.Bookmarks.Remove(p.Id)) _tree.Bookmarks.Add(p.Id);
        MarkChanged();
        RefreshBookmarkState();
        Status = IsBookmarked ? $"Bookmarked {p.DisplayName}." : $"Removed the bookmark for {p.DisplayName}.";
    }

    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private void SetHomePerson()
    {
        if (_current is not { } p) return;
        _tree.HomePersonId = p.Id;
        MarkChanged();
        RefreshBookmarkState();
        Status = $"{p.DisplayName} is now the home person — the tree opens on them.";
        CurrentRelation = RelationToHome(p);
        Show(p.Id, recordHistory: false);   // the family cards say how each is related to the new home person
    }

    [RelayCommand]
    private void GoToHomePerson()
    {
        if (_tree.Person(_tree.HomePersonId) is { } p) OpenPerson(p.Id);
    }
}
