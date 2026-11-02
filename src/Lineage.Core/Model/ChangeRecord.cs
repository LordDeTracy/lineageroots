namespace Lineage.Core.Model;

/// <summary>
/// One edit in the tree's change history, saved in the file: when, by whom, what, and which records.
/// Recent changes also keep each record as it was before and after (the same JSON as the file), so
/// they can be undone after the file is closed and opened again. Older ones keep only the summary.
/// Never changed after it is made (trimming replaces it), so a background save can read it safely.
/// </summary>
public sealed class ChangeRecord
{
    public DateTime When { get; set; }
    public string Who { get; set; } = "";
    public string Label { get; set; } = "";
    public List<ChangedRecord> Records { get; set; } = new();
    public List<RecordState> Before { get; set; } = new();
    public List<RecordState> After { get; set; } = new();

    /// <summary>Still holds the before and after states, so it can be undone.</summary>
    public bool CanUndo => Before.Count > 0;

    /// <summary>The same entry without the before/after states.</summary>
    public ChangeRecord Summary() => new() { When = When, Who = Who, Label = Label, Records = Records };

    /// <summary>Characters of before/after JSON this entry keeps (what makes a file grow).</summary>
    public long Size => Before.Concat(After).Sum(s => (long)(s.Json?.Length ?? 0));
}

/// <summary>A record as it was at one moment; <see cref="Json"/> is null when it didn't exist.</summary>
public sealed record RecordState(string Kind, long Id, string? Json);

/// <summary>How a change touched one record. <see cref="How"/> is Added, Changed or Removed.</summary>
public sealed record ChangedRecord(string Kind, long Id, string Name, string How);
