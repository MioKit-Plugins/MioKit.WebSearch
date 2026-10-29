using System.Net;
using System.Text;
using WebSearch.Services;

namespace MioKit.WebSearch.Tests;

[Collection("Website backend")]
public sealed class FaviconResponseLimitTests(WebsiteBackendFixture fixture)
{
    private const int HtmlLimit = 1024 * 1024;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedPageInspectsOnlyPrefixAndStillRequestsFallback(bool earlyIcon)
    {
        _ = fixture;
        var ct = TestContext.Current.CancellationToken;
        const string link = "<link rel=\"icon\" href=\"/site-icon.png\">";
        var padding = new string(' ', HtmlLimit + 64);
        var page = new CountingStream(Encoding.UTF8.GetBytes(earlyIcon ? link + padding : padding + link));
        var handler = new PageHandler(page);
        var plugin = TestPluginContext.CreatePlugin(Path.Combine(Path.GetTempPath(), "favicon-response-tests-" + Guid.NewGuid().ToString("N")));
        await using var service = new FaviconService(plugin, handler);
        await service.StartAsync(ct);

        // Icon requests deliberately return 404: no graphics backend, files or real network involved.
        Assert.False(await service.RefreshAsync(new Uri("https://large.test/"), ct));

        Assert.Equal(HtmlLimit, page.BytesRead);
        Assert.True(page.IsDisposed);
        Assert.Equal(earlyIcon ? new[] { "/", "/site-icon.png", "/favicon.ico" } : new[] { "/", "/favicon.ico" }, handler.Paths);
    }

    [Fact]
    public async Task OversizedImageIsRejectedAndNextCandidateIsTried()
    {
        var ct = TestContext.Current.CancellationToken;
        var page = new CountingStream("<link rel=\"icon\" href=\"/site-icon.png\">"u8.ToArray());
        var image = new CountingStream(new byte[512 * 1024 + 100]);
        var handler = new PageHandler(page, image);
        var plugin = TestPluginContext.CreatePlugin(Path.Combine(Path.GetTempPath(), "favicon-response-tests-" + Guid.NewGuid().ToString("N")));
        await using var service = new FaviconService(plugin, handler);
        await service.StartAsync(ct);

        Assert.False(await service.RefreshAsync(new Uri("https://large-image.test/"), ct));
        Assert.Equal(new[] { "/", "/site-icon.png", "/favicon.ico" }, handler.Paths);
        Assert.Equal(512 * 1024 + 1, image.BytesRead);
        Assert.True(image.IsDisposed);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public async Task StrictReaderAcceptsCompleteResponsesUpToLimit(int length)
    {
        var bytes = Enumerable.Range(0, length).Select(x => (byte)x).ToArray();
        using var stream = new MemoryStream(bytes);
        Assert.Equal(bytes, await FaviconService.ReadLimitedAsync(stream, 8, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StrictReaderRejectsOverflowInsteadOfReturningTruncatedImage()
    {
        using var stream = new CountingStream(new byte[30]);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            FaviconService.ReadLimitedAsync(stream, 8, TestContext.Current.CancellationToken));
        Assert.Equal(9, stream.BytesRead);
    }

    [Fact]
    public async Task PrefixReaderStillHonorsCancellation()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        using var stream = new CountingStream(new byte[30]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FaviconService.ReadLimitedAsync(stream, 8, cts.Token, allowTruncation: true));
        Assert.Equal(0, stream.BytesRead);
    }

    private sealed class PageHandler(CountingStream page, CountingStream? image = null) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            var body = path == "/" ? page : path == "/site-icon.png" ? image : null;
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body), RequestMessage = request });
        }
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        // Unknown-length HTTP body, as with a chunked or decompressed response.
        public override bool CanSeek => false;
        public int BytesRead { get; private set; }
        public bool IsDisposed { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
