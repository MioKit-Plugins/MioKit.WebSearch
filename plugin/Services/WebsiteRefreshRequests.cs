namespace WebSearch.Services;

// Accessed on the UI thread. Requests remain pending until a current batch is published.
internal sealed class WebsiteRefreshRequests
{
    private readonly HashSet<string> ids = new(StringComparer.Ordinal);
    private bool all;
    private bool allIcons;
    private readonly HashSet<string> iconIds = new(StringComparer.Ordinal);
    private long version;

    internal void Add(string? id, bool refreshIcons)
    {
        if (id is null) all = true;
        else ids.Add(id);
        if (refreshIcons)
        {
            if (id is null) allIcons = true;
            else iconIds.Add(id);
        }
        version++;
    }

    internal WebsiteRefreshBatch Capture() => new(version, all, allIcons,
        ids.ToHashSet(StringComparer.Ordinal), iconIds.ToHashSet(StringComparer.Ordinal));

    internal bool Complete(WebsiteRefreshBatch batch)
    {
        if (version != batch.Version) return false;
        ids.Clear();
        iconIds.Clear();
        all = allIcons = false;
        return true;
    }
}

internal sealed record WebsiteRefreshBatch(long Version, bool All, bool AllIcons,
    HashSet<string> Ids, HashSet<string> IconIds);
