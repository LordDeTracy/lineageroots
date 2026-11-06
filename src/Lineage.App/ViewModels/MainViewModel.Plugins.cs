using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Plugins;

namespace Lineage.App.ViewModels;

/// <summary>A plugin as Settings lists it, with its on/off switch.</summary>
public sealed partial class PluginRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public PluginRow(PluginInfo info, bool enabled) { Info = info; _enabled = enabled; }
    public PluginInfo Info { get; }
    public string Name => Info.Plugin.Name;
    public string Description => Info.Plugin.Description;
    public string Kind => Info.Kind;
    public string File => Path.GetFileName(Info.File);
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _enabled;
}

public partial class MainViewModel
{
    /// <summary>Documents\Lineage Roots\Plugins: drop a plugin's .dll here and switch it on in Settings.</summary>
    public static string PluginsFolder => Path.Combine(Services.AppSettings.CopiesFolder, "Plugins");

    private IReadOnlyList<PluginInfo> _plugins = Array.Empty<PluginInfo>();
    public IReadOnlyList<string> PluginProblems { get; private set; } = Array.Empty<string>();

    /// <summary>Report and chart plugins that are switched on, for the Reports tab.</summary>
    public ObservableCollection<PluginRow> PluginReports { get; } = new();
    /// <summary>Tool plugins that are switched on, for the ⋯ menu.</summary>
    public ObservableCollection<PluginRow> PluginTools { get; } = new();
    public bool HasPluginReports => PluginReports.Count > 0;
    public bool HasPluginTools => PluginTools.Count > 0;

    /// <summary>Looks in the plugins folder again (at start-up, and after Settings changes).</summary>
    public void LoadPlugins()
    {
        try { Directory.CreateDirectory(PluginsFolder); } catch (Exception) { /* read-only Documents: no plugins */ }
        _plugins = PluginHost.Find(PluginsFolder, out var problems);
        PluginProblems = problems;
        RefreshPluginLists();
    }

    /// <summary>For tests: use these plugins, switched on, without looking in the folder or saving settings.</summary>
    internal void UsePlugins(IReadOnlyList<PluginInfo> plugins)
    {
        _plugins = plugins;
        _settings.EnabledPlugins = plugins.Select(p => p.Key).ToList();
        RefreshPluginLists();
    }

    public IReadOnlyList<PluginRow> PluginRows() =>
        _plugins.Select(p => new PluginRow(p, _settings.EnabledPlugins.Contains(p.Key))).ToList();

    public void SetPluginsEnabled(IEnumerable<PluginRow> rows)
    {
        _settings.EnabledPlugins = rows.Where(r => r.Enabled).Select(r => r.Info.Key).ToList();
        _settings.Save();
        RefreshPluginLists();
    }

    private void RefreshPluginLists()
    {
        PluginReports.Clear();
        PluginTools.Clear();
        foreach (var p in _plugins.Where(p => _settings.EnabledPlugins.Contains(p.Key)))
            (p.Plugin is IToolPlugin ? PluginTools : p.Plugin is IReportPlugin or IChartPlugin ? PluginReports : null)?.Add(new PluginRow(p, true));
        OnPropertyChanged(nameof(HasPluginReports));
        OnPropertyChanged(nameof(HasPluginTools));
    }

    private IEnumerable<T> EnabledPluginsOf<T>() where T : IPlugin =>
        _plugins.Where(p => _settings.EnabledPlugins.Contains(p.Key)).Select(p => p.Plugin).OfType<T>();

    private IImporterPlugin? ImporterFor(string path) =>
        EnabledPluginsOf<IImporterPlugin>().FirstOrDefault(i => i.Patterns.Any(pattern =>
            Path.GetFileName(path).EndsWith(pattern.TrimStart('*'), StringComparison.OrdinalIgnoreCase)));

    /// <summary>Runs a plugin from the Reports tab or the ⋯ menu.</summary>
    [RelayCommand]
    private async Task RunPluginAsync(PluginRow? row)
    {
        if (row == null || !HasTree) return;
        try
        {
            switch (row.Info.Plugin)
            {
                case IReportPlugin report:
                    await SaveDocumentAsync(row.Name, () => report.Build(_tree, _current));
                    break;
                case IChartPlugin chart when _current != null:
                    var path = await _dialogs.SaveFileAsync($"Save {row.Name}", SafeName($"{row.Name} - {_current.DisplayName}") + ".svg", ("SVG picture", "*.svg"));
                    if (path == null) return;
                    await File.WriteAllTextAsync(path, chart.DrawSvg(_tree, _current));
                    Status = $"Saved {Path.GetFileName(path)}";
                    OpenExternal(path);
                    break;
                case IToolPlugin tool:
                    var message = "";
                    Change(row.Name, _tree.People.Values, () => message = tool.Run(_tree, _current));
                    Status = message;
                    break;
            }
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync($"The plugin \"{row.Name}\" ran into a problem", ex.Message);
        }
    }
}
