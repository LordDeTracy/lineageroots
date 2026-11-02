using CommunityToolkit.Mvvm.Input;
using Lineage.App.Reports;
using Lineage.App.Services;
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

    /// <summary>
    /// A research report for a client about the person showing: who it's for, who prepared it and the research
    /// question are asked first; the rest comes from the evidence, conclusions and research log.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private async Task ClientReportAsync()
    {
        if (_current is not { } p) return;
        var client = await _dialogs.PromptAsync("Client research report", "Who is the report for? (Leave empty for none.)", "", Array.Empty<string>());
        if (client == null) return;
        var researcher = await _dialogs.PromptAsync("Client research report", "Who prepared it?",
            _settings.ResearcherName.Length > 0 ? _settings.ResearcherName : Environment.UserName, Array.Empty<string>());
        if (researcher == null) return;
        var objective = await _dialogs.PromptAsync("Client research report",
            $"What was the research question? (Leave empty for the usual: documenting {p.DisplayName}'s birth, marriage and death.)", "", Array.Empty<string>());
        if (objective == null) return;
        if (researcher.Trim() != _settings.ResearcherName)
        {
            _settings.ResearcherName = researcher.Trim();
            try { _settings.Save(); } catch (Exception) { }
        }
        var options = new ClientReportOptions(client, researcher, objective);
        await SaveDocumentAsync($"Research report - {p.DisplayName}", () => ClientReport.Build(_tree, p, options));
    }

    /// <summary>The research log (tasks, searches made, finished tasks) as PDF or Word.</summary>
    [RelayCommand]
    private Task PrintResearchAsync() => SaveDocumentAsync("Research log", () => Narrative.ResearchLog(_tree));

    /// <summary>The research log as a CSV spreadsheet.</summary>
    [RelayCommand]
    private async Task ExportResearchCsvAsync()
    {
        var path = await _dialogs.SaveFileAsync("Export the research log", "Research log.csv", ("Spreadsheet (CSV)", "*.csv"));
        if (path == null) return;
        static string Quote(string v) => v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        try
        {
            var lines = Narrative.ResearchCsv(_tree).Select(row => string.Join(",", row.Select(Quote)));
            await File.WriteAllTextAsync(path, string.Join("\r\n", lines) + "\r\n", new System.Text.UTF8Encoding(true));
            Status = $"Exported the research log to {Path.GetFileName(path)}";
        }
        catch (Exception ex) { await _dialogs.ShowMessageAsync("Couldn't export", ex.Message); }
    }

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
        var treeFolder = FilePath is { } fp ? Path.GetDirectoryName(Path.GetFullPath(fp)) : null;
        book.FindMedia = file => Path.IsPathRooted(file) ? file : treeFolder != null ? Path.Combine(treeFolder, file) : null;
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

    /// <summary>
    /// Saves the tree as one web page for a phone (search, a page per person, their ancestors), which opens from an
    /// email, a cloud drive or the phone's files without internet. Living people are left out or hidden as chosen.
    /// </summary>
    [RelayCommand]
    private async Task ExportPhoneViewerAsync()
    {
        if (!HasTree) return;
        var privacy = new ExportOptionsViewModel(_tree, "Make a phone viewer", "Save");
        if (!await _dialogs.ExportOptionsAsync(privacy)) return;
        var path = await _dialogs.SaveFileAsync("Save the phone viewer", SafeName(TreeName) + " - phone.html", ("Web page", "*.html"));
        if (path == null) return;
        try
        {
            Status = "Making the phone viewer…";
            var source = _tree;
            var options = privacy.ToOptions();
            var keepAll = privacy.KeepsEverything;
            var viewer = new PhoneViewerOptions { Title = TreeName, HomePersonId = _tree.HomePersonId ?? _current?.Id };
            await Task.Run(() =>
            {
                var tree = keepAll ? source : Privacy.Filter(source, options);
                File.WriteAllText(path, PhoneViewer.Build(tree, viewer, Translator.T), new System.Text.UTF8Encoding(false));
            });
            Status = $"Saved {Path.GetFileName(path)}";
            await _dialogs.ShowMessageAsync("Phone viewer saved",
                $"Send {Path.GetFileName(path)} to the phone — by email, a cloud drive such as OneDrive or Google Drive, or a USB cable — and open it in the phone's web browser. It works without the internet, and nothing needs installing.\n\nIt's a copy: make a new one after you add to the tree.");
        }
        catch (Exception ex)
        {
            Status = "";
            await _dialogs.ShowMessageAsync("Could not make the phone viewer", ex.Message);
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
