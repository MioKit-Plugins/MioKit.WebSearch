namespace WebSearch.Features;

public static partial class WebsiteExtension
{
    [EavRelation(typeof(IWebsiteFeature))]
    public static EavProperty<string?> CustomIconIdProperty { get; } =
        EavPropertyBuilder<string?>.Create()
            .WithId(WebSearchConst.WebsiteCustomIconIdPropertyGuid)
            .WithName("CustomIconId")
            .WithPolicy(EavCachePolicy.Absolute)
            .WithStoreType(MioStoreType.String)
            .Build();

    [EavRelation(typeof(IWebsiteFeature))]
    public static EavProperty<string?> UrlTemplateProperty { get; } =
        EavPropertyBuilder<string?>.Create()
            .WithId(Guid.Parse("D7DAE140-9385-4C97-A71B-E293722B56F7"))
            .WithName("UrlTemplate")
            .WithPolicy(EavCachePolicy.Absolute)
            .WithStoreType(MioStoreType.String)
            .Build();

    [EavRelation(typeof(IWebsiteFeature))]
    public static EavProperty<string?> QuerySourcePolicyProperty { get; } =
        EavPropertyBuilder<string?>.Create()
            .WithId(Guid.Parse("8AE9CD9D-0BC6-4589-859B-00CCB39A7CC1"))
            .WithName("QuerySourcePolicy")
            .WithPolicy(EavCachePolicy.Absolute)
            .WithStoreType(MioStoreType.String)
            .WithDefaultValue(nameof(Models.QuerySourcePolicy.InputThenClipboard))
            .Build();

    [EavRelation(typeof(IWebsiteFeature))]
    public static EavProperty<string?> FaviconFileNameProperty { get; } =
        EavPropertyBuilder<string?>.Create()
            .WithId(Guid.Parse("49C8222D-98D3-4E64-BAE4-6B84491CB43B"))
            .WithName("FaviconFileName")
            .WithPolicy(EavCachePolicy.Absolute)
            .WithStoreType(MioStoreType.String)
            .Build();
}
