using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using MioKit.Sdk;
using Serilog;
using WebSearch.Features;
using WebSearch.Models;
using WebSearch.Nodes;
using WebSearch.Services;

namespace MioKit.WebSearch.Tests;

[CollectionDefinition("Website backend")]
public sealed class WebsiteBackendCollection : ICollectionFixture<WebsiteBackendFixture>;

/// <summary>In-memory SDK stores only; no Application, WebView controller, UI thread or host database.</summary>
public sealed class WebsiteBackendFixture
{
    public WebsiteBackendFixture()
    {
        var sdk = typeof(MioObject).Assembly;
        var factory = sdk.GetType("MioKit.Sdk.StoreFactory")!.GetProperty("StoreFactoryFunc", BindingFlags.NonPublic | BindingFlags.Static)!;
        var owner = Expression.Parameter(typeof(MioObject));
        factory.SetValue(null, Expression.Lambda(factory.PropertyType,
            Expression.New(typeof(DesignMioStore).GetConstructor([typeof(MioObject)])!, owner), owner).Compile());
        var context = (MioAppContext)Activator.CreateInstance(typeof(MioAppContext), true)!;
        typeof(MioAppContext).GetProperty("FrameworkLogger")!.SetValue(context, new LoggerConfiguration().CreateLogger());
        typeof(MioAppContext).GetProperty("EventBus")!.SetValue(context, new QuietEventBus());
        typeof(MioAppContext).GetProperty("Current")!.SetValue(null, context);
    }
    public static void Attach(WebsiteNode node, bool value = true) =>
        typeof(MioObject).GetProperty("IsAttachRootTree")!.SetValue(node, value);
    private sealed class QuietEventBus : IMioEventBus
    {
        public Task PublishAsync<T>(T e, Mode mode = Mode.WaitForNone, CancellationToken cancellation = default) where T : IEvent => Task.CompletedTask;
        public void RegisterEventHandle<T>(IMioEventHandler<T> handler) where T : IEvent { }
        public void UnregisterEventHandle<T>(IMioEventHandler<T> handler) where T : IEvent { }
    }
}

