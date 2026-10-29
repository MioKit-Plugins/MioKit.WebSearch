using Avalonia.Threading;
using System.Text.Json;
using WebSearch.Services;

namespace MioKit.WebSearch.Tests;

public sealed class WebsiteBridgeTransportTests
{
    private static bool Trusted(string source) => WebsitePolicy.Origin(source) == "https://trusted.test";
    [Fact]
    public async Task BootstrapRequiresTrustedSenderCurrentSourceAndActiveSession()
    {
        var active = true; var channel = new FakeChannel();
        using var transport = new WebsiteBridgeTransport(channel, () => active, Trusted, _ => { }, Dispatcher.UIThread);
        await transport.InitializeAsync();
        var forwarded = 0; transport.MessageReceived += (_, _) => forwarded++;
        channel.Emit("about:blank", "{\"type\":\"mio-bootstrap\"}");
        channel.Source = "about:blank";
        channel.Emit("https://trusted.test/", "{\"type\":\"mio-bootstrap\"}");
        channel.Source = "https://trusted.test/"; active = false;
        channel.Emit(channel.Source, "{\"type\":\"mio-bootstrap\"}");
        Assert.Empty(channel.Replies);
        active = true; channel.Emit(channel.Source, "{\"type\":\"mio-bootstrap\"}");
        Assert.Equal(["{\"type\":\"mio-host-ready\"}"], channel.Replies);
        Assert.Equal(0, forwarded);
    }
    [Fact]
    public async Task DisposingBeforeInitializationStillReleasesChannel()
    {
        var channel = new FakeChannel();
        var transport = new WebsiteBridgeTransport(channel, () => true, Trusted, _ => { }, Dispatcher.UIThread);
        transport.Dispose(); transport.Dispose();
        Assert.True(channel.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.InitializeAsync());
    }
    [Fact]
    public async Task InputRequiresBothSenderAndCurrentDocumentToBeTrusted()
    {
        var channel = new FakeChannel();
        using var transport = new WebsiteBridgeTransport(channel, () => true, Trusted, _ => { }, Dispatcher.UIThread);
        var calls = 0; transport.MessageReceived += (_, _) => calls++;
        await transport.InitializeAsync();
        channel.Emit("https://untrusted.test", "{\"messageId\":\"a\"}");
        Assert.Equal(0, calls);
        channel.Source = "https://untrusted.test";
        channel.Emit("https://trusted.test", "{\"messageId\":\"a\"}");
        Assert.Equal(0, calls);
        channel.Source = "https://trusted.test";
        channel.Emit("https://trusted.test/path", "{\"messageId\":\"a\"}");
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task ExpiredNavigationDropsRequestsAndLateReplies()
    {
        var active = true; var channel = new FakeChannel();
        using var transport = new WebsiteBridgeTransport(channel, () => active, Trusted, _ => { }, Dispatcher.UIThread);
        var calls = 0; transport.MessageReceived += (_, _) => calls++;
        await transport.InitializeAsync();
        channel.Emit(channel.Source, "{\"messageId\":\"old-request\"}");
        Assert.Equal(1, calls);
        active = false;
        transport.SendMessage("sensitive old result");
        channel.Emit(channel.Source, "{\"messageId\":\"stale-request\"}");
        Assert.Empty(channel.Replies); Assert.Equal(1, calls);
    }
    [Fact]
    public async Task CrossOriginDestinationCannotReceiveAReply()
    {
        var channel = new FakeChannel();
        using var transport = new WebsiteBridgeTransport(channel, () => true, Trusted, _ => { }, Dispatcher.UIThread);
        await transport.InitializeAsync();
        transport.SendMessage("first");
        channel.Source = "https://untrusted.test/";
        transport.SendMessage("secret");
        Assert.Equal(["first"], channel.Replies);
    }
    [Fact]
    public async Task DisposingDetachesChannelAndCancelsClosingHandshake()
    {
        var channel = new FakeChannel();
        var transport = new WebsiteBridgeTransport(channel, () => true, Trusted, _ => { }, Dispatcher.UIThread);
        await transport.InitializeAsync();
        var closing = transport.RunClosingAsync();
        transport.Dispose();
        Assert.True(channel.Disposed);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => closing);
        transport.SendMessage("late");
        Assert.Empty(channel.Replies);
    }
    [Fact]
    public async Task ClosingHandshakeRequiresMatchingNonce()
    {
        var channel = new FakeChannel();
        using var transport = new WebsiteBridgeTransport(channel, () => true, Trusted, _ => { }, Dispatcher.UIThread);
        await transport.InitializeAsync();
        var closing = transport.RunClosingAsync();
        channel.Emit(channel.Source, JsonSerializer.Serialize(new { type = "mio-lifecycle-close", nonce = "wrong" }));
        Assert.False(closing.IsCompleted);
        var script = channel.LastScript!;
        var nonce = JsonSerializer.Deserialize<string>(script[(script.LastIndexOf('(') + 1)..^1]);
        channel.Emit(channel.Source, JsonSerializer.Serialize(new { type = "mio-lifecycle-close", nonce }));
        await closing;
    }
    [Fact]
    public async Task MalformedMessagesAreLoggedAndDoNotReachServiceBridge()
    {
        var logs = new List<string>(); var channel = new FakeChannel();
        using var transport = new WebsiteBridgeTransport(channel, () => true, Trusted, logs.Add, Dispatcher.UIThread);
        var calls = 0; transport.MessageReceived += (_, _) => calls++;
        await transport.InitializeAsync();
        channel.Emit(channel.Source, "not-json");
        channel.Emit(channel.Source, "[]");
        channel.Emit(channel.Source, "{\"type\":\"mio-script-error\",\"message\":\"test error\"}");
        Assert.Equal(0, calls); Assert.Equal(3, logs.Count);
        Assert.Equal("test error", logs[^1]);
    }
    [Theory]
    [InlineData("active")]
    [InlineData("trusted")]
    [InlineData("source")]
    [InlineData("subscriber")]
    public async Task CallbackFailuresAreContainedAndLaterMessagesStillWork(string failure)
    {
        var fail = true;
        var channel = new FakeChannel();
        var logs = new List<string>();
        using var transport = new WebsiteBridgeTransport(channel,
            () => fail && failure == "active" ? throw new ObjectDisposedException("session") : true,
            source => fail && failure == "trusted" ? throw new InvalidOperationException("policy") : Trusted(source),
            logs.Add, Dispatcher.UIThread);
        var calls = 0;
        transport.MessageReceived += (_, _) =>
        {
            if (fail && failure == "subscriber") throw new InvalidOperationException("bridge");
            calls++;
        };
        await transport.InitializeAsync();
        channel.ThrowOnSource = failure == "source";
        channel.Emit("https://trusted.test", "{\"messageId\":\"failed\"}");
        Assert.Equal(0, calls);
        Assert.Single(logs);
        fail = false;
        channel.ThrowOnSource = false;
        channel.Emit("https://trusted.test", "{\"messageId\":\"next\"}");
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FailingErrorReporterCannotEscapeMessageCallback()
    {
        var channel = new FakeChannel();
        using var transport = new WebsiteBridgeTransport(channel, () => true, Trusted,
            _ => throw new ObjectDisposedException("logger"), Dispatcher.UIThread);
        await transport.InitializeAsync();
        channel.Emit(channel.Source, "not-json");
        var calls = 0;
        transport.MessageReceived += (_, _) => calls++;
        channel.Emit(channel.Source, "{}");
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ChannelDisposalFailureStillCancelsClosingHandshake()
    {
        var channel = new FakeChannel { ThrowOnDispose = true };
        var transport = new WebsiteBridgeTransport(channel, () => true, Trusted, _ => { }, Dispatcher.UIThread);
        await transport.InitializeAsync();
        var closing = transport.RunClosingAsync();
        Assert.Throws<InvalidOperationException>(transport.Dispose);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => closing);
        Assert.True(channel.Disposed);
        transport.Dispose();
    }

    private sealed class FakeChannel : IWebsiteMessageChannel
    {
        private string source = "https://trusted.test";
        public bool ThrowOnSource { get; set; }
        public bool ThrowOnDispose { get; set; }
        public string Source
        {
            get => ThrowOnSource ? throw new ObjectDisposedException("WebView") : source;
            set => source = value;
        }
        public event EventHandler<WebsiteWebMessage>? MessageReceived;
        public List<string> Replies { get; } = [];
        public string? LastScript { get; private set; }
        public bool Disposed { get; private set; }
        public void Emit(string source, string json) => MessageReceived?.Invoke(this, new(source, json));
        public void Send(string message) => Replies.Add(message);
        public Task ExecuteScriptAsync(string script) { LastScript = script; return Task.CompletedTask; }
        public void Dispose()
        {
            Disposed = true;
            if (ThrowOnDispose) throw new InvalidOperationException("channel cleanup failed");
        }
    }
}

