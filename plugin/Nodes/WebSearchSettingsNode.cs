namespace WebSearch.Nodes;

[EavType("D981A274-8D9B-48D0-8DF4-33BD6F1AF65A")]
public sealed class WebSearchSettingsNode : MioObject, IWebSearchSettingsFeature
{
    public WebSearchSettingsNode(string id) : base(id)
    {
        Name = "网页快开设置";
        Description = "网页快开设置";
        PreloadPropertySource =
        [
            ..PreloadPropertySource,
            WebSearchSettingsExtension.DefaultBrowserIdProperty,
            WebSearchSettingsExtension.HistoryGlobalEnabledProperty,
            WebSearchSettingsExtension.BookmarkGlobalEnabledProperty
        ];
    }

    public override Guid MioType => WebSearchConst.SettingsType;

    public string DefaultBrowserId
    {
        get => this.GetDefaultBrowserId() ?? BrowserIds.SystemDefault;
        set => this.SetDefaultBrowserId(value);
    }

    public bool HistoryGlobalEnabled
    {
        get => this.GetHistoryGlobalEnabled() ?? false;
        set => this.SetHistoryGlobalEnabled(value);
    }

    public bool BookmarkGlobalEnabled
    {
        get => this.GetBookmarkGlobalEnabled() ?? false;
        set => this.SetBookmarkGlobalEnabled(value);
    }
}
