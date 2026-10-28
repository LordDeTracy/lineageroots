using Lineage.Core.Media;
using SkiaSharp;

namespace Lineage.App.ViewModels;

/// <summary>Reads a photo for the face detector: decoded, scaled so its longer side is at most 1,600 pixels, as RGB.</summary>
public static class FaceFinder
{
    private const int MaxSide = 1600;

    /// <summary>The faces in the photo at this path, or none if it can't be read.</summary>
    public static List<FaceBox> Find(string path)
    {
        try
        {
            using var decoded = SKBitmap.Decode(path);
            if (decoded == null) return new();
            var scale = Math.Min(1.0, (double)MaxSide / Math.Max(decoded.Width, decoded.Height));
            var info = new SKImageInfo(Math.Max(1, (int)(decoded.Width * scale)), Math.Max(1, (int)(decoded.Height * scale)),
                                       SKColorType.Rgba8888, SKAlphaType.Premul);
            using var sized = decoded.Resize(info, SKFilterQuality.Medium) ?? decoded.Copy(SKColorType.Rgba8888);
            if (sized == null) return new();
            var rgba = sized.GetPixelSpan();
            var rgb = new byte[sized.Width * sized.Height * 3];
            for (int p = 0, q = 0; q < rgb.Length; p += 4, q += 3)
            {
                rgb[q] = rgba[p];
                rgb[q + 1] = rgba[p + 1];
                rgb[q + 2] = rgba[p + 2];
            }
            return FaceDetector.Detect(rgb, sized.Width, sized.Height);
        }
        catch (Exception) { return new(); }   // not a picture, or damaged
    }
}
