using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>A client project in the Research tab's Clients list.</summary>
public sealed record ProjectRow(Guid Id, string Client, string Question, string Detail, bool Finished)
{
    public bool HasQuestion => Question.Length > 0;
}

public partial class MainViewModel
{
    /// <summary>Advanced mode (Settings): tools for professional researchers — client projects, locality guides, citations from links.</summary>
    public bool IsAdvanced => _settings.AdvancedMode;

    public ObservableCollection<ProjectRow> ProjectRows { get; } = new();
    public bool IsClientsFilter => ResearchFilterIndex == 6;

    private void RebuildProjects()
    {
        ProjectRows.Clear();
        foreach (var p in _tree.Projects.OrderBy(p => p.Status == ProjectStatus.Finished).ThenBy(p => p.Client, StringComparer.CurrentCultureIgnoreCase))
        {
            var detail = Display.Join(
                p.Status switch { ProjectStatus.OnHold => "On hold", ProjectStatus.Finished => "Finished", _ => "Open" },
                p.BudgetHours > 0 ? $"{p.Hours:0.##} of {p.BudgetHours:0.#} hours" : $"{p.Hours:0.##} hours",
                p.Unbilled > 0 ? $"{p.Money(p.Unbilled)} not yet invoiced" : "",
                p.Outstanding > 0 ? $"{p.Money(p.Outstanding)} awaiting payment" : "",
                p.Due.IsEmpty ? "" : $"due {Display.Date(p.Due)}");
            ProjectRows.Add(new ProjectRow(p.Id, p.Client, p.Question, detail, p.Status == ProjectStatus.Finished));
        }
    }

    [RelayCommand]
    private Task NewProjectAsync() => EditProjectAsync(null);

    [RelayCommand]
    private Task OpenProjectAsync(ProjectRow row) => EditProjectAsync(_tree.Projects.FirstOrDefault(p => p.Id == row.Id));

    private async Task EditProjectAsync(ClientProject? existing)
    {
        var project = existing ?? new ClientProject { Started = GenDate.Parse(DateTime.Today.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)) };
        var editor = new ProjectEditorViewModel(project, existing == null);
        if (!await _dialogs.EditProjectAsync(editor)) return;
        editor.Apply(project);
        if (existing == null) _tree.Projects.Add(project);
        // Projects are business records kept beside the tree, so they're saved straight away rather than undone.
        IsDirty = true;
        ScheduleAutoSave();
        RebuildProjects();
        if (editor.NewInvoice is { } invoice)
            await SaveDocumentAsync($"Invoice {invoice.Number} - {project.Client}",
                () => Lineage.Core.Reports.Narrative.InvoiceDocument(project, invoice, _settings.ResearcherName));
    }
}
