using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace WebSearch.Services;

/// <summary>Uses ServiceBridge's protocol, while binding every request/reply to one document.</summary>
public sealed class WebsiteBridgeTransport : IWebMessageHandler
{
    private readonly IWebsiteMessageChannel channel;
    private readonly Func<bool> active;
    private readonly Func<string, bool> trusted;
    private readonly Action<string> log;
    public event EventHandler<string>? MessageReceived;
    public bool IsInitialized { get; private set; }
    public Dispatcher Dispatcher { get; }
    private TaskCompletionSource? closing;
    private string? closeNonce;
    private bool disposed;
    public WebsiteBridgeTransport(CoreWebView2 core, Func<bool> active, Func<string, bool> trusted,
        Action<string> log, Dispatcher dispatcher) : this(new CoreWebsiteMessageChannel(core), active, trusted, log, dispatcher) { }
    internal WebsiteBridgeTransport(IWebsiteMessageChannel channel, Func<bool> active, Func<string, bool> trusted,
        Action<string> log, Dispatcher dispatcher)
    {
        this.channel = channel; this.active = active; this.trusted = trusted; this.log = log;
        Dispatcher = dispatcher;
    }
    public Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsInitialized) channel.MessageReceived += Receive;
        IsInitialized = true;
        return Task.CompletedTask;
    }
    private void Receive(object? sender, WebsiteWebMessage e)
    {
        try
        {
            if (!IsInitialized || !active() || !trusted(e.Source) || !trusted(channel.Source)) return;
            var json = e.Json;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var type))
            {
                if (type.GetString() == "mio-bootstrap")
                {
                    SendMessage("{\"type\":\"mio-host-ready\"}");
                    return;
                }
                if (type.GetString() == "mio-script-error")
                {
                    log(doc.RootElement.GetProperty("message").GetString() ?? "脚本错误");
                    return;
                }
                if (type.GetString() == "mio-lifecycle-close")
                {
                    if (doc.RootElement.GetProperty("nonce").GetString() == closeNonce) closing?.TrySetResult();
                    return;
                }
            }
            MessageReceived?.Invoke(this, json);
        }
        catch (Exception ex)
        {
            try { log("网页消息无效：" + ex.Message); }
            catch { /* Reporting cannot escape the native message event during shutdown. */ }
        }
    }
    public void SendMessage(string message)
    {
        if (IsInitialized && active() && trusted(channel.Source)) channel.Send(message);
    }
    public async Task RunClosingAsync()
    {
        if (!IsInitialized || !active() || !trusted(channel.Source)) return;
        closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        closeNonce = Guid.NewGuid().ToString("N");
        await channel.ExecuteScriptAsync("window.__mioClose && window.__mioClose(" + JsonSerializer.Serialize(closeNonce) + ")");
        await closing.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        IsInitialized = false;
        try { channel.MessageReceived -= Receive; }
        finally
        {
            try { channel.Dispose(); }
            finally { closing?.TrySetCanceled(); }
        }
    }
}

