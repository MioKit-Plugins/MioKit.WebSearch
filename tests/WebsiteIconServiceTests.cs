using Avalonia.Media.Imaging;
using System.Buffers.Binary;
using MioKit.Sdk;
using WebSearch;
using WebSearch.Features;
using WebSearch.Models;
using WebSearch.Nodes;
using WebSearch.Services;

namespace MioKit.WebSearch.Tests;

[Collection("Website backend")]
public sealed class WebsiteIconServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "websearch-icon-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeIcons icons;
    private readonly WebSearchPlugin plugin = new();
    private readonly FaviconService favicon;
    private readonly WebsiteIconService service;
    private static readonly Lazy<bool> Graphics = new(() =>
    {
        Avalonia.Skia.SkiaPlatform.Initialize();
        using var stream = new MemoryStream(Png);
        typeof(IconSource).GetProperty(nameof(IconSource.Default))!.SetValue(null, new Bitmap(stream));
        typeof(IconSource).GetProperty(nameof(IconSource.DefaultDataUrl))!.SetValue(null, "data:image/png;base64," + Convert.ToBase64String(Png));
        return true;
    });
    internal static readonly byte[] Png = CreatePng();
    private static byte[] CreatePng()
    {
        using var bitmap = new SkiaSharp.SKBitmap(2, 2);
        bitmap.Erase(SkiaSharp.SKColors.CornflowerBlue);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    public WebsiteIconServiceTests(WebsiteBackendFixture fixture)
    {
        _ = Graphics.Value;
        Directory.CreateDirectory(root);
        icons = new(root);
        favicon = new(plugin);
        service = new(icons, favicon, plugin);
    }

    public void Dispose()
    {
        favicon.Dispose();
        var absolute = Path.GetFullPath(root);
        if (!absolute.StartsWith(Path.Combine(Path.GetTempPath(), "websearch-icon-tests-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected test directory.");
        Directory.Delete(absolute, true);
    }

    private string Source(string name = "source.png")
    {
        var path = Path.Combine(root, name);
        File.WriteAllBytes(path, Png);
        return path;
    }
    private static WebsiteNode LocalNode(string id = "site")
    {
        var node = new WebsiteNode(id) { UrlTemplate = "https://same.test/" };
        node.SetSourceKind("LocalFile");
        return node;
    }
    private async Task<string> Import(WebsiteNode node, string? path = null)
    {
        await service.ApplyChangeAsync(node.GetCustomIconId(), "Replace", path ?? Source(), next =>
        {
            node.SetCustomIconId(next);
            return Task.CompletedTask;
        });
        return node.GetCustomIconId()!;
    }

    [Fact]
    public async Task PreviewDoesNotImportOrChangeTheSavedReference()
    {
        var preview = await service.PreviewFileAsync(Source());
        Assert.StartsWith("data:image/png;base64,", preview.PreviewDataUrl);
        Assert.Empty(icons.Stored);
        Assert.Equal(0, icons.Imports);
    }

    [Fact]
    public async Task IcoSelectionIsDecodedAndImportedThroughTheHostsPngDecoder()
    {
        var data = new byte[22 + Png.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 1);
        data[6] = 2; data[7] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), 32);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), Png.Length);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), 22);
        Png.CopyTo(data, 22);
        var source = Path.Combine(root, "source.ico"); File.WriteAllBytes(source, data);
        Assert.StartsWith("data:image/png;base64,", (await service.PreviewFileAsync(source)).PreviewDataUrl);
        var node = LocalNode();
        await Import(node, source);
        Assert.Equal("自定义图片", (await service.GetPreviewAsync(node)).IconState);
    }

    [Fact]
    public async Task ImportSurvivesDeletingSourceAndRecreatingTheService()
    {
        var node = LocalNode();
        var source = Source();
        var id = await Import(node, source);
        File.Delete(source);
        var restarted = new WebsiteIconService(icons, favicon, plugin);
        var preview = await restarted.GetPreviewAsync(node);
        Assert.True(preview.HasCustomIcon);
        Assert.Equal("自定义图片", preview.IconState);
        Assert.Equal(IconLifecycle.Persistent, icons.Stored[id].Lifecycle);
        using var first = await restarted.OpenAsync(node, IconRequest.Default, default);
        using var second = await restarted.OpenAsync(node, IconRequest.Default, default);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task SameOriginWebsitesReceiveIndependentIconsAndReplacingDeletesOnlyTheirOldAsset()
    {
        var a = LocalNode("a"); var b = LocalNode("b");
        var oldA = await Import(a); var idB = await Import(b);
        Assert.NotEqual(oldA, idB);
        await service.ApplyChangeAsync(oldA, "Replace", Source(), next =>
        {
            Assert.Contains(oldA, icons.Stored.Keys);
            a.SetCustomIconId(next);
            return Task.CompletedTask;
        });
        Assert.DoesNotContain(oldA, icons.Stored.Keys);
        Assert.Contains(idB, icons.Stored.Keys);
        Assert.Contains(a.GetCustomIconId()!, icons.Stored.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportOrCommitFailurePreservesOldAssetAndCleansTheNewOne(bool failCommit)
    {
        var node = LocalNode(); var previous = await Import(node);
        icons.FailImport = !failCommit;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyChangeAsync(previous, "Replace", Source(), _ =>
            throw new InvalidOperationException("commit failed")));
        Assert.Equal(previous, node.GetCustomIconId());
        Assert.Equal(previous, Assert.Single(icons.Stored).Key);
    }

    [Fact]
    public async Task ResetRemovesTheReferenceThenDeletesTheOwnedAssetAndKeepLeavesItUntouched()
    {
        var node = LocalNode(); var previous = await Import(node);
        await service.ApplyChangeAsync(previous, "Keep", "missing", next =>
        {
            Assert.Equal(previous, next);
            return Task.CompletedTask;
        });
        Assert.Empty(icons.Deleted);
        await service.ApplyChangeAsync(previous, "Reset", null, next =>
        {
            Assert.Null(next);
            Assert.Contains(previous, icons.Stored.Keys);
            node.SetCustomIconId(next);
            return Task.CompletedTask;
        });
        Assert.Null(node.GetCustomIconId());
        Assert.Empty(icons.Stored);
        Assert.Equal(previous, Assert.Single(icons.Deleted));
    }

    [Fact]
    public async Task MissingOrDamagedSavedImageFallsBackAndCanBeReplaced()
    {
        var node = LocalNode(); var id = await Import(node);
        Assert.Equal("自定义图片", (await service.GetPreviewAsync(node)).IconState);
        File.WriteAllText(icons.Stored[id].FilePath, "damaged image");
        var broken = await service.GetPreviewAsync(node);
        Assert.True(broken.HasCustomIcon);
        Assert.Contains("不可用", broken.IconState);
        Assert.Equal(IconSource.DefaultDataUrl, broken.IconDataUrl);
        File.Delete(icons.Stored[id].FilePath);
        using var fallback = await service.OpenAsync(node, IconRequest.Default, default);
        Assert.Same(IconSource.Default, fallback!.Image);
        await Import(node);
        Assert.Equal("自定义图片", (await service.GetPreviewAsync(node)).IconState);
    }

    [Fact]
    public async Task InvalidSourcesNeverReachTheHostOrCommit()
    {
        var tooLarge = Path.Combine(root, "large.png");
        using (var stream = File.Create(tooLarge)) stream.SetLength(WebsiteIconService.MaxFileBytes + 1);
        var corrupt = Path.Combine(root, "corrupt.png"); File.WriteAllText(corrupt, "not an image");
        var disappeared = Source("disappeared.png");
        await service.PreviewFileAsync(disappeared); File.Delete(disappeared);
        foreach (var path in new[] { tooLarge, corrupt, Source("wrong.svg"), disappeared })
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyChangeAsync(null, "Replace", path,
                _ => throw new Exception("must not commit")));
        Assert.Equal(0, icons.Imports);
        Assert.Empty(icons.Stored);
    }

    [Fact]
    public async Task CleanupNeverDeletesOtherOwnerOrUnrelatedPluginAssets()
    {
        var node = LocalNode(); var id = await Import(node);
        icons.Stored[id] = icons.Stored[id] with { OwnerId = "other-plugin" };
        await service.DeleteOwnedAsync(id);
        await service.DeleteOwnedAsync("unrelated-icon");
        Assert.Empty(icons.Deleted);
    }

    [Fact]
    public async Task CancelledSelectionAndSaveDoNotImportOrCommit()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PreviewFileAsync(Source(), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyChangeAsync(null, "Replace", Source(),
            _ => throw new Exception("must not commit"), cts.Token));
        Assert.Empty(icons.Stored);
    }

    private sealed class FakeIcons(string root) : IPluginIconService
    {
        public string OwnerId => WebSearchConst.PluginId;
        public Dictionary<string, IconDescriptor> Stored { get; } = [];
        public List<string> Deleted { get; } = [];
        public int Imports { get; private set; }
        public bool FailImport { get; set; }
        public Task<IconDescriptor> ImportUserAsync(IconImportRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested(); Imports++;
            if (FailImport) throw new InvalidOperationException("host import failed");
            using var decoded = SkiaSharp.SKBitmap.Decode(request.SourcePath)
                ?? throw new InvalidOperationException("Host cannot decode this image container.");
            var target = Path.Combine(root, request.IconId + ".png");
            File.Copy(request.SourcePath, target);
            var descriptor = new IconDescriptor
            {
                IconId = request.IconId, OwnerId = OwnerId, FilePath = target, RelativePath = Path.GetFileName(target),
                SourceKind = IconSourceKind.UserUpload, Lifecycle = IconLifecycle.Persistent, ContentSha256 = "test",
                FileSizeBytes = new FileInfo(target).Length, PixelWidth = 1, PixelHeight = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            Stored.Add(request.IconId, descriptor);
            return Task.FromResult(descriptor);
        }
        public Task<IconDescriptor?> GetAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Stored.TryGetValue(id, out var descriptor) ? descriptor : null);
        public ValueTask<IIconLease?> OpenStoredAsync(string id, IconRequest request, CancellationToken ct = default) =>
            Stored.TryGetValue(id, out var descriptor) ? OpenFileAsync(descriptor.FilePath, request, ct) : ValueTask.FromResult<IIconLease?>(null);
        public ValueTask<IIconLease?> OpenFileAsync(string path, IconRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path) || !FaviconImageDecoder.TryLoadBitmap(File.ReadAllBytes(path), out var bitmap))
                return ValueTask.FromResult<IIconLease?>(null);
            return ValueTask.FromResult<IIconLease?>(IconLease.Owned(bitmap));
        }
        public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        {
            Deleted.Add(id);
            if (!Stored.Remove(id, out var descriptor)) return Task.FromResult(false);
            File.Delete(descriptor.FilePath);
            return Task.FromResult(true);
        }
        public Task<IconDescriptor> ImportManagedAsync(IconImportRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IconDescriptor> SaveGeneratedAsync(GeneratedIconRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IconDescriptor?> GetOrCreateWindowsApplicationAsync(WindowsApplicationIconRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<IIconLease?> OpenRemoteAsync(Uri uri, IconRequest request, RemoteIconOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
