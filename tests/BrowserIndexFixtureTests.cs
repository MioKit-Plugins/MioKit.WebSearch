using System.Text.Json;
using Microsoft.Data.Sqlite;
using WebSearch.Services;

namespace WebSearch.Tests;

public sealed class BrowserIndexFixtureTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"miokit-websearch-tests-{Guid.NewGuid():N}");

    public BrowserIndexFixtureTests()
    {
        Directory.CreateDirectory(_directory);
        SQLitePCL.Batteries_V2.Init();
    }

    [Fact]
    public void RecursivelyParsesChromiumBookmarkFolders()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "bookmark_bar": {
                "type": "folder",
                "name": "书签栏",
                "children": [
                  {
                    "type": "folder",
                    "name": "开发",
                    "children": [
                      { "type": "url", "name": "MioKit", "url": "https://example.test/miokit" }
                    ]
                  },
                  { "type": "url", "name": "Bing", "url": "https://www.bing.com/" }
                ]
              }
            }
            """);

        var entries = BrowserIndexService.ParseChromiumBookmarks(document.RootElement);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, x => x.Title == "MioKit" && x.Folder == "bookmark_bar/书签栏/开发");
        Assert.Contains(entries, x => x.Title == "Bing" && x.Folder == "bookmark_bar/书签栏");
    }

    [Theory]
    [InlineData("https://example.test/#/a", "https://example.test/#/b")]
    [InlineData("https://example.test/path", "https://example.test/path/")]
    [InlineData("https://example.test/?q=a", "https://example.test/?q=a/")]
    [InlineData("https://example.test/path#one", "https://example.test/path#two")]
    public void NormalizedUrlPreservesDistinctDestinations(string first, string second)
    {
        Assert.NotEqual(BrowserIndexService.NormalizeUrl(new Uri(first)), BrowserIndexService.NormalizeUrl(new Uri(second)));
    }

    [Theory]
    [InlineData("https://EXAMPLE.test", "https://example.test/")]
    [InlineData("https://EXAMPLE.test:443/path/#section", "https://example.test/path/#section")]
    public void NormalizedUrlStillDeduplicatesEquivalentAuthorities(string first, string second)
    {
        Assert.Equal(BrowserIndexService.NormalizeUrl(new Uri(first)), BrowserIndexService.NormalizeUrl(new Uri(second)));
    }

    [Fact]
    public void SnapshotReplacementNeverLeavesMixedContents()
    {
        var current = Path.Combine(_directory, "browser-index.db");
        var next = Path.Combine(_directory, "browser-index.new");
        File.WriteAllText(current, "old-complete-snapshot");
        File.WriteAllText(next, "new-complete-snapshot");

        BrowserIndexService.ReplaceSnapshot(next, current);

        Assert.Equal("new-complete-snapshot", File.ReadAllText(current));
        Assert.False(File.Exists(next));
    }

    [Fact]
    public async Task SqlSearchUsesLiteralWildcardsAndNoPinyinMatching()
    {
        var database = await CreateFixtureDatabaseAsync();

        var chinese = await BrowserIndexService.SearchDatabaseAsync(
            database, "百度", 50, CancellationToken.None);
        var pinyin = await BrowserIndexService.SearchDatabaseAsync(
            database, "baidu", 50, CancellationToken.None);
        var percent = await BrowserIndexService.SearchDatabaseAsync(
            database, "%", 50, CancellationToken.None);
        var underscore = await BrowserIndexService.SearchDatabaseAsync(
            database, "_", 50, CancellationToken.None);

        var urlOnly = await BrowserIndexService.SearchDatabaseAsync(
            database, "example.test", 50, CancellationToken.None);

        Assert.Single(chinese);
        Assert.Empty(pinyin);
        Assert.Empty(urlOnly);
        Assert.Single(percent);
        Assert.Equal("100% real", percent[0].Title);
        Assert.Single(underscore);
        Assert.Equal("under_score", underscore[0].Title);
    }

    [Fact]
    public async Task BookmarkResultsSortByTitleThenFolder()
    {
        var database = await CreateFixtureDatabaseAsync();

        var results = await BrowserIndexService.SearchDatabaseAsync(
            database, "common", 50, CancellationToken.None);

        Assert.Equal(new[] { "alpha common", "beta common" }, results.Select(x => x.Title));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private async Task<string> CreateFixtureDatabaseAsync()
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.db");
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await BrowserIndexService.CreateSchemaAsync(connection, CancellationToken.None);

        await InsertAsync(connection, "chinese", "百度", "https://example.test/chinese", "开发");
        await InsertAsync(connection, "percent", "100% real", "https://example.test/percent", null);
        await InsertAsync(connection, "underscore", "under_score", "https://example.test/underscore", null);
        await InsertAsync(connection, "beta-common", "beta common", "https://example.test/beta", "z-folder");
        await InsertAsync(connection, "alpha-common", "alpha common", "https://example.test/alpha", "a-folder");
        return path;
    }

    private static async Task InsertAsync(
        SqliteConnection connection,
        string normalizedUrl,
        string title,
        string url,
        string? folder)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO bookmarks
                (normalized_url, browser, profile, title, url, folder)
            VALUES
                ($normalizedUrl, 'Fixture', 'Default', $title, $url, $folder);
            """;
        command.Parameters.AddWithValue("$normalizedUrl", normalizedUrl);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$url", url);
        command.Parameters.AddWithValue("$folder", (object?)folder ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }
}
