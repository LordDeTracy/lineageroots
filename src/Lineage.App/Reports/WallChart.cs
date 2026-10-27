using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Lineage.App.Controls;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Size = Avalonia.Size;

namespace Lineage.App.Reports;

/// <summary>One big page for a print shop, or ordinary pages to print at home and tape together.</summary>
public enum WallChartMode { SinglePoster, Tiled }

/// <summary>Paper for tiled wall charts; Auto follows the computer's region like the other reports.</summary>
public enum WallChartPaper { Auto, Letter, A4 }

/// <summary>How to print a chart as a wall chart.</summary>
public sealed record WallChartOptions
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public WallChartMode Mode { get; init; } = WallChartMode.SinglePoster;
    public WallChartPaper Paper { get; init; } = WallChartPaper.Auto;
    /// <summary>Printed size: 1 prints the chart at its on-screen size (96 chart pixels per inch, names about 10 pt).</summary>
    public double Scale { get; init; } = 1;
    /// <summary>Resolution of the chart picture; lowered automatically for very large charts.</summary>
    public int Dpi { get; init; } = 200;
    /// <summary>Print in the light theme whatever the app is showing: it reads better on paper and saves ink.</summary>
    public bool LightTheme { get; init; } = true;
}

/// <summary>What an export will produce (or produced): page grid, finished size and resolution.</summary>
public sealed record WallChartPlan(WallChartMode Mode, int Columns, int Rows, double WidthMm, double HeightMm, int Dpi, double Scale)
{
    public int Pages => Columns * Rows;
}

/// <summary>
/// Prints any chart as a wall chart PDF. The chart is rendered at print resolution into bitmaps (in strips,
/// so memory stays bounded, and never more than ~120 megapixels in all) and placed on either one poster
/// page with a title band, or on landscape Letter/A4 pages with overlaps, cut marks and "row, column"
/// labels for taping together.
/// </summary>
public static class WallChart
{
    public const double MaxPosterWidthMm = 2500;
    private const double MaxPageMm = 5000;          // PDF viewers stop at about 5 m
    private const double MaxPixels = 120e6, StripPixels = 32e6;
    private const int MaxTiledPages = 400;
    private const double PtPerDip = 72.0 / 96, MmPerPt = 25.4 / 72;
    private const float PosterMargin = 48;
    private const float TileMargin = 30, TileHeader = 24, Overlap = 18;

    private static readonly string[] ThemeKeys =
    {
        "Bg", "Surface", "SurfaceAlt", "Sidebar", "Line", "LineStrong", "Ink", "Muted", "Faint", "Accent", "AccentHover",
        "AccentSoft", "OnAccent", "Hover", "Selected", "MaleBg", "MaleInk", "FemaleBg", "FemaleInk", "OtherBg", "OtherInk", "Danger",
    };

    static WallChart() => PdfFonts.Setup();

    /// <summary>Works out the pages without printing, e.g. to tell the user "6 pages, 1.2 × 0.6 m".</summary>
    public static WallChartPlan Plan(ChartBase chart, WallChartOptions options)
    {
        var attached = TopLevel.GetTopLevel(chart) != null;
        var size = MeasureChart(chart, attached);
        if (attached) chart.InvalidateMeasure();
        return Plan(size, options);
    }

    /// <summary>Writes the PDF. Call on the UI thread (the chart is drawn there).</summary>
    public static WallChartPlan Export(ChartBase chart, string path, WallChartOptions options)
    {
        var document = Prepare(chart, options, out var plan);
        document.GeneratePdf(path);
        return plan;
    }

    /// <summary>Draws the chart on the UI thread, then builds the PDF in the background.</summary>
    public static async Task<WallChartPlan> ExportAsync(ChartBase chart, string path, WallChartOptions options)
    {
        var document = Prepare(chart, options, out var plan);
        await Task.Run(() => document.GeneratePdf(path));
        return plan;
    }

    // ---- planning ---------------------------------------------------------------------------------

    private static (float W, float H) PaperSize(WallChartPaper paper)
    {
        var letter = paper == WallChartPaper.Letter ||
                     paper == WallChartPaper.Auto && RegionInfo.CurrentRegion.TwoLetterISORegionName is "US" or "CA";
        var size = letter ? PageSizes.Letter : PageSizes.A4;
        return (Math.Max(size.Width, size.Height), Math.Min(size.Width, size.Height));   // landscape
    }

    private static (float W, float H) TileArea(WallChartPaper paper)
    {
        var (w, h) = PaperSize(paper);
        return (w - 2 * TileMargin, h - 2 * TileMargin - TileHeader);
    }

