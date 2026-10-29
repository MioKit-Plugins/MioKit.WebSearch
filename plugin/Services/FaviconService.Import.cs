using Microsoft.Data.Sqlite;

namespace WebSearch.Services;

public sealed partial class FaviconService
{
    private async Task ImportChromiumFaviconsAsync(
        BrowserProfileSource profile,
        Dictionary<string, Uri> needed,
        CancellationToken cancellationToken)
    {
        var faviconsPath = Path.Combine(profile.DirectoryPath, "Favicons");
        if (!File.Exists(faviconsPath) || needed.Count == 0)
        {
            return;
        }

        var copy = SqliteDatabaseCopy.Copy(faviconsPath);
        try
        {
            await using var source = SqliteDatabaseCopy.CreateConnection(copy, true);
            await source.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (!await TableExistsAsync(source, "icon_mapping", cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var hasBitmaps = await TableExistsAsync(source, "favicon_bitmaps", cancellationToken)
                .ConfigureAwait(false);
            foreach (var origin in needed.Values.ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = hasBitmaps
                    ? await QueryChromiumBitmapAsync(source, origin, cancellationToken).ConfigureAwait(false)
                    : await QueryChromiumLegacyAsync(source, origin, cancellationToken).ConfigureAwait(false);
                if (bytes is null || !TryWritePng(origin, bytes))
                {
                    continue;
                }

                needed.Remove(OriginKey(origin));
            }
        }
        finally
        {
            SqliteDatabaseCopy.Delete(copy);
        }
    }

    private async Task ImportFirefoxFaviconsAsync(
        BrowserProfileSource profile,
        Dictionary<string, Uri> needed,
        CancellationToken cancellationToken)
    {
        var faviconsPath = Path.Combine(profile.DirectoryPath, "favicons.sqlite");
        if (!File.Exists(faviconsPath) || needed.Count == 0)
        {
            return;
        }

        var copy = SqliteDatabaseCopy.Copy(faviconsPath);
        try
        {
            await using var source = SqliteDatabaseCopy.CreateConnection(copy, true);
            await source.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (!await TableExistsAsync(source, "moz_icons", cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            foreach (var origin in needed.Values.ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = await QueryFirefoxFaviconAsync(source, origin, cancellationToken).ConfigureAwait(false);
                if (bytes is null || !TryWritePng(origin, bytes))
                {
                    continue;
                }

                needed.Remove(OriginKey(origin));
            }
        }
        finally
        {
            SqliteDatabaseCopy.Delete(copy);
        }
    }

    internal static async Task<byte[]?> QueryChromiumBitmapAsync(
        SqliteConnection connection,
        Uri origin,
        CancellationToken cancellationToken)
    {
        GetPageUrlFilters(origin, out var exact, out var prefix);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT fb.image_data
            FROM icon_mapping im
            INNER JOIN favicon_bitmaps fb ON fb.icon_id = im.icon_id
            WHERE fb.image_data IS NOT NULL
              AND (im.page_url = $exact OR im.page_url LIKE $prefix ESCAPE '\')
            ORDER BY (fb.width * fb.height) DESC, length(fb.image_data) DESC
            LIMIT 1;
            """;
        AddPageUrlParameters(command, exact, prefix);
        return await ReadBlobAsync(command, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<byte[]?> QueryChromiumLegacyAsync(
        SqliteConnection connection,
        Uri origin,
        CancellationToken cancellationToken)
    {
        GetPageUrlFilters(origin, out var exact, out var prefix);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT f.image_data
            FROM icon_mapping im
            INNER JOIN favicons f ON f.id = im.icon_id
            WHERE f.image_data IS NOT NULL
              AND (im.page_url = $exact OR im.page_url LIKE $prefix ESCAPE '\')
            ORDER BY length(f.image_data) DESC
            LIMIT 1;
            """;
        AddPageUrlParameters(command, exact, prefix);
        return await ReadBlobAsync(command, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<byte[]?> QueryFirefoxFaviconAsync(
        SqliteConnection connection,
        Uri origin,
        CancellationToken cancellationToken)
    {
        GetPageUrlFilters(origin, out var exact, out var prefix);
        if (await TableExistsAsync(connection, "moz_pages_w_icons", cancellationToken).ConfigureAwait(false)
            && await TableExistsAsync(connection, "moz_icons_to_pages", cancellationToken).ConfigureAwait(false))
        {
            await using var mapped = connection.CreateCommand();
            mapped.CommandText =
                """
                SELECT i.data
                FROM moz_pages_w_icons p
                JOIN moz_icons_to_pages ip ON ip.page_id = p.id
                JOIN moz_icons i ON i.id = ip.icon_id
                WHERE i.data IS NOT NULL
                  AND (p.page_url = $exact OR p.page_url LIKE $prefix ESCAPE '\')
                ORDER BY i.width DESC, length(i.data) DESC
                LIMIT 1;
                """;
            AddPageUrlParameters(mapped, exact, prefix);
            var mappedBytes = await ReadBlobAsync(mapped, cancellationToken).ConfigureAwait(false);
            if (mappedBytes is not null)
            {
                return mappedBytes;
            }
        }

        await using var icons = connection.CreateCommand();
        icons.CommandText =
            """
            SELECT data
            FROM moz_icons
            WHERE data IS NOT NULL
              AND (icon_url = $exact OR icon_url LIKE $prefix ESCAPE '\')
            ORDER BY width DESC, length(data) DESC
            LIMIT 1;
            """;
        AddPageUrlParameters(icons, exact, prefix);
        return await ReadBlobAsync(icons, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", name);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static void AddPageUrlParameters(SqliteCommand command, string exact, string prefix)
    {
        command.Parameters.AddWithValue("$exact", exact);
        command.Parameters.AddWithValue("$prefix", prefix);
    }

    private static async Task<byte[]?> ReadBlobAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is byte[] bytes && bytes.Length >= 8 ? bytes : null;
    }
}
