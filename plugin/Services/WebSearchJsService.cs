using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WebSearch.Services;

[JsService("WebSearchService")]
public partial class WebSearchJsService : ObservableObject, IMioEventHandler<MioPropertyChangedEventMessage>, IPluginLifecycleComponent
{
    private readonly WebSearchPlugin _plugin;
    private readonly BrowserCatalogService _catalog;
    private readonly BrowserIndexService _index;
    private readonly FaviconService _favicon;
    private readonly BrowserLaunchService _browserLaunch;
    private readonly IMioDataProvider _dataProvider;
    private readonly WebsiteWindowService _windows;
    private readonly WebsiteIconService _icons;
    private CancellationTokenSource _websiteLifetime = new();
    private readonly WebsiteRefreshRequests _websiteRefreshRequests = new();
    private readonly Dictionary<string, WebsiteIconKey> _websiteIconKeys = new(StringComparer.Ordinal);
    private CancellationTokenSource? _websiteReloadCancellation;
    private readonly HashSet<Task> _websiteReloadTasks = [];
    private sealed record WebsiteIconKey(string SourceKind, string Url, string? CustomIconId);
    private bool _initialized;
    private bool _loading;

    [ObservableProperty] private string _defaultBrowserId = BrowserIds.SystemDefault;
    [ObservableProperty] private bool _historyGlobalEnabled;
    [ObservableProperty] private bool _bookmarkGlobalEnabled;
    [ObservableProperty] private bool _isSynchronizing;
    [ObservableProperty] private int _bookmarkCount;
    [ObservableProperty] private int _profileCount;
    [ObservableProperty] private string _lastSynchronizedAt = "尚未同步";
    [ObservableProperty] private string _lastError = string.Empty;
    [ObservableProperty] private string _requestedWebsiteId = string.Empty;

    public WebSearchJsService(
        WebSearchPlugin plugin,
        BrowserCatalogService catalog,
        BrowserIndexService index,
        FaviconService favicon,
        BrowserLaunchService browserLaunch,
        IMioDataProvider dataProvider,
        WebsiteWindowService windows,
        WebsiteIconService icons)
    {
        _plugin = plugin;
        _catalog = catalog;
        _index = index;
        _favicon = favicon;
        _browserLaunch = browserLaunch;
        _dataProvider = dataProvider;
        _windows = windows;
        _icons = icons;
    }

    public ObservableCollection<BrowserOptionDto> Browsers { get; } = [];
    public ObservableCollection<BrowserProfileDto> Profiles { get; } = [];
    public ObservableCollection<string> ProfileSyncErrors { get; } = [];
    public ObservableCollection<WebsiteDto> Websites { get; } = [];
    public string DefaultWebsiteIconDataUrl => IconSource.DefaultDataUrl;

    public event EventHandler? DataChanged;

    public void Initialize()
    {
        if (_initialized)
        {
            ReloadAll();
            return;
        }

        _initialized = true;
        MioAppContext.Current.EventBus.RegisterEventHandle<MioPropertyChangedEventMessage>(this);
        _index.StateChanged += OnIndexStateChanged;
        _favicon.IconsChanged += OnFaviconChanged;
        ReloadAll();
    }

    public Task RefreshIndex() => _index.SynchronizeAsync(CancellationToken.None);

    public async Task<BookmarkBrowsePage> BrowseBookmarks(
        string query,
        string browser,
        int offset,
        int limit)
    {
        var (items, total) = await _index
            .BrowseAsync(query, browser, offset, limit, CancellationToken.None)
            .ConfigureAwait(false);
        var enriched = items.Select(hit => new BookmarkBrowseItem(
            hit.Title,
            hit.Url,
            hit.Browser,
            hit.Profile,
            hit.Folder,
            ResolveBookmarkFavicon(hit.Url))).ToList();
        return new BookmarkBrowsePage(enriched, total);
    }