    private static (float Title, float Subtitle, float Footer, float Header, float FooterBand) PosterText(double chartWidthPt, bool subtitle)
    {
        var title = (float)Math.Clamp(chartWidthPt / 45, 20, 64);
        var sub = title * 0.45f;
        var footer = Math.Max(8, title * 0.3f);
        var header = title * 1.45f + (subtitle ? sub * 1.6f : 0) + title * 0.6f;
        return (title, sub, footer, header, footer * 1.6f + title * 0.5f);
    }

    private static WallChartPlan Plan(Size chartDip, WallChartOptions o)
    {
        if (chartDip.Width < 1 || chartDip.Height < 1)
            throw new InvalidOperationException("There is nothing to print: the chart is empty.");
        var scale = o.Scale > 0 ? o.Scale : 1;
        double wPt = chartDip.Width * PtPerDip, hPt = chartDip.Height * PtPerDip;

        if (o.Mode == WallChartMode.SinglePoster)
        {
            var maxW = MaxPosterWidthMm / MmPerPt - 2 * PosterMargin;
            var maxH = MaxPageMm / MmPerPt - 2 * PosterMargin - 400;
            scale = Math.Min(scale, Math.Min(maxW / wPt, maxH / hPt));
            var (_, _, _, header, footer) = PosterText(wPt * scale, o.Subtitle.Length > 0);
            var pageW = wPt * scale + 2 * PosterMargin;
            var pageH = hPt * scale + 2 * PosterMargin + header + footer;
            var dpi = Dpi(o.Dpi, wPt * scale * hPt * scale);
            return new WallChartPlan(o.Mode, 1, 1, pageW * MmPerPt, pageH * MmPerPt, dpi, scale);
        }

        var (aw, ah) = TileArea(o.Paper);
        int cols, rows;
        while (true)
        {
            cols = Math.Max(1, (int)Math.Ceiling((wPt * scale - Overlap) / (aw - Overlap) - 1e-9));
            rows = Math.Max(1, (int)Math.Ceiling((hPt * scale - Overlap) / (ah - Overlap) - 1e-9));
            if (cols * rows <= MaxTiledPages) break;
            scale *= 0.9;
        }
        var tiledDpi = Dpi(o.Dpi, (double)cols * rows * aw * ah);
        return new WallChartPlan(o.Mode, cols, rows, wPt * scale * MmPerPt, hPt * scale * MmPerPt, tiledDpi, scale);
    }

    /// <summary>The requested resolution, lowered so the pictures stay under the pixel budget.</summary>
    private static int Dpi(int requested, double areaPt2)
    {
        var pxPerPt = Math.Min(Math.Clamp(requested, 72, 600) / 72.0, Math.Sqrt(MaxPixels / Math.Max(1, areaPt2)));
        return Math.Max(1, (int)Math.Floor(pxPerPt * 72));
    }

    // ---- drawing ----------------------------------------------------------------------------------

    private static Size MeasureChart(ChartBase chart, bool attached)
    {
        chart.Measure(Size.Infinity);
        var size = chart.DesiredSize;
        if (!attached) chart.Arrange(new Rect(size));
        return size;
    }

    /// <summary>Draws the chart and lays out the document (internal so tests can preview pages as images).</summary>
    internal static IDocument Prepare(ChartBase chart, WallChartOptions o, out WallChartPlan plan)
    {
        var attached = TopLevel.GetTopLevel(chart) != null;
        var theme = o.LightTheme || chart.ActualThemeVariant == ThemeVariant.Default ? ThemeVariant.Light : chart.ActualThemeVariant;
        var pinned = PinTheme(chart, theme);
        try
        {
            var size = MeasureChart(chart, attached);
            plan = Plan(size, o);
            var colours = new Colours(Hex(chart, "Bg"), Hex(chart, "Ink"), Hex(chart, "Muted"), Hex(chart, "Faint"),
                                      Hex(chart, "Line"), Hex(chart, "Accent"));
            var background = chart.Resources.TryGetValue("Bg", out var bg) && bg is IBrush b ? b : Brushes.White;
            return o.Mode == WallChartMode.SinglePoster
                ? Poster(chart, size, o, plan, colours, background)
                : Tiled(chart, size, o, plan, colours, background);
        }
        finally
        {
            foreach (var key in pinned) chart.Resources.Remove(key);
            if (attached) chart.InvalidateMeasure();     // let the window lay it out at its real size again
            chart.InvalidateVisual();
        }
    }

