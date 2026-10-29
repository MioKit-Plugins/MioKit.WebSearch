using System.Text.Json;
using WebSearch.Models;
using WebSearch.Services;

namespace WebSearch.Tests;

public sealed class WebsiteConfigurationChangesTests
{
    [Fact]
    public void RehydratedUnchangedConfigurationDoesNotRequireReopen()
    {
        var previous = new WebsiteConfiguration
        {
            OpenMode = "Embedded",
            Runtime = new() { Permissions = ["storage"], Scripts = [new() { Code = "console.log('ready')" }] }
        };
        var next = JsonSerializer.Deserialize<WebsiteConfiguration>(JsonSerializer.Serialize(previous))!;
        Assert.False(WebsiteConfigurationChanges.RequiresReopen(previous, "https://example.test/", next, " https://example.test/ "));
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("trusted-origin")]
    [InlineData("script")]
    [InlineData("mapping")]
    [InlineData("data-directory")]
    [InlineData("browser-permission")]
    [InlineData("file-grant")]
    [InlineData("navigation")]
    [InlineData("source")]
    [InlineData("local-file")]
    [InlineData("debug")]
    [InlineData("open-mode")]
    [InlineData("url")]
    public void RuntimeChangesRequireReopen(string change)
    {
        var previous = new WebsiteConfiguration { OpenMode = "Embedded" };
        var next = new WebsiteConfiguration { OpenMode = "Embedded" };
        var url = "https://example.test/";
        switch (change)
        {
            case "permission": next.Runtime.Permissions.Add("storage"); break;
            case "trusted-origin": next.Runtime.TrustedOrigins.Add("https://other.test"); break;
            case "script": next.Runtime.Scripts.Add(new() { Code = "console.log('changed')" }); break;
            case "mapping": next.Runtime.Mappings.Add(new() { Host = "app.local", Directory = @"D:\app" }); break;
            case "data-directory": next.UserDataFolder = @"D:\website-data"; break;
            case "browser-permission": next.Runtime.BrowserPermissions.Add("Camera"); break;
            case "file-grant": next.Runtime.FileDirectories.Add(new() { Directory = @"D:\files", Write = true }); break;
            case "navigation": next.Runtime.CurrentNavigation = "ExternalUntrusted"; break;
            case "source": next.SourceKind = "LocalFile"; break;
            case "local-file": next.LocalFilePath = @"D:\app\index.html"; break;
            case "debug": next.DebugEnabled = true; break;
            case "open-mode": next.OpenMode = "External"; break;
            case "url": url = "https://other.test/"; break;
        }
        Assert.True(WebsiteConfigurationChanges.RequiresReopen(previous, "https://example.test/", next, url));
    }
}
