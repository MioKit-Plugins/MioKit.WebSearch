using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Diagnostics;
using WebSearch.Views;

namespace WebSearch.Services;

public partial class WebSearchJsService
{
    private readonly SemaphoreSlim websiteSaveGate = new(1, 1);
    Task IPluginLifecycleComponent.StartAsync(CancellationToken cancellationToken)
    {
        if (_websiteLifetime.IsCancellationRequested)
        {
            _websiteLifetime.Dispose();
            _websiteLifetime = new();
        }
        return Task.CompletedTask;
    }
    async Task IPluginLifecycleComponent.StopAsync(CancellationToken cancellationToken)
    {
        Task[] pending = [];
        await RunOnUiThreadAsync(() =>
        {
            _websiteLifetime.Cancel();
            pending = _websiteReloadTasks.ToArray();
        });
        if (_initialized)
        {
            MioAppContext.Current.EventBus.UnregisterEventHandle<MioPropertyChangedEventMessage>(this);
            _index.StateChanged -= OnIndexStateChanged;
            _favicon.IconsChanged -= OnFaviconChanged;
            _initialized = false;
        }
        await Task.WhenAll(pending);
    }
    public async Task<WebsiteEditorRequest> GetWebsiteEditor(string id)
    {
        var node = FindWebsite(id) ?? throw new InvalidOperationException("找不到网站。");
        var config = await node.ReadConfigurationAsync();
        var icon = await _icons.GetPreviewAsync(node, _websiteLifetime.Token);
        return new()
        {
            Id = node.Id, Name = node.Name ?? "", Description = node.Description ?? "",
            UrlTemplate = node.UrlTemplate, QuerySourcePolicy = node.QuerySourcePolicy.ToString(),
            Configuration = config,
            RequiresReopen = await _windows.RequiresReopenAsync(node.Id),
            HasCustomIcon = icon.HasCustomIcon, IconDataUrl = icon.IconDataUrl, IconState = icon.IconState,
            EffectiveDataDirectory = config.OpenMode == "Embedded"
                ? WebsitePolicy.DataDirectory(config, node.Id, _plugin.Context.PluginDataPath) : ""
        };
    }
    public async Task<WebsiteSaveResult> SaveWebsite(WebsiteEditorRequest request)
    {
        await websiteSaveGate.WaitAsync();
        try
        {
            request.Id = string.IsNullOrEmpty(request.Id) ? "websearch-site-" + Guid.NewGuid().ToString("N") : request.Id;
            var error = WebsitePolicy.Validate(request);
            if (!string.IsNullOrEmpty(error)) return new(false, request.Id, error);
            WebsiteIconService.ValidateAction(request.IconAction, request.IconSourcePath);
            await _windows.ValidateDataDirectoryAsync(request.Id, request.Configuration);
            var node = FindWebsite(request.Id);
            if (node is null && await _dataProvider.ExistsAsync(request.Id))
                return new(false, request.Id, "网站标识已被占用。");
            var created = node is null;
            node ??= new WebsiteNode(request.Id);
            var previousConfiguration = await node.ReadConfigurationAsync();
            var configurationChanged = created || WebsiteConfigurationChanges.RequiresReopen(
                previousConfiguration, node.UrlTemplate, request.Configuration, request.UrlTemplate ?? "");
            var iconChanged = created || request.IconAction != "Keep"
                || previousConfiguration.SourceKind != request.Configuration.SourceKind
                || node.UrlTemplate != (request.UrlTemplate ?? "").Trim();
            var requiresReopen = false;
            var previousIconId = node.GetCustomIconId();
            await _icons.ApplyChangeAsync(previousIconId, request.IconAction, request.IconSourcePath, async nextIconId =>
            {
                if (!created && configurationChanged)
                    requiresReopen = await _windows.ConfigurationChangedAsync(node.Id);
                try
                {
                    // A rejected insert must not publish a node or enqueue its property writes.
                    if (created && !await _dataProvider.StoreAsync(node))
                        throw new InvalidOperationException("网站保存失败，请重试。");
                    await RunOnUiThreadAsync(() =>
                    {
                        var name = request.Name.Trim();
                        var description = (request.Description ?? "").Trim();
                        var url = (request.UrlTemplate ?? "").Trim();
                        var queryPolicy = Enum.Parse<QuerySourcePolicy>(request.QuerySourcePolicy);
                        if (node.Name != name) node.Name = name;
                        if (node.Description != description) node.Description = description;
                        if (node.UrlTemplate != url) node.UrlTemplate = url;
                        if (node.QuerySourcePolicy != queryPolicy) node.QuerySourcePolicy = queryPolicy;
                        if (configurationChanged) node.WriteConfiguration(request.Configuration);
                        if (previousIconId != nextIconId) node.SetCustomIconId(nextIconId);
                    });
                    if (created)
                    {
                        await RunOnUiThreadAsync(() => node.SetParent(_plugin.WebsiteGroup));
                    }
                }
                catch
                {
                    await RunOnUiThreadAsync(() =>
                    {
                        if (node.GetCustomIconId() != previousIconId) node.SetCustomIconId(previousIconId);
                    });
                    throw;
                }
            }, _websiteLifetime.Token);
            await RunOnUiThreadAsync(() => QueueWebsiteRefresh(node.Id, iconChanged));
            if (iconChanged)
            {
                node.InvalidateIcon();
                await _windows.RefreshIconsAsync(node.Id);
            }
            if (iconChanged && request.Configuration.SourceKind == "Online" && string.IsNullOrEmpty(node.GetCustomIconId()))
                _favicon.RequestRefresh(node);
            return new(true, node.Id, "", requiresReopen);
        }
        catch (Exception ex)
        {
            _plugin.Context.Logger.Warning(ex, "Failed to save website configuration.");
            return new(false, request.Id, ex.Message);
        }
        finally { websiteSaveGate.Release(); }
    }
    public async Task<WebsiteIconPickResult?> PickWebsiteIcon()
    {
        var path = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var owner = WindowManager.ShowUniqueWindow<PluginWebView>(WebSearchConst.PluginId);
            var files = await owner.StorageProvider.OpenFilePickerAsync(new()
            {
                Title = "选择网站图标", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("图片（不超过 5 MB）")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.ico"]
                }]
            });
            return files.FirstOrDefault()?.TryGetLocalPath();
        });
        return string.IsNullOrEmpty(path) ? null : await _icons.PreviewFileAsync(path, _websiteLifetime.Token);
    }
    public async Task<string> OpenWebsite(string id)
    {
        try
        {
            var node = FindWebsite(id) ?? throw new InvalidOperationException("找不到网站。");
            await _windows.OpenAsync(node, WebsiteUrlResolver.ResolveForHotKey(node.UrlTemplate, null));
            return "";
        }
        catch (Exception ex) { return ex.Message; }
    }
    public async Task<string> ReopenWebsite(string id)
    {
        try { await _windows.ReopenAsync(FindWebsite(id) ?? throw new InvalidOperationException("找不到网站。")); return ""; }
        catch (Exception ex) { return ex.Message; }
    }
    public async Task<string> PickLocalFile()
    {
        var owner = WindowManager.ShowUniqueWindow<PluginWebView>(WebSearchConst.PluginId);
        var files = await owner.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "选择本地网页", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("网页文件") { Patterns = ["*.html", "*.htm"] }]
        });
        return files.FirstOrDefault()?.TryGetLocalPath() ?? "";
    }
    public async Task<string> PickDirectory()
    {
        var owner = WindowManager.ShowUniqueWindow<PluginWebView>(WebSearchConst.PluginId);
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new() { Title = "选择目录", AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath() ?? "";
    }
    public async Task<string> OpenDataDirectory(string id)
    {
        try
        {
            var draft = await GetWebsiteEditor(id);
            if (draft.Configuration.OpenMode != "Embedded")
                return "外部浏览器管理自己的网站数据，无需设置插件数据目录。";
            await _windows.ValidateDataDirectoryAsync(id, draft.Configuration);
            Directory.CreateDirectory(draft.EffectiveDataDirectory);
            Process.Start(new ProcessStartInfo(draft.EffectiveDataDirectory) { UseShellExecute = true });
            return "";
        }
        catch (Exception ex) { return ex.Message; }
    }
}

