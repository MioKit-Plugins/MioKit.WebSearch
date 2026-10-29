using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;

namespace WebSearch.Services;

public sealed class FaviconChangedEventArgs(Uri origin) : EventArgs
{
    public Uri Origin { get; } = origin;
}

public sealed partial class FaviconService : IDisposable
{
    public event EventHandler<FaviconChangedEventArgs>? IconsChanged;
    private const int MaxHtmlBytes = 1024 * 1024;
    private const int MaxImageBytes = 512 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 MioKit.WebSearch/0.1";
    private const string HtmlAccept = "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8";
    private const string ImageAccept = "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8";
    private readonly WebSearchPlugin _plugin;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _httpGate = new(2, 2);
    private readonly SemaphoreSlim _prefetchGate = new(1, 1);
    private readonly FaviconDataUrlMemoryCache _dataUrlCache = new();
    private readonly ConcurrentDictionary<string, byte> _failedOrigins = new(StringComparer.OrdinalIgnoreCase);
    // Accessed under _workGate together with task admission and shutdown.
    private readonly Dictionary<string, Task<bool>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentBag<WeakReference<MioObject>>> _watchers =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _sqliteInitialized;

    public FaviconService(WebSearchPlugin plugin) : this(plugin, new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        AutomaticDecompression = DecompressionMethods.All
    })
    {
    }

    internal FaviconService(WebSearchPlugin plugin, HttpMessageHandler handler)
    {
        _plugin = plugin;
        _httpClient = new HttpClient(handler)
        {
            Timeout = RequestTimeout
        };
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
    }

    public void Watch(Uri origin, MioObject node)
    {
        _watchers.GetOrAdd(OriginKey(origin), _ => []).Add(new WeakReference<MioObject>(node));
    }

    public string? TryGetPath(Uri origin)
    {
        var path = GetIconPath(origin);
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length >= 8 ? path : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string GetDataUrl(WebsiteNode website)
    {
        var origin = WebsiteUrlResolver.GetOrigin(website.UrlTemplate);
        return origin is null ? string.Empty : GetDataUrl(origin);
    }

    public string GetDataUrl(Uri origin)
    {
        var fileName = GetFileName(origin);
        try
        {
            return _dataUrlCache.GetOrCreate(fileName, () => TryCreatePngDataUrl(GetIconPath(origin)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _plugin.Context.Logger.Debug(exception, "Failed to read cached favicon for {Origin}.", origin);
            return string.Empty;
        }
    }

    public void RequestRefresh(WebsiteNode website)
    {
        var origin = WebsiteUrlResolver.GetOrigin(website.UrlTemplate);
        if (origin is not null)
        {
            RequestRefresh(origin);
        }
    }

    public void RequestRefresh(Uri origin)
    {
        _ = RefreshSingleFlightAsync(origin, false, CancellationToken.None);
    }

    public Task<bool> RefreshAsync(WebsiteNode website, CancellationToken cancellationToken)
    {
        var origin = WebsiteUrlResolver.GetOrigin(website.UrlTemplate);
        return origin is null
            ? Task.FromResult(false)
            : RefreshAsync(origin, cancellationToken);
    }

    public Task<bool> RefreshAsync(Uri origin, CancellationToken cancellationToken)
    {
        return RefreshSingleFlightAsync(origin, true, cancellationToken);
    }

    internal void RequestPrefetch(
        IReadOnlyList<BrowserProfileSource> profiles,
        IReadOnlyList<Uri> origins)
    {
        _ = PrefetchAsync(profiles, origins, CancellationToken.None);
    }

    internal Task PrefetchAsync(
        IReadOnlyList<BrowserProfileSource> profiles,
        IReadOnlyList<Uri> origins,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_workGate)
        {
            if (!_acceptingWork) return Task.CompletedTask;
            var task = TrackWorkLocked(async token =>
            {
                await PrefetchCoreAsync(profiles, origins, token).ConfigureAwait(false);
                return true;
            });
            return task.WaitAsync(cancellationToken);
        }
    }

    private async Task PrefetchCoreAsync(
        IReadOnlyList<BrowserProfileSource> profiles,
        IReadOnlyList<Uri> origins,
        CancellationToken cancellationToken)
    {
        if (!await _prefetchGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var needed = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
            foreach (var origin in origins)
            {
                if (TryGetPath(origin) is null && !_failedOrigins.ContainsKey(OriginKey(origin)))
                {
                    needed.TryAdd(OriginKey(origin), origin);
                }
            }

            if (needed.Count == 0)
            {
                return;
            }

            EnsureSqliteInitialized();
            foreach (var profile in profiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (needed.Count == 0)
                {
                    break;
                }

                try
                {
                    if (profile.IsFirefox)
                    {
                        await ImportFirefoxFaviconsAsync(profile, needed, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await ImportChromiumFaviconsAsync(profile, needed, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _plugin.Context.Logger.Debug(
                        exception,
                        "Failed to import favicons from {Browser}/{Profile}.",
                        profile.BrowserName,
                        profile.ProfileName);
                }
            }

            foreach (var origin in needed.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequestRefresh(origin);
            }
        }
        finally
        {
            _prefetchGate.Release();
        }
    }

    internal static string GetFileName(Uri origin) =>
        $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(origin.AbsoluteUri)))}.png";

    internal static string OriginKey(Uri origin) => origin.AbsoluteUri;

    internal static void GetPageUrlFilters(Uri origin, out string exact, out string prefix)
    {
        exact = origin.AbsoluteUri.TrimEnd('/');
        prefix = $"{BrowserIndexService.EscapeLike(exact + "/")}%";
    }

    internal static string TryCreatePngDataUrl(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length < 8 || file.Length > MaxImageBytes)
            {
                return string.Empty;
            }

            var bytes = File.ReadAllBytes(path);
            ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
            if (!bytes.AsSpan(0, pngSignature.Length).SequenceEqual(pngSignature))
            {
                return string.Empty;
            }

            return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private Task<bool> RefreshSingleFlightAsync(Uri origin, bool force, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = OriginKey(origin);
        lock (_workGate)
        {
            if (!_acceptingWork) return Task.FromResult(false);
            if (force) _failedOrigins.TryRemove(key, out _);
            else
            {
                if (TryGetPath(origin) is not null) return Task.FromResult(true);
                if (_failedOrigins.ContainsKey(key)) return Task.FromResult(false);
            }
            if (!_inflight.TryGetValue(key, out var task))
            {
                task = TrackWorkLocked(token => RunHttpRefreshAsync(origin, token), key);
                _inflight.Add(key, task);
            }
            // A caller can stop waiting without canceling other callers sharing this origin.
            return task.WaitAsync(cancellationToken);
        }
    }

    private async Task<bool> RunHttpRefreshAsync(Uri origin, CancellationToken cancellationToken)
    {
        var key = OriginKey(origin);
        await _httpGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var originCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            originCts.CancelAfter(RequestTimeout);
            var originToken = originCts.Token;

            var candidates = new List<Uri>();
            try
            {
                using var pageResponse = await SendAsync(origin, HtmlAccept, originToken).ConfigureAwait(false);
                if (pageResponse.IsSuccessStatusCode)
                {
                    var htmlBytes = await ReadLimitedAsync(
                        await pageResponse.Content.ReadAsStreamAsync(originToken).ConfigureAwait(false),
                        MaxHtmlBytes,
                        originToken,
                        allowTruncation: true).ConfigureAwait(false);
                    var html = Encoding.UTF8.GetString(htmlBytes);
                    candidates.AddRange(FindIconCandidates(pageResponse.RequestMessage?.RequestUri ?? origin, html));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                LogTransportFailure("Failed to inspect page favicon links for {Origin}.", exception, origin);
                if (IsHardTransportFailure(exception) || originCts.IsCancellationRequested)
                {
                    _failedOrigins.TryAdd(key, 0);
                    return false;
                }
            }

            candidates.Add(new Uri(origin, "/favicon.ico"));
            foreach (var candidate in candidates.Distinct().Take(8))
            {
                if (originCts.IsCancellationRequested)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var response = await SendAsync(candidate, ImageAccept, originToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode
                        || IsUnusableMediaType(response.Content.Headers.ContentType?.MediaType))
                    {
                        continue;
                    }

                    var bytes = await ReadLimitedAsync(
                        await response.Content.ReadAsStreamAsync(originToken).ConfigureAwait(false),
                        MaxImageBytes,
                        originToken).ConfigureAwait(false);
                    if (TryWritePng(origin, bytes))
                    {
                        return true;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    LogTransportFailure("Failed to cache favicon {FaviconUrl}.", exception, candidate);
                    if (IsHardTransportFailure(exception) && candidate.Host == origin.Host)
                    {
                        break;
                    }
                }
            }

            _failedOrigins.TryAdd(key, 0);
            return false;
        }
        finally
        {
            _httpGate.Release();
        }
    }

    private bool TryWritePng(Uri origin, byte[] bytes)
    {
        if (bytes.Length is < 8 or > MaxImageBytes)
        {
            return false;
        }

        if (FaviconImageDecoder.IsNonImagePayload(bytes))
        {
            return false;
        }

        try
        {
            if (!FaviconImageDecoder.TryLoadBitmap(bytes, out var bitmap))
            {
                _plugin.Context.Logger.Debug(
                    "Failed to decode favicon bytes for {Origin} ({Length} bytes).",
                    origin,
                    bytes.Length);
                return false;
            }

            using (bitmap)
            {
                var fileName = GetFileName(origin);
                var iconDirectory = GetIconDirectory();
                Directory.CreateDirectory(iconDirectory);
                var iconPath = Path.Combine(iconDirectory, fileName);
                var temporaryPath = $"{iconPath}.{Guid.NewGuid():N}.tmp";
                try
                {
                    using (var output = File.Create(temporaryPath))
                    {
                        bitmap.Save(output, PngBitmapEncoderOptions.Default);
                        output.Flush();
                    }

                    File.Move(temporaryPath, iconPath, true);
                }
                finally
                {
                    File.Delete(temporaryPath);
                }

                _dataUrlCache.Invalidate(fileName);
                _failedOrigins.TryRemove(OriginKey(origin), out _);
                OnIconReady(origin);
                return true;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _plugin.Context.Logger.Debug(exception, "Failed to write favicon for {Origin}.", origin);
            return false;
        }
    }

    private void OnIconReady(Uri origin)
    {
        if (_watchers.TryRemove(OriginKey(origin), out var bag))
        {
            foreach (var weak in bag)
            {
                if (weak.TryGetTarget(out var node) && node is IIconProviderFeature icon)
                {
                    icon.InvalidateIcon();
                }
            }
        }

        var group = _plugin.FindFirstDescendant<WebsiteSearchGroup>();
        if (group is null)
        {
            return;
        }

        foreach (var website in group.Children.OfType<WebsiteNode>())
        {
            var websiteOrigin = WebsiteUrlResolver.GetOrigin(website.UrlTemplate);
            if (websiteOrigin is not null
                && string.Equals(OriginKey(websiteOrigin), OriginKey(origin), StringComparison.OrdinalIgnoreCase))
            {
                website.InvalidateIcon();
            }
        }
        IconsChanged?.Invoke(this, new FaviconChangedEventArgs(origin));
    }

    private async Task<HttpResponseMessage> SendAsync(
        Uri uri,
        string accept,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Accept", accept);
        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
    }

    private void LogTransportFailure(string message, Exception exception, object arg)
    {
        if (IsExpectedTransportFailure(exception))
        {
            _plugin.Context.Logger.Debug(message, arg);
            return;
        }

        _plugin.Context.Logger.Debug(exception, message, arg);
    }

    internal static bool IsUnusableMediaType(string? mediaType) =>
        mediaType is not null
        && (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase));

    internal static bool IsExpectedTransportFailure(Exception exception) =>
        exception is OperationCanceledException or TimeoutException
        || exception.InnerException is TimeoutException or IOException
        || IsHardTransportFailure(exception);

    internal static bool IsHardTransportFailure(Exception exception)
    {
        if (exception is OperationCanceledException or TimeoutException)
        {
            return true;
        }

        return exception is HttpRequestException http
            && http.HttpRequestError is HttpRequestError.ConnectionError
                or HttpRequestError.NameResolutionError
                or HttpRequestError.SecureConnectionError
                or HttpRequestError.HttpProtocolError
                or HttpRequestError.ResponseEnded;
    }

    private string GetIconPath(Uri origin) =>
        Path.Combine(GetIconDirectory(), GetFileName(origin));

    private string GetIconDirectory() =>
        Path.Combine(_plugin.Context.PluginDataPath, "favicons");

    private void EnsureSqliteInitialized()
    {
        if (_sqliteInitialized)
        {
            return;
        }

        SQLitePCL.Batteries_V2.Init();
        _sqliteInitialized = true;
    }

    private static IEnumerable<Uri> FindIconCandidates(Uri pageUri, string html)
    {
        foreach (Match link in LinkTagRegex().Matches(html))
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attribute in AttributeRegex().Matches(link.Value))
            {
                attributes[attribute.Groups[1].Value] =
                    WebUtility.HtmlDecode(attribute.Groups[3].Value);
            }

            if (!attributes.TryGetValue("rel", out var rel)
                || !rel.Contains("icon", StringComparison.OrdinalIgnoreCase)
                || !attributes.TryGetValue("href", out var href)
                || !Uri.TryCreate(pageUri, href, out var candidate)
                || (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            yield return candidate;
        }
    }

    internal static async Task<byte[]> ReadLimitedAsync(
        Stream source,
        int limit,
        CancellationToken cancellationToken,
        bool allowTruncation = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await using var output = new MemoryStream(Math.Min(limit, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = limit - (int)output.Length;
            // HTML discovery only needs a bounded prefix. Never truncate image payloads:
            // their extra byte is read solely to distinguish an exact-size image from overflow.
            if (allowTruncation && remaining == 0) return output.ToArray();
            var count = (int)Math.Min(buffer.Length, (long)remaining + (allowTruncation ? 0 : 1));
            var read = await source.ReadAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > limit)
            {
                throw new InvalidDataException($"Response exceeded the {limit}-byte limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkTagRegex();

    [GeneratedRegex(@"([\w:-]+)\s*=\s*(['""])(.*?)\2",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex AttributeRegex();
}

internal sealed class FaviconDataUrlMemoryCache
{
    private readonly ConcurrentDictionary<string, Lazy<string>> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public string GetOrCreate(string key, Func<string> loader)
    {
        var entry = _entries.GetOrAdd(
            key,
            _ => new Lazy<string>(loader, LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var value = entry.Value;
            if (string.IsNullOrEmpty(value))
            {
                RemoveIfCurrent(key, entry);
            }

            return value;
        }
        catch
        {
            RemoveIfCurrent(key, entry);
            throw;
        }
    }

    public void Invalidate(string key) => _entries.TryRemove(key, out _);

    public void Clear() => _entries.Clear();

    private void RemoveIfCurrent(string key, Lazy<string> entry)
    {
        if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
        {
            _entries.TryRemove(key, out _);
        }
    }
}
