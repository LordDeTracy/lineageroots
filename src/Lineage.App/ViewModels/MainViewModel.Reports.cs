using CommunityToolkit.Mvvm.Input;
using Lineage.App.Reports;
using Lineage.Core.Export;
using Lineage.Core.Model;
using Lineage.Core.Reports;

namespace Lineage.App.ViewModels;

public sealed partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private Task FamilyGroupReportAsync() =>
        MakeReportAsync("Family group sheet", (t, p, path) => ReportBuilder.FamilyGroupSheet(t, p, path));

    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private Task AncestorReportAsync() =>
        MakeReportAsync("Ancestors", (t, p, path) => ReportBuilder.Ancestors(t, p, path));

    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private Task DescendantReportAsync() =>
        MakeReportAsync("Descendants", (t, p, path) => ReportBuilder.Descendants(t, p, path));

    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private Task RegisterReportAsync() => _current is { } p
        ? SaveDocumentAsync($"Descendants of {p.DisplayName}", () => Narrative.Register(_tree, p, 4)) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private Task IndividualSummaryAsync() => _current is { } p
        ? SaveDocumentAsync($"Summary - {p.DisplayName}", () => Narrative.IndividualSummary(_tree, p)) : Task.CompletedTask;

    [RelayCommand]
    private Task PlaceListReportAsync() => SaveDocumentAsync("Places", () => Narrative.PlaceList(_tree));

    [RelayCommand]
    private Task SurnameListReportAsync() => SaveDocumentAsync("Surnames", () => Narrative.SurnameList(_tree));

    [RelayCommand]
    private Task BirthdayCalendarAsync() => SaveDocumentAsync("Birthday calendar", () => Narrative.BirthdayCalendar(_tree));

    /// <summary>A whole family history book starting from the current person.</summary>
    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private async Task FamilyBookAsync()
    {
        if (_current is not { } p) return;
        var options = new BookOptionsViewModel(p.DisplayName, Environment.UserName);
        if (!await _dialogs.BookOptionsAsync(options)) return;
        var book = options.ToOptions();
        await SaveDocumentAsync(SafeName(book.Title.Length > 0 ? book.Title : $"Family of {p.DisplayName}"),
                                () => Narrative.FamilyBook(_tree, p, book), options.AsWord);
    }

    /// <summary>Asks where to save and writes the report as PDF or Word (chosen by the file type).</summary>
    private async Task SaveDocumentAsync(string name, Func<ReportDocument> build, bool wordFirst = false)
    {
        (string, string)[] types = wordFirst
            ? new[] { ("Word document", "*.docx"), ("PDF", "*.pdf") }
            : new[] { ("PDF", "*.pdf"), ("Word document", "*.docx") };
        var path = await _dialogs.SaveFileAsync($"Save {name}", SafeName(name) + (wordFirst ? ".docx" : ".pdf"), types);
        if (path == null) return;
        try
        {
            Status = "Writing the report…";
            await Task.Run(() =>
            {
                var doc = build();
                if (path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) DocumentDocx.Save(doc, path);
                else DocumentPdf.Save(doc, path);
            });
            Status = $"Saved {Path.GetFileName(path)}";
            OpenExternal(path);
        }
        catch (Exception ex)
        {
            Status = "";
            await _dialogs.ShowMessageAsync("Could not create the report", ex.Message);
        }
    }

    /// <summary>Exports a small website of the tree (open index.html in any browser) for sharing with family.</summary>
    [RelayCommand]
    private async Task ExportWebsiteAsync()
    {
        if (!HasTree) return;
        var privacy = new ExportOptionsViewModel(_tree, "Make a family website", "Choose folder");
        if (!await _dialogs.ExportOptionsAsync(privacy)) return;
        var folder = await _dialogs.PickFolderAsync("Choose an empty folder for the website");
        if (folder == null) return;
        try
        {
            Status = "Building the website…";
            var source = _tree;
            var options = privacy.ToOptions();
            var keepAll = privacy.KeepsEverything;
            var site = new HtmlSiteOptions
            {
                Title = TreeName, HomePersonId = _tree.HomePersonId ?? _current?.Id,
                IncludePhotos = !privacy.ExcludeMedia,
            };
            var treeFolder = FilePath is { } fp ? Path.GetDirectoryName(Path.GetFullPath(fp)) : null;
            var index = await Task.Run(() =>
            {
                var tree = keepAll ? source : Privacy.Filter(source, options);
                return HtmlSite.Export(tree, folder, site,
                    file => Path.IsPathRooted(file) ? file : treeFolder != null ? Path.Combine(treeFolder, file) : null);
            });
            Status = $"Website saved in {folder}";
            OpenExternal(index);
        }
        catch (Exception ex)
        {
            Status = "";
            await _dialogs.ShowMessageAsync("Could not build the website", ex.Message);
        }
    }

    private async Task MakeReportAsync(string kind, Action<FamilyTree, Person, string> build)
    {
        if (_current is not { } p) return;
        var path = await _dialogs.SaveFileAsync($"Save {kind.ToLowerInvariant()} report",
            SafeName($"{kind} - {p.DisplayName}") + ".pdf", ("PDF", "*.pdf"));
        if (path == null) return;
        try
        {
            Status = $"Creating {kind.ToLowerInvariant()} report…";
            var tree = _tree;
            await Task.Run(() => build(tree, p, path));
            Status = $"Saved {Path.GetFileName(path)}";
            OpenExternal(path);
        }
        catch (Exception ex)
        {
            Status = "";
            await _dialogs.ShowMessageAsync("Could not create the report", ex.Message);
        }
    }
}
