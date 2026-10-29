namespace WebSearch.Nodes;

[EavType("F3951B4C-8C6D-4A3A-839F-01BF0772D9C3")]
public sealed class WebsiteNode : MioObject,
    IWebsiteFeature,
    IEmbeddedWebsiteFeature,
    IIconProviderFeature,
    IHotkeyFeature,
    IAliasNameFeature,
    IUseFeature,
    IPinnedFeature,
    IIgnoreSearchFeature,
    IResultActionProviderFeature
{
    public WebsiteNode(string id) : base(id)
    {
        PreloadPropertySource =
        [
            ..PreloadPropertySource,
            WebsiteExtension.UrlTemplateProperty,
            WebsiteExtension.CustomIconIdProperty,
            EmbeddedWebsiteExtension.SourceKindProperty,
            EmbeddedWebsiteExtension.OpenModeProperty,
            EmbeddedWebsiteExtension.LocalFilePathProperty,
            WebsiteExtension.QuerySourcePolicyProperty,
            AliasNameExtension.AliasNameProperty,
            IgnoreSearchExtension.IgnoreSearchProperty
        ];
    }

    public override Guid MioType => WebSearchConst.WebsiteType;

    public string UrlTemplate
    {
        get => this.GetUrlTemplate() ?? string.Empty;
        set => this.SetUrlTemplate(value);
    }

    public QuerySourcePolicy QuerySourcePolicy
    {
        get => Enum.TryParse<QuerySourcePolicy>(this.GetQuerySourcePolicy(), true, out var policy)
            ? policy
            : Models.QuerySourcePolicy.InputThenClipboard;
        set => this.SetQuerySourcePolicy(value.ToString());
    }

    public ValueTask<IIconLease?> GetIconAsync(
        IconRequest request,
        CancellationToken cancellationToken) =>
        WebSearchRegister.ComponentContextInstance.Resolve<WebsiteIconService>()
            .OpenAsync(this, request, cancellationToken);

    public ValueTask<IEnumerable<ISearchResultAction>> GetActionAsync(SearchResult searchResult)
    {
        var isPinned = this.GetIsPinned() == true;
        var ignoreSearch = this.GetIgnoreSearch() == true;

        var actions = new List<ISearchResultAction>
        {
            isPinned ? CancelPinAction.Instance : PinAction.Instance,
            AliasNameAction.Instance,
            HotKeyAction.Instance
        };

        if (!ignoreSearch)
        {
            actions.Add(IgnoreAction.Instance);
        }

        return ValueTask.FromResult<IEnumerable<ISearchResultAction>>(actions);
    }

    public async Task InvokeAsync(InvokeContext context)
    {
        var url = ResolveInvokeUrl(context);
        await WebSearchRegister.ComponentContextInstance.Resolve<WebsiteWindowService>().OpenAsync(this, url);
        await this.IncreaseAsync().ConfigureAwait(false);
    }

    private string ResolveInvokeUrl(InvokeContext context)
    {
        if (context.SearchResult?.Payload is OpenUrlPayload searchPayload)
        {
            return searchPayload.Url;
        }

        if (context.Payload is OpenUrlPayload invokePayload)
        {
            return invokePayload.Url;
        }

        if (!WebsiteUrlResolver.IsTemplate(UrlTemplate))
        {
            return UrlTemplate;
        }

        return WebsiteUrlResolver.ResolveForHotKey(UrlTemplate, context.Context?.Clipboard.Text);
    }
}
