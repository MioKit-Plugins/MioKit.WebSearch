using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media.Imaging;

namespace WebSearch.Services;

/// <summary>User icons belong to individual websites; automatic favicons remain shared by origin.</summary>
public sealed class WebsiteIconService(
    IPluginIconService icons,
    FaviconService favicon,
    WebSearchPlugin plugin)
{
    internal const long MaxFileBytes = 5 * 1024 * 1024;
    internal const string IconIdPrefix = "websearch-site-icon-";
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".webp", ".ico" };
    // Only successful previews are cached; missing/damaged assets can recover without restarting.
    private readonly ConcurrentDictionary<string, PreviewEntry> previews = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim previewGate = new(2, 2);
    private sealed record PreviewEntry(long Length, DateTime ModifiedAt, string DataUrl);

    public static void ValidateAction(string action, string? sourcePath)
    {
        if (action is not ("Keep" or "Replace" or "Reset"))
            throw new InvalidOperationException("图标操作无效。");
        if (action == "Replace") ValidateFile(sourcePath);
    }

    internal static void ValidateFile(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new InvalidOperationException("图标文件不存在，请重新选择图片。");
        var file = new FileInfo(sourcePath);
        if (!Extensions.Contains(file.Extension))
            throw new InvalidOperationException("图标仅支持 PNG、JPG/JPEG、WebP、ICO。");
        if (file.Length == 0 || file.Length > MaxFileBytes)
            throw new InvalidOperationException("图标文件不能为空，且不能超过 5 MB。");
    }

    public Task<WebsiteIconPickResult> PreviewFileAsync(string sourcePath, CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            ValidateFile(sourcePath);
            var data = await CreatePreviewAsync(sourcePath, ct).ConfigureAwait(false);
            return new WebsiteIconPickResult(sourcePath, data);
        }, ct);

    private static async Task<string> CreatePreviewAsync(string path, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        if (!FaviconImageDecoder.TryLoadBitmap(bytes, out var bitmap))
            throw new InvalidOperationException("图标文件不是有效的图片，请重新选择。");
        using (bitmap)
        {
            ct.ThrowIfCancellationRequested();
            var scale = Math.Min(1d, 96d / Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height));
            using var thumbnail = bitmap.CreateScaledBitmap(new PixelSize(
                Math.Max(1, (int)(bitmap.PixelSize.Width * scale)),
                Math.Max(1, (int)(bitmap.PixelSize.Height * scale))));
            using var stream = new MemoryStream();
            thumbnail.Save(stream, new PngBitmapEncoderOptions());
            return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
        }
    }

    /// <summary>Import before committing the reference; remove the previous asset only after success.</summary>
    public async Task ApplyChangeAsync(string? previousId, string action, string? sourcePath,
        Func<string?, Task> commit, CancellationToken ct = default)
    {
        ValidateAction(action, sourcePath);
        string? importedId = null;
        try
        {
            var nextId = previousId;
            if (action == "Replace")
            {
                // Decode before changing any node state. The host validates and normalizes again on import.
                await PreviewFileAsync(sourcePath!, ct).ConfigureAwait(false);
                importedId = IconIdPrefix + Guid.NewGuid().ToString("N");
                var descriptor = await Task.Run(() => icons.ImportUserAsync(new()
                {
                    IconId = importedId, SourcePath = sourcePath!
                }, ct), ct).ConfigureAwait(false);
                using var lease = await icons.OpenStoredAsync(descriptor.IconId, IconRequest.Default, ct)
                    .ConfigureAwait(false);
                if (lease is null) throw new InvalidOperationException("无法读取导入的图标，请重新选择图片。");
                nextId = descriptor.IconId;
            }
            else if (action == "Reset") nextId = null;

            ct.ThrowIfCancellationRequested();
            await commit(nextId).ConfigureAwait(false);
        }
        catch
        {
            if (importedId is not null) await DeleteOwnedAsync(importedId).ConfigureAwait(false);
            throw;
        }
        if (action != "Keep" && previousId != importedId)
            await DeleteOwnedAsync(previousId).ConfigureAwait(false);
    }

    public async Task DeleteOwnedAsync(string? iconId)
    {
        if (iconId is null || !iconId.StartsWith(IconIdPrefix, StringComparison.Ordinal)) return;
        previews.TryRemove(iconId, out _);
        try
        {
            var descriptor = await icons.GetAsync(iconId).ConfigureAwait(false);
            if (descriptor?.OwnerId == icons.OwnerId)
                await icons.DeleteAsync(iconId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            plugin.Context?.Logger.Warning(ex, "Failed to remove website icon {IconId}.", iconId);
        }
    }

    public async ValueTask<IIconLease?> OpenAsync(WebsiteNode node, IconRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (node.GetCustomIconId() is { Length: > 0 } iconId)
        {
            try
            {
                var custom = await icons.OpenStoredAsync(iconId, request, ct).ConfigureAwait(false);
                if (custom is not null) return custom;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { plugin.Context?.Logger.Debug(ex, "Failed to open website icon {IconId}.", iconId); }
        }
        var origin = node.GetSourceKind() == "LocalFile" ? null : WebsiteUrlResolver.GetOrigin(node.UrlTemplate);
        if (origin is not null)
        {
            if (favicon.TryGetPath(origin) is { } path)
            {
                var automatic = await icons.OpenFileAsync(path, request, ct).ConfigureAwait(false);
                if (automatic is not null) return automatic;
            }
            favicon.Watch(origin, node);
            favicon.RequestRefresh(origin);
        }
        return IconLease.Borrowed(IconSource.Default);
    }

    public async Task<WebsiteIconPreview> GetPreviewAsync(WebsiteNode node, CancellationToken ct = default)
    {
        var iconId = node.GetCustomIconId();
        var hasCustom = !string.IsNullOrEmpty(iconId);
        if (hasCustom)
        {
            try
            {
                // Check the descriptor even for cached previews so deleted assets do not appear healthy.
                var descriptor = await icons.GetAsync(iconId!, ct).ConfigureAwait(false);
                if (descriptor is not null && File.Exists(descriptor.FilePath))
                {
                    var file = new FileInfo(descriptor.FilePath);
                    await previewGate.WaitAsync(ct).ConfigureAwait(false);
                    string data;
                    try
                    {
                        if (previews.TryGetValue(iconId!, out var cached)
                            && cached.Length == file.Length && cached.ModifiedAt == file.LastWriteTimeUtc)
                            data = cached.DataUrl;
                        else
                        {
                            data = await Task.Run(() => CreatePreviewAsync(descriptor.FilePath, ct), ct).ConfigureAwait(false);
                            previews[iconId!] = new(file.Length, file.LastWriteTimeUtc, data);
                        }
                    }
                    finally { previewGate.Release(); }
                    return new(true, data, "自定义图片");
                }
                previews.TryRemove(iconId!, out _);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                plugin.Context?.Logger.Debug(ex, "Failed to preview website icon {IconId}.", iconId);
                previews.TryRemove(iconId!, out _);
            }
        }
        var automatic = node.GetSourceKind() == "LocalFile" ? "" : await Task.Run(() => favicon.GetDataUrl(node), ct)
            .ConfigureAwait(false);
        return new(hasCustom, string.IsNullOrEmpty(automatic) ? IconSource.DefaultDataUrl : automatic,
            hasCustom ? "自定义图片不可用，请重新选择" : string.IsNullOrEmpty(automatic) ? "默认图标" : "自动获取");
    }
}
