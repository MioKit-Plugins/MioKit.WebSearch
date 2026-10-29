using System.Text.Json;

namespace WebSearch.Services;

internal static class WebsiteConfigurationChanges
{
    // Presentation and query-source preferences do not affect an already loaded document.
    internal static bool RequiresReopen(
        WebsiteConfiguration previous, string previousUrl,
        WebsiteConfiguration next, string nextUrl) =>
        !string.Equals(previousUrl.Trim(), nextUrl.Trim(), StringComparison.Ordinal)
        || !string.Equals(JsonSerializer.Serialize(previous), JsonSerializer.Serialize(next), StringComparison.Ordinal);
}