    /// <summary>
    /// Puts the theme's colours on the chart itself for the duration of the export. That forces the print
    /// theme, and also lets a chart that isn't in a window yet find its colours at all.
    /// </summary>
    private static List<string> PinTheme(ChartBase chart, ThemeVariant theme)
    {
        var added = new List<string>();
        if (Application.Current is not { } app) return added;
        foreach (var key in ThemeKeys)
            if (!chart.Resources.ContainsKey(key) && app.TryGetResource(key, theme, out var value) && value != null)
            {
                chart.Resources[key] = value;
                added.Add(key);
            }
        return added;
    }

    private sealed record Colours(string Bg, string Ink, string Muted, string Faint, string Line, string Accent);

    private static string Hex(ChartBase chart, string key) =>
        chart.Resources.TryGetValue(key, out var v) && v is ISolidColorBrush s
            ? $"#{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}"
            : key == "Bg" ? "#FFFFFF" : "#555555";

    /// <summary>Draws one region of the chart (in chart pixels) at the given density and returns it as PNG.</summary>
    private static byte[] RenderRegion(ChartBase chart, Rect region, Size chartSize, double pxPerDip, IBrush background)
    {
        var pw = Math.Max(1, (int)Math.Ceiling(region.Width * pxPerDip - 1e-6));
        var ph = Math.Max(1, (int)Math.Ceiling(region.Height * pxPerDip - 1e-6));
        using var bmp = new RenderTargetBitmap(new PixelSize(pw, ph), new Vector(96 * pxPerDip, 96 * pxPerDip));
        using (var ctx = bmp.CreateDrawingContext())
        using (ctx.PushTransform(Matrix.CreateTranslation(-region.X, -region.Y)))
        {
            // Only the chart's own area gets the page colour; beyond it (last row and column of tiles) stays paper.
            ctx.FillRectangle(background, new Rect(chartSize));
            chart.Render(ctx);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms);
        return ms.ToArray();
    }

