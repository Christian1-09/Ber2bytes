using System.Windows;
using Ber2Bytes.Helpers;
using Ber2Bytes.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Web.WebView2.Core;

namespace Ber2Bytes;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        // Must be added before InitializeComponent so the BlazorWebView can find it.
        Resources.Add("services", App.Services);
        InitializeComponent();
        Closing += (_, _) => App.Services.GetRequiredService<BackupService>().AutoBackup();
    }

    private void OnWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
        var core = e.WebView.CoreWebView2;

        // Product photos are saved as files in %LocalAppData%\Ber2Bytes\images.
        // This maps them to https://productimages.local/<file> so <img> tags can show them.
        core.SetVirtualHostNameToFolderMapping(AppPaths.ImageHost, AppPaths.ImagesFolder,
            CoreWebView2HostResourceAccessKind.Allow);

#if !DEBUG
        // Release build: behave like a desktop app, not a browser.
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
#endif
    }
}
