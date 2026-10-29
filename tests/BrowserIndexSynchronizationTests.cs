using System.Text.Json;
using Microsoft.Data.Sqlite;
using WebSearch.Services;

namespace MioKit.WebSearch.Tests;

[Collection("Website backend")]
public sealed class BrowserIndexSynchronizationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "websearch-sync-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FaviconService favicon;
    private readonly BrowserIndexService index;

    public BrowserIndexSynchronizationTests(WebsiteBackendFixture _)
    {
        var plugin = TestPluginContext.CreatePlugin(root);
        favicon = new(plugin); // Kept stopped: tests never contact websites or real browser profiles.
        index = new(new BrowserCatalogService(), plugin, favicon);
    }

    [Theory]
    [InlineData("{broken json", false)]
    [InlineData("{}", false)]
    [InlineData("{\"roots\":[]}", false)]
    [InlineData(null, false)]
    [InlineData("not a sqlite database", true)]
    public async Task FailedProfileKeepsEntirePreviousSnapshotAndReportsError(string? content, bool firefox)
    {
        var ct = TestContext.Current.CancellationToken;
        var good = Profile("Good");
        await WriteBookmarks(good, "old bookmark", "https://old.test/", ct);
        await index.SynchronizeAsync([good], ct);
        var previousTime = index.LastSynchronizedAt;
        var previousOrigins = index.BookmarkOrigins.ToArray();
        var previousBytes = await File.ReadAllBytesAsync(Path.Combine(root, "browser-index.db"), ct);
        Assert.NotNull(previousTime);

        await WriteBookmarks(good, "new bookmark", "https://new.test/", ct);
        var broken = Profile("Broken", firefox);
        if (content is not null)
            await File.WriteAllTextAsync(Path.Combine(broken.DirectoryPath, firefox ? "places.sqlite" : "Bookmarks"), content, ct);
        await index.SynchronizeAsync([good, broken], ct);

        Assert.Contains("Broken", index.LastError);
        Assert.Contains("已保留", index.LastError);
        Assert.False(index.IsSynchronizing);
        Assert.Equal(previousTime, index.LastSynchronizedAt);
        Assert.Equal(1, index.BookmarkCount);
        Assert.Equal(1, index.ProfileCount);
        Assert.Equal(previousOrigins, index.BookmarkOrigins);
        Assert.Equal(previousBytes, await File.ReadAllBytesAsync(Path.Combine(root, "browser-index.db"), ct));
        Assert.Single(await index.SearchAsync("old bookmark", 50, ct));
        Assert.Empty(await index.SearchAsync("new bookmark", 50, ct));
        Assert.Empty(Directory.GetFiles(root, "*.new"));

        // A later successful pass can still publish and clears the error.
        await index.SynchronizeAsync([good], ct);
        Assert.Empty(index.LastError);
        Assert.Single(await index.SearchAsync("new bookmark", 50, ct));
        Assert.Empty(await index.SearchAsync("old bookmark", 50, ct));
    }

    [Fact]
    public async Task DistinctRoutesSurviveAndDuplicateSourcesDoNotOverwriteDisplayedMetadata()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Profile("First");
        var second = Profile("Second");
        var routeB = Profile("RouteB");
        var slash = Profile("Slash");
        var noSlash = Profile("NoSlash");
        await WriteBookmarks(first, "route first", "https://example.test/#/a", ct);
        await WriteBookmarks(second, "route second", "https://EXAMPLE.test/#/a", ct);
        await WriteBookmarks(routeB, "route b", "https://example.test/#/b", ct);
        await WriteBookmarks(slash, "route slash", "https://example.test/path/", ct);
        await WriteBookmarks(noSlash, "route no slash", "https://example.test/path", ct);

        await index.SynchronizeAsync([first, second, routeB, slash, noSlash], ct);

        Assert.Empty(index.LastError);
        Assert.Equal(4, index.BookmarkCount);
        var hits = await index.SearchAsync("route", 50, ct);
        Assert.Equal(4, hits.Count);
        var retained = Assert.Single(hits, x => x.Url.EndsWith("#/a", StringComparison.Ordinal));
        Assert.Equal("route first", retained.Title);
        Assert.Equal("First", retained.Profile);

        await index.SynchronizeAsync([noSlash, slash, routeB, second, first], ct);
        Assert.Equal(hits, await index.SearchAsync("route", 50, ct));

        await using var connection = SqliteDatabaseCopy.CreateConnection(Path.Combine(root, "browser-index.db"), true);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT profile, title FROM bookmark_sources WHERE normalized_url = $url ORDER BY profile;";
        command.Parameters.AddWithValue("$url", "https://example.test/#/a");
        await using var reader = await command.ExecuteReaderAsync(ct);
        Assert.True(await reader.ReadAsync(ct));
        Assert.Equal("First", reader.GetString(0));
        Assert.Equal("route first", reader.GetString(1));
        Assert.True(await reader.ReadAsync(ct));
        Assert.Equal("Second", reader.GetString(0));
        Assert.Equal("route second", reader.GetString(1));
        Assert.False(await reader.ReadAsync(ct));
    }

    [Fact]
    public async Task DuplicateWithTitleSuppliesOneCompleteRepresentative()
    {
        var ct = TestContext.Current.CancellationToken;
        var unnamed = Profile("AUnnamed");
        var named = Profile("BNamed");
        await WriteBookmarks(unnamed, "", "https://example.test/", ct);
        await WriteBookmarks(named, "named bookmark", "https://example.test/", ct);
        await index.SynchronizeAsync([unnamed, named], ct);

        Assert.Empty(index.LastError);
        Assert.Equal(1, index.BookmarkCount);
        var hit = Assert.Single(await index.SearchAsync("named bookmark", 50, ct));
        Assert.Equal("BNamed", hit.Profile);
    }

    [Fact]
    public async Task ValidEmptyBookmarkFileCanReplacePreviousSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var profile = Profile("Empty");
        await WriteBookmarks(profile, "old bookmark", "https://old.test/", ct);
        await index.SynchronizeAsync([profile], ct);
        await File.WriteAllTextAsync(Path.Combine(profile.DirectoryPath, "Bookmarks"), "{\"roots\":{}}", ct);
        await index.SynchronizeAsync([profile], ct);
        Assert.Empty(index.LastError);
        Assert.Equal(0, index.BookmarkCount);
        Assert.Empty(await index.SearchAsync("old bookmark", 50, ct));
    }

    private BrowserProfileSource Profile(string name, bool firefox = false)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return new(firefox ? "firefox" : "chrome", firefox ? "Firefox" : "Chrome", name, path, firefox);
    }

    private static Task WriteBookmarks(BrowserProfileSource profile, string title, string url, CancellationToken ct) =>
        File.WriteAllTextAsync(Path.Combine(profile.DirectoryPath, "Bookmarks"), JsonSerializer.Serialize(new
        {
            roots = new { bookmark_bar = new { type = "folder", children = new[] { new { type = "url", name = title, url } } } }
        }), ct);

    public void Dispose()
    {
        index.Dispose();
        favicon.Dispose();
        var absolute = Path.GetFullPath(root);
        if (!absolute.StartsWith(Path.Combine(Path.GetTempPath(), "websearch-sync-tests-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected test directory.");
        Directory.Delete(absolute, true);
    }
}
