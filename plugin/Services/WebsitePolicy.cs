using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WebSearch.Services;

/// <summary>Shared validation for the editor, navigation transport and file APIs.</summary>
public static class WebsitePolicy
{
    public static readonly string[] Permissions =
        ["storage", "files.read", "files.write", "clipboard.read", "clipboard.write", "notifications"];
    /// <summary>Removed toggles kept for loading older configs; ignored at runtime.</summary>
    public static readonly string[] ObsoletePermissions = ["dialogs", "external"];
    public static readonly string[] BrowserPermissions =
        ["Camera", "Microphone", "Geolocation", "Notifications", "ClipboardRead", "FileReadWrite"];
    public static readonly string[] Stages = ["DocumentCreated", "DomReady", "Loaded", "Closing"];

    public static string DefaultHost(string nodeId) =>
        "site-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nodeId)))[..16].ToLowerInvariant() + ".local";
    public static string Host(WebsiteConfiguration config, string nodeId) =>
        string.IsNullOrWhiteSpace(config.LocalHostName) ? DefaultHost(nodeId) : config.LocalHostName.Trim().ToLowerInvariant();
    public static string Entry(WebsiteConfiguration config, string nodeId, string onlineUrl)
    {
        if (config.SourceKind != "LocalFile") return onlineUrl;
        if (!config.MapLocalDirectory) return new Uri(Path.GetFullPath(config.LocalFilePath)).AbsoluteUri;
        return "https://" + Host(config, nodeId) + "/" + Uri.EscapeDataString(Path.GetFileName(config.LocalFilePath));
    }
    public static string DataDirectory(WebsiteConfiguration config, string nodeId, string pluginDataPath) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(config.UserDataFolder)
            ? Path.Combine(pluginDataPath, "websites", DefaultHost(nodeId), "browser")
            : config.UserDataFolder);

    public static string? Origin(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant() : null;

    public static bool IsTrusted(WebsiteConfiguration config, string nodeId, string onlineUrl, string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return false;
        if (uri.IsFile)
            return config.SourceKind == "LocalFile" && !config.MapLocalDirectory
                && IsWithin(uri.LocalPath, Path.GetDirectoryName(config.LocalFilePath)!)
                && HasNoReparsePoints(uri.LocalPath);
        var origin = Origin(address);
        if (origin is null) return false;
        return string.Equals(origin, Origin(Entry(config, nodeId, onlineUrl)), StringComparison.OrdinalIgnoreCase)
            || config.Runtime.TrustedOrigins.Any(x => string.Equals(Origin(x), origin, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsWithin(string path, string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var candidate = Path.GetFullPath(path);
        return candidate.Equals(root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    // Reject junctions/symlinks instead of attempting to authorize their alternate targets.
    public static bool HasNoReparsePoints(string path) => HasNoReparsePoints(path, null);

    internal static bool HasNoReparsePoints(string path, string? trustedAncestor)
    {
        var boundary = trustedAncestor is not null && IsWithin(path, trustedAncestor)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(trustedAncestor)) : null;
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            // The host owns this root and its ancestry; links inside a website's profile remain forbidden.
            if (boundary is not null && Path.TrimEndingDirectorySeparator(current).Equals(boundary, StringComparison.OrdinalIgnoreCase))
                return true;
            try
            {
                // Exists follows the target and returns false for a dangling link.
                // Inspect the entry itself so future writes cannot follow such a link.
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        return true;
    }
    public static bool IsValidHost(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 253
        && Regex.IsMatch(value, @"^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]([a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.IgnoreCase)
        && value.ToLowerInvariant() is not ("jsruntime.local" or "miokit.locale" or "localhost");

    public static string Validate(WebsiteEditorRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return "基本：网站名称不能为空。";
        var c = request.Configuration;
        if (c is null || c.Runtime is null) return "基本：设置不能为空。";
        if (c.Version != 1 || c.Runtime.Version != 1) return "基本：不支持此设置版本。";
        if (c.SourceKind is not ("Online" or "LocalFile") || c.OpenMode is not ("External" or "Embedded")) return "基本：网站类型或打开方式无效。";
        if (!Enum.TryParse<QuerySourcePolicy>(request.QuerySourcePolicy, out var queryPolicy) || !Enum.IsDefined(queryPolicy))
            return "基本：查询来源无效。";
        if (c.SourceKind == "Online")
        {
            if (!WebsiteUrlResolver.TryValidateHttpUrl(request.UrlTemplate, out var online)
                || !string.IsNullOrEmpty(online.UserInfo)) return "基本：请输入有效的网址。";
            // The search token must never change the origin or its associated permissions.
            if (c.OpenMode == "Embedded" && Regex.IsMatch(request.UrlTemplate, @"^https?://[^/?#]*\{query\}", RegexOptions.IgnoreCase))
                return "基本：{query} 只能出现在网址路径或问号之后，不能出现在网站名称中。";
        }
        else if (!Path.IsPathFullyQualified(c.LocalFilePath) || !File.Exists(c.LocalFilePath)
            || !HasNoReparsePoints(c.LocalFilePath)
            || !new[] { ".html", ".htm" }.Contains(Path.GetExtension(c.LocalFilePath), StringComparer.OrdinalIgnoreCase))
            return "基本：请选择存在的本地网页文件。";
        // External browsers own their profiles and never use these embedded-window settings.
        // Keep dormant settings so switching back can validate and reuse them.
        if (c.OpenMode == "External") return "";
        if (!string.IsNullOrWhiteSpace(c.UserDataFolder) && !Path.IsPathFullyQualified(c.UserDataFolder))
            return "目录：数据目录必须是完整路径。";
        if (!string.IsNullOrWhiteSpace(c.LocalHostName) && !IsValidHost(c.LocalHostName.Trim().ToLowerInvariant()))
            return "映射：本地访问名无效或属于保留名称。";
        var r = c.Runtime;
        if (r.CurrentNavigation is not ("Current" or "ExternalUntrusted")
            || r.NewWindowMode is not ("NewWindow" or "Current" or "External" or "Block")) return "导航：窗口打开方式无效。";
        if (r.Permissions is null || r.TrustedOrigins is null || r.Mappings is null || r.FileDirectories is null
            || r.Scripts is null || r.BrowserPermissions is null) return "基本：设置列表不能为空。";
        r.Permissions.RemoveAll(static p => ObsoletePermissions.Contains(p));
        if (r.Permissions.Except(Permissions).Any() || r.BrowserPermissions.Except(BrowserPermissions).Any())
            return "权限：包含未知权限。";
        if (r.TrustedOrigins.Any(x => Origin(x) is not { } origin
            || !string.Equals(x.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase)))
            return "权限：可信来源请填写完整网站根地址，不要带路径或通配符。";
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Host(c, request.Id) };
        foreach (var mapping in r.Mappings)
            if (mapping is null || !IsValidHost(mapping.Host) || !hosts.Add(mapping.Host) || !Path.IsPathFullyQualified(mapping.Directory)
                || !Directory.Exists(mapping.Directory) || !HasNoReparsePoints(mapping.Directory)
                || mapping.Access is not ("DenyCors" or "Allow" or "Deny"))
                return "目录映射：请检查本地访问名是否重复、目录是否存在，以及其他网站访问设置。";
        foreach (var grant in r.FileDirectories)
            if (grant is null || !Path.IsPathFullyQualified(grant.Directory) || !Directory.Exists(grant.Directory) || !HasNoReparsePoints(grant.Directory))
                return "权限：授权目录必须存在，且不能是快捷方式或链接目录。";
        if (r.Scripts.Any(x => x is null || !Stages.Contains(x.Stage))) return "脚本：执行时机无效。";
        return "";
    }
}

