using System.Diagnostics;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace Spectacle.Web;

public partial class WebViewHost : UserControl
{
    public const string VirtualHost = "spectacle.local";
    private bool _ready;
    private string? _pendingHtml;
    private string? _virtualFolder;

    public event EventHandler<string>? HostMessageReceived;

    public WebViewHost()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await Web.EnsureCoreWebView2Async();
        Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
        Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        Web.CoreWebView2.NewWindowRequested += OnNewWindow;
        Web.CoreWebView2.NavigationStarting += OnNavStarting;
        Web.CoreWebView2.WebMessageReceived += (_, e) =>
        {
            var json = e.TryGetWebMessageAsString();
            if (!string.IsNullOrEmpty(json))
                HostMessageReceived?.Invoke(this, json);
        };
        _ready = true;
        if (_pendingHtml is not null) DoSetHtml(_pendingHtml);
    }

    public void SetVirtualFolder(string absolutePath)
    {
        _virtualFolder = absolutePath;
        if (_ready)
            Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHost, absolutePath, CoreWebView2HostResourceAccessKind.Allow);
    }

    public void SetHtml(string html)
    {
        if (!_ready) { _pendingHtml = html; return; }
        DoSetHtml(html);
    }

    public void SetZoom(double factor) => Web.ZoomFactor = factor;

    public void Reload() => Web.Reload();

    private void DoSetHtml(string html)
    {
        if (_virtualFolder is not null)
            Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHost, _virtualFolder, CoreWebView2HostResourceAccessKind.Allow);
        Web.NavigateToString(html);
    }

    private void OnNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenInBrowser(e.Uri);
    }

    private void OnNavStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var current = Web.Source?.ToString() ?? $"https://{VirtualHost}/";
        var decision = LinkInterceptor.Decide(current, e.Uri);
        if (decision == NavDecision.OpenInBrowser)
        {
            e.Cancel = true;
            OpenInBrowser(e.Uri);
        }
    }

    private static void OpenInBrowser(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch { /* user cancelled or no handler */ }
    }
}
