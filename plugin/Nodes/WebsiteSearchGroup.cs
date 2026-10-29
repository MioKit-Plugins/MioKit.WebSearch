namespace WebSearch.Nodes;

[EavType("7F64D3EB-15C8-4725-9D29-32D46FD11B25")]
public sealed class WebsiteSearchGroup : MioObject, ISearchableFeature
{
    public WebsiteSearchGroup(string id) : base(id)
    {
        Name = "自定义网站";
        Description = "直接打开网站或使用网站搜索";
    }

    public override Guid MioType => WebSearchConst.WebsiteGroupType;

    public ValueTask SearchAsync(
        SearchRequest request,
        IWriteOnlyResultList<SearchResult> results,
        CancellationToken cancellationToken)
    {
        var query = request.SearchText?.Trim() ?? string.Empty;
        var clipboard = request.Context?.Clipboard.Text;
        if (query.Length == 0 && string.IsNullOrWhiteSpace(clipboard))
        {
            return ValueTask.CompletedTask;
        }

        foreach (var website in Children.OfType<WebsiteNode>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (website.GetIgnoreSearch() == true)
            {
                continue;
            }

            var name = website.Name ?? string.Empty;
            var isLocal = website.GetSourceKind() == "LocalFile";
            var template = isLocal ? website.GetLocalFilePath() ?? "" : website.UrlTemplate;

            if (!isLocal && WebsiteUrlResolver.IsTemplate(template))
            {
                var displayQuery = ResolveDisplayQuery(query, clipboard, website.QuerySourcePolicy);
                if (string.IsNullOrWhiteSpace(displayQuery))
                {
                    continue;
                }

                var finalUrl = WebsiteUrlResolver.Resolve(
                    template,
                    query,
                    clipboard,
                    website.QuerySourcePolicy);
                if (!WebsiteUrlResolver.TryValidateHttpUrl(finalUrl, out _))
                {
                    continue;
                }

                results.Add(new SearchResult(request, website)
                {
                    Title = $"使用 {name} 搜索“{displayQuery}”",
                    Description = finalUrl,
                    Source = "网页快开",
                    Payload = new OpenUrlPayload(finalUrl),
                    PinMode = SortPriority.AlwaysBottom,
                    MatchResult = TextMatchResult.Default()
                });
                continue;
            }

            // Clipboard text supplies template arguments, not a fixed-page name search.
            if (query.Length == 0)
            {
                continue;
            }

            if (!isLocal && !WebsiteUrlResolver.TryValidateHttpUrl(template, out _))
            {
                continue;
            }

            if (website.TryMatch(query, out var match, out var title) && match is { } matched)
            {
                results.Add(new SearchResult(request, website)
                {
                    Title = title,
                    Description = template,
                    Source = "网页",
                    Payload = new OpenUrlPayload(template),
                    PinMode = SortPriority.Normal,
                    MatchResult = matched
                });
            }
        }

        return ValueTask.CompletedTask;
    }

    private static string ResolveDisplayQuery(
        string input,
        string? clipboard,
        QuerySourcePolicy policy) =>
        policy switch
        {
            QuerySourcePolicy.ClipboardOnly => clipboard ?? string.Empty,
            QuerySourcePolicy.InputThenClipboard when string.IsNullOrWhiteSpace(input) => clipboard ?? string.Empty,
            _ => input
        };
}
