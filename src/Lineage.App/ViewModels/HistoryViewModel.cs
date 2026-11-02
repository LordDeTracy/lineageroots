using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>One line of the change history: when, who, what, and the records it touched.</summary>
public sealed class HistoryRow(ChangeRecord change)
{
    public ChangeRecord Change { get; } = change;
    public string When => Change.When.Date == DateTime.Today ? $"Today {Change.When:t}"
        : Change.When.Date == DateTime.Today.AddDays(-1) ? $"Yesterday {Change.When:t}" : Change.When.ToString("g");
    public string Who => Change.Who;
    public string What => Change.Label.Length > 0 ? char.ToUpper(Change.Label[0]) + Change.Label[1..] : "Change";
    public string Records
    {
        get
        {
            var shown = Change.Records.Take(6).Select(r => $"{r.How.ToLowerInvariant()} {(r.Name.Length > 0 ? r.Name : r.Kind.ToLowerInvariant())}");
            var more = Change.Records.Count > 6 ? $" and {Change.Records.Count - 6:N0} more" : "";
            return string.Join("; ", shown) + more;
        }
    }
    public bool CanUndo => Change.CanUndo;
    public string UndoTip => CanUndo ? "Undo this change and every change after it" : "Too old to undo; kept as a record";
}

/// <summary>
/// The change history window: every saved change, newest first, with "undo from here" and a way
/// to clear it (the history can hold details of people since deleted).
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly IList<ChangeRecord> _history;
    private readonly Func<ChangeRecord, int> _undoFrom;
    private readonly Func<Task<bool>> _clear;

    public HistoryViewModel(IList<ChangeRecord> history, Func<ChangeRecord, int> undoFrom, Func<Task<bool>> clear)
    {
        _history = history;
        _undoFrom = undoFrom;
        _clear = clear;
        Rebuild();
    }

    public ObservableCollection<HistoryRow> Rows { get; } = new();
    [ObservableProperty] private string _summary = "";
    public bool IsEmpty => Rows.Count == 0;

    private void Rebuild()
    {
        Rows.Clear();
        foreach (var c in _history.Reverse()) Rows.Add(new HistoryRow(c));
        Summary = Rows.Count == 0 ? "No changes recorded yet. Every edit from now on is listed here and saved with the file."
            : $"{Rows.Count:N0} change{(Rows.Count == 1 ? "" : "s")} since {_history[0].When:d}, newest first. The latest {Rows.Count(r => r.CanUndo):N0} can still be undone, even after closing the program.";
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Undoes this change and every change after it.</summary>
    [RelayCommand]
    private void UndoFrom(HistoryRow row)
    {
        if (!row.CanUndo) return;
        _undoFrom(row.Change);
        Rebuild();
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        if (await _clear()) Rebuild();
    }
}
