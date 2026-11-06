using Lineage.App.Services;
using SkiaSharp;

namespace Lineage.App.Tests;

public class TextRecognitionTests
{
    private static string MakeScan(string folder, string[] lines)
    {
        var path = Path.Combine(folder, "scan.png");
        var info = new SKImageInfo(1400, 120 + lines.Length * 70);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        using var font = new SKPaint { Color = SKColors.Black, TextSize = 44, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Times New Roman") };
        for (var i = 0; i < lines.Length; i++) surface.Canvas.DrawText(lines[i], 60, 90 + i * 70, font);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    [Fact]
    public void PrintedTextIsRead()
    {
        Assert.True(TextRecognition.IsAvailable);
        var folder = Directory.CreateTempSubdirectory("lineage-ocr-").FullName;
        var engine = TextRecognition.Folder;
        try
        {
            TextRecognition.Folder = Path.Combine(folder, "engine");
            var result = TextRecognition.Read(MakeScan(folder, new[] { "Certificate of Marriage", "Thomas Whitfield of Leeds, stonemason", "married on the eighteenth of June 1868" }));
            Assert.Contains("Certificate of Marriage", result.Text);
            Assert.Contains("Thomas Whitfield", result.Text);
            Assert.Contains("1868", result.Text);
            Assert.False(result.IsDoubtful);
        }
        finally
        {
            TextRecognition.Folder = engine;
            try { Directory.Delete(folder, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the engine's DLLs stay loaded */ }
        }
    }

    [Fact]
    public void ANonPictureSaysSo()
    {
        var file = Path.GetTempFileName();
        try
        {
            var ex = Assert.Throws<TextRecognitionException>(() => TextRecognition.Read(file));
            Assert.Contains("as a picture", ex.Message);
        }
        finally { File.Delete(file); }
    }
}

public class ReadScanInEditorsTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task TheSourceEditorPutsTheTextInTheTranscript()
    {
        var folder = Directory.CreateTempSubdirectory("lineage-ocr2-").FullName;
        try
        {
            var path = Path.Combine(folder, "scan.png");
            var info = new SkiaSharp.SKImageInfo(1400, 200);
            using (var surface = SkiaSharp.SKSurface.Create(info))
            {
                surface.Canvas.Clear(SkiaSharp.SKColors.White);
                using var font = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black, TextSize = 44, IsAntialias = true, Typeface = SkiaSharp.SKTypeface.FromFamilyName("Arial") };
                surface.Canvas.DrawText("See https://example.org/records/42 for details", 40, 100, font);
                using var image = surface.Snapshot();
                using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, data.ToArray());
            }
            var editor = new Lineage.App.ViewModels.SourceEditorViewModel(new Lineage.Core.Model.FamilyTree(), new Lineage.Core.Model.Source(), isNew: true)
            {
                PickScan = () => Task.FromResult<string?>(path), ShowLinkTools = true,
            };
            Assert.True(editor.CanReadScan);
            await editor.ReadScanCommand.ExecuteAsync(null);
            Assert.Contains("for details", editor.Text);
            Assert.Equal("https://example.org/records/42", editor.Link);
        }
        finally { Directory.Delete(folder, true); }
    }
}
