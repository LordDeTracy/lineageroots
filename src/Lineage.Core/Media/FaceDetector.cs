using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Lineage.Core.Media;

/// <summary>A face found in a photo, in coordinates normalised to the picture (0..1, top-left origin).</summary>
public sealed record FaceBox(double X, double Y, double Width, double Height, double Score)
{
    public double Area => Width * Height;

    public double Overlap(double x, double y, double w, double h)
    {
        var ix = Math.Max(0, Math.Min(X + Width, x + w) - Math.Max(X, x));
        var iy = Math.Max(0, Math.Min(Y + Height, y + h) - Math.Max(Y, y));
        var inter = ix * iy;
        return inter <= 0 ? 0 : inter / (Area + w * h - inter);
    }
}

/// <summary>
/// Finds faces in a photo, offline, with a small built-in detector (Ultra-Light-Fast-Generic-Face-Detector-1MB,
/// 320×240 input). The whole picture is searched, then four overlapping quarters, so the small faces of a group
/// photo are found too; boxes found more than once are merged. Nothing leaves the computer.
/// </summary>
public static class FaceDetector
{
    private const int InputWidth = 320, InputHeight = 240;
    public const double DefaultThreshold = 0.75;

    private static readonly Lazy<InferenceSession?> Session = new(Load);

    /// <summary>False when the detector couldn't be loaded on this computer (its native library is missing).</summary>
    public static bool IsAvailable => Session.Value != null;

    private static InferenceSession? Load()
    {
        try
        {
            using var stream = typeof(FaceDetector).Assembly.GetManifestResourceStream("Lineage.Core.Media.face-detector.onnx")!;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            var options = new SessionOptions
            {
                InterOpNumThreads = 1, IntraOpNumThreads = Math.Min(4, Environment.ProcessorCount),
                LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,   // the model's harmless export warnings
            };
            return new InferenceSession(copy.ToArray(), options);
        }
        catch (Exception) { return null; }   // e.g. the native runtime isn't there for this platform
    }

    /// <summary>The faces in a picture given as RGB bytes (3 per pixel, rows top to bottom), best first.</summary>
    public static List<FaceBox> Detect(byte[] rgb, int width, int height, double threshold = DefaultThreshold)
    {
        if (Session.Value is not { } session || width < 8 || height < 8) return new();
        var found = new List<FaceBox>();
        // The whole picture, then quarters overlapping by a fifth so a face on a seam is whole in one of them.
        var tiles = new List<(double X, double Y, double W, double H)> { (0, 0, 1, 1) };
        if (width >= 2 * InputWidth || height >= 2 * InputHeight)
            foreach (var y in new[] { 0.0, 0.4 })
                foreach (var x in new[] { 0.0, 0.4 })
                    tiles.Add((x, y, 0.6, 0.6));
        foreach (var tile in tiles)
            found.AddRange(DetectTile(session, rgb, width, height, tile, threshold));
        return Merge(found);
    }

    private static IEnumerable<FaceBox> DetectTile(InferenceSession session, byte[] rgb, int width, int height,
                                                   (double X, double Y, double W, double H) tile, double threshold)
    {
        var input = new DenseTensor<float>(new[] { 1, 3, InputHeight, InputWidth });
        var x0 = tile.X * width;
        var y0 = tile.Y * height;
        var sx = tile.W * width / InputWidth;
        var sy = tile.H * height / InputHeight;
        for (var j = 0; j < InputHeight; j++)
        {
            for (var i = 0; i < InputWidth; i++)
            {
                // Bilinear sample at the centre of the input pixel.
                var fx = Math.Clamp(x0 + (i + 0.5) * sx - 0.5, 0, width - 1);
                var fy = Math.Clamp(y0 + (j + 0.5) * sy - 0.5, 0, height - 1);
                int xa = (int)fx, ya = (int)fy;
                int xb = Math.Min(xa + 1, width - 1), yb = Math.Min(ya + 1, height - 1);
                double tx = fx - xa, ty = fy - ya;
                for (var c = 0; c < 3; c++)
                {
                    double P(int px, int py) => rgb[(py * width + px) * 3 + c];
                    var v = (P(xa, ya) * (1 - tx) + P(xb, ya) * tx) * (1 - ty) + (P(xa, yb) * (1 - tx) + P(xb, yb) * tx) * ty;
                    input[0, c, j, i] = (float)((v - 127) / 128);
                }
            }
        }
        using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input) });
        var scores = results.First(r => r.Name == "scores").AsTensor<float>();
        var boxes = results.First(r => r.Name == "boxes").AsTensor<float>();
        var list = new List<FaceBox>();
        for (var k = 0; k < scores.Dimensions[1]; k++)
        {
            var score = scores[0, k, 1];
            if (score < threshold) continue;
            double l = Math.Clamp(boxes[0, k, 0], 0, 1), t = Math.Clamp(boxes[0, k, 1], 0, 1);
            double r = Math.Clamp(boxes[0, k, 2], 0, 1), b = Math.Clamp(boxes[0, k, 3], 0, 1);
            if (r <= l || b <= t) continue;
            list.Add(new FaceBox(tile.X + l * tile.W, tile.Y + t * tile.H, (r - l) * tile.W, (b - t) * tile.H, score));
        }
        return list;
    }

    /// <summary>
    /// One box per face: the best-scoring box of each overlapping group, and no box mostly inside a better one (a
    /// quarter can see part of a face that the whole picture saw in full).
    /// </summary>
    public static List<FaceBox> Merge(IEnumerable<FaceBox> boxes)
    {
        var kept = new List<FaceBox>();
        foreach (var b in boxes.OrderByDescending(b => b.Score))
        {
            if (kept.Any(k => k.Overlap(b.X, b.Y, b.Width, b.Height) > 0.3 || Inside(b, k) > 0.7 || Inside(k, b) > 0.7)) continue;
            kept.Add(b);
        }
        return kept;
    }

    /// <summary>How much of a lies inside b.</summary>
    private static double Inside(FaceBox a, FaceBox b)
    {
        var ix = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X));
        var iy = Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
        return a.Area <= 0 ? 0 : ix * iy / a.Area;
    }
}
