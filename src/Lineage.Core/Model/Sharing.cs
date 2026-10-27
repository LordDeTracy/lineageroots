namespace Lineage.Core.Model;

/// <summary>
/// A copy of the tree given to a relative. <see cref="Snapshot"/> is the copy exactly as sent (compressed; see
/// <see cref="Sharing.TreeSnapshot"/>), the starting point for telling their changes from yours.
/// <see cref="Rejected"/> holds changes already turned down, so they aren't offered again unless changed again.
/// </summary>
public sealed class ShareRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string With { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
    public string Snapshot { get; set; } = "";
    public HashSet<string> Rejected { get; set; } = new();

    public string Label => $"{(With.Length > 0 ? With : "A relative")}, {Created:d MMM yyyy}";
}

/// <summary>Marks a copy made for a relative: the share's id and the name of the tree it came from.</summary>
public sealed record ShareOrigin(Guid Id, string From);
