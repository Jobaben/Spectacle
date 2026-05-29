using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace Spectacle.Web;

public partial class WebViewHost : UserControl
{
    public const string VirtualHost = "spectacle.local";

    // The preview document is served from this in-memory path over the stable
    // spectacle.local origin (not NavigateToString, whose opaque about:blank
    // origin wipes sessionStorage on every render). A stable origin lets the
    // keynav layer persist scroll/focus/help/reanchor state across re-renders.
    private const string PreviewPath = "__spectacle_preview__.html";

    private bool _ready;
    private string? _pendingHtml;
    private string? _virtualFolder;
    private string? _currentHtml;
    private int _navVersion; // cache-bust so each render triggers a fresh fetch

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

        // Serve the preview document from memory for the stable-origin URL.
        // The pattern matches only the preview path, so images (served via the
        // folder mapping) are untouched; context All avoids depending on how the
        // runtime classifies the top-level document request.
        Web.CoreWebView2.AddWebResourceRequestedFilter(
            $"https://{VirtualHost}/{PreviewPath}*", CoreWebView2WebResourceContext.All);
        Web.CoreWebView2.WebResourceRequested += OnWebResourceRequested;

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
        _currentHtml = html;

        // Keep images resolving against the document folder via the base href.
        if (_virtualFolder is not null)
            Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHost, _virtualFolder, CoreWebView2HostResourceAccessKind.Allow);

        // Navigate to the stable-origin preview URL; the cache-busting query
        // forces a full navigation so init() re-runs with the new content. The
        // origin (scheme+host) is unchanged, so sessionStorage persists.
        Web.CoreWebView2.Navigate(
            $"https://{VirtualHost}/{PreviewPath}?v={++_navVersion}");
    }

    private void OnWebResourceRequested(
        object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (_currentHtml is null) return;
        var bytes = Encoding.UTF8.GetBytes(_currentHtml);
        e.Response = Web.CoreWebView2.Environment.CreateWebResourceResponse(
            new MemoryStream(bytes), 200, "OK",
            "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
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
