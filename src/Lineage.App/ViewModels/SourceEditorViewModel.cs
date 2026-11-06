using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lineage.Core.Model;
using Lineage.Core.Sources;

namespace Lineage.App.ViewModels;

public sealed record RepositoryOption(long Id, string Name)
{
    public static readonly RepositoryOption None = new(0, "None");
    public static readonly RepositoryOption New = new(-1, "New repository…");
    public override string ToString() => Name;
}

/// <summary>A citation template in the picker; <see cref="Template"/> is null for a free-form source.</summary>
public sealed record TemplateOption(SourceTemplate? Template, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One box of a template's form.</summary>
public sealed partial class TemplateFieldRow : ObservableObject
{
    public TemplateFieldRow(TemplateField field, string value) { Field = field; _value = value; }
    public TemplateField Field { get; }
    public string Label => Field.Label;
    public string Example => "e.g. " + Field.Example;
    [ObservableProperty] private string _value;
}

/// <summary>Edits a source and the repository (archive, library) that holds it.</summary>
public sealed partial class SourceEditorViewModel : ObservableObject
{
    private readonly FamilyTree _tree;

    public SourceEditorViewModel(FamilyTree tree, Source s, bool isNew)
    {
        _tree = tree;
        IsNew = isNew;
        Title = isNew ? "New source" : "Edit source";
        _sourceTitle = s.Title;
        _author = s.Author;
        _publication = s.Publication;
        _callNumber = s.CallNumber;
        _type = s.Type;
        _kindIndex = (int)s.Kind;
        _text = s.Text;
        _note = s.Note;
        Repositories = new[] { RepositoryOption.None }
            .Concat(tree.Repositories.Values.OrderBy(r => r.Name).Select(r => new RepositoryOption(r.Id, r.Name.Length > 0 ? r.Name : $"Repository {r.Id}")))
            .Append(RepositoryOption.New).ToList();
        _repository = Repositories.FirstOrDefault(r => r.Id == s.RepositoryId) ?? RepositoryOption.None;
        LoadRepository();
        _fieldValues = new Dictionary<string, string>(s.Fields);
        _templateOption = TemplateOptions.FirstOrDefault(o => o.Template?.Id == s.Template) ?? TemplateOptions[0];
        BuildFields();
        _generatedTitle = HasTemplate ? Preview(SourceTemplates.Title) : "";
    }

    // ---- citation template -------------------------------------------------------------------

    public static IReadOnlyList<TemplateOption> TemplateOptions { get; } =
        new[] { new TemplateOption(null, "None — I'll fill in the details myself") }
            .Concat(SourceTemplates.All.OrderBy(t => t.Group).ThenBy(t => t.Name).Select(t => new TemplateOption(t, $"{t.Group}: {t.Name}")))
            .ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTemplate))]
    private TemplateOption _templateOption;
    public bool HasTemplate => TemplateOption.Template != null;
    public ObservableCollection<TemplateFieldRow> Fields { get; } = new();
    /// <summary>Values typed so far, kept when switching templates so shared fields ("Year", "Place") carry over.</summary>
    private readonly Dictionary<string, string> _fieldValues;
    /// <summary>The title the template made last, so a title the user typed isn't overwritten.</summary>
    private string _generatedTitle = "";

    [ObservableProperty] private string _previewFull = "";
    [ObservableProperty] private string _previewShort = "";
    [ObservableProperty] private string _previewBibliography = "";

    partial void OnTemplateOptionChanged(TemplateOption value) => BuildFields();

    // ---- the transcript, read from a scan ----

    /// <summary>Asks which picture to read; set by the caller, and null where text can't be read.</summary>
    public Func<Task<string?>>? PickScan { get; init; }
    public bool CanReadScan => PickScan != null && Lineage.App.Services.TextRecognition.IsAvailable;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasScanMessage))] private string _scanMessage = "";
    public bool HasScanMessage => ScanMessage.Length > 0;

    /// <summary>Puts the printed text of a scan into the transcript; a web link in it goes in the link box (Advanced mode).</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task ReadScanAsync()
    {
        if (PickScan == null || await PickScan() is not { } path) return;
        ScanMessage = "Reading the text…";
        try
        {
            var found = await Task.Run(() => Lineage.App.Services.TextRecognition.Read(path));
            if (found.Text.Length == 0) { ScanMessage = "No text was found in that picture."; return; }
            Text = Text.Trim().Length > 0 ? Text.TrimEnd() + "\n\n" + found.Text : found.Text;
            ScanMessage = found.IsDoubtful
                ? "Added below, but the reader wasn't sure of it (it does best with clear print, not handwriting). Check it against the scan."
                : "Added below. Check it against the scan.";
            if (ShowLinkTools && Link.Trim().Length == 0 && System.Text.RegularExpressions.Regex.Match(found.Text, @"https?://\S+") is { Success: true } url)
            {
                Link = url.Value.TrimEnd('.', ',', ')', ';');
                ScanMessage += " It mentions a web link, now in the link box above.";
            }
        }
        catch (Lineage.App.Services.TextRecognitionException ex) { ScanMessage = ex.Message; }
    }

    // ---- a citation from a web link (Advanced mode) ----

    public bool ShowLinkTools { get; init; }
    [ObservableProperty] private string _link = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasLinkMessage))] private string _linkMessage = "";
    public bool HasLinkMessage => LinkMessage.Length > 0;

    /// <summary>Picks the template a web link calls for and fills in what the link itself says.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void FillFromLink()
    {
        if (LinkCitations.From(Link, DateTime.Today) is not { } found)
        {
            LinkMessage = "That doesn't look like a web link.";
            return;
        }
        TemplateOption = TemplateOptions.First(o => o.Template?.Id == found.TemplateId);
        foreach (var row in Fields)
            if (found.Fields.TryGetValue(row.Field.Key, out var value)) row.Value = value;
        LinkMessage = $"Filled in from the {found.Site} link. Add what the page itself says (names, dates, places), and the page or entry on each citation.";
    }

    private void BuildFields()
    {
        foreach (var row in Fields) _fieldValues[row.Field.Key] = row.Value;
        Fields.Clear();
        if (TemplateOption.Template is { } t)
            foreach (var f in t.Fields)
            {
                var row = new TemplateFieldRow(f, _fieldValues.GetValueOrDefault(f.Key, ""));
                row.PropertyChanged += (_, _) => UpdatePreview();
                Fields.Add(row);
            }
        UpdatePreview();
    }

    /// <summary>The source as the template would cite it, with the template's examples filling empty fields' places.</summary>
    private string Preview(Func<Source, string> render)
    {
        var s = new Source { Template = TemplateOption.Template?.Id ?? "" };
        foreach (var row in Fields) s.Fields[row.Field.Key] = row.Value;
        return render(s);
    }

    private void UpdatePreview()
    {
        PreviewFull = HasTemplate ? Preview(s => SourceTemplates.FullNote(s, "p. 12")) : "";
        PreviewShort = HasTemplate ? Preview(s => SourceTemplates.ShortNote(s, "p. 14")) : "";
        PreviewBibliography = HasTemplate ? Preview(SourceTemplates.Bibliography) : "";
        // Keep the title in step while it's still the one the template made.
        if (HasTemplate && (SourceTitle.Trim().Length == 0 || SourceTitle == _generatedTitle))
        {
            _generatedTitle = Preview(SourceTemplates.Title);
            SourceTitle = _generatedTitle;
        }
        OnPropertyChanged(nameof(IsValid));
    }

    public string Title { get; }
    public bool IsNew { get; }
    public bool CanDelete => !IsNew;
    public IReadOnlyList<RepositoryOption> Repositories { get; }

    [ObservableProperty] private string _sourceTitle;
    [ObservableProperty] private string _author;
    [ObservableProperty] private string _publication;
    [ObservableProperty] private string _callNumber;
    [ObservableProperty] private string _type;
    /// <summary>Original record, copy or index, or compiled work (evidence analysis).</summary>
    [ObservableProperty] private int _kindIndex;
    public static string[] Kinds => CitationRow.SourceKinds;
    [ObservableProperty] private string _text;
    [ObservableProperty] private string _note;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasRepository))] private RepositoryOption _repository;
    [ObservableProperty] private string _repositoryName = "";
    [ObservableProperty] private string _repositoryAddress = "";
    [ObservableProperty] private string _repositoryPhone = "";

    public bool HasRepository => Repository.Id != 0;
    public bool IsValid => SourceTitle.Trim().Length > 0;

    partial void OnRepositoryChanged(RepositoryOption value) => LoadRepository();

    private void LoadRepository()
    {
        var r = _tree.Repositories.GetValueOrDefault(Repository.Id);
        RepositoryName = r?.Name ?? "";
        RepositoryAddress = r?.Address ?? "";
        RepositoryPhone = r?.Phone ?? "";
    }

    public void Apply(Source s)
    {
        s.Title = SourceTitle.Trim();
        s.Author = Author.Trim();
        s.Publication = Publication.Trim();
        s.CallNumber = CallNumber.Trim();
        s.Type = Type.Trim();
        s.Kind = (SourceKind)Math.Clamp(KindIndex, 0, 3);
        s.Text = Text;
        s.Note = Note;
        s.Template = TemplateOption.Template?.Id ?? "";
        s.Fields = HasTemplate
            ? Fields.Where(r => r.Value.Trim().Length > 0).ToDictionary(r => r.Field.Key, r => r.Value.Trim())
            : new Dictionary<string, string>();
        if (Repository.Id == 0) { s.RepositoryId = null; return; }
        var repo = Repository.Id > 0 ? _tree.Repositories[Repository.Id] : _tree.AddRepository(new Repository());
        repo.Name = RepositoryName.Trim();
        repo.Address = RepositoryAddress.Trim();
        repo.Phone = RepositoryPhone.Trim();
        s.RepositoryId = repo.Id;
    }
}
