using Avalonia.Platform.Storage;
using System.Text.Json;

namespace WebSearch.Services;

/// <summary>Per-document capability context; invalidated before the next document is exposed.</summary>
public sealed class WebsitePageContext(
    WebsiteNode node, WebsiteConfiguration configuration, string onlineUrl,
    Func<string> address, Func<bool> active, TopLevel owner, WebsiteFileGrantSession? grants = null)
{
    public WebsiteNode Node { get; } = node;
    public WebsiteConfiguration Configuration { get; } = configuration;
    public TopLevel Owner { get; } = owner;
    public string Address => address();
    private readonly WebsiteFileGrantSession fileGrants = grants ?? new();

    public void EnsureDocument()
    {
        if (!active() || !node.IsAttachRootTree
            || !WebsitePolicy.IsTrusted(configuration, node.Id, onlineUrl, Address))
            throw new WebsiteAccessException("document.expired", "当前页面没有可用的授权会话。");
    }
    public void Check(string permission)
    {
        EnsureDocument();
        if (!(node.GetRuntimeOptions()?.Permissions ?? []).Contains(permission))
            throw new WebsiteAccessException("permission.denied", "尚未授权：" + permission);
    }
    public string AuthorizePath(string path, bool write)
    {
        Check(write ? "files.write" : "files.read");
        if (!Path.IsPathFullyQualified(path))
            throw new WebsiteAccessException("path.invalid", "文件路径必须是绝对路径。");
        var full = Path.GetFullPath(path);
        if (!WebsitePolicy.HasNoReparsePoints(full))
            throw new WebsiteAccessException("path.denied", "不允许通过快捷方式或链接目录访问文件。");
        var persistent = node.GetRuntimeOptions()?.FileDirectories ?? [];
        var temporary = fileGrants.Allows(full, write);
        if (persistent.Any(g => (write ? g.Write : g.Read) && WebsitePolicy.IsWithin(full, g.Directory)) || temporary)
            return full;
        throw new WebsiteAccessException("path.denied", "文件不在已授权的目录或所选文件范围内。");
    }
    public void Grant(string path, bool directory, bool write)
        => fileGrants.Grant(path, directory, write);
}

/// <summary>One window's temporary selections; shared by its documents, never by sibling windows.</summary>
public sealed class WebsiteFileGrantSession
{
    private readonly List<(string Path, bool Directory, bool Write)> grants = [];
    private readonly object gate = new();
    public void Grant(string path, bool directory, bool write)
    {
        lock (gate) grants.Add((Path.GetFullPath(path), directory, write));
    }
    public bool Allows(string path, bool write)
    {
        lock (gate) return grants.Any(g => (!write || g.Write)
            && (g.Directory ? WebsitePolicy.IsWithin(path, g.Path) : path.Equals(g.Path, StringComparison.OrdinalIgnoreCase)));
    }
    public void Clear() { lock (gate) grants.Clear(); }
}
public sealed class WebsiteAccessException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

