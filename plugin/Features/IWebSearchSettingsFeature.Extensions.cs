namespace WebSearch.Features;

public static partial class WebSearchSettingsExtension
{
    [EavRelation(typeof(IWebSearchSettingsFeature))]
    public static EavProperty<string?> DefaultBrowserIdProperty { get; } =
        EavPropertyBuilder<string?>.Create()
            .WithId(Guid.Parse("3C19957A-2FE7-4E59-9F29-87237163151E"))
            .WithName("DefaultBrowserId")
            .WithPolicy(EavCachePolicy.Absolute)
            .WithStoreType(MioStoreType.String)
            .WithDefaultValue(BrowserIds.SystemDefault)
            .Build();

    [EavRelation(typeof(IWebSearchSettingsFeature))]
    public static EavProperty<bool?> HistoryGlobalEnabledProperty { get; } =
        EavPropertyBuilder<bool?>.Create()
            .WithId(WebSearchConst.HistoryGlobalEnabledPropertyGuid)
            .WithName("HistoryGlobalEnabled")
            .WithPolicy(EavCachePolicy.Absolute)
            .WithStoreType(MioStoreType.Boolean)
            .WithPropertyConvert(BoolPropertyConvert.Instance)
            .WithDefaultValue(false)
            .Build();

    [EavRelation(typeof(IWebSearchSettingsFeature))]
    public static EavProperty<bool?> BookmarkGlobalEnabledProperty { get; } =
        EavPropertyBuilder<bool?>.Create()
            .WithId(WebSearchConst.BookmarkGlobalEnabledPropertyGuid)
            .WithName("BookmarkGlobalEnabled")
            .WithPolicy(EavCachePolicy.Absolute)
            .WithStoreType(MioStoreType.Boolean)
            .WithPropertyConvert(BoolPropertyConvert.Instance)
            .WithDefaultValue(false)
            .Build();
}
