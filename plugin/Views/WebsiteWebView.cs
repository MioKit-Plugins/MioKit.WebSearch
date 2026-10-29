using Microsoft.Web.WebView2.Core;
using MioKit.Webview2;

namespace WebSearch.Views;

public sealed class WebsiteWebView : MioWebview2
{
    public WebsiteConfiguration Configuration { get; set; } = new();
    public string DataDirectory { get; set; } = "";
    public CoreWebView2Environment? SharedEnvironment { get; set; }
    public Func<CoreWebView2NewWindowRequestedEventArgs, Task>? PopupRequested { get; set; }
    public Action<Exception>? ErrorReported { get; set; }
    protected override string GetUserDataFolder() => DataDirectory;
    protected override Task<CoreWebView2Environment> CreateEnvironmentAsync(string path, CoreWebView2EnvironmentOptions options) =>
        SharedEnvironment is null ? base.CreateEnvironmentAsync(path, options) : Task.FromResult(SharedEnvironment);
    protected override Task OnSourceChanged(Uri? oldValue, Uri? newValue)
    {
        if (newValue is not null) CoreWebView2?.Navigate(newValue.AbsoluteUri);
        return Task.CompletedTask;
    }
    protected override async void HandleNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        IDisposable? deferral = null;
        try
        {
            e.Handled = true;
            if (PopupRequested is null) return;
            deferral = e.GetDeferral();
            await PopupRequested(e);
        }
        catch (Exception ex) { ReportError(ex); }
        finally
        {
            try { deferral?.Dispose(); }
            catch (Exception ex) { ReportError(ex); }
        }
    }
    protected override void HandleWebResourceRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try { HandleResource(e); }
        catch (Exception ex)
        {
            ReportError(ex);
            try
            {
                if (CoreWebView2 is { } core)
                    e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
            }
            catch (Exception responseError) { ReportError(responseError); }
        }
    }
    private void ReportError(Exception exception)
    {
        try
        {
            if (ErrorReported is { } report) report(exception);
            else Serilog.Log.Warning(exception, "Website WebView callback failed.");
        }
        catch { /* Error callbacks must not escape a native WebView event. */ }
    }
    private void HandleResource(CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)) return;
        string? path = null;
        string? root = null;
        if (uri.IsFile)
        {
            root = Configuration.SourceKind == "LocalFile" ? Path.GetDirectoryName(Configuration.LocalFilePath) : null;
            path = uri.LocalPath;
        }
        else
        {
            var mapping = Configuration.Runtime.Mappings.FirstOrDefault(m => m.Enabled && m.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase));
            if (mapping is not null) root = mapping.Directory;
            else if (Configuration.SourceKind == "LocalFile" && Configuration.MapLocalDirectory
                && uri.Host.Equals(AppName, StringComparison.OrdinalIgnoreCase)) root = Path.GetDirectoryName(Configuration.LocalFilePath);
            if (root is not null) path = Path.Combine(root, Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        }
        if (path is null) return;
        if (root is null || !WebsitePolicy.IsWithin(path, root) || !WebsitePolicy.HasNoReparsePoints(path))
        {
            if (CoreWebView2 is { } core)
                e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
            return;
        }
        if (uri.IsFile) base.HandleWebResourceRequest(this, e);
    }
    public string WebsiteHost { get; set; } = "";
    public override string AppName => WebsiteHost;
}

