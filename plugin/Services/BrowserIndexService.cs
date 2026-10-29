using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace WebSearch.Services;

internal sealed record BrowserImportEntry(string Title, string Url, string Folder);

public sealed class BrowserIndexService : IDisposable
{
    private readonly BrowserCatalogService _catalog;
    private readonly WebSearchPlugin _plugin;
    private readonly FaviconService _favicon;
    private readonly SemaphoreSlim _databaseGate = new(1, 1);
    private readonly SemaphoreSlim _synchronizationGate = new(1, 1);
    private readonly string _databasePath;
    private bool _sqliteInitialized;

    public BrowserIndexService(
        BrowserCatalogService catalog,
        WebSearchPlugin plugin,
        FaviconService favicon)
    {
        _catalog = catalog;
        _plugin = plugin;
        _favicon = favicon;
        Directory.CreateDirectory(plugin.Context.PluginDataPath);
        _databasePath = Path.Combine(plugin.Context.PluginDataPath, "browser-index.db");
    }

    public event EventHandler? StateChanged;

    public bool IsSynchronizing { get; private set; }
    public DateTimeOffset? LastSynchronizedAt { get; private set; }
    public int BookmarkCount { get; private set; }
    public int ProfileCount { get; private set; }
    public string LastError { get; private set; } = string.Empty;
    public IReadOnlyList<Uri> BookmarkOrigins { get; private set; } = [];

    public void RequestSynchronization()
    {
        _ = Task.Run(() => SynchronizeAsync(CancellationToken.None));
    }

    public Task SynchronizeAsync(CancellationToken cancellationToken) =>
        SynchronizeCoreAsync(null, cancellationToken);

    internal Task SynchronizeAsync(
        IReadOnlyList<BrowserProfileSource> profiles, CancellationToken cancellationToken) =>
        SynchronizeCoreAsync(profiles, cancellationToken);