    public string OpenBookmark(string url)
    {
        try
        {
            _browserLaunch.Open(url);
            return string.Empty;
        }
        catch (Exception exception)
        {
            _plugin.Context.Logger.Warning(exception, "Failed to open bookmark {Url}.", url);
            return $"打开失败：{exception.Message}";
        }
    }

    private string ResolveBookmarkFavicon(string url)
    {
        var origin = WebsiteUrlResolver.GetOrigin(url);
        return origin is null ? string.Empty : _favicon.GetDataUrl(origin);
    }

    public async Task<string> AddWebsite(
        string name,
        string urlTemplate,
        string querySourcePolicy,
        string description)
    {
        var result = await SaveWebsite(new WebsiteEditorRequest
        {
            Name = name, UrlTemplate = urlTemplate, QuerySourcePolicy = querySourcePolicy, Description = description
        });
        return result.Error;
    }

    public async Task<string> UpdateWebsite(
        string id, string name, string urlTemplate, string querySourcePolicy, string description)
    {
        if (FindWebsite(id) is null) return "找不到要编辑的网站。";
        var request = await GetWebsiteEditor(id);
        request.Name = name; request.UrlTemplate = urlTemplate;
        request.QuerySourcePolicy = querySourcePolicy; request.Description = description;
        return (await SaveWebsite(request)).Error;
    }

    public async Task<string> DeleteWebsite(string id)
    {
        await websiteSaveGate.WaitAsync();
        try
        {
            var website = FindWebsite(id);
            if (website is null) return "找不到要删除的网站。";
            var iconId = website.GetCustomIconId();
            await _windows.CloseNodeAsync(website.Id);
            await _dataProvider.DeleteAsync(website.Id).ConfigureAwait(false);
            await RunOnUiThreadAsync(() => website.SetParent(null));
            await _icons.DeleteOwnedAsync(iconId);
            await RunOnUiThreadAsync(() => QueueWebsiteRefresh(id));
            return string.Empty;
        }
        catch (Exception exception)
        {
            _plugin.Context.Logger.Warning(exception, "Failed to delete custom website {WebsiteId}.", id);
            return $"删除网站失败：{exception.Message}";
        }
        finally { websiteSaveGate.Release(); }
    }

    public async Task<string> RefreshFavicon(string id)
    {
        var website = FindWebsite(id);
        if (website is null)
        {
            return "找不到要刷新图标的网站。";
        }

        var refreshed = await _favicon.RefreshAsync(website, CancellationToken.None).ConfigureAwait(false);
        await RunOnUiThreadAsync(() => QueueWebsiteRefresh(id, refreshIcons: true));
        return refreshed ? string.Empty : "未能从该网站获取有效图标。";
    }

    public string PreviewFavicon(string urlTemplate)
    {
        var origin = WebsiteUrlResolver.GetOrigin(urlTemplate);
        return origin is null ? string.Empty : _favicon.GetDataUrl(origin);
    }

    public async Task<string> RefreshFaviconByUrl(string urlTemplate)
    {
        var origin = WebsiteUrlResolver.GetOrigin(urlTemplate);
        if (origin is null)
        {
            return "地址无效，无法获取图标。";
        }

        var refreshed = await _favicon.RefreshAsync(origin, CancellationToken.None).ConfigureAwait(false);
        await RunOnUiThreadAsync(() => RefreshOriginIcons(origin));
        return refreshed ? string.Empty : "未能从该网站获取有效图标。";
    }

    public string PreviewUrl(string urlTemplate, string query, string querySourcePolicy)
    {
        if (!Enum.TryParse<QuerySourcePolicy>(querySourcePolicy, true, out var policy))
        {
            policy = QuerySourcePolicy.InputThenClipboard;
        }

        return WebsiteUrlResolver.Resolve(urlTemplate ?? string.Empty, query, "剪贴板示例", policy);
    }

    partial void OnDefaultBrowserIdChanged(string value)
    {
        if (_loading || !_initialized)
        {
            return;
        }

        if (value is not (BrowserIds.SystemDefault or BrowserIds.Edge or BrowserIds.Chrome or BrowserIds.Firefox))
        {
            return;
        }

        _plugin.Settings.DefaultBrowserId = value;
    }

