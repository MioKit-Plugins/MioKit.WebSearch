namespace WebSearch.Services;

// Keep decisions independent of WebView controllers so every navigation and popup
// mode can be checked against the same rules used by real windows.
internal static class WebsiteNavigationPolicy
{
    public static bool Allows(WebsiteConfiguration config, string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme is "http" or "https" || (uri.IsFile && config.SourceKind == "LocalFile"
            && WebsitePolicy.IsWithin(uri.LocalPath, Path.GetDirectoryName(config.LocalFilePath)!)
            && WebsitePolicy.HasNoReparsePoints(uri.LocalPath)) || address == "about:blank";
    }

    public static bool OpensExternally(WebsiteConfiguration config, string nodeId, string onlineUrl, string address) =>
        config.Runtime.CurrentNavigation == "ExternalUntrusted"
        && !WebsitePolicy.IsTrusted(config, nodeId, onlineUrl, address)
        && WebsiteUrlResolver.TryValidateHttpUrl(address, out _);

    public static WebsitePopupDecision Popup(WebsiteConfiguration config, WebsiteRuntimeOptions runtime,
        string address, bool userInitiated, bool revoked)
    {
        if (revoked) return WebsitePopupDecision.Revoked;
        if (!Allows(config, address)) return WebsitePopupDecision.Unsupported;
        if (!userInitiated && !runtime.AllowAutomaticPopups) return WebsitePopupDecision.AutomaticBlocked;
        return runtime.NewWindowMode switch
        {
            "NewWindow" => WebsitePopupDecision.NewWindow,
            "Current" => WebsitePopupDecision.Current,
            "External" => WebsitePopupDecision.External,
            _ => WebsitePopupDecision.Block
        };
    }
}

internal enum WebsitePopupDecision { Revoked, Unsupported, AutomaticBlocked, Block, Current, External, NewWindow }
