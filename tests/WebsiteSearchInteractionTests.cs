using System.Reflection;
using MioKit.Sdk;
using WebSearch.Features;
using WebSearch.Models;
using WebSearch.Nodes;

namespace MioKit.WebSearch.Tests;

[Collection("Website backend")]
public sealed class WebsiteSearchInteractionTests : IDisposable
{
    private readonly PropertyInfo matcherProperty = typeof(SearchHelper).GetProperty("TextMatcher", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly object? previousMatcher;
    private readonly WebsiteSearchGroup group = new("search-test");
    public WebsiteSearchInteractionTests(WebsiteBackendFixture _)
    {
        previousMatcher = matcherProperty.GetValue(null);
        matcherProperty.SetValue(null, new ContainsMatcher());
    }
    public void Dispose() => matcherProperty.SetValue(null, previousMatcher);

    private WebsiteNode Add(string name, string url, bool local = false)
    {
        var node = new WebsiteNode(Guid.NewGuid().ToString("N")) { Name = name, UrlTemplate = url };
        if (local) { node.SetSourceKind("LocalFile"); node.SetLocalFilePath(url); }
        node.SetParent(group);
        return node;
    }
    private async Task<Results> Search(string? query, string? clipboard = null)
    {
        var context = new InvocationSnapshot();
        var clip = new ClipboardSnapshot();
        typeof(ClipboardSnapshot).GetProperty(nameof(ClipboardSnapshot.Text))!.SetValue(clip, clipboard);
        typeof(InvocationSnapshot).GetProperty(nameof(InvocationSnapshot.Clipboard))!.SetValue(context, clip);
        var results = new Results();
        await group.SearchAsync(new() { SearchText = query, Context = context }, results, TestContext.Current.CancellationToken);
        return results;
    }

    [Theory]
    [InlineData(null, QuerySourcePolicy.InputThenClipboard)]
    [InlineData("", QuerySourcePolicy.InputThenClipboard)]
    [InlineData(" \t", QuerySourcePolicy.InputThenClipboard)]
    [InlineData(null, QuerySourcePolicy.ClipboardOnly)]
    [InlineData("", QuerySourcePolicy.ClipboardOnly)]
    [InlineData(" \t", QuerySourcePolicy.ClipboardOnly)]
    public async Task EmptyInputUsesClipboardForEligibleTemplates(string? query, QuerySourcePolicy policy)
    {
        var site = Add("Search", "https://search.test/{QUERY}?q={query}");
        site.QuerySourcePolicy = policy;
        var result = Assert.Single(await Search(query, "复制 中文 & text"));
        Assert.Same(site, result.OwnerObject);
        Assert.Contains("复制 中文 & text", result.Title);
        var encoded = Uri.EscapeDataString("复制 中文 & text");
        Assert.Equal("https://search.test/" + encoded + "?q=" + encoded, Assert.IsType<OpenUrlPayload>(result.Payload).Url);
        Assert.Equal(SortPriority.AlwaysBottom, result.PinMode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task InputOnlyTemplateDoesNotUseClipboardWhenInputIsEmpty(string? query)
    {
        Add("Search", "https://search.test/?q={query}").QuerySourcePolicy = QuerySourcePolicy.InputOnly;
        Assert.Empty(await Search(query, "clipboard"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(" \t", " \r\n")]
    public async Task EmptyInputAndEmptyClipboardDoNotContributeAnyWebsite(string? query, string? clipboard)
    {
        foreach (var policy in Enum.GetValues<QuerySourcePolicy>())
            Add(policy.ToString(), "https://search.test/?q={query}").QuerySourcePolicy = policy;
        Add("Fixed", "https://fixed.test/");
        Assert.Empty(await Search(query, clipboard));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task ClipboardOnlyTemplateDoesNotOfferAnEmptySearchWhenClipboardHasNoText(string? clipboard)
    {
        Add("Search", "https://search.test/?q={query}").QuerySourcePolicy = QuerySourcePolicy.ClipboardOnly;
        Assert.Empty(await Search("typed query", clipboard));
    }

    [Fact]
    public async Task ClipboardSearchExcludesFixedPagesLocalFilesIgnoredAndInputOnlyTemplates()
    {
        var eligible = Add("Search", "https://search.test/?q={query}");
        Add("Input", "https://input.test/?q={query}").QuerySourcePolicy = QuerySourcePolicy.InputOnly;
        Add("Ignored", "https://ignored.test/?q={query}").SetIgnoreSearch(true);
        Add("clipboard", "https://clipboard.test/");
        Add("clipboard", Path.Combine(Path.GetTempPath(), "{query}", "clipboard.html"), local: true);
        var result = Assert.Single(await Search("", "clipboard"));
        Assert.Same(eligible, result.OwnerObject);
    }

    [Fact]
    public async Task ClipboardSearchKeepsEachResultSnapshotAndDropsActionsAfterClipboardIsCleared()
    {
        Add("Search", "https://search.test/?q={query}");
        var first = Assert.Single(await Search("", "first copied text"));
        var second = Assert.Single(await Search("", "second 中文"));
        Assert.Equal("https://search.test/?q=first%20copied%20text", Assert.IsType<OpenUrlPayload>(first.Payload).Url);
        Assert.Equal("https://search.test/?q=second%20%E4%B8%AD%E6%96%87", Assert.IsType<OpenUrlPayload>(second.Payload).Url);
        Assert.Empty(await Search(""));
    }

    [Theory]
    [InlineData(QuerySourcePolicy.InputOnly, "typed 中文")]
    [InlineData(QuerySourcePolicy.InputThenClipboard, "typed 中文")]
    [InlineData(QuerySourcePolicy.ClipboardOnly, "copied & text")]
    public async Task TemplateResultUsesConfiguredQuerySnapshotAndCarriesTheExecutableNode(QuerySourcePolicy policy, string expected)
    {
        var site = Add("Search", "https://search.test/{QUERY}?q={query}");
        site.QuerySourcePolicy = policy;
        var result = Assert.Single(await Search("typed 中文", "copied & text"));
        Assert.Same(site, result.OwnerObject);
        Assert.Equal(SortPriority.AlwaysBottom, result.PinMode);
        Assert.Contains(expected, result.Title);
        Assert.Equal("https://search.test/" + Uri.EscapeDataString(expected) + "?q=" + Uri.EscapeDataString(expected), Assert.IsType<OpenUrlPayload>(result.Payload).Url);
    }

    [Fact]
    public async Task AliasMatchUsesAliasAsTitleAndOriginalAddressAsPayload()
    {
        var node = Add("Tools", "https://example.test/");
        node.SetAliasName(["开发文档"]);
        var result = Assert.Single(await Search("开发"));
        Assert.Equal("开发文档", result.Title);
        Assert.Same(node, result.OwnerObject);
        Assert.Equal(node.UrlTemplate, Assert.IsType<OpenUrlPayload>(result.Payload).Url);
    }

    [Fact]
    public async Task LocalFilenameContainingQueryTokenIsSearchedAsAFileRatherThanATemplate()
    {
        var path = Path.Combine(Path.GetTempPath(), "websearch-{query}", "local.html");
        var local = Add("本地工作台", path, local: true);
        var result = Assert.Single(await Search("工作台"));
        Assert.Same(local, result.OwnerObject);
        Assert.Equal(SortPriority.Normal, result.PinMode);
        Assert.Equal(path, Assert.IsType<OpenUrlPayload>(result.Payload).Url);
        Assert.Empty(await Search("unrelated"));
    }

    [Fact]
    public async Task IgnoreSearchExcludesBothFixedWebsitesAndTemplates()
    {
        Add("Site", "https://fixed.test/").SetIgnoreSearch(true);
        Add("Search", "https://search.test/?q={query}").SetIgnoreSearch(true);
        Assert.Empty(await Search("Site"));
    }

    [Fact]
    public async Task InvalidWebAddressesNeverAppearEvenWhenTheirNameMatches()
    {
        Add("unsafe", "javascript:alert(1)");
        Add("unsafe template", "file:///C:/{query}");
        Assert.Empty(await Search("unsafe"));
    }

    [Fact]
    public async Task FixedWebsiteMatchesOnlyNameAndAliasNotDescriptionOrUrl()
    {
        var node = Add("测试", "https://example.com/checkin");
        node.Description = "contains co in description";
        Assert.Empty(await Search("co"));
        Assert.Empty(await Search("checkin"));
        Assert.Empty(await Search("description"));

        var byName = Assert.Single(await Search("测试"));
        Assert.Same(node, byName.OwnerObject);

        node.SetAliasName(["签到入口"]);
        var byAlias = Assert.Single(await Search("签到"));
        Assert.Equal("签到入口", byAlias.Title);
        Assert.Same(node, byAlias.OwnerObject);
    }

    [Fact]
    public async Task CancelledSearchStopsBeforeEmittingAnyResult()
    {
        Add("Search", "https://search.test/?q={query}");
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var results = new Results();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => group.SearchAsync(new() { SearchText = "query" }, results, cts.Token).AsTask());
        Assert.Empty(results);
    }

    private sealed class Results : List<SearchResult>, IWriteOnlyResultList<SearchResult>
    {
        public bool IsEmpty => Count == 0;
        List<SearchResult> IWriteOnlyResultList<SearchResult>.ToList() => [.. this];
    }
    // Only the host text matcher is substituted; node traversal, aliases, ignore flags,
    // template expansion and result construction all run through production code.
    private sealed class ContainsMatcher : ITextMatcher
    {
        public TextMatchResult Match(string text, string query, MatchOptions? options = null) =>
            text.Contains(query, StringComparison.OrdinalIgnoreCase)
                ? TextMatchResult.Success(TextMatchType.Contains, [0]) : TextMatchResult.Failure();
        public Task<TextMatchResult> MatchAsync(string text, string query, MatchOptions? options = null) => throw new InvalidOperationException("Search must use the synchronous matcher.");
        public void CacheKey(string key) { }
        public void CacheKeys(IEnumerable<string>? keys) { }
        public void ClearCache() { }
    }
}
