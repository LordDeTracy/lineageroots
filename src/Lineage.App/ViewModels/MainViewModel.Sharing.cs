using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Export;
using Lineage.Core.Model;
using Lineage.Core.Sharing;
using Lineage.Core.Storage;

namespace Lineage.App.ViewModels;

/// <summary>Sharing a copy with a relative, and merging the changes they send back.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Saves a copy for a relative and keeps a record of exactly what was sent.</summary>
    [RelayCommand]
    private async Task ShareWithRelativeAsync()
    {
        if (!HasTree) return;
        var with = await _dialogs.PromptAsync("Share a copy with a relative",
            "Who is the copy for? This keeps copies for different relatives apart.", "", Array.Empty<string>());
        if (with == null) return;
        var hideLiving = await _dialogs.ConfirmAsync("Leave out living people?",
            "Choose Yes to replace living people's names and details with \"Living\" (recommended). Choose No to send everything, for close family you trust.");
        var path = await _dialogs.SaveFileAsync("Save the copy to send", $"{TreeName} for {(with.Trim().Length > 0 ? with.Trim() : "a relative")}.lineage",
            ("Lineage Roots trees", "*.lineage"));
        if (path == null) return;

        var privacy = new PrivacyOptions { Living = hideLiving ? LivingMode.Privatize : LivingMode.Include };
        Status = "Making the copy…";
        var tree = _tree;
        var (copy, share) = await Task.Run(() => ShareReview.CreateCopy(tree, with, privacy));
        try { await Task.Run(() => LineageStore.Save(copy, path)); }
        catch (Exception ex)
        {
            Status = "";
            _tree.Shares.Remove(share);
            await _dialogs.ShowMessageAsync("Couldn't save the copy", ex.Message);
            return;
        }
        MarkChanged();   // the record of what was sent is part of this tree
        Status = $"Saved a copy for {(share.With.Length > 0 ? share.With : "a relative")}";
        await _dialogs.ShowMessageAsync("Copy saved",
            $"Send {Path.GetFileName(path)} to {(share.With.Length > 0 ? share.With : "your relative")}. They can open it in Lineage Roots, add and correct things, and send it back. " +
            "Then choose \"Review a relative's changes\" to go through what they changed, one change at a time.\n\n" +
            "Save your own tree now: it keeps the record of what you sent, which the review needs.");
    }

    /// <summary>Opens a copy sent back by a relative and offers their changes one by one.</summary>
    [RelayCommand]
    private async Task ReviewRelativeChangesAsync()
    {
        if (!HasTree) return;
        var path = await _dialogs.OpenFileAsync("Open the copy your relative sent back",
            ("Lineage Roots or GEDCOM", "*.lineage;*.ged"), ("Lineage Roots trees", "*.lineage"), ("GEDCOM", "*.ged"));
        if (path == null) return;

        FamilyTree theirs;
        try
        {
            theirs = await Task.Run(() => Path.GetExtension(path).Equals(".ged", StringComparison.OrdinalIgnoreCase)
                ? Lineage.Core.Import.Gedcom.GedcomImporter.ImportFile(path)
                : LineageStore.Load(path));
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't open the copy", ex.Message);
            return;
        }

        var share = _tree.Shares.FirstOrDefault(s => s.Id == theirs.SharedFrom?.Id);
        if (share == null && _tree.Shares.Count > 0)
        {
            // Another program may have dropped the marker; let the user say which copy it is.
            var labels = _tree.Shares.Select(s => s.Label).ToList();
            var answer = await _dialogs.PromptAsync("Which copy is this?",
                "This file doesn't say which copy it came from. Pick the copy you sent:", labels[^1], labels);
            if (answer == null) return;
            share = _tree.Shares.FirstOrDefault(s => s.Label == answer);
        }
        if (share == null)
        {
            await _dialogs.ShowMessageAsync("Not a copy of this tree",
                "This file wasn't shared from this tree, so there's no record of what was sent to compare against. " +
                "To look through any other tree for people to copy, use \"Compare with another tree\".");
            return;
        }

        List<ShareChange> changes;
        try { changes = await Task.Run(() => ShareReview.Compare(_tree, theirs, share)); }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't compare the copy", ex.Message);
            return;
        }
        if (changes.Count == 0)
        {
            await _dialogs.ShowMessageAsync("Nothing new", $"Nothing has changed in {(share.With.Length > 0 ? share.With + "'s" : "the")} copy that isn't already in your tree (or that you've turned down before).");
            return;
        }

        var review = new ReviewChangesViewModel(share.With, changes);
        if (!await _dialogs.ReviewChangesAsync(review)) return;
        var accepted = review.Accepted;
        if (accepted.Count > 0)
            Change($"take changes from {review.With}", ShareReview.Touched(_tree, theirs, accepted), () => ShareReview.Apply(_tree, theirs, accepted));
        ShareReview.Reject(share, review.Refused);
        MarkChanged();
        Status = $"Took {accepted.Count:N0} of {changes.Count:N0} changes from {review.With}";
    }
}
