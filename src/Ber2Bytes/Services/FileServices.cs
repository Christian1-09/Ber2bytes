using System.Diagnostics;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ber2Bytes.Data;
using Ber2Bytes.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace Ber2Bytes.Services;

/// <summary>Saves product photos as small JPEG files.</summary>
public class ImageService
{
    public const long MaxUploadBytes = 8 * 1024 * 1024;

    /// <summary>URL the WebView uses to show a product photo (null when there is none).</summary>
    public static string? Url(string? fileName) =>
        string.IsNullOrEmpty(fileName) ? null : $"https://{AppPaths.ImageHost}/{fileName}";

    /// <summary>Resizes the photo to max 500px, puts it on a white background, saves as JPEG and returns the file name.</summary>
    public string SaveProductImage(byte[] data)
    {
        // WPF imaging must run on the UI (STA) thread.
        return Application.Current.Dispatcher.Invoke(() =>
        {
            using var input = new MemoryStream(data);
            var frame = BitmapFrame.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            const int max = 500;
            double scale = Math.Min(1.0, (double)max / Math.Max(frame.PixelWidth, frame.PixelHeight));
            int w = Math.Max(1, (int)Math.Round(frame.PixelWidth * scale));
            int h = Math.Max(1, (int)Math.Round(frame.PixelHeight * scale));

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                dc.DrawImage(frame, new Rect(0, 0, w, h));
            }
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);

            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            var name = $"{Guid.NewGuid():N}.jpg";
            using var output = File.Create(Path.Combine(AppPaths.ImagesFolder, name));
            encoder.Save(output);
            return name;
        });
    }

    public void Delete(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        try
        {
            var path = Path.Combine(AppPaths.ImagesFolder, Path.GetFileName(fileName));
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) { AppLog.Error("Could not delete image " + fileName, ex); }
    }
}

/// <summary>Windows Save/Open dialogs and opening files with their default program.</summary>
public class FileDialogService
{
    public string? AskSavePath(string defaultFileName, string filter, string? initialFolder = null)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dlg = new SaveFileDialog
            {
                FileName = defaultFileName,
                Filter = filter,
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = initialFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            return dlg.ShowDialog(Application.Current.MainWindow) == true ? dlg.FileName : null;
        });
    }

    public string? AskOpenPath(string filter)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dlg = new OpenFileDialog { Filter = filter, CheckFileExists = true };
            return dlg.ShowDialog(Application.Current.MainWindow) == true ? dlg.FileName : null;
        });
    }

    public void OpenWithDefaultApp(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Error("Could not open " + path, ex); }
    }

    public void ShowInFolder(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch (Exception ex) { AppLog.Error("Could not open folder " + path, ex); }
    }
}

/// <summary>Backup = a .zip with the database and the product photos.</summary>
public class BackupService(IDbContextFactory<AppDbContext> dbf, SettingsService settings)
{
    private const string DbEntryName = "ber2bytes.db";

    /// <summary>Creates a backup zip at <paramref name="zipPath"/>.</summary>
    public void CreateBackup(string zipPath)
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"b2b-backup-{Guid.NewGuid():N}.db");
        try
        {
            // SQLite's online backup API makes a consistent copy even while the app is running.
            using (var source = new SqliteConnection($"Data Source={AppPaths.DatabaseFile}"))
            using (var target = new SqliteConnection($"Data Source={tempDb};Pooling=False"))
            {
                source.Open();
                target.Open();
                source.BackupDatabase(target);
            }

            if (File.Exists(zipPath)) File.Delete(zipPath);
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(tempDb, DbEntryName);
            foreach (var img in Directory.GetFiles(AppPaths.ImagesFolder))
                zip.CreateEntryFromFile(img, "images/" + Path.GetFileName(img));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(tempDb)) File.Delete(tempDb);
        }
    }

    /// <summary>Checks that the zip is a Ber2Bytes backup. Returns an error message or null.</summary>
    public string? Validate(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            return zip.GetEntry(DbEntryName) == null ? "This file is not a Ber2Bytes backup." : null;
        }
        catch
        {
            return "This file is not a valid .zip backup.";
        }
    }

    /// <summary>
    /// Replaces the current database and photos with the backup, then restarts the app.
    /// A safety copy of the current data is saved first in the auto-backups folder.
    /// </summary>
    public void RestoreAndRestart(string zipPath)
    {
        CreateBackup(Path.Combine(AppPaths.AutoBackupFolder, $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.zip"));
        SqliteConnection.ClearAllPools();

        var extractDir = Path.Combine(Path.GetTempPath(), $"b2b-restore-{Guid.NewGuid():N}");
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        foreach (var f in new[] { AppPaths.DatabaseFile, AppPaths.DatabaseFile + "-wal", AppPaths.DatabaseFile + "-shm" })
            if (File.Exists(f)) File.Delete(f);
        File.Copy(Path.Combine(extractDir, DbEntryName), AppPaths.DatabaseFile, true);

        foreach (var img in Directory.GetFiles(AppPaths.ImagesFolder)) File.Delete(img);
        var imgDir = Path.Combine(extractDir, "images");
        if (Directory.Exists(imgDir))
            foreach (var img in Directory.GetFiles(imgDir))
                File.Copy(img, Path.Combine(AppPaths.ImagesFolder, Path.GetFileName(img)), true);

        Directory.Delete(extractDir, true);
        settings.ClearCache();

        var exe = Environment.ProcessPath;
        if (exe != null) Process.Start(exe);
        Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
    }

    /// <summary>Automatic daily backup (keeps the last 14). Called when the app closes.</summary>
    public void AutoBackup()
    {
        try
        {
            if (!settings.Current.AutoBackupOnExit) return;
            var path = Path.Combine(AppPaths.AutoBackupFolder, $"auto-{DateTime.Now:yyyy-MM-dd}.zip");
            CreateBackup(path);
            foreach (var old in Directory.GetFiles(AppPaths.AutoBackupFolder, "auto-*.zip")
                         .OrderByDescending(f => f).Skip(14))
                File.Delete(old);
        }
        catch (Exception ex) { AppLog.Error("Auto backup failed", ex); }
    }

    /// <summary>Removes all sales, returns, stock movements and the activity log (keeps products, users, settings).</summary>
    public async Task ClearTransactionsAsync(int userId, string userName)
    {
        await using var db = await dbf.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Returns.ExecuteDeleteAsync();
        await db.SaleItems.ExecuteDeleteAsync();
        await db.Sales.ExecuteDeleteAsync();
        await db.StockMovements.ExecuteDeleteAsync();
        await db.AuditLogs.ExecuteDeleteAsync();
        AuditService.Add(db, userId, userName, "Start fresh", "All transactions were cleared");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
