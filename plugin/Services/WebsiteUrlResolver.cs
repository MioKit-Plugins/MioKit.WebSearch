using System.Text;

namespace WebSearch.Services;

public static class WebsiteUrlResolver
{
    public const string QueryToken = "{query}";

    public static bool IsTemplate(string? value) =>
        value?.Contains(QueryToken, StringComparison.OrdinalIgnoreCase) == true;

    public static string Resolve(
        string template,
        string? input,
        string? clipboard,
        QuerySourcePolicy policy)
    {
        var query = policy switch
        {
            QuerySourcePolicy.InputOnly => input,
            QuerySourcePolicy.ClipboardOnly => clipboard,
            _ => string.IsNullOrWhiteSpace(input) ? clipboard : input
        };

        if (!IsTemplate(template))
        {
            return template;
        }

        query ??= string.Empty;
        return ReplaceOrdinalIgnoreCase(template, QueryToken, Uri.EscapeDataString(query));
    }

    public static string ResolveForHotKey(string template, string? clipboard) =>
        Resolve(
            template,
            input: null,
            string.IsNullOrWhiteSpace(clipboard) ? string.Empty : clipboard,
            QuerySourcePolicy.ClipboardOnly);

    public static bool TryValidateHttpUrl(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = ReplaceOrdinalIgnoreCase(value.Trim(), QueryToken, "test");
        return Uri.TryCreate(candidate, UriKind.Absolute, out uri!)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public static Uri? GetOrigin(string template)
    {
        if (!TryValidateHttpUrl(template, out var uri))
        {
            return null;
        }

        return new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port).Uri;
    }

    private static string ReplaceOrdinalIgnoreCase(string source, string oldValue, string newValue)
    {
        var result = new StringBuilder(source.Length);
        var position = 0;
        while (true)
        {
            var index = source.IndexOf(oldValue, position, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                result.Append(source, position, source.Length - position);
                return result.ToString();
            }

            result.Append(source, position, index - position);
            result.Append(newValue);
            position = index + oldValue.Length;
        }
    }
}
