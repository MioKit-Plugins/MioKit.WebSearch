using WebSearch.Services;

namespace MioKit.WebSearch.Tests;

public sealed class FaviconDataUrlTests
{
    [Fact]
    public void MemoryCache_LoadsSuccessfulValueOnce()
    {
        var cache = new FaviconDataUrlMemoryCache();
        var loadCount = 0;

        var first = cache.GetOrCreate("site.png", () =>
        {
            loadCount++;
            return "data:image/png;base64,abc";
        });
        var second = cache.GetOrCreate("site.png", () =>
        {
            loadCount++;
            return "unexpected";
        });

        Assert.Equal(first, second);
        Assert.Equal(1, loadCount);
    }

    [Fact]
    public void MemoryCache_ReloadsAfterInvalidation()
    {
        var cache = new FaviconDataUrlMemoryCache();
        var loadCount = 0;
        string Load() => $"value-{++loadCount}";

        Assert.Equal("value-1", cache.GetOrCreate("site.png", Load));
        cache.Invalidate("site.png");
        Assert.Equal("value-2", cache.GetOrCreate("site.png", Load));
    }

    [Fact]
    public void MemoryCache_DoesNotRetainMissingOrDamagedValue()
    {
        var cache = new FaviconDataUrlMemoryCache();

        Assert.Empty(cache.GetOrCreate("site.png", () => string.Empty));
        Assert.Equal("recovered", cache.GetOrCreate("site.png", () => "recovered"));
    }

    [Fact]
    public void TryCreatePngDataUrl_ReturnsEmptyForMissingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png");

        Assert.Empty(FaviconService.TryCreatePngDataUrl(path));
    }

    [Fact]
    public void TryCreatePngDataUrl_ReturnsEmptyForInvalidPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"invalid-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllText(path, "not a png");

            Assert.Empty(FaviconService.TryCreatePngDataUrl(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryCreatePngDataUrl_ReturnsDataUrlForPngSignature()
    {
        var path = Path.Combine(Path.GetTempPath(), $"valid-{Guid.NewGuid():N}.png");
        var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4 };
        try
        {
            File.WriteAllBytes(path, bytes);

            var result = FaviconService.TryCreatePngDataUrl(path);

            Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(bytes)}", result);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
