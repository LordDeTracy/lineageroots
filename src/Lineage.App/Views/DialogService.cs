using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Lineage.App.ViewModels;
using Lineage.Core.FamilySearch;

namespace Lineage.App.Views;

public interface IDialogService
{
    Task<string?> OpenFileAsync(string title, params (string Name, string Patterns)[] filters);
    Task<IReadOnlyList<string>> OpenFilesAsync(string title, params (string Name, string Patterns)[] filters);
    Task<string?> SaveFileAsync(string title, string suggestedName, params (string Name, string Patterns)[] filters);
    Task ShowMessageAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message);
    /// <summary>A question with several answers; returns the index of the one chosen (the last answers Escape).</summary>
    Task<int> ChooseAsync(string title, string message, params string[] buttons);
    /// <summary>"Save changes before closing?" with Save, Don't save and Cancel.</summary>
    Task<SaveChoice> AskSaveChangesAsync(string treeName);
    /// <summary>Shows the person editor; true when saved. The caller applies the edits (so they can be undone).</summary>
    Task<bool> EditPersonAsync(PersonEditorViewModel editor);
    Task<EditorResult> EditEventAsync(EventEditorViewModel editor);
    Task<EditorResult> EditSourceAsync(SourceEditorViewModel editor);
    Task<EditorResult> EditResearchAsync(ResearchEditorViewModel editor);
    Task<EditorResult> EditDnaMatchAsync(DnaMatchEditorViewModel editor);
    Task<bool> ReviewChangesAsync(ReviewChangesViewModel review);
    Task<bool> FindGpsAsync(FindGpsViewModel review);
    Task<bool> EnterCensusAsync(CensusEntryViewModel form);
    Task<bool> TranscribeAsync(TranscribeViewModel form);
    Task<bool> EditSettingsAsync(SettingsViewModel settings);
    Task<bool> ExportOptionsAsync(ExportOptionsViewModel options);
    /// <summary>Shows a tree comparison; true when the user asked to copy the ticked people in.</summary>
    Task<bool> CompareTreesAsync(CompareTreesViewModel compare);
    Task<bool> BookOptionsAsync(BookOptionsViewModel options);
    Task<string?> PickFolderAsync(string title);
    /// <summary>The photo viewer; true when the user pressed Done (or clicked a tagged person).</summary>
    Task<bool> ShowPhotosAsync(PhotoViewerViewModel viewer);
    /// <summary>Asks for one line of text, with suggestions; null when cancelled.</summary>
    Task<string?> PromptAsync(string title, string message, string initial, IReadOnlyList<string> suggestions);
    /// <summary>Asks for a locked file's password (showing its hint); null when cancelled.</summary>
    Task<string?> AskPasswordAsync(string treeName, string hint, bool wrong);
    /// <summary>Asks for a new password (typed twice), an optional hint, and that the user understands it can't be recovered.</summary>
    Task<PasswordChoice?> ChoosePasswordAsync(bool changing);
    Task ShowHistoryAsync(HistoryViewModel history);
    /// <summary>The clean-up wizard; true when the user pressed Clean up.</summary>
    Task<bool> CleanupAsync(CleanupViewModel wizard);
    /// <summary>Opens the problem finder beside the main window (not modal, so the user can fix things).</summary>
    void ShowProblems(ProblemsViewModel problems);
    void ShowLocalShare(LocalShareViewModel share);
    Task ShowTutorialAsync(TutorialViewModel tutorial);
    void ShowRelationship(RelationshipViewModel relationship);
    Task ShowDuplicatesAsync(DuplicatesViewModel duplicates);
    Task<bool> FamilySearchSettingsAsync(FamilySearchSettingsViewModel settings);
    Task<FsPerson?> FamilySearchSearchAsync(FamilySearchSearchViewModel search);
    Task<bool> FamilySearchCompareAsync(FamilySearchCompareViewModel compare);
}

/// <summary>A password the user chose, with the reminder to show when it's asked for.</summary>
public sealed record PasswordChoice(string Password, string Hint);

public sealed class DialogService(Window owner) : IDialogService
{
    private static List<FilePickerFileType> Types((string Name, string Patterns)[] filters) =>
        filters.Select(f => new FilePickerFileType(f.Name) { Patterns = f.Patterns.Split(';') }).ToList();

