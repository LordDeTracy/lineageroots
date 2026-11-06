using Avalonia;

namespace Lineage.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Services.Portable.Apply();   // on a USB stick, keep everything beside the program
        // Release check: read the text in a picture into a file, then quit (--ocr in.png out.txt).
        if (args.Length == 3 && args[0] == "--ocr")
        {
            try { File.WriteAllText(args[2], Services.TextRecognition.Read(args[1]).Text); }
            catch (Exception ex) { File.WriteAllText(args[2], "ERROR: " + ex); }
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
