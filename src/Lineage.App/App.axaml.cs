using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Lineage.App.Services;
using Lineage.App.ViewModels;
using Lineage.App.Views;

namespace Lineage.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>Opens another main window (each window holds its own tree), optionally loading a file.</summary>
    public static MainWindow OpenWindow(string? file)
    {
        var window = new MainWindow();
        var vm = new MainViewModel(new DialogService(window));
        window.DataContext = vm;
        if (file != null) window.Opened += async (_, _) => await vm.OpenPathAsync(file);
        window.Show();
        return window;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            var settings = AppSettings.Load();
            Lineage.App.Controls.MapView.AllowOnlineTiles = settings.OnlineMaps;
            RequestedThemeVariant = settings.Theme switch
            {
                "Dark" => ThemeVariant.Dark,
                "Light" => ThemeVariant.Light,
                _ => ThemeVariant.Default,
            };

            var args = (desktop.Args ?? Array.Empty<string>()).ToList();
            // Developer options: --screenshot out.png, --dark, --light, --size WxH, --scroll Y,
            // --view pedigree|fan|sources|home|places|research, --dialog person|event|source|duplicates|familysearch
            // (captures that window), --tab N (editor tab)
            string? screenshot = TakeOption(args, "--screenshot");
            var size = TakeOption(args, "--size");
            var scroll = TakeOption(args, "--scroll");
            var view = TakeOption(args, "--view");
            if (args.Remove("--dark")) RequestedThemeVariant = ThemeVariant.Dark;
            if (args.Remove("--light")) RequestedThemeVariant = ThemeVariant.Light;
            var dialog = TakeOption(args, "--dialog");
            var tab = TakeOption(args, "--tab");
            var person = TakeOption(args, "--person");

            var window = new MainWindow();
            if (size?.Split('x') is [var w, var h]) { window.Width = double.Parse(w); window.Height = double.Parse(h); }
            var vm = new MainViewModel(new DialogService(window));
            window.DataContext = vm;
            desktop.MainWindow = window;
            var file = args.FirstOrDefault(a => !a.StartsWith("--"));
            window.Opened += async (_, _) =>
            {
                if (file != null) await vm.OpenPathAsync(file);
                if (long.TryParse(person, out var pid)) vm.NavigateCommand.Execute(pid);
                switch (view)
                {
                    case "pedigree": vm.View = PersonView.Pedigree; break;
                    case "fan": vm.View = PersonView.FanChart; break;
                    case "descendants": vm.View = PersonView.Descendants; break;
                    case "hourglass": vm.View = PersonView.Hourglass; break;
                    case "map": vm.View = PersonView.Map; break;
                    case "sources": vm.Section = AppSection.Sources; break;
                    case "home": vm.Section = AppSection.Home; break;
                    case "places": vm.Section = AppSection.Places; break;
                    case "research": vm.Section = AppSection.Research; break;
                }
                if (scroll != null) window.ScrollPageTo(double.Parse(scroll));
                if (dialog != null)
                {
                    _ = dialog switch
                    {
                        "event" => vm.EditEventCommand.ExecuteAsync(vm.Events.FirstOrDefault(e => e.Event.Citations.Count > 0) ?? vm.Events.FirstOrDefault()),
                        "source" => vm.EditSourceCommand.ExecuteAsync(null),
                        "duplicates" => vm.FindDuplicatesCommand.ExecuteAsync(null),
                        "familysearch" => vm.FamilySearchSettingsCommand.ExecuteAsync(null),
                        "settings" => vm.OpenSettingsCommand.ExecuteAsync(null),
                        "book" => vm.FamilyBookCommand.ExecuteAsync(null),
                        "export" => vm.ExportGedcomCommand.ExecuteAsync(null),
                        "research" => vm.NewResearchCommand.ExecuteAsync(null),
                        "problems" => Task.Run(() => Avalonia.Threading.Dispatcher.UIThread.Invoke(() => vm.FindProblemsCommand.Execute(null))),
                        "relationship" => Task.Run(() => Avalonia.Threading.Dispatcher.UIThread.Invoke(() => vm.FindRelationshipCommand.Execute(null))),
                        _ => vm.EditPersonCommand.ExecuteAsync(null),
                    };
                    await Task.Delay(1000);
                    if (screenshot != null && desktop.Windows.LastOrDefault() is { } shown && shown != window)
                    {
                        if (tab != null && shown.DataContext is PersonEditorViewModel editor) editor.TabIndex = int.Parse(tab);
                        await MainWindow.SaveScreenshotAsync(shown, screenshot);
                        window.CloseWithoutAsking();
                    }
                    return;
                }
                if (screenshot != null) await MainWindow.SaveScreenshotAsync(window, screenshot);
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static string? TakeOption(List<string> args, string name)
    {
        var i = args.IndexOf(name);
        if (i < 0 || i + 1 >= args.Count) return null;
        var value = args[i + 1];
        args.RemoveRange(i, 2);
        return value;
    }
}
