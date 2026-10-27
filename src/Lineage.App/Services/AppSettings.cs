using System.Text.Json;

namespace Lineage.App.Services;

/// <summary>Per-user settings, kept in %AppData%\LineageRoots\settings.json. No passwords or tokens are stored.</summary>
public sealed class AppSettings
{
    public string FamilySearchAppKey { get; set; } = "";
    /// <summary>Use FamilySearch's integration (test) environment, for app keys not yet approved for production.</summary>
    public bool FamilySearchIntegration { get; set; }
    /// <summary>Local port for the sign-in redirect; the redirect URI registered with FamilySearch must match.</summary>
    public int FamilySearchPort { get; set; } = 52817;

    public string FamilySearchRedirectUri => $"http://127.0.0.1:{FamilySearchPort}/familysearch/";

    /// <summary>Minutes between recovery copies while there are unsaved changes; 0 turns it off.</summary>
    public int AutoSaveMinutes { get; set; } = 5;
    /// <summary>How many earlier versions of each tree file to keep in the backups folder; 0 turns it off.</summary>
    public int BackupsToKeep { get; set; } = 10;
    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";
    /// <summary>Whether map views may download map pictures from OpenStreetMap.</summary>
    public bool OnlineMaps { get; set; } = true;
    /// <summary>Whether to ask GitHub for a newer version when the program starts.</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>A release the user chose to skip (its tag, e.g. "v0.3.0"); not offered again at start-up.</summary>
    public string SkippedVersion { get; set; } = "";

    /// <summary>Where settings are kept; tests point this at a temporary file so they never touch the user's own.</summary>
    internal static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LineageRoots", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new() : new();
        }
        catch { return new(); } // a damaged settings file shouldn't stop the app
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
