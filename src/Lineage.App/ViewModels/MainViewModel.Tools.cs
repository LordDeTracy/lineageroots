using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Analysis;
using Lineage.Core.Editing;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>Compares this tree with another file and copies chosen people in.</summary>
    [RelayCommand]
    private async Task CompareTreesAsync()
    {
        if (!HasTree) return;
        var path = await _dialogs.OpenFileAsync("Compare with another family file",
            ("Family files", "*.ged;*.aq;*.lineage"), ("GEDCOM", "*.ged"), ("AQ 16", "*.aq"), ("Lineage Roots trees", "*.lineage"));
        if (path == null) return;
        FamilyTree other;
        CompareResult result;
        try
        {
            Status = "Comparing…";
            var mine = _tree;
            (other, result) = await Task.Run(() =>
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var t = ext switch
                {
                    ".aq" => Lineage.Core.Import.Aq.AqImporter.Import(path),
                    ".ged" => Lineage.Core.Import.Gedcom.GedcomImporter.ImportFile(path),
                    _ => Lineage.Core.Storage.LineageStore.Load(path),
                };
                return (t, TreeCompare.Compare(mine, t));
            });
        }
        catch (Exception ex)
        {
            Status = "";
            await _dialogs.ShowMessageAsync("Could not compare", ex.Message);
            return;
        }
        Status = "";
        var compare = new CompareTreesViewModel(_tree, other, Path.GetFileNameWithoutExtension(path), result, id => OpenPerson(id));
        if (!await _dialogs.CompareTreesAsync(compare)) return;
        var chosen = compare.ChosenIds;
        if (chosen.Count == 0) return;
        List<long> added = new();
        Change($"copy {chosen.Count} people from {compare.OtherName}",
               TreeCompare.ImportTouched(_tree, other, chosen, compare.Result),
               () => added = TreeCompare.Import(_tree, other, chosen, compare.Result));
        Status = $"Copied {added.Count:N0} {(added.Count == 1 ? "person" : "people")} from {compare.OtherName}. Ctrl+Z undoes it.";
        if (added.Count > 0) OpenPerson(added[0]);
    }

    [RelayCommand]
    private async Task FindDuplicatesAsync()
    {
        if (!HasTree) return;
        Status = "Looking for duplicates…";
        var tree = _tree;
        var matches = await Task.Run(() => Duplicates.Find(tree));
        Status = matches.Count == 0 ? "No likely duplicates found." : $"Found {matches.Count} possible duplicates.";
        var merged = 0;
        var review = new DuplicatesViewModel(_tree, matches,
            merge: (keep, other) =>
            {
                var tagged = Lineage.Core.Media.MediaLibrary.TaggedIn(_tree, other.Id).Select(r => r.Owner);
                Change($"merge {other.DisplayName}", Merger.Touched(_tree, keep, other).Concat(tagged), () =>
                {
                    Merger.Merge(_tree, keep, other);
                    Lineage.Core.Media.MediaLibrary.Retag(_tree, other.Id, keep.Id);   // photo tags follow the merge
                }, keep.Id);
                merged++;
            },
            dismiss: (a, b) =>
            {
                // Stored with the tree so the pair isn't suggested again. A tree-level setting, so mark dirty.
                _tree.NotDuplicates.Add(FamilyTree.PairKey(a.Id, b.Id));
                IsDirty = true;
            });
        await _dialogs.ShowDuplicatesAsync(review);
        if (merged > 0) Status = $"Merged {merged} duplicate{(merged == 1 ? "" : "s")}. Ctrl+Z undoes the last merge.";
    }
}
