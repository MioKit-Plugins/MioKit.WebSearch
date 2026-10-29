namespace WebSearch.Nodes;

[EavType(WebSearchConst.BookmarkResultTypeId)]
public sealed class BookmarkResultNode : MioObject, IInvokeFeature, IIconProviderFeature, IMemoryNodeFeature
{
    public BookmarkResultNode(string url) : base($"websearch-bookmark-{Guid.NewGuid():N}")
    {
        Url = url;
        Name = url;
    }

    public override Guid MioType => WebSearchConst.BookmarkResultType;

    public string Url { get; }

    public ValueTask<IIconLease?> GetIconAsync(
        IconRequest request,
        CancellationToken cancellationToken)
    {
        var origin = WebsiteUrlResolver.GetOrigin(Url);
        if (origin is null)
        {
            return ValueTask.FromResult<IIconLease?>(IconLease.Borrowed(IconSource.Default));
        }

        var plugin = WebSearchRegister.ComponentContextInstance.Resolve<WebSearchPlugin>();
        var favicon = WebSearchRegister.ComponentContextInstance.Resolve<FaviconService>();
        var path = favicon.TryGetPath(origin);
        if (path is not null)
        {
            return plugin.Context.Icons.OpenFileAsync(path, request, cancellationToken);
        }

        favicon.Watch(origin, this);
        favicon.RequestRefresh(origin);
        return ValueTask.FromResult<IIconLease?>(IconLease.Borrowed(IconSource.Default));
    }

    public Task InvokeAsync(InvokeContext context)
    {
        var payload = context.SearchResult?.Payload as OpenUrlPayload
                      ?? context.Payload as OpenUrlPayload;
        WebSearchRegister.ComponentContextInstance.Resolve<BrowserLaunchService>()
            .Open(payload?.Url ?? Url);
        return Task.CompletedTask;
    }
}
