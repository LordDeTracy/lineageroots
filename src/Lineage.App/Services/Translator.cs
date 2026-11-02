using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform;

namespace Lineage.App.Services;

/// <summary>
/// Shows the program in another language. Each language is a list of the English labels with their translations
/// (Assets/Lang/xx.json); whenever a label, button, menu item, tooltip, placeholder or window title is given an English
/// phrase that has a translation, the translation is shown instead. Names, notes and anything typed are left alone,
/// and anything not in the list stays in English.
/// Phrases built with names or numbers in them are listed as patterns ("Saved {0} in {1} ms"): the parts that fill
/// the gaps are kept, and translated too when they're phrases themselves ("Undo {0}" with "add person").
/// </summary>
public static class Translator
{
    public static IReadOnlyList<(string Code, string Name)> Languages { get; } = new[]
    {
        ("en", "English"), ("sv", "Svenska"), ("de", "Deutsch"), ("es", "Español"), ("it", "Italiano"), ("fr", "Français"), ("ru", "Русский"), ("pt", "Português"),
    };

    private static Dictionary<string, string> _map = new();
    private static List<Pattern> _patterns = new();

    private sealed record Pattern(string Head, string Tail, Regex Match, string Translation);
    private static bool _installed;

    public static string Language { get; private set; } = "en";

    /// <summary>Loads a language (unknown or missing ones fall back to English).</summary>
    public static void Use(string code)
    {
        Language = "en";
        _map = new();
        _patterns = new();
        if (code is "en" or "" || Languages.All(l => l.Code != code)) return;
        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://LineageRoots/Assets/Lang/{code}.json"));
            _map = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
            _patterns = _map.Where(p => p.Value.Length > 0 && Gap.IsMatch(p.Key)).Select(p => MakePattern(p.Key, p.Value))
                .OrderByDescending(p => p.Head.Length + p.Tail.Length).ToList();
            Language = code;
        }
        catch (Exception) { /* no list for it: English */ }
    }

    /// <summary>The translation of an English phrase, or the phrase itself.</summary>
    public static string T(string english) => T(english, 0);

    /// <summary>A report or book in the program's language (headings, labels and set phrases; names are kept).</summary>
    public static Lineage.Core.Reports.ReportDocument Translate(Lineage.Core.Reports.ReportDocument doc) =>
        Language == "en" ? doc : doc.Translated(T);

    private static readonly Regex Gap = new(@"\{(\d)\}");

    private static Pattern MakePattern(string english, string translation)
    {
        var pieces = Gap.Split(english);   // literal, gap number, literal, gap number, …
        var regex = "^";
        for (var i = 0; i < pieces.Length; i++)
            regex += i % 2 == 0 ? Regex.Escape(pieces[i]) : $"(?<g{pieces[i]}>.+?)";
        return new Pattern(pieces[0], pieces[^1], new Regex(regex + "$", RegexOptions.Singleline | RegexOptions.CultureInvariant), translation);
    }

    private static string T(string english, int depth)
    {
        if (_map.TryGetValue(english, out var t) && t.Length > 0) return t;
        if (depth > 1 || english.Length > 600) return english;
        foreach (var p in _patterns)
        {
            if (!english.StartsWith(p.Head, StringComparison.Ordinal) || !english.EndsWith(p.Tail, StringComparison.Ordinal)) continue;
            var m = p.Match.Match(english);
            // "Born 1842 · Died 1911" is two phrases, not "Born {0}" with all the rest as the gap.
            if (!m.Success || m.Groups.Values.Skip(1).Any(g => g.Value.Contains(" · ")) && !p.Match.ToString().Contains('·')) continue;
            return Gap.Replace(p.Translation, g => m.Groups["g" + g.Groups[1].Value] is { Success: true } part ? T(part.Value, depth + 1) : g.Value);
        }
        if (english.Contains(" · "))
        {
            var parts = Regex.Split(english, @"(\s+·\s+)");
            var joined = string.Concat(parts.Select((part, i) => i % 2 == 0 ? T(part, depth + 1) : part));
            if (joined != english) return joined;
        }
        return english;
    }

    /// <summary>Hooks the controls that show text, once, so every screen is translated as it's built.</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((c, e) => Swap(c, TextBlock.TextProperty, e.NewValue));
        ContentControl.ContentProperty.Changed.AddClassHandler<ContentControl>((c, e) => Swap(c, ContentControl.ContentProperty, e.NewValue));
        HeaderedItemsControl.HeaderProperty.Changed.AddClassHandler<HeaderedItemsControl>((c, e) => Swap(c, HeaderedItemsControl.HeaderProperty, e.NewValue));
        TextBox.WatermarkProperty.Changed.AddClassHandler<TextBox>((c, e) => Swap(c, TextBox.WatermarkProperty, e.NewValue));
        AutoCompleteBox.WatermarkProperty.Changed.AddClassHandler<AutoCompleteBox>((c, e) => Swap(c, AutoCompleteBox.WatermarkProperty, e.NewValue));
        ComboBox.PlaceholderTextProperty.Changed.AddClassHandler<ComboBox>((c, e) => Swap(c, ComboBox.PlaceholderTextProperty, e.NewValue));
        Window.TitleProperty.Changed.AddClassHandler<Window>((c, e) => Swap(c, Window.TitleProperty, e.NewValue));
        ToolTip.TipProperty.Changed.AddClassHandler<Control>((c, e) => Swap(c, ToolTip.TipProperty, e.NewValue));
    }

    private static void Swap(AvaloniaObject target, AvaloniaProperty property, object? value)
    {
        if (Language == "en" || value is not string english || english.Length == 0) return;
        var translated = T(english);
        if (!ReferenceEquals(translated, english) && translated != english) target.SetCurrentValue(property, translated);
    }
}
