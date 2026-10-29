namespace WebSearch.Nodes;

public sealed class BrowserDatabaseSearchNode : MioObject, ISearchableFeature, IMemoryNodeFeature
{
    public BrowserDatabaseSearchNode(string id) : base(id)
    {
        Name = "浏览器收藏夹";
    }

    public override Guid MioType => WebSearchConst.BrowserResultType;

    public async ValueTask SearchAsync(
        SearchRequest request,
        IWriteOnlyResultList<SearchResult> results,
        CancellationToken cancellationToken)
    {
        var query = request.SearchText?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var index = WebSearchRegister.ComponentContextInstance.Resolve<BrowserIndexService>();
        var hits = await index.SearchAsync(query, 50, cancellationToken).ConfigureAwait(false);
        foreach (var hit in hits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var title = string.IsNullOrWhiteSpace(hit.Title) ? hit.Url : hit.Title;
            var owner = new BookmarkResultNode(hit.Url)
            {
                Name = title,
                Description = BuildDescription(hit)
            };
            results.Add(new SearchResult(request, owner)
            {
                Title = title,
                Description = owner.Description,
                Source = "收藏夹",
                Payload = new OpenUrlPayload(hit.Url),
                MatchResult = CreateDirectMatch(title, hit.Url, query)
            });
        }
    }

    private static string BuildDescription(BrowserIndexHit hit)
    {
        var location = string.IsNullOrWhiteSpace(hit.Folder) ? hit.Profile : $"{hit.Profile} · {hit.Folder}";
        return $"{hit.Url} · {hit.Browser} · {location}";
    }

    private static TextMatchResult CreateDirectMatch(string title, string url, string query)
    {
        var text = title.Contains(query, StringComparison.OrdinalIgnoreCase) ? title : url;
        var start = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (start < 0 || start > byte.MaxValue)
        {
            return TextMatchResult.Default();
        }

        var length = Math.Min(query.Length, byte.MaxValue - start + 1);
        return TextMatchResult.Success(
            TextMatchType.Succession,
            Enumerable.Range(start, length).Select(x => (byte)x).ToArray());
    }
}
