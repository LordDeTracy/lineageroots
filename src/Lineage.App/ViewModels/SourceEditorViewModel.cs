using CommunityToolkit.Mvvm.ComponentModel;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

public sealed record RepositoryOption(long Id, string Name)
{
    public static readonly RepositoryOption None = new(0, "None");
    public static readonly RepositoryOption New = new(-1, "New repository…");
    public override string ToString() => Name;
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
        if (Repository.Id == 0) { s.RepositoryId = null; return; }
        var repo = Repository.Id > 0 ? _tree.Repositories[Repository.Id] : _tree.AddRepository(new Repository());
        repo.Name = RepositoryName.Trim();
        repo.Address = RepositoryAddress.Trim();
        repo.Phone = RepositoryPhone.Trim();
        s.RepositoryId = repo.Id;
    }
}
