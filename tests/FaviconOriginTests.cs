using Microsoft.Data.Sqlite;
using WebSearch.Services;

namespace WebSearch.Tests;

public sealed class FaviconOriginTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"miokit-websearch-favicon-{Guid.NewGuid():N}");

    public FaviconOriginTests()
    {
        Directory.CreateDirectory(_directory);
        SQLitePCL.Batteries_V2.Init();
    }

    [Fact]
    public void FileNameIsStableForTheSameOrigin()
    {
        var origin = new Uri("https://example.test/");
        var first = FaviconService.GetFileName(origin);
        var second = FaviconService.GetFileName(origin);

        Assert.Equal(first, second);
        Assert.EndsWith(".png", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FileNameDiffersAcrossOrigins()
    {
        var first = FaviconService.GetFileName(new Uri("https://example.test/"));
        var second = FaviconService.GetFileName(new Uri("https://other.test/"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void PageUrlPrefixDoesNotMatchLookalikeHosts()
    {
        FaviconService.GetPageUrlFilters(new Uri("https://example.test/"), out var exact, out var prefix);

        Assert.Equal("https://example.test", exact);
        Assert.Equal("https://example.test/%", prefix);
        Assert.DoesNotContain("example.test.evil", prefix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChromiumQuerySelectsLargestBitmapForMatchingOrigin()
    {
        var path = Path.Combine(_directory, "Favicons");
        await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE icon_mapping (
                    id INTEGER PRIMARY KEY,
                    page_url TEXT NOT NULL,
                    icon_id INTEGER NOT NULL
                );
                CREATE TABLE favicon_bitmaps (
                    id INTEGER PRIMARY KEY,
                    icon_id INTEGER NOT NULL,
                    image_data BLOB,
                    width INTEGER,
                    height INTEGER
                );
                INSERT INTO icon_mapping(page_url, icon_id) VALUES
                    ('https://example.test/path', 1),
                    ('https://example.test.evil/path', 2);
                INSERT INTO favicon_bitmaps(icon_id, image_data, width, height) VALUES
                    (1, x'0102030405060708', 16, 16),
                    (1, x'1112131415161718', 32, 32),
                    (2, x'2122232425262728', 64, 64);
                """;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var reader = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await reader.OpenAsync(TestContext.Current.CancellationToken);
        var bytes = await FaviconService.QueryChromiumBitmapAsync(
            reader,
            new Uri("https://example.test/"),
            TestContext.Current.CancellationToken);

        Assert.Equal([0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18], bytes);
    }

    [Fact]
    public async Task FirefoxQueryUsesPageMappingThenIconUrl()
    {
        var path = Path.Combine(_directory, "favicons.sqlite");
        await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE moz_icons (
                    id INTEGER PRIMARY KEY,
                    icon_url TEXT NOT NULL,
                    width INTEGER,
                    data BLOB
                );
                CREATE TABLE moz_pages_w_icons (
                    id INTEGER PRIMARY KEY,
                    page_url TEXT NOT NULL
                );
                CREATE TABLE moz_icons_to_pages (
                    page_id INTEGER,
                    icon_id INTEGER
                );
                INSERT INTO moz_icons(id, icon_url, width, data) VALUES
                    (1, 'https://example.test/favicon.ico', 16, x'0102030405060708'),
                    (2, 'https://other.test/favicon.ico', 32, x'1112131415161718');
                INSERT INTO moz_pages_w_icons(id, page_url) VALUES
                    (1, 'https://example.test/docs');
                INSERT INTO moz_icons_to_pages(page_id, icon_id) VALUES (1, 1);
                """;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var reader = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await reader.OpenAsync(TestContext.Current.CancellationToken);
        var bytes = await FaviconService.QueryFirefoxFaviconAsync(
            reader,
            new Uri("https://example.test/"),
            TestContext.Current.CancellationToken);

        Assert.Equal([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08], bytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
