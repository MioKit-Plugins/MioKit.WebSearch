namespace WebSearch.Features;

public static partial class EmbeddedWebsiteExtension
{
    [EavRelation(typeof(IEmbeddedWebsiteFeature))]
    public static EavProperty<string?> SourceKindProperty { get; } =
        EavPropertyBuilder<string?>.Create().WithId(WebSearchConst.WebsiteSourceKindPropertyGuid)
            .WithName("SourceKind").WithPolicy(EavCachePolicy.Absolute).WithStoreType(MioStoreType.String)
            .WithDefaultValue("Online")
            .Build();

    [EavRelation(typeof(IEmbeddedWebsiteFeature))]
    public static EavProperty<string?> OpenModeProperty { get; } =
        EavPropertyBuilder<string?>.Create().WithId(WebSearchConst.WebsiteOpenModePropertyGuid)
            .WithName("OpenMode").WithPolicy(EavCachePolicy.Absolute).WithStoreType(MioStoreType.String)
            .WithDefaultValue("External")
            .Build();

    [EavRelation(typeof(IEmbeddedWebsiteFeature))]
    public static EavProperty<string?> LocalFilePathProperty { get; } =
        EavPropertyBuilder<string?>.Create().WithId(WebSearchConst.WebsiteLocalFilePathPropertyGuid)
            .WithName("LocalFilePath").WithPolicy(EavCachePolicy.Absolute).WithStoreType(MioStoreType.String)
            .Build();

    [EavRelation(typeof(IEmbeddedWebsiteFeature))]
    public static EavProperty<string?> UserDataFolderProperty { get; } =
        EavPropertyBuilder<string?>.Create().WithId(WebSearchConst.WebsiteUserDataFolderPropertyGuid)
            .WithName("UserDataFolder").WithPolicy(EavCachePolicy.Absolute).WithStoreType(MioStoreType.String)
            .Build();

    [EavRelation(typeof(IEmbeddedWebsiteFeature))]
    public static EavProperty<bool?> MapLocalDirectoryProperty { get; } =
        EavPropertyBuilder<bool?>.Create().WithId(WebSearchConst.WebsiteMapLocalDirectoryPropertyGuid)
            .WithName("MapLocalDirectory").WithPolicy(EavCachePolicy.Absolute).WithStoreType(MioStoreType.Boolean)
            .WithPropertyConvert(BoolPropertyConvert.Instance)
            .WithDefaultValue(true)
            .Build();

    [EavRelation(typeof(IEmbeddedWebsiteFeature))]
    public static EavProperty<string?> LocalHostNameProperty { get; } =
        EavPropertyBuilder<string?>.Create().WithId(WebSearchConst.WebsiteLocalHostNamePropertyGuid)
            .WithName("LocalHostName").WithPolicy(EavCachePolicy.Absolute).WithStoreType(MioStoreType.String)
            .Build();

    [EavRelation(typeof(IEmbeddedWebsiteFeature))]
    public static EavProperty<bool?> DebugEnabledProperty { get; } =
        EavPropertyBuilder<bool?>.Create().WithId(WebSearchConst.WebsiteDebugEnabledPropertyGuid)
            .WithName("DebugEnabled").WithPolicy(EavCachePolicy.Absolute).WithStoreType(MioStoreType.Boolean)
            .WithPropertyConvert(BoolPropertyConvert.Instance)
            .WithDefaultValue(false)
            .Build();

    [EavRelation(typeof(IEmbeddedWebsiteFeature))]
    public static EavProperty<WebsiteRuntimeOptions> RuntimeOptionsProperty { get; } =
        EavPropertyBuilder<WebsiteRuntimeOptions>.Create().WithId(WebSearchConst.WebsiteRuntimeOptionsPropertyGuid)
            .WithName("RuntimeOptions").WithPolicy(EavCachePolicy.Absolute).WithStoreType(MioStoreType.LongText)
            .WithPropertyConvert(JsonLongTextConvert<WebsiteRuntimeOptions>.Instance)
            .Build();

    [EavRelation(typeof(IEmbeddedWebsiteFeature))]
    public static EavProperty<string?> WebsiteStorageJsonProperty { get; } =
        EavPropertyBuilder<string?>.Create().WithId(WebSearchConst.WebsiteStorageJsonPropertyGuid)
            .WithName("WebsiteStorageJson").WithPolicy(EavCachePolicy.Absolute).WithStoreType(MioStoreType.LongText)
            .Build();

    public static async Task<WebsiteConfiguration> ReadConfigurationAsync(this IEmbeddedWebsiteFeature node) => new()
    {
        SourceKind = await node.GetSourceKindAsync() ?? "Online",
        OpenMode = await node.GetOpenModeAsync() ?? "External",
        LocalFilePath = await node.GetLocalFilePathAsync() ?? "",
        UserDataFolder = await node.GetUserDataFolderAsync() ?? "",
        MapLocalDirectory = await node.GetMapLocalDirectoryAsync() ?? true,
        LocalHostName = await node.GetLocalHostNameAsync() ?? "",
        DebugEnabled = await node.GetDebugEnabledAsync() ?? false,
        Runtime = await node.GetRuntimeOptionsAsync() ?? new()
    };

    public static void WriteConfiguration(this IEmbeddedWebsiteFeature node, WebsiteConfiguration value)
    {
        node.SetSourceKind(value.SourceKind);
        node.SetOpenMode(value.OpenMode);
        node.SetLocalFilePath(value.LocalFilePath);
        node.SetUserDataFolder(value.UserDataFolder);
        node.SetMapLocalDirectory(value.MapLocalDirectory);
        node.SetLocalHostName(value.LocalHostName);
        node.SetDebugEnabled(value.DebugEnabled);
        node.SetRuntimeOptions(value.Runtime);
    }
}

