using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;
using WebSearch.Views;
using System.Collections.Concurrent;
using MioKit.Extensions.Extensions;

namespace WebSearch.Services;

public sealed class WebsiteWindowService(
    WebSearchPlugin plugin, BrowserLaunchService browser, WebsiteIconService icons) : IPluginLifecycleComponent
{
    private readonly HashSet<WebsiteWindow> windows = [];
    private readonly ConcurrentDictionary<string, byte> reopening = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> opening = new();
    private volatile bool running;
    public Task StartAsync(CancellationToken cancellationToken = default) { running = true; return Task.CompletedTask; }
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        running = false;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var closing = windows.ToArray();
            foreach (var window in closing) window.ForceClose();
            await Task.WhenAll(closing.Select(w => w.Stopped));
        });
    }
    public async Task OpenAsync(WebsiteNode node, string url)
    {
        var gate = opening.GetOrAdd(node.Id, _ => new(1, 1));
        await gate.WaitAsync();
        try { await OpenWebsiteAsync(node, url); }
        finally { gate.Release(); }
    }
    private async Task OpenWebsiteAsync(WebsiteNode node, string url)
    {
        if (!running) throw new InvalidOperationException("插件尚未运行或已停止。");
        var config = await node.ReadConfigurationAsync();
        var draft = new WebsiteEditorRequest { Id = node.Id, Name = node.Name ?? "", UrlTemplate = node.UrlTemplate,
            QuerySourcePolicy = node.QuerySourcePolicy.ToString(), Configuration = config };
        var error = WebsitePolicy.Validate(draft);
        if (error.Length > 0) throw new InvalidOperationException(error);
        if (config.OpenMode == "External")
        {
            if (config.SourceKind == "LocalFile")
                browser.OpenLocalFile(config.LocalFilePath);
            else browser.Open(url);
            return;
        }
        await ValidateDataDirectoryAsync(node.Id, config);
        await Dispatcher.UIThread.InvokeAsync(() => OpenCoreAsync(node, config, url));
    }
    private async Task OpenCoreAsync(WebsiteNode node, WebsiteConfiguration config, string url)
    {
        if (!running) return;
        if (windows.Any(w => w.NodeId == node.Id && w.RequiresReopen))
            await CloseNodeAsync(node.Id);
        if (!running) return;
        var created = false;
        var dataDirectory = WebsitePolicy.DataDirectory(config, node.Id, plugin.Context.PluginDataPath);
        var environment = windows.Where(w => w.NodeId == node.Id).Select(w => w.Core?.Environment)
            .FirstOrDefault(e => e is not null && e.UserDataFolder.Equals(dataDirectory, StringComparison.OrdinalIgnoreCase));
        var window = WindowManager.ShowUniqueWindow<WebsiteWindow>("website-main:" + node.Id, WebSearchConst.PluginId,
            w => { created = true; w.Configure(this, node, config, node.UrlTemplate, environment,
                WebsitePolicy.DataDirectory(config, node.Id, plugin.Context.PluginDataPath)); }, null, null);
        if (created || WebsiteUrlResolver.IsTemplate(node.UrlTemplate))
            await window.NavigateAsync(WebsitePolicy.Entry(config, node.Id, url));
    }
    public WebsiteWindow CreatePopup(WebsiteNode node, WebsiteConfiguration config, string onlineUrl,
        CoreWebView2Environment environment, (uint Width, uint Height)? size)
    {
        if (!running) throw new InvalidOperationException("插件已停止。");
        var window = WindowManager.ShowUniqueWindow<WebsiteWindow>("website-popup:" + Guid.NewGuid().ToString("N"),
            WebSearchConst.PluginId, w =>
            {
                w.Configure(this, node, config, onlineUrl, environment,
                    WebsitePolicy.DataDirectory(config, node.Id, plugin.Context.PluginDataPath));
                if (size is { } dimensions)
                {
                    w.Width = Math.Clamp(dimensions.Width, 580, 1600);
                    w.Height = Math.Clamp(dimensions.Height, 400, 1200);
                }
            }, null, null);
        return window;
    }
    public WebsiteCapabilitiesJsService CreateCapabilities(WebsitePageContext context) =>
        (plugin.Container ?? throw new InvalidOperationException("插件尚未就绪。"))
        .Resolve<WebsiteCapabilitiesJsService>(new TypedParameter(typeof(WebsitePageContext), context));
    public void Register(WebsiteWindow window) => windows.Add(window);
    public void Unregister(WebsiteWindow window) => windows.Remove(window);
    public Task ApplyPluginIconAsync(Window window, CancellationToken ct) => window.SetPluginIcon(plugin.Context, ct);
    public ValueTask<IIconLease?> OpenIconAsync(WebsiteNode node, CancellationToken ct) =>
        icons.OpenAsync(node, IconRequest.Default, ct);
    public async Task RefreshIconsAsync(string id) => await Dispatcher.UIThread.InvokeAsync(async () =>
    {
        await Task.WhenAll(windows.Where(w => w.NodeId == id).Select(w => w.RefreshIconAsync()).ToArray());
    });
    public async Task<bool> ConfigurationChangedAsync(string id) => await Dispatcher.UIThread.InvokeAsync(() =>
    {
        var affected = windows.Where(w => w.NodeId == id).ToArray();
        foreach (var window in affected) window.RevokeCapabilities();
        return affected.Length > 0;
    });
    public async Task<bool> RequiresReopenAsync(string id) => await Dispatcher.UIThread.InvokeAsync(() =>
        windows.Any(window => window.NodeId == id && window.RequiresReopen));
    public async Task CloseNodeAsync(string id)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var closing = windows.Where(w => w.NodeId == id).ToArray();
            foreach (var w in closing) w.ForceClose();
            await Task.WhenAll(closing.Select(w => w.Stopped));
        });
    }
    public async Task ReopenAsync(WebsiteNode node)
    {
        if (!reopening.TryAdd(node.Id, 0)) return;
        var gate = opening.GetOrAdd(node.Id, _ => new(1, 1));
        await gate.WaitAsync();
        try { await CloseNodeAsync(node.Id); await OpenWebsiteAsync(node, WebsiteUrlResolver.ResolveForHotKey(node.UrlTemplate, null)); }
        finally { gate.Release(); reopening.TryRemove(node.Id, out _); }
    }
    public void OpenExternal(string url)
    {
        if (WebsiteUrlResolver.TryValidateHttpUrl(url, out _)) browser.Open(url);
    }
    public void OpenSettings(string id)
    {
        plugin.OpenPluginMainWindow();
        WebSearchRegister.ComponentContextInstance.Resolve<WebSearchJsService>().RequestedWebsiteId = id;
    }
    public void Log(Exception exception, string message) => plugin.Context.Logger.Warning(exception, message);
    public void LogMessage(string id, string message) => plugin.Context.Logger.Debug("Website {WebsiteId}: {Message}", id, message);

    public async Task ValidateDataDirectoryAsync(string id, WebsiteConfiguration config)
    {
        if (config.OpenMode == "External") return;
        var path = WebsitePolicy.DataDirectory(config, id, plugin.Context.PluginDataPath);
        var appData = MioAppContext.Current.Environment.AppDataDirectory;
        WebsiteDataDirectoryPolicy.ValidateCustomLocation(config.UserDataFolder, plugin.Context.PluginPath, plugin.Context.PluginDataPath,
            appData, MioAppContext.Current.Environment.JavaScriptRuntimeDirectory);
        // A window keeps its original profile until it closes, even after its node changes mode or directory.
        var activePaths = await Dispatcher.UIThread.InvokeAsync(() => windows.Where(w => w.NodeId != id)
            .Select(w => w.DataDirectory).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        foreach (var activePath in activePaths)
            WebsiteDataDirectoryPolicy.ValidateIndependent(path, activePath);
        foreach (var other in plugin.WebsiteGroup.Children.OfType<WebsiteNode>().Where(n => n.Id != id))
        {
            var otherConfig = await other.ReadConfigurationAsync();
            if (otherConfig.OpenMode == "External") continue;
            var otherPath = WebsitePolicy.DataDirectory(otherConfig, other.Id, plugin.Context.PluginDataPath);
            WebsiteDataDirectoryPolicy.ValidateIndependent(path, otherPath);
        }
    }
}

