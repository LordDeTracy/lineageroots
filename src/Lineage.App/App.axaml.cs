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
    /// <summary>
    /// Closes every window for an update, asking about unsaved changes in each first. False if the user kept one open.
    /// </summary>
    public static async Task<bool> CloseAllWindowsAsync()
    {
        if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return false;
        var windows = desktop.Windows.OfType<MainWindow>().ToList();
        foreach (var w in windows)
            if (w.DataContext is MainViewModel vm && !await vm.CanCloseAsync()) return false;
        foreach (var w in windows) w.CloseWithoutAsking();
        return true;
    }

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
            Updater.CleanUpAfterUpdate();
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
            // Developer options: --pdf out.pdf (write a person's summary and quit), --screenshot out.png, --dark, --light, --evidence (colour charts by evidence), --size WxH, --scroll Y,
            // --view pedigree|fan|timeline|sources|home|places|research|suggested|quick|dna|clusters|odds|cm, --dialog person|event|source|duplicates|familysearch|dnamatch|census|photo
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
            var evidence = args.Remove("--evidence");
            var pdf = TakeOption(args, "--pdf");

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
                if (evidence) vm.ChartsShowEvidence = true;
                if (pdf != null)
                {
                    // Release check: write the current person's summary as a PDF, then quit.
                    try
                    {
                        var who = vm.Tree.Person(long.TryParse(person, out var id) ? id : vm.Tree.People.Keys.Min())!;
                        Reports.DocumentPdf.Save(Lineage.Core.Reports.Narrative.IndividualSummary(vm.Tree, who), pdf);
                    }
                    catch (Exception ex) { await File.WriteAllTextAsync(pdf + ".error.txt", ex.ToString()); }
                    window.CloseWithoutAsking();
                    return;
                }
                switch (view)
                {
                    case "pedigree": vm.View = PersonView.Pedigree; break;
                    case "fan": vm.View = PersonView.FanChart; break;
                    case "descendants": vm.View = PersonView.Descendants; break;
                    case "hourglass": vm.View = PersonView.Hourglass; break;
                    case "map": vm.View = PersonView.Map; break;
                    case "map-1800": vm.View = PersonView.Map; vm.MapYear = 1800; break;
                    case "timeline": vm.View = PersonView.Timeline; break;
                    case "sources": vm.Section = AppSection.Sources; break;
                    case "home": vm.Section = AppSection.Home; break;
                    case "places": vm.Section = AppSection.Places; break;
                    case "research": vm.Section = AppSection.Research; break;
                    case "dna": vm.Section = AppSection.Dna; break;
                    case "suggested": vm.Section = AppSection.Research; vm.ResearchFilterIndex = 3; break;
                    case "quick": vm.OpenQuickSearchCommand.Execute(null); vm.QuickSearchText = "whit"; break;
                    case "clusters": vm.Section = AppSection.Dna; vm.DnaTabIndex = 1; break;
                    case "chromosomes": vm.Section = AppSection.Dna; vm.DnaTabIndex = 4; break;
                    case "odds":
                        vm.Section = AppSection.Dna;
                        vm.DnaTabIndex = 2;
                        vm.WatoSubject = vm.WatoSubjects.Skip(1).FirstOrDefault() ?? vm.WatoSubject;
                        foreach (var c in vm.ParentChoices.Where(c => c.Text.Contains(" and ")).Take(3))
                            vm.WatoHypothesisToAdd = c;
                        break;
                    case "cm": vm.Section = AppSection.Dna; vm.DnaTabIndex = 3; vm.CmCalculatorText = "412"; break;
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
                        "review" => DemoReview(vm),
                        "census" => vm.EnterCensusHouseholdCommand.ExecuteAsync(null),
                        "photo" => vm.OpenPhotoCommand.ExecuteAsync(vm.Photos.First()),
                        "dnamatch" => vm.EditDnaMatchCommand.ExecuteAsync(vm.DnaMatchRows.Skip(2).First()),
                        "problems" => Task.Run(() => Avalonia.Threading.Dispatcher.UIThread.Invoke(() => vm.FindProblemsCommand.Execute(null))),
                        "relationship" => Task.Run(() => Avalonia.Threading.Dispatcher.UIThread.Invoke(() => vm.FindRelationshipCommand.Execute(null))),
                        _ => vm.EditPersonCommand.ExecuteAsync(null),
                    };
                    await Task.Delay(dialog == "photo" ? 4000 : 1000);   // a photo also has its faces found
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

    /// <summary>For screenshots: a copy of the open tree with a few made-up edits, shown in the review window.</summary>
    private static Task DemoReview(MainViewModel vm)
    {
        var (copy, share) = Lineage.Core.Sharing.ShareReview.CreateCopy(vm.Tree, "Cousin Ada", new Lineage.Core.Export.PrivacyOptions());
        var people = copy.People.Values.OrderBy(p => p.Id).ToList();
        if (people.Count > 2)
        {
            if (people[0].Events.FirstOrDefault() is { } first) first.Place += " (St John's)";
            people[1].Events.Add(Lineage.Core.Model.Event.Of("OCCU", Lineage.Core.Model.GenDate.Parse("1851"), "Leeds, Yorkshire, England"));
            people[1].Events[^1].Value = "Wool sorter";
            people[2].PrimaryName.Given += " Jane";
            vm.Tree.People[people[2].Id].PrimaryName.Given += " Ann";
        }
        var changes = Lineage.Core.Sharing.ShareReview.Compare(vm.Tree, copy, share);
        vm.Tree.Shares.Remove(share);
        return new Views.DialogService(Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d ? d.MainWindow! : null!)
            .ReviewChangesAsync(new ReviewChangesViewModel(share.With, changes));
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
