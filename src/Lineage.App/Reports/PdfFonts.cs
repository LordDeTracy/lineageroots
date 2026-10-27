using QuestPDF.Infrastructure;

namespace Lineage.App.Reports;

/// <summary>
/// Sets QuestPDF up once: its free Community licence (questpdf.com/license) and its default font, Lato, which is
/// built into the program (see Lineage.App.csproj) so the program needs no font folder beside it.
/// </summary>
internal static class PdfFonts
{
    private static int _done;

    public static void Setup()
    {
        if (Interlocked.Exchange(ref _done, 1) == 1) return;
        QuestPDF.Settings.License = LicenseType.Community;
        var assembly = typeof(PdfFonts).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Fonts.Lato-", StringComparison.Ordinal)))
        {
            using var resource = assembly.GetManifestResourceStream(name)!;
            var copy = new MemoryStream();
            resource.CopyTo(copy);
            copy.Position = 0;
            QuestPDF.Drawing.FontManager.RegisterFont(copy);
        }
    }

    /// <summary>How many font files are built in (for tests).</summary>
    public static int Count =>
        typeof(PdfFonts).Assembly.GetManifestResourceNames().Count(n => n.StartsWith("Fonts.Lato-", StringComparison.Ordinal));
}
