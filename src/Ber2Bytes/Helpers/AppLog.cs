namespace Ber2Bytes.Helpers;

/// <summary>Very small file logger for errors (no extra packages needed).</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static void Error(string message, Exception? ex = null)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(AppPaths.LogFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
