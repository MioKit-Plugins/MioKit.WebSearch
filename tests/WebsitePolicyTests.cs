using WebSearch.Models;
using WebSearch.Services;

namespace MioKit.WebSearch.Tests;

public sealed partial class WebsitePolicyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "websearch-policy-" + Guid.NewGuid().ToString("N"));
    public WebsitePolicyTests() => Directory.CreateDirectory(root);
    public void Dispose()
    {
        var absolute = Path.GetFullPath(root);
        if (!absolute.StartsWith(Path.Combine(Path.GetTempPath(), "websearch-policy-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected test directory.");
        Directory.Delete(absolute, true);
    }
    private static WebsiteEditorRequest Online(string url = "https://example.test/search?q={query}") =>
        new() { Id = "site-test", Name = "Test", UrlTemplate = url };
    [Fact]
    public void DefaultsPreserveExternalOpeningAndDenyAllCapabilities()
    {
        var c = new WebsiteConfiguration();
        Assert.Equal("External", c.OpenMode);
        Assert.Equal("NewWindow", c.Runtime.NewWindowMode);
        Assert.Equal("Current", c.Runtime.CurrentNavigation);
        Assert.False(c.Runtime.AllowAutomaticPopups);
        Assert.Empty(c.Runtime.Permissions);
        Assert.Empty(c.Runtime.BrowserPermissions);
        Assert.True(c.MapLocalDirectory);
        Assert.Equal("", WebsitePolicy.Validate(Online()));
    }
    [Theory]
    [InlineData("https://example.test/path", true)]
    [InlineData("https://EXAMPLE.test:443/path", true)]
    [InlineData("https://example.test.evil.test/path", false)]
    [InlineData("http://example.test/path", false)]
    [InlineData("https://example.test:444/path", false)]
    [InlineData("about:blank", false)]
    [InlineData("data:text/html,Hello", false)]
    public void TrustUsesExactOrigin(string address, bool expected) =>
        Assert.Equal(expected, WebsitePolicy.IsTrusted(new(), "test", "https://example.test", address));
    [Fact]
    public void ExtraMappingsDoNotGrantTrustButExplicitOriginDoes()
    {
        var c = new WebsiteConfiguration();
        c.Runtime.Mappings.Add(new() { Host = "assets.local", Directory = root });
        Assert.False(WebsitePolicy.IsTrusted(c, "test", "https://example.test", "https://assets.local/index.html"));
        c.Runtime.TrustedOrigins.Add("https://assets.local");
        Assert.True(WebsitePolicy.IsTrusted(c, "test", "https://example.test", "https://assets.local/index.html"));
    }
    [Theory]
    [InlineData("https://{query}.example.test/")]
    [InlineData("https://example.test:{query}/")]
    [InlineData("https://user:password@example.test/")]
    [InlineData("javascript:alert(1)")]
    public void RejectsUnsafeEntryAndQueryOrigins(string url)
    {
        var draft = Online(url); draft.Configuration.OpenMode = "Embedded";
        Assert.NotEmpty(WebsitePolicy.Validate(draft));
    }
    [Fact]
    public void ExternalModePreservesLegacyHostTemplates() =>
        Assert.Empty(WebsitePolicy.Validate(Online("https://{query}.example.test/")));
    [Theory]
    [InlineData("https://example.test/path")]
    [InlineData("https://*.example.test")]
    [InlineData("https://example.test?query")]
    [InlineData("file:///C:/")]
    public void RejectsNonOriginTrustEntries(string address)
    {
        var draft = Online();
        draft.Configuration.OpenMode = "Embedded";
        draft.Configuration.Runtime.TrustedOrigins.Add(address);
        Assert.NotEmpty(WebsitePolicy.Validate(draft));
    }
    [Theory]
    [InlineData("jsruntime.local")]
    [InlineData("JSRUNTIME.LOCAL")]
    [InlineData("miokit.locale")]
    [InlineData("https://assets.local")]
    [InlineData("assets.local/path")]
    [InlineData("-invalid.local")]
    public void RejectsReservedOrMalformedHosts(string host) => Assert.False(WebsitePolicy.IsValidHost(host));
    [Fact]
    public void HostAndDataDirectoryStayStableAndIsolatedAcrossNodes()
    {
        Assert.Equal(WebsitePolicy.DefaultHost("a"), WebsitePolicy.DefaultHost("a"));
        Assert.NotEqual(WebsitePolicy.DefaultHost("a"), WebsitePolicy.DefaultHost("b"));
        Assert.NotEqual(WebsitePolicy.DataDirectory(new(), "a", root), WebsitePolicy.DataDirectory(new(), "b", root));
    }
    [Fact]
    public void LocalEntryEscapesFilenameAndSupportsRawFileMode()
    {
        var path = Path.Combine(root, "中文 # index.html");
        File.WriteAllText(path, "<p>local</p>");
        var c = new WebsiteConfiguration { SourceKind = "LocalFile", LocalFilePath = path };
        var address = WebsitePolicy.Entry(c, "local", "");
        Assert.Equal("https", new Uri(address).Scheme);
        Assert.Contains("%23", address);
        Assert.True(WebsitePolicy.IsTrusted(c, "local", "", address));
        c.MapLocalDirectory = false;
        Assert.Equal(path, new Uri(WebsitePolicy.Entry(c, "local", "")).LocalPath);
        Assert.True(WebsitePolicy.IsTrusted(c, "local", "", new Uri(path).AbsoluteUri));
        Assert.False(WebsitePolicy.IsTrusted(c, "local", "", new Uri(Path.Combine(Path.GetDirectoryName(root)!, "outside.html")).AbsoluteUri));
    }
    [Fact]
    public void DirectoryBoundaryRejectsSiblingPrefixAndTraversal()
    {
        Assert.True(WebsitePolicy.IsWithin(Path.Combine(root, "nested", "a.txt"), root));
        Assert.False(WebsitePolicy.IsWithin(root + "-other/a.txt", root));
        Assert.False(WebsitePolicy.IsWithin(Path.Combine(root, "..", "outside.txt"), root));
        Assert.True(WebsitePolicy.IsWithin(Path.Combine(Path.GetPathRoot(root)!, "a.txt"), Path.GetPathRoot(root)!));
    }
    [Fact]
    public void JunctionIsRejected()
    {
        var outside = Path.Combine(root, "outside");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(outside);
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            Arguments = "/c mklink /J \"" + link + "\" \"" + outside + "\"",
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        Assert.False(WebsitePolicy.HasNoReparsePoints(Path.Combine(link, "future.txt")));
        Directory.Delete(outside);
        Assert.False(WebsitePolicy.HasNoReparsePoints(Path.Combine(link, "future.txt")));
        // Delete only the verified test-owned junction, without recursing through its target.
        Directory.Delete(link);
    }
    [Fact]
    public void DuplicateMappingIsCaseInsensitive()
    {
        var draft = Online();
        draft.Configuration.OpenMode = "Embedded";
        draft.Configuration.Runtime.Mappings.Add(new() { Host = "assets.local", Directory = root });
        draft.Configuration.Runtime.Mappings.Add(new() { Host = "ASSETS.local", Directory = root });
        Assert.NotEmpty(WebsitePolicy.Validate(draft));
    }
    [Fact]
    public void BuilderGatesFramesAndRegistersLifecycleStages()
    {
        var c = new WebsiteConfiguration();
        c.Runtime.Scripts.Add(new() { Name = "save", Code = "await Mio.storage.set('a', 1);", Stage = "Closing" });
        var script = WebsiteScriptBuilder.Build(c, "a", "https://example.test");
        Assert.Contains("window.top !== window.self", script);
        Assert.Contains("https://example.test", script);
        Assert.Contains("mio-lifecycle-close", script);
        Assert.Contains("DOMContentLoaded", script);
        Assert.Contains("WebsiteCapabilities", script);
    }
}

