namespace WebSearch.Nodes;

[EavType("20381D60-EF6C-48B7-B935-C1E897ADE7A4")]
public sealed class BrowserGlobalSearchGroup : MioObject, ISearchableFeature
{
    public const string NodeId = "miokit-websearch-browser-global";
    private static readonly Guid TypeId = Guid.Parse("20381D60-EF6C-48B7-B935-C1E897ADE7A4");

    private readonly BrowserDatabaseSearchNode _history =
        new($"{NodeId}-history", BrowserItemKind.History);
    private readonly BrowserDatabaseSearchNode _bookmarks =
        new($"{NodeId}-bookmarks", BrowserItemKind.Bookmark);
    private readonly Func<BrowserItemKind, bool> _isGlobalEnabled;
    private readonly Func<BrowserItemKind, SearchRequest, IWriteOnlyResultList<SearchResult>, int,
        CancellationToken, ValueTask> _search;

    public BrowserGlobalSearchGroup(string id) : this(id, null, null)
    {
    }

    internal BrowserGlobalSearchGroup(
        string id,
        Func<BrowserItemKind, bool>? isGlobalEnabled,
        Func<BrowserItemKind, SearchRequest, IWriteOnlyResultList<SearchResult>, int,
            CancellationToken, ValueTask>? search) : base(id)
    {
        Name = "浏览器内容";
        Description = "根据设置在全局搜索中显示历史与收藏";
        _isGlobalEnabled = isGlobalEnabled ?? (kind =>
        {
            var settings = WebSearchRegister.ComponentContextInstance.Resolve<WebSearchPlugin>().Settings;
            return kind == BrowserItemKind.History
                ? settings.HistoryGlobalEnabled
                : settings.BookmarkGlobalEnabled;
        });
        _search = search ?? ((kind, request, results, limit, cancellationToken) =>
            (kind == BrowserItemKind.History ? _history : _bookmarks)
                .SearchAsync(request, results, limit, cancellationToken));
    }

    public override Guid MioType => TypeId;

    public async ValueTask SearchAsync(
        SearchRequest request,
        IWriteOnlyResultList<SearchResult> results,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SearchText))
        {
            return;
        }

        if (_isGlobalEnabled(BrowserItemKind.History))
        {
            await _search(BrowserItemKind.History, request, results, 5, cancellationToken)
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_isGlobalEnabled(BrowserItemKind.Bookmark))
        {
            await _search(BrowserItemKind.Bookmark, request, results, 5, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
