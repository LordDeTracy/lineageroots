using Avalonia;
using Avalonia.Headless;
using Lineage.App;
using Lineage.App.Tests;
using Lineage.App.ViewModels;
using Lineage.App.Views;
using Lineage.Core.FamilySearch;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace Lineage.App.Tests;

/// <summary>The real app (styles, resources) on Avalonia's headless platform, rendering with Skia.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        MainViewModel.Launcher = opened => Opened.Add(opened); // never open real files or web pages from tests
        return AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    public static List<string> Opened { get; } = new();
}

/// <summary>Stands in for the dialogs: each handler plays the user's part.</summary>
public sealed class FakeDialogs : IDialogService
{
    public Func<PersonEditorViewModel, bool> Person { get; set; } = _ => true;
    public Func<EventEditorViewModel, EditorResult> Event { get; set; } = _ => EditorResult.Saved;
    public Func<SourceEditorViewModel, EditorResult> Source { get; set; } = _ => EditorResult.Saved;
    public Func<ResearchEditorViewModel, EditorResult> Research { get; set; } = _ => EditorResult.Saved;
    public Action<DuplicatesViewModel> Duplicates { get; set; } = _ => { };
    public bool ConfirmAnswer { get; set; } = true;
    public SaveChoice SaveAnswer { get; set; } = SaveChoice.DontSave;
    public string? SavePath { get; set; }
    public List<string> Messages { get; } = new();

    public string? OpenPath { get; set; }
    public Task<string?> OpenFileAsync(string title, params (string Name, string Patterns)[] filters) => Task.FromResult(OpenPath);
    public Task<IReadOnlyList<string>> OpenFilesAsync(string title, params (string Name, string Patterns)[] filters) =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    public Task<string?> SaveFileAsync(string title, string suggestedName, params (string Name, string Patterns)[] filters) =>
        Task.FromResult(SavePath);
    public Task ShowMessageAsync(string title, string message) { Messages.Add(message); return Task.CompletedTask; }
    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(ConfirmAnswer);
    public Task<SaveChoice> AskSaveChangesAsync(string treeName) => Task.FromResult(SaveAnswer);
    public Task<bool> EditPersonAsync(PersonEditorViewModel editor) => Task.FromResult(Person(editor));
    public Task<EditorResult> EditEventAsync(EventEditorViewModel editor) => Task.FromResult(Event(editor));
    public Task<EditorResult> EditSourceAsync(SourceEditorViewModel editor) => Task.FromResult(Source(editor));
    public Task<EditorResult> EditResearchAsync(ResearchEditorViewModel editor) => Task.FromResult(Research(editor));
    public Task<bool> EditSettingsAsync(SettingsViewModel settings) => Task.FromResult(false);
    public Func<ExportOptionsViewModel, bool> ExportOptions { get; set; } = _ => true;
    public Func<CompareTreesViewModel, bool> CompareTrees { get; set; } = _ => false;
    public Func<BookOptionsViewModel, bool> BookOptions { get; set; } = _ => true;
    public Task<bool> BookOptionsAsync(BookOptionsViewModel options) => Task.FromResult(BookOptions(options));
    public string? FolderPath { get; set; }
    public Func<PhotoViewerViewModel, bool> Photos { get; set; } = _ => false;
    public Task<bool> ShowPhotosAsync(PhotoViewerViewModel viewer) => Task.FromResult(Photos(viewer));
    public Task<string?> PickFolderAsync(string title) => Task.FromResult(FolderPath);
    public Task<bool> CompareTreesAsync(CompareTreesViewModel compare) => Task.FromResult(CompareTrees(compare));
    public Task<bool> ExportOptionsAsync(ExportOptionsViewModel options) => Task.FromResult(ExportOptions(options));
    public string? PromptAnswer { get; set; }
    public Task<string?> PromptAsync(string title, string message, string initial, IReadOnlyList<string> suggestions) =>
        Task.FromResult(PromptAnswer);
    public ProblemsViewModel? LastProblems { get; private set; }
    public RelationshipViewModel? LastRelationship { get; private set; }
    public void ShowProblems(ProblemsViewModel problems) => LastProblems = problems;
    public void ShowRelationship(RelationshipViewModel relationship) => LastRelationship = relationship;
    public Task ShowDuplicatesAsync(DuplicatesViewModel duplicates) { Duplicates(duplicates); return Task.CompletedTask; }
    public Task<bool> FamilySearchSettingsAsync(FamilySearchSettingsViewModel settings) => Task.FromResult(false);
    public Task<FsPerson?> FamilySearchSearchAsync(FamilySearchSearchViewModel search) => Task.FromResult<FsPerson?>(null);
    public Task<bool> FamilySearchCompareAsync(FamilySearchCompareViewModel compare) => Task.FromResult(false);
}

/// <summary>A small fictional family written to a temp file.</summary>
public static class Demo
{
    public const string Gedcom = """
        0 HEAD
        1 CHAR UTF-8
        0 @S1@ SOUR
        1 TITL Parish registers of St Mary, Harrow
        0 @I1@ INDI
        1 NAME Thomas Edward /Whitfield/
        1 SEX M
        1 BIRT
        2 DATE 14 MAR 1842
        2 PLAC Harrow, Middlesex, England
        2 SOUR @S1@
        3 PAGE Baptisms 1842, entry 118
        1 DEAT
        2 DATE 9 NOV 1911
        1 FAMC @F1@
        1 FAMS @F2@
        0 @I2@ INDI
        1 NAME George /Whitfield/
        1 SEX M
        1 BIRT
        2 DATE ABT 1815
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Mary Ann /Cole/
        1 SEX F
        1 FAMS @F1@
        0 @I4@ INDI
        1 NAME Eliza /Hartley/
        1 SEX F
        1 BIRT
        2 DATE 1845
        1 FAMS @F2@
        0 @I5@ INDI
        1 NAME Clara /Whitfield/
        1 SEX F
        1 BIRT
        2 DATE 1872
        1 FAMC @F2@
        0 @I6@ INDI
        1 NAME Thomas /Whitfeld/
        1 SEX M
        1 BIRT
        2 DATE 1842
        2 PLAC Harrow
        0 @F1@ FAM
        1 HUSB @I2@
        1 WIFE @I3@
        1 CHIL @I1@
        0 @F2@ FAM
        1 HUSB @I1@
        1 WIFE @I4@
        1 MARR
        2 DATE 18 JUN 1868
        2 PLAC Leeds, Yorkshire, England
        1 CHIL @I5@
        0 TRLR
        """;

    public static async Task<(MainViewModel Vm, FakeDialogs Dialogs)> OpenAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lineage-test-{Guid.NewGuid():N}.ged");
        await File.WriteAllTextAsync(path, Gedcom);
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(dialogs);
        await vm.OpenPathAsync(path);
        File.Delete(path);
        return (vm, dialogs);
    }
}
