using SkiaSharp;

namespace Lineage.App.Reports;

/// <summary>
/// A photo made ready for a report: decoded whatever its format, shrunk so a book of portraits stays a sensible size,
/// and saved as JPEG, which both PDF and Word take.
/// </summary>
internal sealed record PictureData(byte[] Jpeg, int Width, int Height)
{
    private const int MaxPixels = 900;

    /// <summary>The picture, or null if the file is missing or isn't an image.</summary>
    public static PictureData? Load(string path)
    {
        try
        {
            using var original = SKBitmap.Decode(path);
            if (original == null || original.Width == 0 || original.Height == 0) return null;
            var scale = Math.Min(1.0, (double)MaxPixels / Math.Max(original.Width, original.Height));
            var w = Math.Max(1, (int)Math.Round(original.Width * scale));
            var h = Math.Max(1, (int)Math.Round(original.Height * scale));
            using var sized = scale < 1 ? original.Resize(new SKImageInfo(w, h), SKFilterQuality.High) : null;
            using var image = SKImage.FromBitmap(sized ?? original);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
            return new PictureData(data.ToArray(), w, h);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
