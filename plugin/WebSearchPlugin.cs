using WebSearch.Views;

namespace WebSearch;

public sealed class WebSearchPlugin : PluginBase, IPluginMainWindowFeature, ISearchableFeature, IInvokeFeature
{
    public WebSearchPlugin() : base(WebSearchConst.PluginId)
    {
        Name = "网页快开";
        Description = "管理网页快开";
        // 如需扩展基类预加载属性列表，在此用 spread 语法：
        // PreloadPropertySource = [..PreloadPropertySource, MyExtension.PathProperty];
    }

    public override Guid MioType => WebSearchConst.PluginType;

    public WebSearchSettingsNode Settings =>
        this.FindFirstDescendant<WebSearchSettingsNode>()!;

    public BookmarkSearchScope BookmarkScope =>
        this.FindFirstDescendant<BookmarkSearchScope>()!;

    public WebsiteSearchGroup WebsiteGroup =>
        this.FindFirstDescendant<WebsiteSearchGroup>()!;

    protected override async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        await EnsureTreeLoadedAsync<WebSearchSettingsNode>(WebSearchConst.SettingsId);
        await EnsureTreeLoadedAsync<BookmarkSearchScope>(WebSearchConst.BookmarkScopeId);
        await EnsureTreeLoadedAsync<WebsiteSearchGroup>(WebSearchConst.WebsiteGroupId);
        await RemoveLegacyHistoryScopeAsync();
        await RemoveLegacyDefaultWebsiteAsync();
    }

    private async Task RemoveLegacyHistoryScopeAsync()
    {
        var history = Children.FirstOrDefault(x =>
            x.Id == WebSearchConst.HistoryScopeId || x.MioType == WebSearchConst.HistoryScopeType);
        if (history is not null)
        {
            history.SetParent(null);
        }

        try
        {
            await Container!.Resolve<IMioDataProvider>().DeleteAsync(WebSearchConst.HistoryScopeId);
        }
        catch (Exception exception)
        {
            Context.Logger.Debug(
                exception,
                "Failed to delete legacy history search scope {HistoryScopeId}.",
                WebSearchConst.HistoryScopeId);
        }
    }

    private async Task RemoveLegacyDefaultWebsiteAsync()
    {
        var websites = WebsiteGroup.Children.OfType<WebsiteNode>().ToList();
        if (websites.Count != 1 || !IsLegacyDefaultWebsite(websites[0]))
        {
            return;
        }

        var website = websites[0];
        website.SetParent(null);
        await Container!.Resolve<IMioDataProvider>().DeleteAsync(website.Id);
        Context.Logger.Information("Removed the legacy default Bing website seed {WebsiteId}.", website.Id);
    }

    private static bool IsLegacyDefaultWebsite(WebsiteNode website) =>
        IsLegacyDefaultWebsite(
            website.Id,
            website.Name,
            website.Description,
            website.UrlTemplate,
            website.QuerySourcePolicy);

    internal static bool IsLegacyDefaultWebsite(
        string id,
        string? name,
        string? description,
        string urlTemplate,
        QuerySourcePolicy querySourcePolicy) =>
        id.StartsWith("websearch-site-", StringComparison.Ordinal)
        && string.Equals(name, "Bing", StringComparison.Ordinal)
        && string.Equals(description, "使用 Bing 搜索网页", StringComparison.Ordinal)
        && string.Equals(
            urlTemplate,
            "https://www.bing.com/search?q={query}",
            StringComparison.Ordinal)
        && querySourcePolicy == QuerySourcePolicy.InputThenClipboard;

    public void OpenPluginMainWindow() =>
        WindowManager.ShowUniqueWindow<PluginWebView>(WebSearchConst.PluginId);

    public override ValueTask<PluginDataCleanupPlan> CreateDataCleanupPlanAsync(
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new PluginDataCleanupPlan
        {
            AdditionalEavRootObjectIds = []
        });
    }

    public async ValueTask SearchAsync(SearchRequest request, IWriteOnlyResultList<SearchResult> writeOnlyResultList,
        CancellationToken cancellationToken)
    {
        if (request.IsEmptySearchText) return;
        if (this.TryMatch(request.SearchText!, out var result, out var title))
        {
            writeOnlyResultList.Add(new SearchResult(request, this)
            {
                Title = title,
                Description = Description,
                MatchResult = result!.Value,
                PinMode = SortPriority.Normal,
                Source = "网页快开",
            });
        }
    }

    public Task InvokeAsync(InvokeContext context)
    {
        OpenPluginMainWindow();
        return Task.CompletedTask;
    }
}
