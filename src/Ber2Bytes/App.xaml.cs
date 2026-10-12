using System.Windows;
using System.Windows.Threading;
using Ber2Bytes.Data;
using Ber2Bytes.Helpers;
using Ber2Bytes.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ber2Bytes;

public partial class App : Application
{
    /// <summary>The app-wide service container (used by MainWindow to give services to Blazor).</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("Unhandled (domain)", args.ExceptionObject as Exception);

        // QuestPDF is free for small businesses under the Community license.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        AppPaths.EnsureFolders();

        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools(); // F12 opens dev tools in Debug builds
#endif
        services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite($"Data Source={AppPaths.DatabaseFile}"));

        // All services are singletons: a desktop app has one window and one signed-in user at a time.
        // Each service creates a short-lived DbContext per operation through the factory.
        services.AddSingleton<AppState>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<HotkeyService>();
        services.AddSingleton<ConfirmService>();
        services.AddSingleton<LocalPrefs>();
        services.AddSingleton<AuditService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<ImageService>();
        services.AddSingleton<CatalogService>();
        services.AddSingleton<InventoryService>();
        services.AddSingleton<PosCart>();
        services.AddSingleton<SalesService>();
        services.AddSingleton<ReturnService>();
        services.AddSingleton<ReportService>();
        services.AddSingleton<DashboardService>();
        services.AddSingleton<AlertService>();
        services.AddSingleton<UserService>();
        services.AddSingleton<ExportService>();
        services.AddSingleton<FileDialogService>();
        services.AddSingleton<BackupService>();

        Services = services.BuildServiceProvider();

        try
        {
            DatabaseInitializer.Initialize(Services.GetRequiredService<IDbContextFactory<AppDbContext>>());
        }
        catch (Exception ex)
        {
            AppLog.Error("Database initialization failed", ex);
            MessageBox.Show("The database could not be opened.\n\n" + ex.Message + $"\n\nDatabase file: {AppPaths.DatabaseFile}",
                "Ber2Bytes", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled (UI)", e.Exception);
        MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message + $"\n\nDetails were saved to:\n{AppPaths.LogFile}",
            "Ber2Bytes", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true; // keep the app running
    }
}