    partial void OnHistoryGlobalEnabledChanged(bool value)
    {
        if (!_loading && _initialized)
        {
            _plugin.Settings.HistoryGlobalEnabled = value;
        }
    }

    partial void OnBookmarkGlobalEnabledChanged(bool value)
    {
        if (!_loading && _initialized)
        {
            _plugin.Settings.BookmarkGlobalEnabled = value;
        }
    }

    Task IMioEventHandler<MioPropertyChangedEventMessage>.HandleAsync(
        MioPropertyChangedEventMessage eventModel,
        CancellationToken cancellationToken)
    {
        if (eventModel.Object is WebsiteNode website)
        {
            RunOnUiThread(() => QueueWebsiteRefresh(website.Id));
        }
        else if (eventModel.Object is WebSearchSettingsNode)
        {
            RunOnUiThread(() =>
            {
                if (!_initialized) return;
                _loading = true;
                try { DefaultBrowserId = _plugin.Settings.DefaultBrowserId; }
                finally { _loading = false; }
            });
        }

        return Task.CompletedTask;
    }

    private void ReloadAll()
    {
        _loading = true;
        try
        {
            DefaultBrowserId = _plugin.Settings.DefaultBrowserId;
            HistoryGlobalEnabled = _plugin.Settings.HistoryGlobalEnabled;
            BookmarkGlobalEnabled = _plugin.Settings.BookmarkGlobalEnabled;
            Replace(Browsers, _catalog.GetBrowserOptions());
            Replace(Profiles, _catalog.DiscoverProfiles());
            ProfileCount = Profiles.Count;
            ReloadWebsites();
            ReloadIndexState();
        }
        finally
        {
            _loading = false;
        }

        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReloadWebsites()
    {
        QueueWebsiteRefresh(refreshIcons: true);
    }

    private void QueueWebsiteRefresh(string? id = null, bool refreshIcons = false)
    {
        if (_websiteLifetime.IsCancellationRequested) return;
        _websiteRefreshRequests.Add(id, refreshIcons);
        _websiteReloadCancellation?.Cancel();
        var cancellation = _websiteReloadCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(_websiteLifetime.Token);
        var task = ReloadWebsitesAsync(cancellation);
        _websiteReloadTasks.Add(task);
        _ = ForgetWebsiteReloadAsync(task);
    }

    private async Task ForgetWebsiteReloadAsync(Task task)
    {
        await task;
        await RunOnUiThreadAsync(() => _websiteReloadTasks.Remove(task));
    }

    private async Task ReloadWebsitesAsync(CancellationTokenSource cancellation)
    {
        var ct = cancellation.Token;
        try
        {
            // Coalesce property notifications and wait until a save/delete has finished.
            await Task.Delay(50, ct);
            await websiteSaveGate.WaitAsync(ct);
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() => RefreshWebsiteRowsAsync(ct));
            }
            finally { websiteSaveGate.Release(); }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { _plugin.Context.Logger.Warning(ex, "Failed to refresh website icons."); }
        finally
        {
            await RunOnUiThreadAsync(() =>
            {
                if (ReferenceEquals(_websiteReloadCancellation, cancellation)) _websiteReloadCancellation = null;
                cancellation.Dispose();
            });
        }
    }

