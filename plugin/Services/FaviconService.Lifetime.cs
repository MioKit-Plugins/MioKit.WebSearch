namespace WebSearch.Services;

public sealed partial class FaviconService : IPluginLifecycleComponent, IAsyncDisposable
{
    private readonly object _workGate = new();
    private readonly HashSet<Task> _pendingWork = [];
    private CancellationTokenSource? _lifetime;
    private bool _acceptingWork;
    private bool _disposed;
    private Task? _stopTask;
    private Task? _disposeTask;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task stopping;
            lock (_workGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_acceptingWork) return;
                stopping = _stopTask ?? Task.CompletedTask;
            }
            await stopping.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_workGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (_acceptingWork) return;
                if (_stopTask is not null && !ReferenceEquals(stopping, _stopTask)) continue;
                _lifetime = new CancellationTokenSource();
                _stopTask = null;
                _acceptingWork = true;
                return;
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_workGate) return BeginStopLocked().WaitAsync(cancellationToken);
    }

    private Task BeginStopLocked()
    {
        _acceptingWork = false;
        if (_stopTask is not null) return _stopTask;
        var lifetime = _lifetime;
        var pending = _pendingWork.ToArray();
        // Cancel outside the admission lock: cancellation callbacks can run synchronously.
        return _stopTask = Task.Run(async () =>
        {
            try
            {
                if (lifetime is not null) await lifetime.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _plugin.Context?.Logger.Warning(exception, "Favicon cancellation callback failed.");
            }
            await Task.WhenAll(pending).ConfigureAwait(false);
            lifetime?.Dispose();
            _watchers.Clear();
            _failedOrigins.Clear();
            _dataUrlCache.Clear();
        });
    }

    // Called with _workGate held; work is registered before shutdown can take its snapshot.
    private Task<bool> TrackWorkLocked(Func<CancellationToken, Task<bool>> work, string? originKey = null)
    {
        var token = _lifetime!.Token;
        Task<bool>? task = null;
        task = Task.Run(async () =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                return await work(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
            catch (Exception exception)
            {
                _plugin.Context?.Logger.Debug(exception, "Favicon background operation failed.");
                return false;
            }
            finally
            {
                lock (_workGate)
                {
                    _pendingWork.Remove(task!);
                    if (originKey is not null) _inflight.Remove(originKey);
                }
            }
        });
        _pendingWork.Add(task);
        return task;
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        lock (_workGate)
        {
            _disposed = true;
            return new ValueTask(_disposeTask ??= DisposeCoreAsync(BeginStopLocked()));
        }
    }

    private async Task DisposeCoreAsync(Task stopping)
    {
        await stopping.ConfigureAwait(false);
        _httpClient.Dispose();
        _httpGate.Dispose();
        _prefetchGate.Dispose();
    }
}
