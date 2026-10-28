using Lineage.Core.Media;

namespace Lineage.Core.Tests;

public class FaceDetectorTests
{
    [Fact]
    public void TheBuiltInDetectorLoadsAndFindsNoFaceInABlankPicture()
    {
        Assert.True(FaceDetector.IsAvailable);
        var (w, h) = (800, 600);   // big enough to be searched in quarters too
        var rgb = new byte[w * h * 3];
        for (var i = 0; i < rgb.Length; i++) rgb[i] = (byte)(i % 3 == 0 ? 90 : 140);
        Assert.Empty(FaceDetector.Detect(rgb, w, h));
        Assert.Empty(FaceDetector.Detect(new byte[12], 2, 2));   // too small to search
    }

    [Fact]
    public void BoxesFoundTwiceAreMerged()
    {
        var merged = FaceDetector.Merge(new[]
        {
            new FaceBox(0.10, 0.10, 0.10, 0.10, 0.90),
            new FaceBox(0.11, 0.10, 0.10, 0.10, 0.95),   // the same face, seen from a quarter
            new FaceBox(0.13, 0.12, 0.04, 0.05, 0.80),   // part of it, inside
            new FaceBox(0.50, 0.50, 0.10, 0.10, 0.85),
        });
        Assert.Equal(new[] { 0.95, 0.85 }, merged.Select(b => b.Score));
    }
}