[Collection("Website backend")]
public sealed partial class WebsiteCapabilitiesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "websearch-backend-" + Guid.NewGuid().ToString("N"));
    private readonly WebsiteStorageService storage = new();
    private readonly TestToasts toasts = new();
    public WebsiteCapabilitiesTests(WebsiteBackendFixture _) => Directory.CreateDirectory(root);
    public void Dispose()
    {
        var absolute = Path.GetFullPath(root);
        if (!absolute.StartsWith(Path.Combine(Path.GetTempPath(), "websearch-backend-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected test directory.");
        Directory.Delete(absolute, true);
    }
    private WebsiteNode Node(params string[] permissions)
    {
        var node = new WebsiteNode("backend-" + Guid.NewGuid().ToString("N")) { Name = "Test", UrlTemplate = "https://example.test/" };
        node.WriteConfiguration(new() { Runtime = new() { Permissions = permissions.ToList() } });
        WebsiteBackendFixture.Attach(node);
        return node;
    }
    private WebsitePageContext Context(WebsiteNode node, Func<string>? address = null, Func<bool>? active = null) =>
        new(node, new(), node.UrlTemplate, address ?? (() => node.UrlTemplate), active ?? (() => true), null!);
    private WebsiteCapabilitiesJsService Api(WebsitePageContext context) => new(context, storage, toasts);
    private static Task<MioApiResult> Call(WebsiteCapabilitiesJsService api, string service, string operation, object args) =>
        api.Invoke(service, operation, JsonSerializer.Serialize(args));
    private static void Failure(MioApiResult result, string code)
    {
        Assert.False(result.Success); Assert.Equal(code, result.Code); Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("storage", "get")]
    [InlineData("files", "readText")]
    [InlineData("files", "writeText")]
    [InlineData("dialogs", "openFile")]
    [InlineData("clipboard", "readText")]
    [InlineData("clipboard", "writeText")]
    [InlineData("notifications", "show")]
    public async Task EveryCapabilityDefaultsToDeniedWithoutOpeningUi(string service, string operation)
    {
        var result = await Call(Api(Context(Node())), service, operation, new { path = Path.Combine(root, "a"), key = "a" });
        Failure(result, "permission.denied");
        Assert.Equal(0, toasts.Count);
    }

    [Fact]
    public async Task ExpiredDetachedAndUntrustedDocumentsAreDenied()
    {
        var node = Node("storage");
        var active = true;
        var address = node.UrlTemplate;
        var api = Api(Context(node, () => address, () => active));
        Assert.True((await Call(api, "storage", "set", new { key = "a", value = "safe" })).Success);
        active = false;
        Failure(await Call(api, "storage", "get", new { key = "a" }), "document.expired");
        active = true; address = "https://other.test/";
        Failure(await Call(api, "storage", "get", new { key = "a" }), "document.expired");
        address = node.UrlTemplate;
        WebsiteBackendFixture.Attach(node, false);
        Failure(await Call(api, "storage", "get", new { key = "a" }), "document.expired");
    }

    [Fact]
    public async Task PermissionRevocationAppliesToExistingContext()
    {
        var node = Node("storage");
        var api = Api(Context(node));
        Assert.True((await Call(api, "storage", "set", new { key = "a", value = 1 })).Success);
        node.SetRuntimeOptions(new());
        Failure(await Call(api, "storage", "set", new { key = "a", value = 2 }), "permission.denied");
        Assert.Equal(1, JsonDocument.Parse(node.GetWebsiteStorageJson()!).RootElement.GetProperty("a").GetInt32());
    }

    [Fact]
    public async Task ConcurrentWindowsShareNodeStorageWithoutLosingWrites()
    {
        var node = Node("storage");
        var first = Api(Context(node)); var second = Api(Context(node));
        var writes = Enumerable.Range(0, 100).Select(i => Call(i % 2 == 0 ? first : second, "storage", "set", new { key = "key" + i, value = new { index = i } }));
        Assert.All(await Task.WhenAll(writes), result => Assert.True(result.Success, result.Message));
        var values = JsonDocument.Parse(node.GetWebsiteStorageJson()!).RootElement;
        Assert.Equal(100, values.EnumerateObject().Count());
        Assert.Equal(37, values.GetProperty("key37").GetProperty("index").GetInt32());
        Assert.Equal(42, ((JsonElement)(await Call(second, "storage", "get", new { key = "key42" })).Value!).GetProperty("index").GetInt32());
    }

    [Fact]
    public async Task DifferentNodesCannotReadOrClearEachOthersStorage()
    {
        var first = Node("storage"); var second = Node("storage");
        await Call(Api(Context(first)), "storage", "set", new { key = "secret", value = "first" });
        var result = await Call(Api(Context(second)), "storage", "get", new { key = "secret" });
        Assert.True(result.Success); Assert.Null(result.Value);
        await Call(Api(Context(second)), "storage", "clear", new { });
        Assert.Contains("first", first.GetWebsiteStorageJson());
    }

    [Fact]
    public async Task StorageSupportsRemoveClearAndInvalidJsonDoesNotCorruptData()
    {
        var node = Node("storage"); var api = Api(Context(node));
        await Call(api, "storage", "set", new { key = "a", value = new[] { 1, 2 } });
        var before = node.GetWebsiteStorageJson();
        Failure(await api.Invoke("storage", "set", "{broken"), "operation.failed");
        Assert.Equal(before, node.GetWebsiteStorageJson());
        await Call(api, "storage", "remove", new { key = "a" });
        Assert.Equal("{}", node.GetWebsiteStorageJson());
        await Call(api, "storage", "set", new { key = "b", value = true });
        await Call(api, "storage", "clear", new { });
        Assert.Equal("{}", node.GetWebsiteStorageJson());
    }

    [Fact]
    public async Task ReadOnlyDirectoryCannotBeWrittenAndMissingGrantIsDenied()
    {
        var node = Node("files.read", "files.write");
        node.SetRuntimeOptions(new() { Permissions = ["files.read", "files.write"], FileDirectories = [new() { Directory = root, Read = true }] });
        var path = Path.Combine(root, "data.txt"); await File.WriteAllTextAsync(path, "existing");
        var api = Api(Context(node));
        Assert.Equal("existing", (await Call(api, "files", "readText", new { path })).Value);
        Failure(await Call(api, "files", "writeText", new { path, content = "changed" }), "path.denied");
        Failure(await Call(api, "files", "readText", new { path = root + "-outside/file.txt" }), "path.denied");
        Failure(await Call(api, "files", "readText", new { path = "relative.txt" }), "path.invalid");
        Assert.Equal("existing", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task TemporarySelectionIsExactAndWindowLocal()
    {
        var node = Node("files.read", "files.write");
        var first = Context(node); var second = Context(node);
        var selected = Path.Combine(root, "selected.txt");
        first.Grant(selected, false, true);
        Assert.True((await Call(Api(first), "files", "writeText", new { path = selected, content = "selected" })).Success);
        Failure(await Call(Api(second), "files", "readText", new { path = selected }), "path.denied");
        Failure(await Call(Api(first), "files", "writeText", new { path = selected + ".other", content = "bad" }), "path.denied");
        Assert.False(File.Exists(selected + ".other"));
    }

    [Fact]
    public async Task SelectionSurvivesTrustedNavigationWithinItsOwnWindow()
    {
        var node = Node("files.read", "files.write");
        var selections = new WebsiteFileGrantSession();
        var firstDocument = new WebsitePageContext(node, new(), node.UrlTemplate, () => node.UrlTemplate, () => true, null!, selections);
        var selected = Path.Combine(root, "selected.txt");
        firstDocument.Grant(selected, false, true);
        var nextDocument = new WebsitePageContext(node, new(), node.UrlTemplate, () => node.UrlTemplate + "next", () => true, null!, selections);
        Assert.True((await Call(Api(nextDocument), "files", "writeText", new { path = selected, content = "saved" })).Success);
        selections.Clear();
        Failure(await Call(Api(nextDocument), "files", "readText", new { path = selected }), "path.denied");
    }

    [Fact]
    public async Task BinaryAndDirectoryOperationsRespectTargetGrant()
    {
        var node = Node("files.read", "files.write");
        node.SetRuntimeOptions(new() { Permissions = ["files.read", "files.write"], FileDirectories = [new() { Directory = root, Read = true, Write = true }] });
        var api = Api(Context(node));
        var folder = Path.Combine(root, "nested");
        Assert.True((await Call(api, "files", "createDirectory", new { path = folder })).Success);
        var path = Path.Combine(folder, "binary.dat"); var content = Convert.ToBase64String([0, 1, 127, 255]);
        Assert.True((await Call(api, "files", "writeBase64", new { path, content })).Success);
        Assert.Equal(content, (await Call(api, "files", "readBase64", new { path })).Value);
        Failure(await Call(api, "files", "move", new { path, target = root + "-outside/a.dat" }), "path.denied");
        Assert.True(File.Exists(path));
        var target = Path.Combine(root, "moved.dat");
        Assert.True((await Call(api, "files", "move", new { path, target })).Success);
        Assert.False(File.Exists(path)); Assert.True(File.Exists(target));
        Assert.True((await Call(api, "files", "delete", new { path = target })).Success);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task RestoredConfigurationAndStorageUseNodeExtensions()
    {
        var node = Node("storage", "notifications");
        var config = await node.ReadConfigurationAsync();
        config.OpenMode = "Embedded"; config.DebugEnabled = true;
        config.Runtime.NewWindowMode = "Current";
        config.Runtime.Scripts.Add(new() { Code = "await Mio.storage.set('saved',true);", Stage = "Closing" });
        node.WriteConfiguration(config);
        var restored = new WebsiteNode("restored") { UrlTemplate = node.UrlTemplate };
        restored.WriteConfiguration(JsonSerializer.Deserialize<WebsiteConfiguration>(JsonSerializer.Serialize(await node.ReadConfigurationAsync()))!);
        var actual = await restored.ReadConfigurationAsync();
        Assert.Equal("Embedded", actual.OpenMode); Assert.True(actual.DebugEnabled);
        Assert.Equal("Current", actual.Runtime.NewWindowMode); Assert.Single(actual.Runtime.Scripts);
        await Call(Api(Context(node)), "storage", "set", new { key = "persisted", value = new { nested = true } });
        restored.SetWebsiteStorageJson(node.GetWebsiteStorageJson());
        WebsiteBackendFixture.Attach(restored);
        var value = (await Call(Api(Context(restored)), "storage", "get", new { key = "persisted" })).Value;
        Assert.True(((JsonElement)value!).GetProperty("nested").GetBoolean());
    }

    [Fact]
    public async Task LegacyNodeUsesCompatibleDefaults()
    {
        var node = new WebsiteNode("legacy");
        var c = await node.ReadConfigurationAsync();
        Assert.Equal("External", c.OpenMode); Assert.Equal("Online", c.SourceKind);
        Assert.True(c.MapLocalDirectory); Assert.Empty(c.Runtime.Permissions);
    }

    [Fact]
    public async Task NotificationServiceUsesInjectedPluginInstance()
    {
        var api = Api(Context(Node("notifications")));
        var result = await Call(api, "notifications", "show", new { message = "Done", title = "Test" });
        Assert.True(result.Success); Assert.Equal(1, toasts.Count);
    }

    [Fact]
    public async Task UnrecognizedOperationsHaveStableErrors()
    {
        var api = Api(Context(Node("storage", "files.read")));
        Failure(await Call(api, "unknown", "get", new { }), "service.invalid");
        Failure(await Call(api, "storage", "unknown", new { }), "operation.invalid");
        Failure(await Call(api, "files", "unknown", new { path = root }), "path.denied");
    }

    private sealed class TestToasts : IToastService
    {
        public int Count { get; private set; }
        public Guid Show(string message, ToastLevel level = ToastLevel.Info, string? title = null) { Count++; return Guid.NewGuid(); }
        public Guid Show(ToastOptions options) { Count++; return Guid.NewGuid(); }
        public bool Update(Guid id, ToastOptions options) => true;
        public bool Dismiss(Guid id) => true;
    }
}