    public async Task<string?> OpenFileAsync(string title, params (string Name, string Patterns)[] filters)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false, FileTypeFilter = Types(filters),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title, params (string Name, string Patterns)[] filters)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = true, FileTypeFilter = Types(filters),
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName, params (string Name, string Patterns)[] filters)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title, SuggestedFileName = suggestedName, FileTypeChoices = Types(filters),
            DefaultExtension = Path.GetExtension(suggestedName),
        });
        return file?.TryGetLocalPath();
    }

    public Task ShowMessageAsync(string title, string message) => Choose(title, message, "OK");

    public async Task<bool> ConfirmAsync(string title, string message) => await Choose(title, message, "Yes", "No") == 0;

    public Task<int> ChooseAsync(string title, string message, params string[] buttons) => Choose(title, message, buttons);

    public async Task<SaveChoice> AskSaveChangesAsync(string treeName) =>
        await Choose("Unsaved changes", $"Save the changes to \"{treeName}\" before closing?", "Save", "Don't save", "Cancel") switch
        {
            0 => SaveChoice.Save,
            1 => SaveChoice.DontSave,
            _ => SaveChoice.Cancel,
        };

    /// <summary>
    /// A small message window in the app's style. The first button is the default (Enter); the last
    /// one answers Escape or closing the window. Returns the index of the button pressed.
    /// </summary>
    private async Task<int> Choose(string title, string message, params string[] buttons)
    {
        var result = buttons.Length - 1;
        var dialog = new Window
        {
            Title = title, Width = 460, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            [!Window.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Bg"),
        };
        dialog.Classes.Add("editor");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var b = new Button
            {
                Content = new TextBlock { Text = buttons[i] }, MinWidth = 96,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                IsDefault = i == 0, IsCancel = i == buttons.Length - 1,
            };
            b.Classes.Add(i == 0 ? "primary" : "outline");
            if (i == 0 && b.Content is TextBlock t)
                t[!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("OnAccent");
            b.Click += (_, _) => { result = index; dialog.Close(); };
            row.Children.Add(b);
        }
        var footer = new Border
        {
            Padding = new Avalonia.Thickness(24, 14), BorderThickness = new Avalonia.Thickness(0, 1, 0, 0), Child = row,
            [!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Surface"),
            [!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Line"),
        };
        var heading = new TextBlock { Text = title, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        heading.Classes.Add("title");
        var body = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, LineHeight = 21 };
        body.Classes.Add("muted");
        var content = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        content.Children.Add(footer);
        content.Children.Add(new StackPanel { Margin = new Avalonia.Thickness(26, 22, 26, 22), Spacing = 10, Children = { heading, body } });
        dialog.Content = content;
        await dialog.ShowDialog(owner);
        return result;
    }

    public Task<bool> EditPersonAsync(PersonEditorViewModel editor) =>
        new PersonEditorWindow { DataContext = editor }.ShowDialog<bool>(owner);

    public Task<EditorResult> EditEventAsync(EventEditorViewModel editor) =>
        new EventEditorWindow { DataContext = editor }.ShowDialog<EditorResult>(owner);

    public Task<EditorResult> EditSourceAsync(SourceEditorViewModel editor) =>
        new SourceEditorWindow { DataContext = editor }.ShowDialog<EditorResult>(owner);

    public Task<EditorResult> EditResearchAsync(ResearchEditorViewModel editor) =>
        new ResearchEditorWindow { DataContext = editor }.ShowDialog<EditorResult>(owner);

    public Task<bool> TranscribeAsync(TranscribeViewModel form) =>
        new TranscribeWindow { DataContext = form }.ShowDialog<bool>(owner);

    public Task<bool> EnterCensusAsync(CensusEntryViewModel form) =>
        new CensusEntryWindow { DataContext = form }.ShowDialog<bool>(owner);

    public Task<bool> FindGpsAsync(FindGpsViewModel review) =>
        new FindGpsWindow { DataContext = review }.ShowDialog<bool>(owner);

    public Task<bool> ReviewChangesAsync(ReviewChangesViewModel review) =>
        new ReviewChangesWindow { DataContext = review }.ShowDialog<bool>(owner);

    public Task<EditorResult> EditDnaMatchAsync(DnaMatchEditorViewModel editor) =>
        new DnaMatchEditorWindow { DataContext = editor }.ShowDialog<EditorResult>(owner);

    public Task<bool> EditSettingsAsync(SettingsViewModel settings) =>
        new SettingsWindow { DataContext = settings }.ShowDialog<bool>(owner);

    public Task<bool> ExportOptionsAsync(ExportOptionsViewModel options) =>
        new ExportOptionsWindow { DataContext = options }.ShowDialog<bool>(owner);

    public Task<bool> CompareTreesAsync(CompareTreesViewModel compare) =>
        new CompareTreesWindow { DataContext = compare }.ShowDialog<bool>(owner);

    public Task<bool> BookOptionsAsync(BookOptionsViewModel options) =>
        new BookOptionsWindow { DataContext = options }.ShowDialog<bool>(owner);

    public Task<bool> ShowPhotosAsync(PhotoViewerViewModel viewer) =>
        new PhotoViewerWindow { DataContext = viewer }.ShowDialog<bool>(owner);

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PromptAsync(string title, string message, string initial, IReadOnlyList<string> suggestions)
    {
        var box = new AutoCompleteBox
        {
            Text = initial, ItemsSource = suggestions, FilterMode = AutoCompleteFilterMode.Contains, MinimumPrefixLength = 1,
        };
        string? result = null;
        var dialog = new Window
        {
            Title = title, Width = 520, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            [!Window.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Bg"),
        };
        dialog.Classes.Add("editor");
        var ok = new Button { Content = new TextBlock { Text = "OK" }, IsDefault = true, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add("primary");
        ((TextBlock)ok.Content)[!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("OnAccent");
        ok.Click += (_, _) => { result = box.Text; dialog.Close(); };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Classes.Add("outline");
        cancel.Click += (_, _) => dialog.Close();
        var heading = new TextBlock { Text = title };
        heading.Classes.Add("title");
        var body = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        body.Classes.Add("muted");
        var footer = new Border
        {
            Padding = new Avalonia.Thickness(24, 14), BorderThickness = new Avalonia.Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
            [!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Surface"),
            [!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Line"),
        };
        var content = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        content.Children.Add(footer);
        content.Children.Add(new StackPanel { Margin = new Avalonia.Thickness(26, 22), Spacing = 12, Children = { heading, body, box } });
        dialog.Content = content;
        dialog.Opened += (_, _) => box.Focus();
        await dialog.ShowDialog(owner);
        return result;
    }

    public async Task<string?> AskPasswordAsync(string treeName, string hint, bool wrong)
    {
        var box = new TextBox { PasswordChar = '\u25CF', Watermark = "Password" };
        AutomationProperties.SetName(box, "Password");
        var lines = new List<Control>();
        if (wrong) lines.Add(Line("That password isn't right. Passwords are case-sensitive: check Caps Lock.", "Danger"));
        if (hint.Length > 0) lines.Add(Line($"Hint: {hint}", null));
        lines.Add(box);
        string? result = null;
        await Form($"\"{treeName}\" is password-protected", "Type the password to open it.", "Open", lines,
                   () => box.Text?.Length > 0, () => result = box.Text, box);
        return result;
    }

    public async Task<PasswordChoice?> ChoosePasswordAsync(bool changing)
    {
        var first = new TextBox { PasswordChar = '\u25CF', Watermark = "New password" };
        var second = new TextBox { PasswordChar = '\u25CF', Watermark = "Type it again" };
        var hint = new TextBox { Watermark = "Hint (optional) — something only you would understand" };
        var understood = new CheckBox
        {
            Content = new TextBlock
            {
                Text = "I understand that if I forget this password, nobody can open this tree — not me, not Lineage Roots, not anyone. I'll keep a copy of the password somewhere safe.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            },
        };
        AutomationProperties.SetName(first, "New password");
        AutomationProperties.SetName(second, "Repeat the password");
        AutomationProperties.SetName(hint, "Password hint");
        var mismatch = Line("The two passwords don't match.", "Danger");
        mismatch.IsVisible = false;
        second.TextChanged += (_, _) => mismatch.IsVisible = second.Text?.Length > 0 && second.Text != first.Text;
        PasswordChoice? result = null;
        await Form(changing ? "Change the password" : "Protect this tree with a password",
            "The whole tree is encrypted (AES-256) when it's saved, and its backups too. Without the password the file can't be read. " +
            "The hint is stored unencrypted, so don't put the password in it.",
            changing ? "Change password" : "Set password",
            new List<Control> { first, second, mismatch, hint, understood },
            () => first.Text?.Length > 0 && first.Text == second.Text && understood.IsChecked == true
                  && !(hint.Text ?? "").Contains(first.Text!, StringComparison.OrdinalIgnoreCase),
            () => result = new PasswordChoice(first.Text!, hint.Text?.Trim() ?? ""), first);
        return result;
    }

    private static TextBlock Line(string text, string? brush)
    {
        var t = new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        if (brush != null) t[!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(brush);
        else t.Classes.Add("muted");
        return t;
    }

    /// <summary>
    /// A small form in the app's dialog style: heading, explanation, the given controls, then Cancel and an OK
    /// button that is enabled while <paramref name="valid"/> holds (checked as the boxes are typed in or ticked).
    /// </summary>
    private async Task Form(string title, string message, string okText, List<Control> controls, Func<bool> valid, Action accept, Control focus)
    {
        var dialog = new Window
        {
            Title = title, Width = 520, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            [!Window.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Bg"),
        };
        dialog.Classes.Add("editor");
        var ok = new Button { Content = new TextBlock { Text = okText }, IsDefault = true, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add("primary");
        ((TextBlock)ok.Content)[!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("OnAccent");
        ok.Click += (_, _) => { if (!valid()) return; accept(); dialog.Close(); };
        void Check() => ok.IsEnabled = valid();
        foreach (var c in controls)
        {
            if (c is TextBox t) t.TextChanged += (_, _) => Check();
            if (c is CheckBox b) b.IsCheckedChanged += (_, _) => Check();
        }
        Check();
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Classes.Add("outline");
        cancel.Click += (_, _) => dialog.Close();
        var heading = new TextBlock { Text = title, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        heading.Classes.Add("title");
        var footer = new Border
        {
            Padding = new Avalonia.Thickness(24, 14), BorderThickness = new Avalonia.Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
            [!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Surface"),
            [!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Line"),
        };
        var body = new StackPanel { Margin = new Avalonia.Thickness(26, 22), Spacing = 12, Children = { heading, Line(message, null) } };
        foreach (var c in controls) body.Children.Add(c);
        var content = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        content.Children.Add(footer);
        content.Children.Add(body);
        dialog.Content = content;
        dialog.Opened += (_, _) => focus.Focus();
        await dialog.ShowDialog(owner);
    }

    public Task ShowHistoryAsync(HistoryViewModel history) =>
        new HistoryWindow { DataContext = history }.ShowDialog(owner);

    public Task<bool> CleanupAsync(CleanupViewModel wizard) =>
        new CleanupWindow { DataContext = wizard }.ShowDialog<bool>(owner);

    private ProblemsWindow? _problems;
    private RelationshipWindow? _relationship;

    public Task ShowTutorialAsync(TutorialViewModel tutorial) =>
        new TutorialWindow { DataContext = tutorial }.ShowDialog(owner);

    public void ShowLocalShare(LocalShareViewModel share)
    {
        var window = new LocalShareWindow { DataContext = share };
        window.Closed += (_, _) => share.Dispose();   // closing the window stops sharing
        window.Show(owner);
    }

    public void ShowProblems(ProblemsViewModel problems)
    {
        if (_problems is { IsVisible: true }) { _problems.DataContext = problems; _problems.Activate(); return; }
        _problems = new ProblemsWindow { DataContext = problems };
        _problems.Show(owner);
    }

    public void ShowRelationship(RelationshipViewModel relationship)
    {
        if (_relationship is { IsVisible: true }) { _relationship.DataContext = relationship; _relationship.Activate(); return; }
        _relationship = new RelationshipWindow { DataContext = relationship };
        _relationship.Show(owner);
    }

    public Task ShowDuplicatesAsync(DuplicatesViewModel duplicates) =>
        new DuplicatesWindow { DataContext = duplicates }.ShowDialog(owner);

    public Task<bool> FamilySearchSettingsAsync(FamilySearchSettingsViewModel settings) =>
        new FamilySearchSettingsWindow { DataContext = settings }.ShowDialog<bool>(owner);

    public Task<FsPerson?> FamilySearchSearchAsync(FamilySearchSearchViewModel search) =>
        new FamilySearchSearchWindow { DataContext = search }.ShowDialog<FsPerson?>(owner);

    public Task<bool> FamilySearchCompareAsync(FamilySearchCompareViewModel compare) =>
        new FamilySearchCompareWindow { DataContext = compare }.ShowDialog<bool>(owner);
}
