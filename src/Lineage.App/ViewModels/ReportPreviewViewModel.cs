using Avalonia.Media.Imaging;

namespace Lineage.App.ViewModels;

/// <summary>A report's pages as pictures, shown before it is saved or printed.</summary>
public sealed class ReportPreviewViewModel
{
    public ReportPreviewViewModel(string title, IReadOnlyList<byte[]> pages)
    {
        Title = title;
        PageImages = pages;
    }

    public string Title { get; }
    public IReadOnlyList<byte[]> PageImages { get; }
    public string PageCount => PageImages.Count == 1 ? "1 page" : $"{PageImages.Count} pages";

    /// <summary>Decoded lazily by the window (tests never need the pictures).</summary>
    public IEnumerable<Bitmap> Pages => PageImages.Select(b => new Bitmap(new MemoryStream(b)));
}
