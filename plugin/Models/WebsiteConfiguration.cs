namespace WebSearch.Models;

public sealed class WebsiteConfiguration
{
    public int Version { get; set; } = 1;
    public string SourceKind { get; set; } = "Online";
    public string OpenMode { get; set; } = "External";
    public string LocalFilePath { get; set; } = "";
    public string UserDataFolder { get; set; } = "";
    public bool MapLocalDirectory { get; set; } = true;
    public string LocalHostName { get; set; } = "";
    public bool DebugEnabled { get; set; }
    public WebsiteRuntimeOptions Runtime { get; set; } = new();
}

public sealed class WebsiteRuntimeOptions
{
    public int Version { get; set; } = 1;
    public string CurrentNavigation { get; set; } = "Current";
    public string NewWindowMode { get; set; } = "NewWindow";
    public bool AllowAutomaticPopups { get; set; }
    public List<string> TrustedOrigins { get; set; } = [];
    public List<VirtualHostMapping> Mappings { get; set; } = [];
    public List<WebsiteScript> Scripts { get; set; } = [];
    public List<string> Permissions { get; set; } = [];
    public List<FileDirectoryGrant> FileDirectories { get; set; } = [];
    public List<string> BrowserPermissions { get; set; } = [];
}

public sealed class VirtualHostMapping
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "";
    public string Directory { get; set; } = "";
    public string Access { get; set; } = "DenyCors";
}
public sealed class FileDirectoryGrant
{
    public string Directory { get; set; } = "";
    public bool Read { get; set; } = true;
    public bool Write { get; set; }
}
public sealed class WebsiteScript
{
    public string Name { get; set; } = "新脚本";
    public bool Enabled { get; set; } = true;
    public string Stage { get; set; } = "DomReady";
    public string Code { get; set; } = "";
}
public sealed class WebsiteEditorRequest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string UrlTemplate { get; set; } = "";
    public string QuerySourcePolicy { get; set; } = "InputThenClipboard";
    public WebsiteConfiguration Configuration { get; set; } = new();
    public string EffectiveDataDirectory { get; set; } = "";
    public bool RequiresReopen { get; set; }
    public string IconAction { get; set; } = "Keep";
    public string IconSourcePath { get; set; } = "";
    public bool HasCustomIcon { get; set; }
    public string IconDataUrl { get; set; } = "";
    public string IconState { get; set; } = "";
}
public sealed record WebsiteIconPickResult(string SourcePath, string PreviewDataUrl);
public sealed record WebsiteIconPreview(bool HasCustomIcon, string IconDataUrl, string IconState);
public sealed record WebsiteSaveResult(bool Success, string Id, string Error, bool RequiresReopen = false);
public sealed record MioApiResult(bool Success, object? Value, string? Code, string? Message);

