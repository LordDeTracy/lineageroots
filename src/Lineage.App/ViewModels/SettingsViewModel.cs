using CommunityToolkit.Mvvm.ComponentModel;
using Lineage.App.Services;

namespace Lineage.App.ViewModels;

/// <summary>The Settings window: safety copies, appearance and maps.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public static readonly int[] AutoSaveMinutes = { 0, 2, 5, 10, 15 };
    public static readonly int[] BackupCounts = { 0, 5, 10, 20, 50 };
    public static readonly string[] Themes = { "System", "Light", "Dark" };

    public SettingsViewModel(AppSettings s, IReadOnlyList<PluginRow>? plugins = null, IReadOnlyList<string>? pluginProblems = null)
    {
        Plugins = plugins ?? Array.Empty<PluginRow>();
        PluginProblems = string.Join(Environment.NewLine, pluginProblems ?? Array.Empty<string>());
        _autoSaveIndex = Math.Max(0, Array.IndexOf(AutoSaveMinutes, s.AutoSaveMinutes));
        _backupIndex = Math.Max(0, Array.IndexOf(BackupCounts, s.BackupsToKeep));
        _themeIndex = Math.Max(0, Array.IndexOf(Themes, s.Theme));
        _onlineMaps = s.OnlineMaps;
        _saveAutomatically = s.SaveAutomatically;
        _checkForUpdates = s.CheckForUpdates;
        _advancedMode = s.AdvancedMode;
        _languageIndex = Math.Max(0, Translator.Languages.ToList().FindIndex(l => l.Code == s.Language));
    }

    public string[] AutoSaveOptions { get; } = { "Off", "Every 2 minutes", "Every 5 minutes", "Every 10 minutes", "Every 15 minutes" };
    public string[] BackupOptions { get; } = { "Don't keep", "Last 5 versions", "Last 10 versions", "Last 20 versions", "Last 50 versions" };
    public string[] ThemeOptions => Themes;
    public string BackupFolder => Backups.BackupFolder;

    [ObservableProperty] private int _autoSaveIndex;
    [ObservableProperty] private int _backupIndex;
    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private bool _onlineMaps;
    [ObservableProperty] private bool _saveAutomatically;
    [ObservableProperty] private bool _checkForUpdates;
    [ObservableProperty] private int _languageIndex;
    /// <summary>Keep settings and backups beside the program (for a USB stick).</summary>
    [ObservableProperty] private bool _portableMode = Portable.IsOn;
    [ObservableProperty] private bool _advancedMode;
    public string PortableFolder => Portable.DataFolder;
    public IReadOnlyList<string> LanguageOptions { get; } = Translator.Languages.Select(l => l.Name).ToList();
    /// <summary>Plugins found in the plugins folder, each with its on/off switch.</summary>
    public IReadOnlyList<PluginRow> Plugins { get; }
    public bool HasPlugins => Plugins.Count > 0;
    public string PluginProblems { get; }
    public bool HasPluginProblems => PluginProblems.Length > 0;
    public string PluginsFolder => MainViewModel.PluginsFolder;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenPluginsFolder()
    {
        try { Directory.CreateDirectory(PluginsFolder); } catch (Exception) { /* opening shows the problem */ }
        MainViewModel.OpenExternal(PluginsFolder);
    }

    public string VersionText => $"You have version {Updater.Current}. Copyright © 2026 Dellingr Dynamics.";

    public void Apply(AppSettings s)
    {
        s.AutoSaveMinutes = AutoSaveMinutes[AutoSaveIndex];
        s.BackupsToKeep = BackupCounts[BackupIndex];
        s.Theme = Themes[ThemeIndex];
        s.OnlineMaps = OnlineMaps;
        s.SaveAutomatically = SaveAutomatically;
        s.CheckForUpdates = CheckForUpdates;
        s.AdvancedMode = AdvancedMode;
        s.Language = Translator.Languages[Math.Clamp(LanguageIndex, 0, Translator.Languages.Count - 1)].Code;
    }
}
