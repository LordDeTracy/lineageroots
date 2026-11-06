using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Editing;
using Lineage.Core.Export;
using Lineage.Core.Import.Aq;
using Lineage.Core.Import.Gedcom;
using Lineage.Core.Analysis;
using Lineage.Core.Model;
using Lineage.Core.Storage;
using Lineage.App.Services;
using Lineage.App.Views;

namespace Lineage.App.ViewModels;

/// <summary>The sections on the navigation rail, top to bottom.</summary>
/// <summary>The sections on the left-hand rail. "Sources" is shown as Evidence.</summary>
public enum AppSection { Home, People, Sources, Places, Research, Dna, Charts, Reports }

/// <summary>Which picture of the current person the main area shows.</summary>
public enum PersonView { Details, Pedigree, FanChart, Descendants, Hourglass, Map, Timeline, Lifelines, Relationship, Statistics, BowTie }

/// <summary>Ancestors of the person at the centre of a chart, by Ahnentafel number.</summary>
public sealed record ChartData(FamilyTree Tree, long RootId, IReadOnlyDictionary<int, Person> Ancestors);

/// <summary>
/// The main window. Split across files: this one holds state, loading/saving, navigation and the
/// person page; Editing, Media, Sources and Tools hold the rest.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    public const int ChartGenerations = 7;

    private readonly IDialogService _dialogs;
    private FamilyTree _tree = new();
    private UndoHistory _history;
    private List<PersonRow> _allRows = new();
    /// <summary>A place to go back to: a section (Home, Research…) and who was showing.</summary>
    private readonly record struct Spot(AppSection Section, long? PersonId);
    private readonly Stack<Spot> _back = new(), _forward = new();
    private readonly DispatcherTimer _searchTimer;

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        _history = new UndoHistory(_tree);
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ApplyFilter();
            ApplySourceFilter();
            if (IsPlacesSection) ApplyPlaceFilter();
        };
        RebuildRows();
    }

    public FamilyTree Tree => _tree;

    public ObservableCollection<PersonRow> People { get; } = new();
    public ObservableCollection<EventRow> Events { get; } = new();
    public ObservableCollection<PersonCard> Parents { get; } = new();
    public ObservableCollection<AddSlot> MissingParents { get; } = new();
    public ObservableCollection<FamilyCard> Families { get; } = new();
    public ObservableCollection<string> Chips { get; } = new();
    public ObservableCollection<PhotoTile> Photos { get; } = new();
    public ObservableCollection<PlaceRow> Places { get; } = new();

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private PersonRow? _selectedRow;
    [ObservableProperty] private string _title = "Lineage Roots";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _currentName = "";
    /// <summary>How the person showing is related to the home person ("Ann's 3rd great-grandfather").</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasCurrentRelation))] private string _currentRelation = "";
    public bool HasCurrentRelation => CurrentRelation.Length > 0;
    [ObservableProperty] private string _currentDetail = "";
    [ObservableProperty] private string _currentNote = "";
    [ObservableProperty] private string _currentInitials = "";
    [ObservableProperty] private string _currentFamilySearchId = "";
    [ObservableProperty] private bool _currentIsMale;
    [ObservableProperty] private bool _currentIsFemale;
    [ObservableProperty] private bool _hasCurrent;
    [ObservableProperty] private bool _hasNote;
    [ObservableProperty] private bool _hasEvents;
    [ObservableProperty] private bool _hasFamilies;
    [ObservableProperty] private bool _hasPlaces;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasProfilePhoto))] private Bitmap? _profilePhoto;
    public bool HasProfilePhoto => ProfilePhoto != null;
    public bool HasFamilySearchId => CurrentFamilySearchId.Length > 0;
    partial void OnCurrentFamilySearchIdChanged(string value) => OnPropertyChanged(nameof(HasFamilySearchId));
    [ObservableProperty] private bool _hasTree;
    [ObservableProperty] private string _treeName = "";
    [ObservableProperty] private string _peopleCountText = "";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private ChartData? _chart;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ChartGenerationsShown))] private int _pedigreeGenerations = 4;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ChartGenerationsShown))] private int _fanGenerations = 5;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ChartGenerationsShown))] private int _descendantGenerations = 3;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ChartGenerationsShown))] private int _hourglassGenerations = 3;

    public static int[] GenerationChoices { get; } = { 2, 3, 4, 5, 6, 7, 8 };

    /// <summary>The generations picker edits whichever chart is showing (each chart has its own sensible range).</summary>
    public int ChartGenerationsShown
    {
        get => View switch
        {
            PersonView.FanChart => FanGenerations,
            PersonView.Descendants => DescendantGenerations,
            PersonView.Hourglass => HourglassGenerations,
            _ => PedigreeGenerations,
        };
        set
        {
            switch (View)
            {
                case PersonView.FanChart: FanGenerations = Math.Clamp(value, 3, 7); break;
                case PersonView.Descendants: DescendantGenerations = value; break;
                case PersonView.Hourglass: HourglassGenerations = value; break;
                default: PedigreeGenerations = Math.Clamp(value, 2, 6); break;
            }
        }
    }

    // ---- which view is showing ----------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailsView), nameof(IsPedigreeView), nameof(IsFanView), nameof(IsDescendantsView),
                              nameof(IsHourglassView), nameof(IsMapView), nameof(IsTimelineView), nameof(IsChartView), nameof(ViewIndex), nameof(ChartGenerationsShown),
                              nameof(PersonTabIndex), nameof(ChartTabIndex),
                              nameof(IsLifelinesView), nameof(IsRelationshipView), nameof(IsStatisticsView), nameof(IsBowTieView), nameof(HasGenerations))]
    private PersonView _view = PersonView.Details;
    public bool IsDetailsView => View == PersonView.Details;
    public bool IsPedigreeView => View == PersonView.Pedigree;
    public bool IsFanView => View == PersonView.FanChart;
    public bool IsDescendantsView => View == PersonView.Descendants;
    public bool IsHourglassView => View == PersonView.Hourglass;
    public bool IsMapView => View == PersonView.Map;
    public bool IsTimelineView => View == PersonView.Timeline;
    public bool IsLifelinesView => View == PersonView.Lifelines;
    public bool IsRelationshipView => View == PersonView.Relationship;
    public bool IsStatisticsView => View == PersonView.Statistics;
    public bool IsBowTieView => View == PersonView.BowTie;
    /// <summary>Who the lifelines chart shows: 0 the close family, 1 father's side, 2 mother's side, 3 descendants.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(LifelineScope))] private int _lifelineScopeIndex;
    public Lineage.Core.Charts.LifelineScope LifelineScope => (Lineage.Core.Charts.LifelineScope)LifelineScopeIndex;
    /// <summary>Only the family-tree charts have a number of generations to choose.</summary>
    public bool HasGenerations => View is PersonView.Pedigree or PersonView.FanChart or PersonView.Descendants or PersonView.Hourglass or PersonView.BowTie;
    /// <summary>A chart is showing (so the generations, zoom and save-image controls apply).</summary>
    public bool IsChartView => View is not (PersonView.Details or PersonView.Map or PersonView.Timeline);
    partial void OnViewChanged(PersonView value)
    {
        if (IsChart(value))
        {
            _lastChart = value;
            if (Section == AppSection.People) Section = AppSection.Charts;
        }
        else if (Section == AppSection.Charts) Section = AppSection.People;
        if (value == PersonView.Map) RebuildPersonMap();
        else StopMapPlayback();
        if (value == PersonView.Timeline) RebuildTimeline();
    }
    public int ViewIndex { get => (int)View; set => View = (PersonView)value; }

    /// <summary>The section picked on the navigation rail.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SectionIndex), nameof(IsHomeSection), nameof(IsPeopleSection), nameof(IsSourcesSection),
                              nameof(IsPlacesSection), nameof(IsResearchSection), nameof(IsDnaSection), nameof(IsChartsSection),
                              nameof(IsReportsSection), nameof(IsPeopleList), nameof(HasSidebar), nameof(SearchWatermark),
                              nameof(IsEvidenceAnalysis), nameof(IsSourcesList))]
    private AppSection _section = AppSection.People;
    public int SectionIndex { get => (int)Section; set => Section = (AppSection)Math.Clamp(value, 0, (int)AppSection.Reports); }
    public bool IsHomeSection => Section == AppSection.Home;
    public bool IsPeopleSection => Section == AppSection.People;
    public bool IsSourcesSection => Section == AppSection.Sources;
    public bool IsPlacesSection => Section == AppSection.Places;
    public bool IsResearchSection => Section == AppSection.Research;
    public bool IsDnaSection => Section == AppSection.Dna;
    public bool IsChartsSection => Section == AppSection.Charts;
    public bool IsReportsSection => Section == AppSection.Reports;
    /// <summary>The people list sits beside both the person pages and the charts.</summary>
    public bool IsPeopleList => Section is AppSection.People or AppSection.Charts;
    /// <summary>People, Charts, Evidence and Places have a list beside the page; the others use the full width.</summary>
    public bool HasSidebar => Section is AppSection.People or AppSection.Charts or AppSection.Places
                              || (Section == AppSection.Sources && EvidenceModeIndex == 0);
    partial void OnSectionChanged(AppSection value)
    {
        // The Charts tab shows the charts, the People tab the person's page, map and timeline.
        if (value == AppSection.Charts && !IsChartView) View = _lastChart;
        else if (value == AppSection.People && IsChartView) View = PersonView.Details;
        NotifyViewState();
        switch (value)
        {
            case AppSection.Sources: RebuildSources(); if (IsEvidenceAnalysis) RebuildEvidence(); break;
            case AppSection.Places: RebuildPlaceList(); break;
            case AppSection.Research: RebuildResearch(); RefreshProblemsSummary(); break;
            case AppSection.Home: RebuildHome(); break;
            case AppSection.Dna: RebuildDna(); break;
        }
    }

    [RelayCommand] private void ShowView(PersonView view)
    {
        View = view;
        Section = IsChart(view) ? AppSection.Charts : AppSection.People;
    }

    private static bool IsChart(PersonView view) => view is not (PersonView.Details or PersonView.Map or PersonView.Timeline);
    private PersonView _lastChart = PersonView.Pedigree;

    /// <summary>The People tab's views: 0 Details, 1 Map, 2 Timeline.</summary>
    public int PersonTabIndex
    {
        get => View switch { PersonView.Map => 1, PersonView.Timeline => 2, _ => 0 };
        set => View = value switch { 1 => PersonView.Map, 2 => PersonView.Timeline, _ => PersonView.Details };
    }

    /// <summary>The Charts tab's charts: 0 Pedigree, 1 Fan, 2 Descendants, 3 Hourglass, 4 Bow tie, 5 Lifelines, 6 Relationship, 7 Statistics.</summary>
    public int ChartTabIndex
    {
        get => View switch
        {
            PersonView.FanChart => 1, PersonView.Descendants => 2, PersonView.Hourglass => 3, PersonView.BowTie => 4,
            PersonView.Lifelines => 5, PersonView.Relationship => 6, PersonView.Statistics => 7, _ => 0,
        };
        set => View = value switch
        {
            1 => PersonView.FanChart, 2 => PersonView.Descendants, 3 => PersonView.Hourglass, 4 => PersonView.BowTie,
            5 => PersonView.Lifelines, 6 => PersonView.Relationship, 7 => PersonView.Statistics, _ => PersonView.Pedigree,
        };
    }

    private Person? _current;
    internal Person? Current => _current;

    partial void OnSearchTextChanged(string value) { _searchTimer.Stop(); _searchTimer.Start(); }

    /// <summary>Also match names that sound alike or are spelled slightly differently.</summary>
    [ObservableProperty] private bool _soundsLike;
    partial void OnSoundsLikeChanged(bool value) => ApplyFilter();

    public string SearchWatermark => Section switch
    {
        AppSection.Sources => "Search sources and evidence…",
        AppSection.Places => "Search places…",
        _ => "Search names, years or IDs…",
    };
    private bool _syncingSelection;

    partial void OnSelectedRowChanged(PersonRow? value)
    {
        if (!_syncingSelection && value != null) Show(value.Id, recordHistory: true);
    }
    partial void OnIsDirtyChanged(bool value) => UpdateTitle();
    partial void OnFilePathChanged(string? value) => UpdateTitle();

    private void UpdateTitle()
    {
        TreeName = FilePath != null ? Path.GetFileNameWithoutExtension(FilePath) : _tree.Name;
        Title = $"{(IsDirty ? "• " : "")}{TreeName} — Lineage Roots";
    }

    /// <summary>True while nothing has been opened yet: the window shows the welcome screen.</summary>
    public bool IsWelcome => !HasTree;
    /// <summary>A tree is open but no one is shown (it is empty).</summary>
    public bool IsEmptyTree => HasTree && !HasCurrent && IsPeopleList;
    public bool IsPersonShown => HasTree && HasCurrent && IsPeopleList;
    partial void OnHasTreeChanged(bool value) => NotifyViewState();
    partial void OnHasCurrentChanged(bool value) => NotifyViewState();
    private void NotifyViewState()
    {
        OnPropertyChanged(nameof(IsWelcome));
        OnPropertyChanged(nameof(IsEmptyTree));
        OnPropertyChanged(nameof(IsPersonShown));
    }

    // ---- loading and saving ------------------------------------------------------------

    private void Load(FamilyTree tree, string? path, string how, long ms)
    {
        _tree = tree;
        _history = new UndoHistory(tree);
        SetHistoryAuthor();
        NotifyUndo();
        NotifyLock();
        OnPropertyChanged(nameof(Tree));
        HasTree = true;
        FilePath = path;
        IsDirty = path == null;
        _back.Clear();
        _forward.Clear();
        _listFilter = null;
        ListFilterText = "";
        OnPropertyChanged(nameof(HasListFilter));
        Section = AppSection.People;
        RebuildRows();
        RebuildSources();
        RebuildResearch();
        var warnings = tree.Warnings.Count > 0 ? $" — {tree.Warnings.Count} warnings" : "";
        Status = $"{how} {tree.People.Count:N0} people, {tree.Families.Count:N0} families, " +
                 $"{tree.Sources.Count:N0} sources in {ms:N0} ms{warnings}";
        var first = tree.Person(tree.HomePersonId) ?? tree.People.Values.OrderBy(p => p.Id).FirstOrDefault();
        if (first != null) Show(first.Id, recordHistory: false); else ClearCurrent();
        RefreshRecoverable();
        UpdateTitle();
    }

    public async Task OpenPathAsync(string path)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var ext = Path.GetExtension(path).ToLowerInvariant();
            // Importing the same AQ or GEDCOM file again would lose the work saved in its copy: offer the copy.
            if (ext is ".aq" or ".ged" && _settings.ImportCopies.TryGetValue(Path.GetFullPath(path), out var copy) && File.Exists(copy))
            {
                var choice = await _dialogs.ChooseAsync("You've worked on this family before",
                    $"Your changes are saved in {Path.GetFileName(copy)}. Open that, or import {Path.GetFileName(path)} again from the start (without your changes)?",
                    "Open my saved copy", "Import again", "Cancel");
                if (choice == 0) { await OpenPathAsync(copy); return; }
                if (choice != 1) return;
            }
            var plugin = ImporterFor(path);
            var imported = ext is ".aq" or ".ged" || plugin != null;
            var tree = plugin != null ? await Task.Run(() => plugin.Import(path)) : ext switch
            {
                ".aq" => await Task.Run(() => AqImporter.Import(path)),
                ".ged" => await Task.Run(() => GedcomImporter.ImportFile(path)),
                _ => await LoadLineageAsync(path),
            };
            if (tree == null) return;   // the password wasn't given
            Load(tree, imported ? null : path, imported ? "Imported" : "Opened", sw.ElapsedMilliseconds);
            if (imported) await SaveImportedCopyAsync(path);
            else
            {
                RememberLastFile(path);
                if (Backups.For(path) is { } pending) await OfferRecoveryAsync(pending);
            }
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Could not open file", ex.Message);
        }
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        Load(new FamilyTree { Name = "New family" }, null, "Created", 0);
        IsDirty = false;
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        var path = await _dialogs.OpenFileAsync("Open a Lineage Roots tree", ("Lineage Roots trees", "*.lineage"));
        if (path != null) await OpenPathAsync(path);
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        var types = new List<(string, string)> { ("AQ or GEDCOM", "*.aq;*.ged"), ("AQ 16", "*.aq"), ("GEDCOM", "*.ged") };
        types.AddRange(EnabledPluginsOf<Lineage.Core.Plugins.IImporterPlugin>().Select(i => (i.FileTypeName, string.Join(";", i.Patterns))));
        var path = await _dialogs.OpenFileAsync("Import family data", types.ToArray());
        if (path != null) await OpenPathAsync(path);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (FilePath == null) { await SaveAsAsync(); return; }
        await SaveToAsync(FilePath);
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        var path = await _dialogs.SaveFileAsync("Save family file", SafeName(_tree.Name) + ".lineage", ("Lineage Roots trees", "*.lineage"));
        if (path != null) await SaveToAsync(path);
    }

    private async Task SaveToAsync(string path)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var tree = _tree;
            var keep = _settings.BackupsToKeep;
            var previousPath = FilePath;
            // Photo links relative to the old file location must keep pointing at the same files.
            if (previousPath == null || !string.Equals(Path.GetFullPath(previousPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                Lineage.Core.Media.MediaLibrary.Rebase(tree, previousPath, path);
            await Task.Run(() =>
            {
                try { Backups.BackUpBeforeSave(path, keep); }
                catch (Exception) { /* a failed backup must never block saving */ }
                LineageStore.Save(tree, path);
            });
            Backups.ClearRecovery(previousPath, tree.Name);
            Backups.ClearRecovery(path, tree.Name);
            FilePath = path;
            IsDirty = false;
            RememberLastFile(path);
            Status = $"Saved {Path.GetFileName(path)} in {sw.ElapsedMilliseconds:N0} ms";
        }
        catch (Exception ex) { await _dialogs.ShowMessageAsync("Could not save", ex.Message); }
    }

    [RelayCommand]
    private async Task ExportGedcomAsync()
    {
        var privacy = new ExportOptionsViewModel(_tree, "Export GEDCOM", "Continue", _current) { ShowGedcomVersion = true };
        if (!await _dialogs.ExportOptionsAsync(privacy)) return;
        var path = await _dialogs.SaveFileAsync(privacy.Gedcom7 ? "Export GEDCOM 7.0" : "Export GEDCOM 5.5.1", SafeName(_tree.Name) + ".ged", ("GEDCOM", "*.ged"));
        if (path == null) return;
        try
        {
            var source = _tree;
            var options = privacy.ToOptions();
            var keepAll = privacy.KeepsEverything;
            var count = await Task.Run(() =>
            {
                var branch = privacy.Apply(source);
                var tree = keepAll ? branch : Lineage.Core.Export.Privacy.Filter(branch, options);
                if (privacy.Gedcom7) Lineage.Core.Export.Gedcom7.ExportFile(tree, path);
                else GedcomExporter.ExportFile(tree, path);
                return tree.People.Count;
            });
            Status = $"Exported {count:N0} people to {Path.GetFileName(path)}";
        }
        catch (Exception ex) { await _dialogs.ShowMessageAsync("Could not export", ex.Message); }
    }

    /// <summary>
    /// Splits off one person's ancestors or descendants as a family file of its own (to give to a cousin, or to work
    /// on a line separately). The tree that's open isn't changed.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private async Task SaveBranchAsync()
    {
        if (_current is not { } root) return;
        var choice = new ExportOptionsViewModel(_tree, "Save a branch as a new family file", "Continue", root, branchOnly: true) { LivingIndex = 2, ExcludePrivateEvents = false };
        if (!await _dialogs.ExportOptionsAsync(choice)) return;
        var name = $"{root.PrimaryName.Surname.Trim()} branch".Trim();
        var path = await _dialogs.SaveFileAsync("Save the branch", SafeName($"{TreeName} - {root.DisplayName}") + ".lineage", ("Lineage Roots family file", "*.lineage"));
        if (path == null) return;
        try
        {
            var source = _tree;
            var options = choice.ToOptions();
            var keepAll = choice.KeepsEverything;
            var count = await Task.Run(() =>
            {
                var branch = Lineage.Core.Export.Branch.Extract(source, Lineage.Core.Export.Branch.People(source, root, choice.Branch), name);
                branch.HomePersonId = root.Id;
                var tree = keepAll ? branch : Lineage.Core.Export.Privacy.Filter(branch, options);
                Lineage.Core.Storage.LineageStore.Save(tree, path);
                return tree.People.Count;
            });
            Status = $"Saved {count:N0} people in {Path.GetFileName(path)}";
        }
        catch (Exception ex) { await _dialogs.ShowMessageAsync("Could not save the branch", ex.Message); }
    }

    private async Task<bool> ConfirmDiscardAsync() =>
        !IsDirty || _tree.People.Count == 0 ||
        await _dialogs.ConfirmAsync("Unsaved changes", "Discard the changes to the current family file?");

    internal static string SafeName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    // ---- people list and navigation -------------------------------------------------------

    private void RebuildRows()
    {
        _allRows = _tree.People.Values.Select(p => new PersonRow(p)).OrderBy(r => r.SortKey).ToList();
        ApplyFilter();
        FillRelations(_allRows);
    }

    private CancellationTokenSource? _relations;

    /// <summary>
    /// Works out everyone's relationship to the home person in the background and shows it in the people list a batch
    /// at a time, so even a big tree's list appears at once. A newer list (after an edit) cancels an older run.
    /// </summary>
    private void FillRelations(IReadOnlyList<PersonRow> rows)
    {
        _relations?.Cancel();
        if (_tree.Person(_tree.HomePersonId) is not { } home) return;
        var cancel = (_relations = new CancellationTokenSource()).Token;
        var tree = _tree;
        _ = Task.Run(() =>
        {
            var batch = new List<(PersonRow Row, string Text)>();
            void Flush()
            {
                var done = batch.ToList();
                batch.Clear();
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (cancel.IsCancellationRequested) return;
                    foreach (var (row, text) in done) row.Relation = text;
                });
            }
            try
            {
                foreach (var row in rows)
                {
                    if (cancel.IsCancellationRequested) return;
                    if (tree.Person(row.Id) is not { } p) continue;
                    var text = p.Id == home.Id ? "Home person" : Relationship.Describe(tree, home, p) is { Related: true } r ? r.Text : "";
                    if (text.Length > 0) batch.Add((row, text));
                    if (batch.Count >= 200) Flush();
                }
                Flush();
            }
            catch (Exception) { /* the tree changed under us; the next rebuild starts again */ }
        }, cancel);
    }

    /// <summary>
    /// How someone is related to the home person, for the person page and family cards: "Ann's great-grandfather",
    /// "Ann's wife's aunt", "Home person"; empty with no home person or no connection.
    /// </summary>
    private string RelationToHome(Person p)
    {
        if (_tree.Person(_tree.HomePersonId) is not { } home) return "";
        if (home.Id == p.Id) return "Home person";
        var r = Relationship.Describe(_tree, home, p);
        if (!r.Related || r.Text.Length == 0) return "";
        var first = home.PrimaryName.Given.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? home.DisplayName;
        return $"{first}'s {r.Text}";
    }

    private PersonCard Card(Person p, string role = "") => new(p, role) { Relation = RelationToHome(p) };

    /// <summary>The running version, shown small in the corner ("Version 0.4.4").</summary>
    public string VersionText { get; } = $"Version {Lineage.App.Services.Updater.Current.ToString(3)}";

    // ---- a list to work through (a tree-health line from Home: everyone with no birth date…) ----

    private HashSet<long>? _listFilter;
    [ObservableProperty] private string _listFilterText = "";
    public bool HasListFilter => _listFilter != null;

    /// <summary>Opens a Home line: for a tree-health line, its people become the list, so each can be fixed in turn.</summary>
    [RelayCommand]
    private void OpenHomeLine(HomeLine line)
    {
        if (line.Ids is { Count: > 1 } ids)
        {
            _listFilter = ids.ToHashSet();
            ListFilterText = $"{ids.Count:N0} {line.Text}";
            OnPropertyChanged(nameof(HasListFilter));
            ApplyFilter();
        }
        if (line.PersonId is { } id) OpenPerson(id);
    }

    [RelayCommand]
    private void ClearListFilter()
    {
        _listFilter = null;
        ListFilterText = "";
        OnPropertyChanged(nameof(HasListFilter));
        ApplyFilter();
    }

    // ---- people list filters (beyond the name search) ----

    [ObservableProperty] private bool _showFilters;
    [ObservableProperty] private string _filterPlace = "";
    [ObservableProperty] private string _filterBornFrom = "";
    [ObservableProperty] private string _filterBornTo = "";
    [ObservableProperty] private string _filterDiedFrom = "";
    [ObservableProperty] private string _filterDiedTo = "";
    [ObservableProperty] private string _filterLivedFrom = "";
    [ObservableProperty] private string _filterLivedTo = "";
    [ObservableProperty] private int _filterSexIndex;
    public static IReadOnlyList<string> FilterSexes { get; } = new[] { "Anyone", "Men", "Women" };

    partial void OnFilterPlaceChanged(string value) => FiltersChanged();
    partial void OnFilterBornFromChanged(string value) => FiltersChanged();
    partial void OnFilterBornToChanged(string value) => FiltersChanged();
    partial void OnFilterDiedFromChanged(string value) => FiltersChanged();
    partial void OnFilterDiedToChanged(string value) => FiltersChanged();
    partial void OnFilterLivedFromChanged(string value) => FiltersChanged();
    partial void OnFilterLivedToChanged(string value) => FiltersChanged();
    partial void OnFilterSexIndexChanged(int value) => FiltersChanged();

    private static int Year(string text) => int.TryParse(text.Trim(), out var y) && y is > 0 and < 3000 ? y : 0;

    public PersonFilter CurrentFilter => new(FilterPlace, Year(FilterBornFrom), Year(FilterBornTo), Year(FilterDiedFrom),
        Year(FilterDiedTo), Year(FilterLivedFrom), Year(FilterLivedTo),
        FilterSexIndex switch { 1 => Sex.Male, 2 => Sex.Female, _ => null });

    public string FiltersText => CurrentFilter.Count is var n and > 0 ? $"Filters ({n})" : "Filters";
    public bool HasActiveFilters => !CurrentFilter.IsEmpty;

    private void FiltersChanged()
    {
        OnPropertyChanged(nameof(FiltersText));
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilter();
    }

    [RelayCommand] private void ToggleFilters() => ShowFilters = !ShowFilters;

    [RelayCommand]
    private void ClearFilters()
    {
        (FilterPlace, FilterBornFrom, FilterBornTo, FilterDiedFrom, FilterDiedTo, FilterLivedFrom, FilterLivedTo) = ("", "", "", "", "", "", "");
        FilterSexIndex = 0;
    }

    private void ApplyFilter()
    {
        var words = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<PersonRow> rows = _allRows;
        if (words.Length == 0) foreach (var r in _allRows) r.FoundInNotes = false;
        if (words.Length > 0)
        {
            // Ranked: exact matches first, then (with "Sounds like") close spellings.
            var ranked = SearchIds(SearchText).ToList();
            var byId = _allRows.ToDictionary(r => r.Id);
            // Then anyone whose notes or event descriptions contain the words typed.
            var found = ranked.ToHashSet();
            var mentioned = _allRows.Where(r => !found.Contains(r.Id) && _tree.Person(r.Id) is { } p
                                                && PersonFilter.Mentions(_tree, p, SearchText)).Select(r => r.Id).ToHashSet();
            foreach (var r in _allRows) r.FoundInNotes = mentioned.Contains(r.Id);
            rows = ranked.Concat(mentioned).Where(byId.ContainsKey).Select(id => byId[id]);
        }
        if (_listFilter is { } only) rows = rows.Where(r => only.Contains(r.Id));
        var filter = CurrentFilter;
        if (!filter.IsEmpty) rows = rows.Where(r => _tree.Person(r.Id) is { } p && filter.Matches(_tree, p));
        People.Clear();
        foreach (var r in rows) People.Add(r);
        PeopleCountText = People.Count == _allRows.Count
            ? $"{_allRows.Count:N0} {(_allRows.Count == 1 ? "PERSON" : "PEOPLE")}"
            : $"{People.Count:N0} of {_allRows.Count:N0}";
        if (_current != null) SyncSelection(_current.Id);
    }

    [RelayCommand]
    private void Navigate(long id)
    {
        var from = Section;   // so Back returns to Home or the list this was opened from
        if (Section != AppSection.Charts) Section = AppSection.People;   // in Charts, clicking someone centres them
        Show(id, recordHistory: true, from);
    }

    /// <summary>Opens a person's details page (double-click in a chart).</summary>
    [RelayCommand]
    private void OpenPerson(long id) { Navigate(id); View = PersonView.Details; }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        _forward.Push(new Spot(Section, _current?.Id));
        GoTo(_back.Pop());
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Forward()
    {
        _back.Push(new Spot(Section, _current?.Id));
        GoTo(_forward.Pop());
    }

    private void GoTo(Spot spot)
    {
        if (spot.PersonId is { } id) Show(id, recordHistory: false);
        Section = spot.Section;
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
    }

    private bool CanGoBack() => _back.Count > 0;
    private bool CanGoForward() => _forward.Count > 0;

    private void Show(long id, bool recordHistory, AppSection? from = null)
    {
        var p = _tree.Person(id);
        if (p == null) return;
        var section = from ?? Section;
        if (recordHistory && (_current != null || section != AppSection.People)
            && (_current?.Id != id || section != AppSection.People))
        {
            _back.Push(new Spot(section, _current?.Id));
            _forward.Clear();
        }
        _current = p;
        HasCurrent = true;
        CurrentName = p.DisplayName.Length > 0 ? p.DisplayName : "(no name)";
        CurrentRelation = RelationToHome(p);
        CurrentInitials = Display.Initials(p);
        CurrentIsMale = p.Sex == Sex.Male;
        CurrentIsFemale = p.Sex == Sex.Female;
        CurrentFamilySearchId = p.FamilySearchId;
        var b = p.Birth;
        var d = p.Death;
        var ageAtDeath = AgeAt(p, d?.Date);
        CurrentDetail = Display.Join(
            b != null && !b.Date.IsEmpty ? $"Born {Display.Date(b.Date)}" : "",
            d != null && !d.Date.IsEmpty ? $"Died {Display.Date(d.Date)}" : d != null ? "Deceased" : "",
            ageAtDeath.Length > 0 ? $"aged {ageAtDeath}" : "");
        CurrentNote = p.Note.Trim();
        HasNote = CurrentNote.Length > 0;

        Chips.Clear();
        if (p.Title.Length > 0) Chips.Add(p.Title);
        foreach (var t in p.Tags) Chips.Add(t);
        foreach (var n in p.Names.Skip(1).Where(n => n.Full.Length > 0)) Chips.Add($"{(n.Type == "married" ? "Married name" : "Also known as")} {n.Full}");
        foreach (var test in p.DnaTests)
            Chips.Add(Display.Join(test.Kind.Length > 0 ? $"{test.Kind} DNA".Replace("DNA DNA", "DNA") : "DNA test", test.Haplogroup).Replace("  ·  ", " "));
        Chips.Add($"ID {p.Id}");

        RebuildTimeline(p);

        Parents.Clear();
        MissingParents.Clear();
        var parentFamily = _tree.Family(p.ParentFamilyIds.FirstOrDefault());
        if (parentFamily != null)
            foreach (var pid in parentFamily.PartnerIds)
                if (_tree.Person(pid) is { } parent)
                    Parents.Add(Card(parent, parent.Sex switch { Sex.Male => "Father", Sex.Female => "Mother", _ => "Parent" }));
        if (Parents.Count == 0) MissingParents.Add(new AddSlot("Add parents"));

        Families.Clear();
        foreach (var fid in p.SpouseFamilyIds)
        {
            if (_tree.Family(fid) is not { } f) continue;
            var spouse = _tree.SpouseIn(f, p);
            var marr = f.Marriage;
            var summary = Display.Join(
                marr != null && !marr.Date.IsEmpty ? $"Married {Display.Date(marr.Date)}" : marr != null ? "Married" : "",
                marr != null ? Display.FirstPart(marr.Place) : "",
                f.Status switch { CoupleStatus.Divorced => "Divorced", CoupleStatus.Unmarried => "Not married", _ => "" },
                f.Plural ? "Plural marriage" : "");
            Families.Add(new FamilyCard
            {
                FamilyId = f.Id,
                Spouse = spouse != null ? Card(spouse, spouse.Sex switch { Sex.Male => "Husband", Sex.Female => "Wife", _ => "Spouse" }) : null,
                Summary = summary.Length > 0 ? summary : "Add marriage details",
                Children = _tree.ChildrenOf(f).Select(c => Card(c)).ToList(),
            });
        }
        HasFamilies = Families.Count > 0;

        ShowPhotos(p);
        ShowAssociates(p);
        RebuildPlaces(p);
        RebuildPersonResearch(p);
        RebuildPersonHints(p);
        Chart = new ChartData(_tree, p.Id, _tree.Ancestors(p, ChartGenerations));
        if (IsMapView) RebuildPersonMap();
        if (IsTimelineView) RebuildTimeline();

        SyncSelection(id);
        RefreshBookmarkState();
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
        NotifyPersonCommands();
    }

    private void SyncSelection(long id)
    {
        if (SelectedRow?.Id == id) return;
        _syncingSelection = true;
        SelectedRow = People.FirstOrDefault(r => r.Id == id);
        _syncingSelection = false;
    }

    /// <summary>The person's own events plus those of their marriages, in date order.</summary>
    private void RebuildTimeline(Person p)
    {
        Events.Clear();
        var items = p.Events.Select(e => (e, owner: (Entity)p, suffix: ""))
            .Concat(p.SpouseFamilyIds.Select(id => _tree.Family(id)).OfType<Family>().SelectMany(f =>
                f.Events.Select(e => (e, owner: (Entity)f,
                    suffix: _tree.SpouseIn(f, p) is { } s ? $" — {s.DisplayName}" : ""))));
        foreach (var (e, owner, suffix) in items.OrderBy(x => x.e.Date.SortKey == 0 ? int.MaxValue : x.e.Date.SortKey))
            Events.Add(ToRow(p, e, owner, suffix) with { Proof = Lineage.Core.Analysis.Evidence.Assess(_tree, e, owner) });
        HasEvents = Events.Count > 0;
        ProofTally = EvidenceTally(Events.Select(r => r.Proof!.Level).ToList());
    }

    private void ClearCurrent()
    {
        _current = null;
        HasCurrent = false;
        CurrentName = CurrentDetail = CurrentNote = CurrentInitials = CurrentFamilySearchId = "";
        HasNote = HasEvents = HasFamilies = HasPlaces = false;
        ProfilePhoto = null;
        Chart = null;
        Events.Clear(); Parents.Clear(); MissingParents.Clear(); Families.Clear(); Chips.Clear();
        Photos.Clear(); Places.Clear(); TimelineRows.Clear(); CensusRows.Clear(); PersonHints.Clear();
        NotifyPersonCommands();
    }

    /// <summary>Whole years between the person's birth and a date, or "" when either year is unknown.</summary>
    internal static string AgeAt(Person p, GenDate? date)
    {
        var born = p.Birth?.Date;
        if (born == null || date == null || born.Year == 0 || date.Year == 0 || date.Kind == DateKind.Text) return "";
        var age = date.Year - born.Year;
        if (born.Month > 0 && date.Month > 0 && (date.Month < born.Month || (date.Month == born.Month && date.Day > 0 && date.Day < born.Day)))
            age--;
        return age is > 0 and < 130 ? age.ToString() : "";
    }

    private static EventRow ToRow(Person p, Event e, Entity owner, string suffix)
    {
        var detail = new List<string>();
        if (e.Value.Length > 0 && e.Value != "Y") detail.Add(e.Value);
        if (e.Status.Length > 0) detail.Add(e.Status);
        if (e.Temple.Length > 0) detail.Add("Temple " + e.Temple);
        if (e.Cause.Length > 0) detail.Add("Cause: " + e.Cause);
        if (e.Citations.Count > 0) detail.Add($"{e.Citations.Count} source{(e.Citations.Count > 1 ? "s" : "")}");
        var place = e.Place2.Length > 0 ? $"{e.Place} → {e.Place2}" : e.Place;
        var age = ReferenceEquals(e, p.Birth) ? "" : AgeAt(p, e.Date);
        return new EventRow(EventTypes.Label(e) + suffix, e.Date.Year > 0 ? e.Date.Year.ToString() : "",
                            Display.Date(e.Date), age.Length > 0 ? $"age {age}" : "", place, string.Join("  ·  ", detail),
                            e.Location != null, e, owner);
    }

    /// <summary>Opens a file or web page with the system's default app. Replaceable so tests never launch anything.</summary>
    internal static Action<string> Launcher { get; set; } = target =>
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { /* no handler for that file type */ }
    };

    internal static void OpenExternal(string target) => Launcher(target);
}
