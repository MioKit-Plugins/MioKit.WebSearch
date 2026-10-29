using WebSearch.Models;
using WebSearch.Services;

namespace MioKit.WebSearch.Tests;

public sealed class WebsiteNavigationPolicyTests
{
    [Theory]
    [InlineData("NewWindow", true, false, "NewWindow")]
    [InlineData("Current", true, false, "Current")]
    [InlineData("External", true, false, "External")]
    [InlineData("Block", true, false, "Block")]
    [InlineData("NewWindow", false, false, "AutomaticBlocked")]
    [InlineData("Current", false, false, "AutomaticBlocked")]
    [InlineData("External", false, false, "AutomaticBlocked")]
    [InlineData("Block", false, false, "AutomaticBlocked")]
    [InlineData("NewWindow", false, true, "NewWindow")]
    [InlineData("Current", false, true, "Current")]
    [InlineData("External", false, true, "External")]
    [InlineData("Block", false, true, "Block")]
    [InlineData("Unknown", true, true, "Block")]
    public void PopupModesRespectUserGestureAndAutomaticPopupSetting(string mode, bool userInitiated, bool automatic, string expected)
    {
        var config = new WebsiteConfiguration();
        config.Runtime.NewWindowMode = mode;
        config.Runtime.AllowAutomaticPopups = automatic;
        Assert.Equal(expected, WebsiteNavigationPolicy.Popup(config, config.Runtime, "https://login.test/", userInitiated, false).ToString());
        Assert.Equal(WebsitePopupDecision.Revoked, WebsiteNavigationPolicy.Popup(config, config.Runtime, "https://login.test/", userInitiated, true));
        Assert.Equal(WebsitePopupDecision.Unsupported, WebsiteNavigationPolicy.Popup(config, config.Runtime, "javascript:alert(1)", userInitiated, false));
    }

    [Theory]
    [InlineData("https://entry.test/next", true, false)]
    [InlineData("http://entry.test/", true, true)]
    [InlineData("https://entry.test:444/", true, true)]
    [InlineData("https://other.test/", true, true)]
    [InlineData("https://extra.test/page", true, false)]
    [InlineData("about:blank", true, false)]
    [InlineData("data:text/html,hi", false, false)]
    [InlineData("javascript:alert(1)", false, false)]
    [InlineData("file:///C:/private.html", false, false)]
    [InlineData("relative", false, false)]
    public void NavigationAndExternalRoutingUseExactTrustAndSupportedProtocols(string address, bool allowed, bool external)
    {
        var config = new WebsiteConfiguration();
        config.Runtime.TrustedOrigins.Add("https://extra.test");
        Assert.Equal(allowed, WebsiteNavigationPolicy.Allows(config, address));
        Assert.False(WebsiteNavigationPolicy.OpensExternally(config, "site", "https://entry.test", address));
        config.Runtime.CurrentNavigation = "ExternalUntrusted";
        Assert.Equal(external, WebsiteNavigationPolicy.OpensExternally(config, "site", "https://entry.test", address));
    }

    [Fact]
    public void LocalNavigationStaysInsideEntryDirectoryWithAndWithoutHostMapping()
    {
        var path = Path.Combine(Path.GetTempPath(), "websearch-navigation-" + Guid.NewGuid().ToString("N"), "index.html");
        var config = new WebsiteConfiguration { SourceKind = "LocalFile", LocalFilePath = path };
        foreach (var mapped in new[] { true, false })
        {
            config.MapLocalDirectory = mapped;
            Assert.True(WebsiteNavigationPolicy.Allows(config, new Uri(path).AbsoluteUri));
            Assert.True(WebsiteNavigationPolicy.Allows(config, new Uri(Path.Combine(Path.GetDirectoryName(path)!, "nested", "other.html")).AbsoluteUri));
            Assert.False(WebsiteNavigationPolicy.Allows(config, new Uri(Path.Combine(Path.GetDirectoryName(path)! + "-sibling", "private.html")).AbsoluteUri));
            Assert.False(WebsiteNavigationPolicy.Allows(config, new Uri(Path.Combine(Path.GetDirectoryName(path)!, "..", "private.html")).AbsoluteUri));
        }
    }
}
