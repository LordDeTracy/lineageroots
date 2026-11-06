using System.Reflection;
using SkiaSharp;
using Tesseract;

namespace Lineage.App.Services;

/// <summary>What was read from a picture: the text, and how sure the engine was (0 to 1).</summary>
public sealed record RecognizedText(string Text, double Confidence)
{
    /// <summary>Handwriting and faded print come out as nonsense, so low scores get a warning.</summary>
    public bool IsDoubtful => Confidence < 0.6;
}

/// <summary>The engine couldn't run; the message says why, in words for the user.</summary>
public sealed class TextRecognitionException : Exception
{
    public TextRecognitionException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Reads printed text from scanned documents with the Tesseract engine (Apache 2.0), on the computer, with nothing sent
/// anywhere. The engine and its English data are built into the Windows programs and unpacked beside the backups the first
/// time they're used, because a single-file program can't load them from inside itself.
/// Works on printed and typed text; handwriting is mostly beyond it.
/// </summary>
public static class TextRecognition
{
    private const string VisualCppLink = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
    private const string Prefix = "Ocr.";
    private static readonly object Gate = new();

    /// <summary>Smaller scans are enlarged first: the engine reads letters best at about 30 pixels tall.</summary>
    private const int MinimumWidth = 1800;

    /// <summary>Where the engine is unpacked; versioned so a later program brings its own.</summary>
    internal static string Folder { get; set; } = Path.Combine(Backups.Root, "Ocr", "5.2.0");

    private static string[] Resources => typeof(TextRecognition).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).ToArray();

    /// <summary>The engine is only in the 64-bit Windows programs.</summary>
    public static bool IsAvailable => OperatingSystem.IsWindows() && Environment.Is64BitProcess && Resources.Length > 0;

    /// <summary>Reads the text in a picture. Slow (seconds), so call it away from the screen's thread.</summary>
    /// <exception cref="TextRecognitionException">The engine couldn't run or the picture couldn't be read.</exception>
    public static RecognizedText Read(string imagePath)
    {
        if (!IsAvailable) throw new TextRecognitionException("Reading text from scans is only available in the Windows version.");
        byte[] png;
        try { png = Prepare(imagePath); }
        catch (Exception ex) when (ex is not TextRecognitionException)
        {
            throw new TextRecognitionException($"Couldn't open {Path.GetFileName(imagePath)} as a picture.", ex);
        }
        lock (Gate)
        {
            try
            {
                Unpack();
                TesseractEnviornment.CustomSearchPath = Folder;
                using var engine = new TesseractEngine(Folder, "eng", EngineMode.Default);
                using var pix = Pix.LoadFromMemory(png);
                using var page = engine.Process(pix);
                return new RecognizedText(page.GetText().Trim(), page.GetMeanConfidence());
            }
            catch (Exception ex) when (ex is not TextRecognitionException)
            {
                if (IsMissingLibrary(ex))
                    throw new TextRecognitionException(
                        "The text reader couldn't start. This usually means the computer needs Microsoft's Visual C++ Redistributable, a free download: " + VisualCppLink, ex);
                throw new TextRecognitionException($"The text reader ran into a problem: {ex.Message}", ex);
            }
        }
    }

    /// <summary>Decodes whatever the format, drops colour and enlarges small scans, as PNG for the engine.</summary>
    private static byte[] Prepare(string imagePath)
    {
        using var original = SKBitmap.Decode(imagePath) ?? throw new InvalidDataException("Not a picture.");
        var scale = original.Width < MinimumWidth ? Math.Min(4.0, (double)MinimumWidth / original.Width) : 1.0;
        var info = new SKImageInfo((int)(original.Width * scale), (int)(original.Height * scale), SKColorType.Gray8, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        surface.Canvas.DrawBitmap(original, new SKRect(0, 0, info.Width, info.Height), paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>The DLLs go in x64 (where the Tesseract library looks), the language data beside it.</summary>
    private static void Unpack()
    {
        var x64 = Path.Combine(Folder, "x64");
        Directory.CreateDirectory(x64);
        var assembly = typeof(TextRecognition).Assembly;
        foreach (var name in Resources)
        {
            var file = name[Prefix.Length..];
            var target = Path.Combine(file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? x64 : Folder, file);
            using var source = assembly.GetManifestResourceStream(name)!;
            if (File.Exists(target) && new FileInfo(target).Length == source.Length) continue;
            var part = target + ".part";
            using (var output = File.Create(part)) source.CopyTo(output);
            File.Move(part, target, overwrite: true);
        }
    }

    private static bool IsMissingLibrary(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e is DllNotFoundException or BadImageFormatException || e.Message.Contains("Failed to find library", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("Failed to load", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
