using WebSearch.Models;
using Plugin = global::WebSearch.WebSearchPlugin;

namespace MioKit.WebSearch.Tests;

public sealed class LegacyDefaultWebsiteTests
{
    [Fact]
    public void IsLegacyDefaultWebsite_MatchesExactHistoricalSeed()
    {
        Assert.True(Plugin.IsLegacyDefaultWebsite(
            "websearch-site-existing",
            "Bing",
            "使用 Bing 搜索网页",
            "https://www.bing.com/search?q={query}",
            QuerySourcePolicy.InputThenClipboard));
    }

    [Fact]
    public void IsLegacyDefaultWebsite_DoesNotMatchUserModifiedWebsite()
    {
        Assert.False(Plugin.IsLegacyDefaultWebsite(
            "websearch-site-existing",
            "Bing",
            "我的常用搜索",
            "https://www.bing.com/search?q={query}",
            QuerySourcePolicy.InputThenClipboard));
    }
}
