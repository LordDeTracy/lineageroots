namespace Lineage.App.Services;

/// <summary>
/// Portable mode, for running from a USB stick: when a "Lineage Roots data" folder sits beside the program, settings,
/// family files made from imports, plugins and backups are kept there instead of on the computer, so nothing is left
/// behind and the same setup goes wherever the stick does.
/// </summary>
public static class Portable
{
    public const string FolderName = "Lineage Roots data";

    /// <summary>The program's own folder (the folder of the single-file exe).</summary>
    public static string ProgramFolder => AppContext.BaseDirectory;

    public static string DataFolder => Path.Combine(ProgramFolder, FolderName);

    public static bool IsOn => Directory.Exists(DataFolder);

    /// <summary>Points settings, copies and backups at the portable folder when it's there. Called first thing at start-up.</summary>
    public static void Apply()
    {
        if (!IsOn) return;
        AppSettings.FilePath = Path.Combine(DataFolder, "settings.json");
        AppSettings.CopiesFolder = Path.Combine(DataFolder, "Family files");
        Backups.Root = DataFolder;
    }

    /// <summary>
    /// Turns portable mode on (making the folder and taking the current settings along) or off (removing the folder only
    /// when it holds nothing but settings). Takes effect when the program next starts. Returns a sentence for the user.
    /// </summary>
    public static string Switch(bool on)
    {
        if (on)
        {
            Directory.CreateDirectory(DataFolder);
            var settings = Path.Combine(DataFolder, "settings.json");
            if (!File.Exists(settings) && File.Exists(AppSettings.FilePath)) File.Copy(AppSettings.FilePath, settings);
            return $"Portable mode is on: from the next start, settings, backups and imported family files are kept in \"{FolderName}\" beside the program.";
        }
        if (!Directory.Exists(DataFolder)) return "Portable mode is off.";
        var others = Directory.EnumerateFileSystemEntries(DataFolder).Where(f => !f.EndsWith("settings.json", StringComparison.OrdinalIgnoreCase)).ToList();
        if (others.Count == 0) { Directory.Delete(DataFolder, true); return "Portable mode is off from the next start."; }
        Directory.Move(DataFolder, DataFolder + " (not in use)");
        return $"Portable mode is off from the next start. Your files are kept in \"{FolderName} (not in use)\" beside the program.";
    }
}
