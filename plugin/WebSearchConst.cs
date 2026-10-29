namespace WebSearch;

public static class WebSearchConst
{
    public const string PluginId = "miokit.websearch";

    public const string PluginTypeId = "C057B1CE-1FD8-47E1-ABAD-C28DD4E942BB";
    public static readonly Guid PluginType = Guid.Parse(PluginTypeId);

    public const string SettingsId = "miokit-websearch-settings";
    public const string HistoryGlobalEnabledPropertyId = "6427CA24-C57A-4AE5-A022-94ABF30EC55A";
    public static readonly Guid HistoryGlobalEnabledPropertyGuid = Guid.Parse(HistoryGlobalEnabledPropertyId);
    public const string BookmarkGlobalEnabledPropertyId = "07FD5F21-7290-435E-B67D-99798351AFF7";
    public static readonly Guid BookmarkGlobalEnabledPropertyGuid = Guid.Parse(BookmarkGlobalEnabledPropertyId);
    public const string HistoryScopeId = "miokit-websearch-history";
    public const string BookmarkScopeId = "miokit-websearch-bookmarks";
    public const string WebsiteGroupId = "miokit-websearch-websites";

    public static readonly Guid SettingsType = Guid.Parse("D981A274-8D9B-48D0-8DF4-33BD6F1AF65A");
    public static readonly Guid HistoryScopeType = Guid.Parse("4E9149B1-74C2-4E6B-B1A7-2B8FF58F42A5");
    public static readonly Guid BookmarkScopeType = Guid.Parse("2A14CC9F-ED8C-4E35-9F6A-BDE41DE0BF02");
    public static readonly Guid WebsiteGroupType = Guid.Parse("7F64D3EB-15C8-4725-9D29-32D46FD11B25");
    public static readonly Guid WebsiteType = Guid.Parse("F3951B4C-8C6D-4A3A-839F-01BF0772D9C3");
    public static readonly Guid BrowserResultType = Guid.Parse("36C4B80A-8D7B-4D77-8F19-82002D87FD72");
    public const string BookmarkResultTypeId = "6B1E9A4C-3F27-4D80-9C5A-8E4B2F17D0A3";
    public static readonly Guid BookmarkResultType = Guid.Parse(BookmarkResultTypeId);

    public const string WebsiteSourceKindPropertyId = "B1288C3D-AFDB-42D1-9456-D789013EC066";
    public static readonly Guid WebsiteSourceKindPropertyGuid = Guid.Parse(WebsiteSourceKindPropertyId);

    public const string WebsiteCustomIconIdPropertyId = "A6A4D3FC-9720-4421-BE77-E749285A5E58";
    public static readonly Guid WebsiteCustomIconIdPropertyGuid = Guid.Parse(WebsiteCustomIconIdPropertyId);

    public const string WebsiteOpenModePropertyId = "0BF26E92-2A13-40B9-BD63-B384B4953A1C";
    public static readonly Guid WebsiteOpenModePropertyGuid = Guid.Parse(WebsiteOpenModePropertyId);

    public const string WebsiteLocalFilePathPropertyId = "FAB74F24-5165-499D-BBBE-FD7D32F0CE67";
    public static readonly Guid WebsiteLocalFilePathPropertyGuid = Guid.Parse(WebsiteLocalFilePathPropertyId);

    public const string WebsiteUserDataFolderPropertyId = "0071F3CF-BF60-4B70-8B47-6E8DAE605C58";
    public static readonly Guid WebsiteUserDataFolderPropertyGuid = Guid.Parse(WebsiteUserDataFolderPropertyId);

    public const string WebsiteMapLocalDirectoryPropertyId = "A354404A-52D2-4037-A081-8AE6E1040A1B";
    public static readonly Guid WebsiteMapLocalDirectoryPropertyGuid = Guid.Parse(WebsiteMapLocalDirectoryPropertyId);

    public const string WebsiteLocalHostNamePropertyId = "CCC1FFDF-6498-4301-B6D2-8B0069D11B3E";
    public static readonly Guid WebsiteLocalHostNamePropertyGuid = Guid.Parse(WebsiteLocalHostNamePropertyId);

    public const string WebsiteDebugEnabledPropertyId = "F6C38A37-D392-4546-9892-845193240A2C";
    public static readonly Guid WebsiteDebugEnabledPropertyGuid = Guid.Parse(WebsiteDebugEnabledPropertyId);

    public const string WebsiteRuntimeOptionsPropertyId = "752FF8E2-797E-4331-B53B-4B3BA4B4EEFB";
    public static readonly Guid WebsiteRuntimeOptionsPropertyGuid = Guid.Parse(WebsiteRuntimeOptionsPropertyId);

    public const string WebsiteStorageJsonPropertyId = "9B5A3DA5-BE47-4B6B-B650-617BF056AD24";
    public static readonly Guid WebsiteStorageJsonPropertyGuid = Guid.Parse(WebsiteStorageJsonPropertyId);
}
