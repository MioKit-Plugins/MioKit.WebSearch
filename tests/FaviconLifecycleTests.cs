using System.Net;
using WebSearch.Services;

namespace MioKit.WebSearch.Tests;

[Collection("Website backend")]
public sealed class FaviconLifecycleTests(WebsiteBackendFixture fixture)
{
    private FaviconService Create(BlockingHandler handler)
    {
        _ = fixture;
        // No response reaches image decoding or disk writes; every request is intercepted.
        var plugin = TestPluginContext.CreatePlugin(Path.Combine(Path.GetTempPath(), "websearch-lifecycle-tests"));
        return new(plugin, handler);
    }

    [Fact]
    public async Task CompletedRefreshIsRemovedSoAnExplicitRetryCanRun()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MissingIconHandler();
        var plugin = TestPluginContext.CreatePlugin(Path.Combine(Path.GetTempPath(), "websearch-lifecycle-tests"));
        await using var service = new FaviconService(plugin, handler);
        await service.StartAsync(ct);
        var origin = new Uri("https://missing.test/");
        Assert.False(await service.RefreshAsync(origin, ct));
        Assert.Equal(2, handler.Requests);
        Assert.False(await service.RefreshAsync(origin, ct));
        Assert.Equal(4, handler.Requests);
    }

    [Fact]
    public async Task StopCancelsActiveAndQueuedRequestsAndWaitsBeforeDisposingResources()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new BlockingHandler();
        var service = Create(handler);
        try
        {
            await service.StartAsync(ct);
            var refreshes = Enumerable.Range(0, 6)
                .Select(i => service.RefreshAsync(new Uri($"https://site{i}.test/"), ct)).ToArray();
            await handler.WaitForRequestsAsync(2, ct);

            var stopping = service.StopAsync(ct);
            await handler.CancellationSeen.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.False(stopping.IsCompleted);
            Assert.False(handler.Disposed);
            Assert.False(await service.RefreshAsync(new Uri("https://late.test/"), ct));
            service.RequestRefresh(new Uri("https://late-background.test/"));
            service.RequestPrefetch([], [new Uri("https://late-prefetch.test/")]);

            handler.AllowExit.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.All(await Task.WhenAll(refreshes), result => Assert.False(result));
            Assert.Equal(2, handler.Started);
            Assert.Equal(0, handler.Active);
            await service.StopAsync(ct);
            await service.DisposeAsync();
            Assert.True(handler.Disposed);
            Assert.Equal(0, handler.ActiveWhenDisposed);
        }
        finally
        {
            handler.AllowExit.TrySetResult();
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeWithoutStopAlsoDrainsPrefetchedRequestsAndRejectsNewWork()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new BlockingHandler();
        var service = Create(handler);
        try
        {
            await service.StartAsync(ct);
            await service.PrefetchAsync([], Enumerable.Range(0, 6)
                .Select(i => new Uri($"https://prefetch{i}.test/")).ToArray(), ct);
            await handler.WaitForRequestsAsync(2, ct);

            var disposing = service.DisposeAsync().AsTask();
            await handler.CancellationSeen.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.False(disposing.IsCompleted);
            Assert.False(handler.Disposed);
            handler.AllowExit.TrySetResult();
            await disposing.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.True(handler.Disposed);
            Assert.Equal(0, handler.ActiveWhenDisposed);
            Assert.Equal(2, handler.Started);

            service.RequestRefresh(new Uri("https://after-dispose.test/"));
            service.RequestPrefetch([], [new Uri("https://after-dispose.test/")]);
            Assert.False(await service.RefreshAsync(new Uri("https://after-dispose.test/"), ct));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StartAsync(ct));
        }
        finally
        {
            handler.AllowExit.TrySetResult();
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task StoppedServiceCanRestartAndRefreshTheSameOrigin()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new BlockingHandler();
        var service = Create(handler);
        var origin = new Uri("https://restart.test/");
        try
        {
            await service.StartAsync(ct);
            var first = service.RefreshAsync(origin, ct);
            await handler.WaitForRequestsAsync(1, ct);
            handler.AllowExit.TrySetResult();
            await service.StopAsync(ct);
            Assert.False(await first);
            Assert.False(handler.Disposed);

            await service.StartAsync(ct);
            var second = service.RefreshAsync(origin, ct);
            await handler.WaitForRequestsAsync(2, ct);
            await service.StopAsync(ct);
            Assert.False(await second);
            Assert.Equal(2, handler.Started);
        }
        finally
        {
            handler.AllowExit.TrySetResult();
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task CancelingOneCallerDoesNotCancelAnotherCallerSharingTheOrigin()
    {
        var ct = TestContext.Current.CancellationToken;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var handler = new BlockingHandler();
        var service = Create(handler);
        var origin = new Uri("https://shared.test/");
        try
        {
            await service.StartAsync(ct);
            var first = service.RefreshAsync(origin, caller.Token);
            await handler.WaitForRequestsAsync(1, ct);
            var second = service.RefreshAsync(origin, ct);
            await caller.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            Assert.False(second.IsCompleted);
            Assert.Equal(1, handler.Started);
            Assert.False(handler.CancellationSeen.Task.IsCompleted);

            handler.AllowExit.TrySetResult();
            await service.StopAsync(ct);
            Assert.False(await second);
        }
        finally
        {
            handler.AllowExit.TrySetResult();
            await service.DisposeAsync();
        }
    }

    private sealed class MissingIconHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource[] requests = Enumerable.Range(0, 6)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        private int started;
        private int active;
        public int Started => Volatile.Read(ref started);
        public int Active => Volatile.Read(ref active);
        public bool Disposed { get; private set; }
        public int ActiveWhenDisposed { get; private set; }
        public TaskCompletionSource CancellationSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowExit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitForRequestsAsync(int count, CancellationToken ct) =>
            requests[count - 1].Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref active);
            var number = Interlocked.Increment(ref started);
            if (number <= requests.Length) requests[number - 1].TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new(HttpStatusCode.NotFound);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationSeen.TrySetResult();
                // Delayed cleanup proves Stop/Dispose wait for real completion, not just cancellation.
                await AllowExit.Task;
                throw;
            }
            finally { Interlocked.Decrement(ref active); }
        }

        protected override void Dispose(bool disposing)
        {
            ActiveWhenDisposed = Active;
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
