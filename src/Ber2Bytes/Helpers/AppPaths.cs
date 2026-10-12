namespace Ber2Bytes.Helpers;

/// <summary>Where the app keeps its data on this computer.</summary>
public static class AppPaths
{
    /// <summary>%LocalAppData%\Ber2Bytes</summary>
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ber2Bytes");

    public static string DatabaseFile => Path.Combine(DataFolder, "ber2bytes.db");
    public static string ImagesFolder => Path.Combine(DataFolder, "images");
    public static string LogFile => Path.Combine(DataFolder, "error-log.txt");
    public static string PrefsFile => Path.Combine(DataFolder, "prefs.json");
    public static string AutoBackupFolder => Path.Combine(DataFolder, "auto-backups");

    /// <summary>Virtual host name WebView2 uses to serve product photos.</summary>
    public const string ImageHost = "productimages.local";

    public static void EnsureFolders()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(ImagesFolder);
        Directory.CreateDirectory(AutoBackupFolder);
    }
}
