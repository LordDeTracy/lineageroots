using CommunityToolkit.Mvvm.Input;

namespace Lineage.App.ViewModels;

/// <summary>The clean-up wizard: tidies names, dates and places after a preview, as one undoable change.</summary>
public sealed partial class MainViewModel
{
    [RelayCommand]
    private async Task CleanUpAsync()
    {
        if (!HasTree) return;
        var wizard = new CleanupViewModel(_tree);
        if (!await _dialogs.CleanupAsync(wizard)) return;
        var chosen = wizard.Chosen.ToList();
        if (chosen.Count == 0) return;
        var changed = 0;
        Change("clean up names, dates and places",
               chosen.SelectMany(f => f.Touched()).Distinct().ToList(),
               () => { foreach (var f in chosen) changed += f.Apply(); });
        Status = $"Cleaned up {changed:N0} value{(changed == 1 ? "" : "s")}. Ctrl+Z undoes it.";
    }
}
