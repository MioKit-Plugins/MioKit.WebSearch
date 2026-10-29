namespace WebSearch.Models;

public sealed record OpenUrlPayload(string Url);

public sealed record BrowserIndexHit(
    string Title,
    string Url,
    string Browser,
    string Profile,
    string? Folder);

public sealed record BookmarkBrowseItem(
    string Title,
    string Url,
    string Browser,
    string Profile,
    string? Folder,
    string FaviconDataUrl);

public sealed record BookmarkBrowsePage(
    IReadOnlyList<BookmarkBrowseItem> Items,
    int Total);

public sealed record WebsiteDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string UrlTemplate { get; init; } = string.Empty;
    public string QuerySourcePolicy { get; init; } = nameof(Models.QuerySourcePolicy.InputThenClipboard);
    public bool IsSearchTemplate { get; init; }
    public string SourceKind { get; init; } = "Online";
    public string OpenMode { get; init; } = "External";
    public string IconState { get; init; } = "暂无图标";
    public string FaviconDataUrl { get; init; } = string.Empty;
    public bool HasCustomIcon { get; init; }
    public string IconDataUrl { get; init; } = string.Empty;
}

public sealed class BrowserOptionDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsAvailable { get; init; }
    public string Path { get; init; } = string.Empty;
}

public sealed class BrowserProfileDto
{
    public string Browser { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}
