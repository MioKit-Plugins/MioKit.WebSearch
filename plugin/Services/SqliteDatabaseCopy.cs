using Microsoft.Data.Sqlite;

namespace WebSearch.Services;

internal static class SqliteDatabaseCopy
{
    public static string Copy(string sourcePath)
    {
        var copyPath = Path.Combine(Path.GetTempPath(), $"miokit-websearch-{Guid.NewGuid():N}.sqlite");
        File.Copy(sourcePath, copyPath, true);
        CopySidecar(sourcePath, copyPath, "-wal");
        CopySidecar(sourcePath, copyPath, "-shm");
        return copyPath;
    }

    public static void Delete(string path)
    {
        TryDelete(path);
        TryDelete(path + "-wal");
        TryDelete(path + "-shm");
    }

    public static SqliteConnection CreateConnection(string path, bool readOnly)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        };
        return new SqliteConnection(builder.ToString());
    }

    private static void CopySidecar(string source, string target, string suffix)
    {
        if (File.Exists(source + suffix))
        {
            File.Copy(source + suffix, target + suffix, true);
        }
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
}
