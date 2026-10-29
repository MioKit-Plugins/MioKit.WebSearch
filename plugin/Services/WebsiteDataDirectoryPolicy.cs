namespace WebSearch.Services;

internal static class WebsiteDataDirectoryPolicy
{
    public static void ValidateCustomLocation(string? path, string pluginPath, string pluginDataPath,
        string appData, string javaScriptRuntime)
    {
        // An unset value uses a host-owned default, including Debugger's app_data inside PluginPath.
        // Validate the user's override, not the resolved default directory.
        if (string.IsNullOrWhiteSpace(path)) return;
        path = Path.GetFullPath(path);
        if (!WebsitePolicy.HasNoReparsePoints(path, pluginDataPath) || File.Exists(path)
            || WebsitePolicy.IsWithin(appData, path)
            || WebsitePolicy.IsWithin(path, pluginPath)
            || Path.TrimEndingDirectorySeparator(Path.GetFullPath(pluginDataPath)).Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), StringComparison.OrdinalIgnoreCase)
            || WebsitePolicy.IsWithin(path, Path.Combine(appData, "webview2"))
            || WebsitePolicy.IsWithin(path, javaScriptRuntime))
            throw new InvalidOperationException("数据目录不能使用系统保留位置或链接路径。");
    }

    public static void ValidateIndependent(string path, string otherPath)
    {
        if (WebsitePolicy.IsWithin(path, otherPath) || WebsitePolicy.IsWithin(otherPath, path))
            throw new InvalidOperationException("数据目录与其他网站重叠，请选择独立目录。");
    }
}