    private static IDocument Poster(ChartBase chart, Size size, WallChartOptions o, WallChartPlan plan, Colours c, IBrush background)
    {
        var wPt = size.Width * PtPerDip * plan.Scale;
        var hPt = size.Height * PtPerDip * plan.Scale;
        var pxPerDip = plan.Dpi / 72.0 * PtPerDip * plan.Scale;
        var text = PosterText(wPt, o.Subtitle.Length > 0);

        // Strips keep each bitmap to a manageable size however big the poster is.
        var widthPx = size.Width * pxPerDip;
        var stripDip = Math.Max(64, Math.Floor(StripPixels / widthPx)) / pxPerDip;
        var strips = new List<(byte[] Png, float HeightPt)>();
        for (double y = 0; y < size.Height - 1e-6; y += stripDip)
        {
            var h = Math.Min(stripDip, size.Height - y);
            strips.Add((RenderRegion(chart, new Rect(0, y, size.Width, h), size, pxPerDip, background),
                        (float)(h * PtPerDip * plan.Scale)));
        }

        var title = o.Title.Trim().Length > 0 ? o.Title.Trim() : "Family tree";
        var pageW = (float)(wPt + 2 * PosterMargin);
        var pageH = (float)(hPt + 2 * PosterMargin + text.Header + text.FooterBand) + 2;
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(new PageSize(pageW, pageH));
            page.Margin(PosterMargin);
            page.PageColor(c.Bg);
            page.DefaultTextStyle(t => t.FontColor(c.Ink));
            page.Header().Height(text.Header).Column(col =>
            {
                col.Item().Text(t => { t.ClampLines(1); t.Span(title).FontSize(text.Title).SemiBold().FontColor(c.Ink); });
                if (o.Subtitle.Length > 0)
                    col.Item().Text(t => { t.ClampLines(1); t.Span(o.Subtitle).FontSize(text.Subtitle).FontColor(c.Muted); });
                col.Item().PaddingTop(text.Title * 0.25f).LineHorizontal(Math.Max(0.75f, text.Title / 40)).LineColor(c.Line);
            });
            page.Content().Column(col =>
            {
                foreach (var (png, h) in strips)
                    col.Item().Height(h).Image(png).UseOriginalImage().FitUnproportionally();
            });
            page.Footer().Height(text.FooterBand).AlignBottom().Row(row =>
            {
                row.RelativeItem().Text(DateTime.Now.ToString("d MMMM yyyy", CultureInfo.CurrentCulture)).FontSize(text.Footer).FontColor(c.Faint);
                row.AutoItem().Text("Made with Lineage Roots").FontSize(text.Footer).FontColor(c.Faint);
            });
        }));
    }

    private static IDocument Tiled(ChartBase chart, Size size, WallChartOptions o, WallChartPlan plan, Colours c, IBrush background)
    {
        var (pageW, pageH) = PaperSize(o.Paper);
        var (aw, ah) = TileArea(o.Paper);
        var dipPerPt = 1 / (PtPerDip * plan.Scale);
        var pxPerDip = plan.Dpi / 72.0 / dipPerPt;
        var title = o.Title.Trim().Length > 0 ? o.Title.Trim() : "Family tree";

        var pages = new List<(int Row, int Col, byte[] Png)>();
        for (var r = 0; r < plan.Rows; r++)
            for (var col = 0; col < plan.Columns; col++)
            {
                var region = new Rect(col * (aw - Overlap) * dipPerPt, r * (ah - Overlap) * dipPerPt, aw * dipPerPt, ah * dipPerPt);
                pages.Add((r, col, RenderRegion(chart, region, size, pxPerDip, background)));
            }

        var count = pages.Count;
        return Document.Create(doc =>
        {
            var n = 0;
            foreach (var (row, column, png) in pages)
            {
                n++;
                var number = n;
                var marks = CutMarks(pageW, pageH, aw, ah, hasRight: column < plan.Columns - 1, hasBelow: row < plan.Rows - 1, c);
                doc.Page(page =>
                {
                    page.Size(new PageSize(pageW, pageH));
                    page.Margin(0);
                    page.PageColor("#FFFFFF");
                    page.Content().Layers(layers =>
                    {
                        layers.Layer().Svg(marks).FitArea();
                        layers.PrimaryLayer().PaddingHorizontal(TileMargin).PaddingTop(TileMargin).Column(col =>
                        {
                            col.Item().Height(14).Row(r =>
                            {
                                r.RelativeItem().Text(t =>
                                {
                                    t.ClampLines(1);
                                    t.Span(title).FontSize(8.5f).SemiBold().FontColor(c.Ink);
                                    t.Span($"   row {row + 1}, column {column + 1}").FontSize(8.5f).FontColor(c.Accent).SemiBold();
                                });
                                r.AutoItem().AlignBottom().Text($"page {number} of {count} · {plan.Columns} across × {plan.Rows} down · Made with Lineage Roots")
                                 .FontSize(7).FontColor(c.Faint);
                            });
                            col.Item().PaddingTop(TileHeader - 14).Width(aw).Height(ah).Image(png).UseOriginalImage().FitUnproportionally();
                            col.Item().PaddingTop(9).Text("To assemble: cut each page along the marks at its left and top edges (not in the first column or row) " +
                                                          "and lay it over its neighbour, with the cut edge on the green marks.")
                               .FontSize(6.5f).FontColor(c.Faint);
                        });
                    });
                });
            }
        });
    }

    /// <summary>Thin crop marks at the picture's corners, and green marks where the next page's edge goes.</summary>
    private static string CutMarks(float pageW, float pageH, float aw, float ah, bool hasRight, bool hasBelow, Colours c)
    {
        float x0 = TileMargin, y0 = TileMargin + TileHeader, x1 = x0 + aw, y1 = y0 + ah;
        var sb = new StringBuilder();
        string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        void Line(double ax, double ay, double bx, double by, string colour, double width) =>
            sb.Append($"<line x1=\"{F(ax)}\" y1=\"{F(ay)}\" x2=\"{F(bx)}\" y2=\"{F(by)}\" stroke=\"{colour}\" stroke-width=\"{F(width)}\"/>");

        foreach (var x in new[] { x0, x1 })
        {
            Line(x, y0 - 9, x, y0 - 3, c.Faint, 0.5);            // vertical ticks above and below
            Line(x, y1 + 3, x, y1 + 10, c.Faint, 0.5);
        }
        foreach (var y in new[] { y0, y1 })
        {
            Line(x0 - 16, y, x0 - 3, y, c.Faint, 0.5);           // horizontal ticks left and right
            Line(x1 + 3, y, x1 + 16, y, c.Faint, 0.5);
        }
        if (hasRight)
        {
            var x = x1 - Overlap;
            Line(x, y0 - 9, x, y0 - 2, c.Accent, 0.9);
            Line(x, y1 + 2, x, y1 + 10, c.Accent, 0.9);
        }
        if (hasBelow)
        {
            var y = y1 - Overlap;
            Line(x0 - 16, y, x0 - 2, y, c.Accent, 0.9);
            Line(x1 + 2, y, x1 + 16, y, c.Accent, 0.9);
        }
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(pageW)}\" height=\"{F(pageH)}\" viewBox=\"0 0 {F(pageW)} {F(pageH)}\">{sb}</svg>";
    }
}