    private async Task SynchronizeCoreAsync(
        IReadOnlyList<BrowserProfileSource>? sources, CancellationToken cancellationToken)
    {
        if (!await _synchronizationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        IsSynchronizing = true;
        LastError = string.Empty;
        RaiseStateChanged();
        var snapshotPath = _databasePath + $".{Guid.NewGuid():N}.new";
        try
        {
            EnsureSqliteInitialized();
            var profiles = sources ?? _catalog.DiscoverProfileSources();
            var origins = await BuildSnapshotAsync(snapshotPath, profiles, cancellationToken).ConfigureAwait(false);

            await _databaseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ReplaceSnapshot(snapshotPath, _databasePath);
            }
            finally
            {
                _databaseGate.Release();
            }

            await RefreshCountsAsync(cancellationToken).ConfigureAwait(false);
            ProfileCount = profiles.Count;
            BookmarkOrigins = origins;
            LastSynchronizedAt = DateTimeOffset.Now;
            if (origins.Count > 0)
            {
                _favicon.RequestPrefetch(profiles, origins);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            _plugin.Context.Logger.Error(exception, "Browser index synchronization failed.");
        }
        finally
        {
            TryDelete(snapshotPath);
            IsSynchronizing = false;
            _synchronizationGate.Release();
            RaiseStateChanged();
        }
    }

    public async Task<IReadOnlyList<BrowserIndexHit>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_databasePath) || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        EnsureSqliteInitialized();
        await _databaseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SearchDatabaseAsync(
                _databasePath,
                query,
                limit,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _databaseGate.Release();
        }
    }

    /// <summary>设置页浏览收藏夹：允许空关键词列出全部，并支持分页与浏览器筛选。</summary>
    public async Task<(IReadOnlyList<BrowserIndexHit> Items, int Total)> BrowseAsync(
        string? query,
        string? browser,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_databasePath))
        {
            return ([], 0);
        }

        EnsureSqliteInitialized();
        await _databaseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await BrowseDatabaseAsync(
                _databasePath,
                query ?? string.Empty,
                browser ?? string.Empty,
                offset,
                limit,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _databaseGate.Release();
        }
    }

    public static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    public void Dispose()
    {
        _databaseGate.Dispose();
        _synchronizationGate.Dispose();
    }

    private async Task<IReadOnlyList<Uri>> BuildSnapshotAsync(
        string path,
        IReadOnlyList<BrowserProfileSource> profiles,
        CancellationToken cancellationToken)
    {
        var origins = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        await using var connection = SqliteDatabaseCopy.CreateConnection(path, false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await CreateSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        // Keep the representative stable even when directory enumeration order changes.
        foreach (var profile in profiles.OrderBy(x => x.BrowserId, StringComparer.Ordinal)
                     .ThenBy(x => x.DirectoryPath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (profile.IsFirefox)
                {
                    await ImportFirefoxAsync(connection, transaction, profile, origins, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await ImportChromiumAsync(connection, transaction, profile, origins, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Publish only complete snapshots; a temporary source failure is not a deletion.
                throw new IOException(
                    $"无法同步 {profile.BrowserName}/{profile.ProfileName}，已保留上次的收藏夹。{exception.Message}",
                    exception);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return origins.Values.ToList();
    }

    private async Task ImportChromiumAsync(
        SqliteConnection target,
        SqliteTransaction transaction,
        BrowserProfileSource profile,
        IDictionary<string, Uri> origins,
        CancellationToken cancellationToken)
    {
        var bookmarksPath = Path.Combine(profile.DirectoryPath, "Bookmarks");
        await using var stream = new FileStream(
            bookmarksPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("roots", out var roots)
            || roots.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("收藏夹文件格式无效或已损坏。");
        }

        foreach (var entry in ParseChromiumBookmarks(roots))
        {
            await UpsertAsync(
                target,
                transaction,
                profile,
                entry.Title,
                entry.Url,
                entry.Folder,
                origins,
                cancellationToken).ConfigureAwait(false);
        }
    }

    internal static IReadOnlyList<BrowserImportEntry> ParseChromiumBookmarks(JsonElement roots)
    {
        var entries = new List<BrowserImportEntry>();
        foreach (var root in roots.EnumerateObject())
        {
            ParseChromiumBookmarkElement(root.Value, root.Name, entries);
        }

        return entries;
    }

    private static void ParseChromiumBookmarkElement(
        JsonElement element,
        string folder,
        ICollection<BrowserImportEntry> entries)
    {
        if (element.TryGetProperty("type", out var type)
            && type.GetString() == "url"
            && element.TryGetProperty("url", out var urlElement)
            && WebsiteUrlResolver.TryValidateHttpUrl(urlElement.GetString(), out _))
        {
            var title = element.TryGetProperty("name", out var name) ? name.GetString() : string.Empty;
            entries.Add(new BrowserImportEntry(title ?? string.Empty, urlElement.GetString()!, folder));
        }

        if (!element.TryGetProperty("children", out var children)
            || children.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var ownName = element.TryGetProperty("name", out var folderName)
            ? folderName.GetString()
            : null;
        var nextFolder = string.IsNullOrWhiteSpace(ownName)
            ? folder
            : string.IsNullOrWhiteSpace(folder) ? ownName! : $"{folder}/{ownName}";
        foreach (var child in children.EnumerateArray())
        {
            ParseChromiumBookmarkElement(child, nextFolder, entries);
        }
    }

    private async Task ImportFirefoxAsync(
        SqliteConnection target,
        SqliteTransaction transaction,
        BrowserProfileSource profile,
        IDictionary<string, Uri> origins,
        CancellationToken cancellationToken)
    {
        var placesPath = Path.Combine(profile.DirectoryPath, "places.sqlite");
        var copy = SqliteDatabaseCopy.Copy(placesPath);
        try
        {
            await using var source = SqliteDatabaseCopy.CreateConnection(copy, true);
            await source.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var bookmarks = source.CreateCommand();
            bookmarks.CommandText =
                """
                SELECT p.url, COALESCE(b.title, p.title, ''), COALESCE(parent.title, '')
                FROM moz_bookmarks b
                JOIN moz_places p ON p.id = b.fk
                LEFT JOIN moz_bookmarks parent ON parent.id = b.parent
                WHERE b.type = 1 AND p.url LIKE 'http%';
                """;
            await using var reader = await bookmarks.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                await UpsertAsync(
                    target,
                    transaction,
                    profile,
                    reader.GetString(1),
                    reader.GetString(0),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    origins,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            SqliteDatabaseCopy.Delete(copy);
        }
    }

    private static async Task UpsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BrowserProfileSource profile,
        string title,
        string url,
        string? folder,
        IDictionary<string, Uri> origins,
        CancellationToken cancellationToken)
    {
        if (!WebsiteUrlResolver.TryValidateHttpUrl(url, out var uri))
        {
            return;
        }

        var origin = WebsiteUrlResolver.GetOrigin(uri.AbsoluteUri);
        if (origin is not null)
        {
            origins.TryAdd(origin.AbsoluteUri, origin);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO bookmarks
                (normalized_url, browser, profile, title, url, folder)
            VALUES
                ($normalizedUrl, $browser, $profile, $title, $url, $folder)
            ON CONFLICT(normalized_url) DO UPDATE SET
                title = excluded.title,
                browser = excluded.browser,
                profile = excluded.profile,
                url = excluded.url,
                folder = excluded.folder
            WHERE TRIM(bookmarks.title) = '' AND TRIM(excluded.title) <> '';
            INSERT OR IGNORE INTO bookmark_sources
                (normalized_url, browser, profile_path, profile, title, url, folder)
            VALUES
                ($normalizedUrl, $browser, $profilePath, $profile, $title, $url, $folder);
            """;
        command.Parameters.AddWithValue("$normalizedUrl", NormalizeUrl(uri));
        command.Parameters.AddWithValue("$browser", profile.BrowserName);
        command.Parameters.AddWithValue("$profile", profile.ProfileName);
        command.Parameters.AddWithValue("$profilePath", profile.DirectoryPath);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$url", uri.AbsoluteUri);
        command.Parameters.AddWithValue("$folder", folder ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshCountsAsync(CancellationToken cancellationToken)
    {
        await _databaseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = SqliteDatabaseCopy.CreateConnection(_databasePath, true);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM bookmarks;";
            var count = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            BookmarkCount = count is long value ? (int)value : Convert.ToInt32(count);
        }
        finally
        {
            _databaseGate.Release();
        }
    }

    internal static async Task CreateSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = DELETE;
            PRAGMA synchronous = NORMAL;
            CREATE TABLE bookmarks (
                normalized_url TEXT NOT NULL PRIMARY KEY,
                browser TEXT NOT NULL,
                profile TEXT NOT NULL,
                title TEXT NOT NULL,
                url TEXT NOT NULL,
                folder TEXT NULL
            );
            CREATE INDEX ix_bookmarks_title
                ON bookmarks(title COLLATE NOCASE);
            CREATE TABLE bookmark_sources (
                normalized_url TEXT NOT NULL,
                browser TEXT NOT NULL,
                profile_path TEXT NOT NULL,
                profile TEXT NOT NULL,
                title TEXT NOT NULL,
                url TEXT NOT NULL,
                folder TEXT NOT NULL,
                PRIMARY KEY (normalized_url, browser, profile_path, folder, title)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private void EnsureSqliteInitialized()
    {
        if (_sqliteInitialized)
        {
            return;
        }

        SQLitePCL.Batteries_V2.Init();
        _sqliteInitialized = true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary/cache cleanup is best effort.
        }
    }

    internal static string NormalizeUrl(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Host = uri.Host.ToLowerInvariant()
        };
        // Fragments can be application routes; trailing slashes can identify different resources.
        return builder.Uri.AbsoluteUri;
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    internal static void ReplaceSnapshot(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath, true);

    internal static async Task<IReadOnlyList<BrowserIndexHit>> SearchDatabaseAsync(
        string databasePath,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var results = new List<BrowserIndexHit>();
        await using var connection = SqliteDatabaseCopy.CreateConnection(databasePath, true);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT title, url, browser, profile, folder
            FROM bookmarks
            WHERE title LIKE $pattern ESCAPE '\' COLLATE NOCASE
            ORDER BY title COLLATE NOCASE, folder COLLATE NOCASE
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$pattern", $"%{EscapeLike(query)}%");
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 50));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new BrowserIndexHit(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return results;
    }

    internal static async Task<(IReadOnlyList<BrowserIndexHit> Items, int Total)> BrowseDatabaseAsync(
        string databasePath,
        string query,
        string browser,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var filter = query.Trim();
        var browserFilter = browser.Trim();
        var empty = string.IsNullOrEmpty(filter) ? 1 : 0;
        var browserEmpty = string.IsNullOrEmpty(browserFilter) ? 1 : 0;
        var pattern = string.IsNullOrEmpty(filter) ? "%" : $"%{EscapeLike(filter)}%";
        // Keep SQL as one string — concatenating raw string literals can leave a bare AND.
        const string filterSql =
            """
            ($browser_empty <> 0 OR lower(browser) = lower($browser))
            AND (
              $empty <> 0
              OR title LIKE $pattern ESCAPE '\' COLLATE NOCASE
              OR url LIKE $pattern ESCAPE '\' COLLATE NOCASE
              OR IFNULL(folder, '') LIKE $pattern ESCAPE '\' COLLATE NOCASE
              OR profile LIKE $pattern ESCAPE '\' COLLATE NOCASE
            )
            """;

        await using var connection = SqliteDatabaseCopy.CreateConnection(databasePath, true);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM bookmarks WHERE " + filterSql + ";";
        BindBrowseParameters(countCommand, empty, pattern, browserEmpty, browserFilter);
        var totalObject = await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var total = totalObject is long longTotal ? (int)longTotal : Convert.ToInt32(totalObject);

        var results = new List<BrowserIndexHit>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT title, url, browser, profile, folder FROM bookmarks WHERE "
            + filterSql
            + " ORDER BY title COLLATE NOCASE, folder COLLATE NOCASE LIMIT $limit OFFSET $offset;";
        BindBrowseParameters(command, empty, pattern, browserEmpty, browserFilter);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));
        command.Parameters.AddWithValue("$offset", Math.Max(0, offset));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new BrowserIndexHit(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return (results, total);
    }

    private static void BindBrowseParameters(
        SqliteCommand command,
        int empty,
        string pattern,
        int browserEmpty,
        string browserFilter)
    {
        command.Parameters.AddWithValue("$empty", empty);
        command.Parameters.AddWithValue("$pattern", pattern);
        command.Parameters.AddWithValue("$browser_empty", browserEmpty);
        command.Parameters.AddWithValue("$browser", browserFilter);
    }
}