    private async Task RefreshWebsiteRowsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var batch = _websiteRefreshRequests.Capture();
        var nodes = _plugin.WebsiteGroup.Children.OfType<WebsiteNode>().ToArray();
        var rows = Websites.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var keys = new Dictionary<string, WebsiteIconKey>(_websiteIconKeys, StringComparer.Ordinal);
        foreach (var node in nodes.Where(x => batch.All || batch.Ids.Contains(x.Id) || !rows.ContainsKey(x.Id)))
        {
            ct.ThrowIfCancellationRequested();
            var source = node.GetSourceKind() ?? "Online";
            var key = new WebsiteIconKey(source, node.UrlTemplate, node.GetCustomIconId());
            rows.TryGetValue(node.Id, out var previous);
            var row = new WebsiteDto
            {
                Id = node.Id, Name = node.Name ?? "", Description = node.Description ?? "",
                UrlTemplate = source == "LocalFile" ? node.GetLocalFilePath() ?? "" : node.UrlTemplate,
                QuerySourcePolicy = node.QuerySourcePolicy.ToString(),
                IsSearchTemplate = source != "LocalFile" && WebsiteUrlResolver.IsTemplate(node.UrlTemplate),
                SourceKind = source, OpenMode = node.GetOpenMode() ?? "External"
            };
            if (!batch.AllIcons && !batch.IconIds.Contains(node.Id)
                && previous is not null && keys.TryGetValue(node.Id, out var previousKey) && previousKey == key)
            {
                row = row with { IconState = previous.IconState, HasCustomIcon = previous.HasCustomIcon,
                    IconDataUrl = previous.IconDataUrl, FaviconDataUrl = previous.FaviconDataUrl };
            }
            else
            {
                var icon = await _icons.GetPreviewAsync(node, ct);
                var favicon = source == "LocalFile" ? "" : await Task.Run(() => _favicon.GetDataUrl(node), ct);
                row = row with { IconState = icon.IconState, HasCustomIcon = icon.HasCustomIcon,
                    IconDataUrl = icon.IconDataUrl, FaviconDataUrl = favicon };
            }
            rows[node.Id] = row;
            keys[node.Id] = key;
        }
        ct.ThrowIfCancellationRequested();
        if (!_websiteRefreshRequests.Complete(batch)) return;
        var liveIds = nodes.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var ordered = rows.Values.Where(x => liveIds.Contains(x.Id))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        SynchronizeWebsiteRows(Websites, ordered);
        _websiteIconKeys.Clear();
        foreach (var id in liveIds)
            if (keys.TryGetValue(id, out var key)) _websiteIconKeys[id] = key;
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static void SynchronizeWebsiteRows(ObservableCollection<WebsiteDto> collection, IReadOnlyList<WebsiteDto> rows)
    {
        var ids = rows.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        for (var i = collection.Count - 1; i >= 0; i--)
            if (!ids.Contains(collection[i].Id)) collection.RemoveAt(i);
        for (var i = 0; i < rows.Count; i++)
        {
            var current = -1;
            for (var j = i; j < collection.Count; j++)
                if (collection[j].Id == rows[i].Id) { current = j; break; }
            if (current < 0) collection.Insert(i, rows[i]);
            else
            {
                if (current != i) collection.Move(current, i);
                if (collection[i] != rows[i]) collection[i] = rows[i];
            }
        }
    }

    private void ReloadIndexState()
    {
        IsSynchronizing = _index.IsSynchronizing;
        BookmarkCount = _index.BookmarkCount;
        ProfileCount = _index.ProfileCount == 0 ? Profiles.Count : _index.ProfileCount;
        LastSynchronizedAt = _index.LastSynchronizedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未同步";
        LastError = _index.LastError;
        Replace(ProfileSyncErrors, _index.ProfileSyncErrors);
    }

    private void OnIndexStateChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            ReloadIndexState();
            DataChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnFaviconChanged(object? sender, FaviconChangedEventArgs e) =>
        RunOnUiThread(() => RefreshOriginIcons(e.Origin));

    private void RefreshOriginIcons(Uri origin)
    {
        foreach (var node in _plugin.WebsiteGroup.Children.OfType<WebsiteNode>())
            if (node.GetSourceKind() != "LocalFile" && WebsiteUrlResolver.GetOrigin(node.UrlTemplate) == origin)
                QueueWebsiteRefresh(node.Id, refreshIcons: true);
    }

    private WebsiteNode? FindWebsite(string id) =>
        _plugin.WebsiteGroup.Children.OfType<WebsiteNode>()
            .FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var value in values)
        {
            collection.Add(value);
        }
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private static async Task RunOnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }
}
