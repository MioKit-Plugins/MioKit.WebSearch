using System.Text.Json;
using Avalonia.Threading;
using WebSearch.Features;
using WebSearch.Services;

namespace MioKit.WebSearch.Tests;

public sealed partial class WebsiteCapabilitiesTests
{
    [Theory]
    [InlineData("storage", "get")]
    [InlineData("storage", "set")]
    [InlineData("files", "readText")]
    [InlineData("files", "writeText")]
    [InlineData("dialogs", "openFile")]
    [InlineData("dialogs", "saveFile")]
    [InlineData("clipboard", "readText")]
    [InlineData("clipboard", "writeText")]
    [InlineData("notifications", "show")]
    [InlineData("external", "open")]
    public async Task InvalidatedSessionRejectsEveryAuthorizedCapabilityBeforeSideEffects(string service, string operation)
    {
        var node = Node(WebsitePolicy.Permissions);
        var context = Context(node, active: () => false);
        var result = await Call(Api(context), service, operation, new { key = "secret", path = Path.Combine(root, "future.txt"), content = "bad", url = "https://example.test/" });
        Failure(result, "document.expired");
        Assert.False(File.Exists(Path.Combine(root, "future.txt")));
        Assert.Equal(0, toasts.Count);
        Assert.Null(node.GetWebsiteStorageJson());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"key\":42,\"value\":true}")]
    public async Task MalformedArgumentsLeaveExistingStorageUntouchedAndNextRequestWorks(string json)
    {
        var node = Node("storage"); var api = Api(Context(node));
        Assert.True((await Call(api, "storage", "set", new { key = "a", value = "original" })).Success);
        var before = node.GetWebsiteStorageJson();
        Failure(await api.Invoke("storage", "set", json), "operation.failed");
        Assert.Equal(before, node.GetWebsiteStorageJson());
        Assert.Equal("original", ((JsonElement)(await Call(api, "storage", "get", new { key = "a" })).Value!).GetString());
    }

    [Fact]
    public async Task RemovingDirectoryGrantOverridesAnExistingDocument()
    {
        var node = Node("files.read", "files.write");
        var runtime = node.GetRuntimeOptions()!;
        runtime.FileDirectories.Add(new() { Directory = root, Read = true, Write = true });
        node.SetRuntimeOptions(runtime);
        var api = Api(Context(node)); var path = Path.Combine(root, "data.txt");
        Assert.True((await Call(api, "files", "writeText", new { path, content = "original" })).Success);
        node.SetRuntimeOptions(new() { Permissions = ["files.read", "files.write"] });
        Failure(await Call(api, "files", "readText", new { path }), "path.denied");
        Failure(await Call(api, "files", "writeText", new { path, content = "bad" }), "path.denied");
        Assert.Equal("original", File.ReadAllText(path));
    }

    [Fact]
    public async Task PickerGrantCannotOverrideRevokedFilePermission()
    {
        var node = Node("files.read", "files.write"); var context = Context(node);
        var path = Path.Combine(root, "selected.txt");
        context.Grant(path, directory: false, write: true);
        node.SetRuntimeOptions(new());
        Failure(await Call(Api(context), "files", "writeText", new { path, content = "bad" }), "permission.denied");
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ReadSelectionCannotBeUsedForWritesOrSiblingFiles()
    {
        var node = Node("files.read", "files.write"); var context = Context(node);
        var path = Path.Combine(root, "selected.txt");
        File.WriteAllText(path, "original");
        context.Grant(path, directory: false, write: false);
        var api = Api(context);
        Assert.Equal("original", (await Call(api, "files", "readText", new { path })).Value);
        Failure(await Call(api, "files", "writeText", new { path, content = "bad" }), "path.denied");
        Failure(await Call(api, "files", "readText", new { path = path + ".other" }), "path.denied");
    }

    [Fact]
    public async Task InvalidBase64AndMoveCollisionDoNotDestroyExistingFiles()
    {
        var node = Node("files.read", "files.write");
        node.SetRuntimeOptions(new() { Permissions = ["files.read", "files.write"], FileDirectories = [new() { Directory = root, Read = true, Write = true }] });
        var api = Api(Context(node));
        var path = Path.Combine(root, "source.txt"); var target = Path.Combine(root, "target.txt");
        File.WriteAllText(path, "source"); File.WriteAllText(target, "target");
        Failure(await Call(api, "files", "writeBase64", new { path, content = "invalid base64!" }), "operation.failed");
        Failure(await Call(api, "files", "move", new { path, target }), "operation.failed");
        Assert.Equal("source", File.ReadAllText(path)); Assert.Equal("target", File.ReadAllText(target));
        Failure(await Call(api, "files", "delete", new { path = root }), "operation.failed");
        Assert.True(Directory.Exists(root));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/private.txt")]
    [InlineData("data:text/html,hello")]
    [InlineData("relative/path")]
    public async Task ExternalOpenRejectsNonWebUrlsEvenOnTrustedDocuments(string url) =>
        Failure(await Call(Api(Context(Node())), "external", "open", new { url }), "url.invalid");

    [Fact]
    public async Task EmptyAndUnicodeStorageKeysAndJsonNullRoundTrip()
    {
        var api = Api(Context(Node("storage")));
        foreach (var key in new[] { "", "中文/键", "__proto__" })
            Assert.True((await Call(api, "storage", "set", new { key, value = new { nested = new object?[] { 1, null, "中文" } } })).Success);
        var keys = Assert.IsType<string[]>((await Call(api, "storage", "keys", new { })).Value);
        Assert.Equal(new[] { "", "中文/键", "__proto__" }, keys);
        Assert.True((await api.Invoke("storage", "set", "{\"key\":\"null\",\"value\":null}")).Success);
        Assert.Equal(JsonValueKind.Null, ((JsonElement)(await Call(api, "storage", "get", new { key = "null" })).Value!).ValueKind);
        Assert.True((await Call(api, "storage", "remove", new { key = "missing" })).Success);
    }

    [Fact]
    public async Task RevokingSessionDropsAnInFlightCapabilityResultAndRejectsFurtherRequests()
    {
        var active = true;
        var node = Node("storage");
        var api = Api(Context(node, active: () => active));
        Assert.True((await Call(api, "storage", "set", new { key = "secret", value = "private value" })).Success);
        var channel = new CapabilityChannel(node.UrlTemplate);
        using var transport = new WebsiteBridgeTransport(channel, () => active,
            source => WebsitePolicy.IsTrusted(new(), node.Id, node.UrlTemplate, source), _ => { }, Dispatcher.UIThread);
        var resultReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task pending = Task.CompletedTask;
        var requests = 0;
        async Task Handle()
        {
            requests++;
            var result = await Call(api, "storage", "get", new { key = "secret" });
            Assert.True(result.Success);
            Assert.Equal("private value", ((JsonElement)result.Value!).GetString());
            resultReady.SetResult();
            await releaseReply.Task;
            transport.SendMessage(JsonSerializer.Serialize(result));
        }
        transport.MessageReceived += (_, _) => pending = Handle();
        await transport.InitializeAsync();
        channel.Emit();
        await resultReady.Task.WaitAsync(TestContext.Current.CancellationToken);
        active = false; // Same predicate used by the window for navigation and configuration revocation.
        releaseReply.SetResult();
        await pending;
        channel.Emit();
        Assert.Equal(1, requests);
        Assert.Empty(channel.Replies);
    }

    private sealed class CapabilityChannel(string source) : IWebsiteMessageChannel
    {
        public string Source => source;
        public event EventHandler<WebsiteWebMessage>? MessageReceived;
        public List<string> Replies { get; } = [];
        public void Emit() => MessageReceived?.Invoke(this, new(Source, "{\"messageId\":\"get-secret\"}"));
        public void Send(string message) => Replies.Add(message);
        public Task ExecuteScriptAsync(string script) => Task.CompletedTask;
        public void Dispose() { }
    }
}
