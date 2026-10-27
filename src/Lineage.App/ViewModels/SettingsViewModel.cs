using CommunityToolkit.Mvvm.ComponentModel;
using Lineage.App.Services;

namespace Lineage.App.ViewModels;

/// <summary>The Settings window: safety copies, appearance and maps.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public static readonly int[] AutoSaveMinutes = { 0, 2, 5, 10, 15 };
    public static readonly int[] BackupCounts = { 0, 5, 10, 20, 50 };
    public static readonly string[] Themes = { "System", "Light", "Dark" };

    public SettingsViewModel(AppSettings s)
    {
        _autoSaveIndex = Math.Max(0, Array.IndexOf(AutoSaveMinutes, s.AutoSaveMinutes));
        _backupIndex = Math.Max(0, Array.IndexOf(BackupCounts, s.BackupsToKeep));
        _themeIndex = Math.Max(0, Array.IndexOf(Themes, s.Theme));
        _onlineMaps = s.OnlineMaps;
        _checkForUpdates = s.CheckForUpdates;
    }

    public string[] AutoSaveOptions { get; } = { "Off", "Every 2 minutes", "Every 5 minutes", "Every 10 minutes", "Every 15 minutes" };
    public string[] BackupOptions { get; } = { "Don't keep", "Last 5 versions", "Last 10 versions", "Last 20 versions", "Last 50 versions" };
    public string[] ThemeOptions => Themes;
    public string BackupFolder => Backups.BackupFolder;

    [ObservableProperty] private int _autoSaveIndex;
    [ObservableProperty] private int _backupIndex;
    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private bool _onlineMaps;
    [ObservableProperty] private bool _checkForUpdates;
    public string VersionText => $"You have version {Updater.Current}.";

    public void Apply(AppSettings s)
    {
        s.AutoSaveMinutes = AutoSaveMinutes[AutoSaveIndex];
        s.BackupsToKeep = BackupCounts[BackupIndex];
        s.Theme = Themes[ThemeIndex];
        s.OnlineMaps = OnlineMaps;
        s.CheckForUpdates = CheckForUpdates;
    }
}
