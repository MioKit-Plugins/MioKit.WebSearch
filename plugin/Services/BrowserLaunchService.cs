using System.Diagnostics;

namespace WebSearch.Services;

public sealed class BrowserLaunchService(
    BrowserCatalogService catalog,
    WebSearchPlugin plugin)
{
    private readonly Action<ProcessStartInfo> startProcess = info => Process.Start(info);

    internal BrowserLaunchService(BrowserCatalogService catalog, WebSearchPlugin plugin, Action<ProcessStartInfo> startProcess)
        : this(catalog, plugin) => this.startProcess = startProcess;

    internal void OpenLocalFile(string path) => startProcess(new ProcessStartInfo(new Uri(path).AbsoluteUri)
    {
        UseShellExecute = true
    });

    public void Open(string url)
    {
        if (!WebsiteUrlResolver.TryValidateHttpUrl(url, out var uri))
        {
            plugin.Context.Logger.Warning("Blocked non-http(s) URL: {Url}", url);
            return;
        }

        var settings = plugin.Settings;
        var browserId = settings.DefaultBrowserId;
        if (browserId != BrowserIds.SystemDefault)
        {
            var executable = catalog.FindBrowserExecutable(browserId);
            if (!string.IsNullOrWhiteSpace(executable))
            {
                try
                {
                    var startInfo = new ProcessStartInfo(executable)
                    {
                        UseShellExecute = false
                    };
                    startInfo.ArgumentList.Add(uri.AbsoluteUri);
                    startProcess(startInfo);
                    return;
                }
                catch (Exception exception)
                {
                    plugin.Context.Logger.Warning(
                        exception,
                        "Failed to start selected browser {BrowserId}; falling back to system default.",
                        browserId);
                }
            }
            else
            {
                plugin.Context.Logger.Warning(
                    "Selected browser {BrowserId} is unavailable; falling back to system default.",
                    browserId);
            }
        }

        startProcess(new ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
    }
}
