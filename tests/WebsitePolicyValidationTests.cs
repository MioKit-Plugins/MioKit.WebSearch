using System.Text.Json;
using WebSearch.Models;
using WebSearch.Services;

namespace MioKit.WebSearch.Tests;

public sealed partial class WebsitePolicyTests
{
    [Theory]
    [InlineData("directory")]
    [InlineData("host")]
    [InlineData("mapping")]
    [InlineData("file-grant")]
    [InlineData("permissions")]
    [InlineData("browser-permissions")]
    [InlineData("navigation")]
    [InlineData("scripts")]
    [InlineData("trusted-origins")]
    public void DormantEmbeddedOptionsDoNotBlockExternalSavingButAreCheckedOnSwitch(string option)
    {
        var request = Online();
        var c = request.Configuration;
        switch (option)
        {
            case "directory": c.UserDataFolder = "relative/profile"; break;
            case "host": c.LocalHostName = "invalid host"; break;
            case "mapping": c.Runtime.Mappings.Add(new() { Host = "assets.local", Directory = Path.Combine(root, "missing") }); break;
            case "file-grant": c.Runtime.FileDirectories.Add(new() { Directory = Path.Combine(root, "missing") }); break;
            case "permissions": c.Runtime.Permissions.Add("unknown"); break;
            case "browser-permissions": c.Runtime.BrowserPermissions.Add("unknown"); break;
            case "navigation": c.Runtime.CurrentNavigation = "unknown"; break;
            case "scripts": c.Runtime.Scripts.Add(new() { Stage = "unknown" }); break;
            case "trusted-origins": c.Runtime.TrustedOrigins.Add("https://example.test/path"); break;
        }
        var original = JsonSerializer.Serialize(c);
        Assert.Empty(WebsitePolicy.Validate(request));
        Assert.Equal(original, JsonSerializer.Serialize(c));
        c.OpenMode = "Embedded";
        Assert.NotEmpty(WebsitePolicy.Validate(request));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("configuration")]
    [InlineData("runtime")]
    [InlineData("version")]
    [InlineData("runtime-version")]
    [InlineData("source-kind")]
    [InlineData("open-mode")]
    [InlineData("query-policy")]
    [InlineData("data-directory")]
    [InlineData("current-navigation")]
    [InlineData("new-window")]
    [InlineData("permissions")]
    [InlineData("browser-permissions")]
    [InlineData("trusted-origins")]
    [InlineData("mappings")]
    [InlineData("scripts")]
    [InlineData("file-directories")]
    [InlineData("unknown-permission")]
    [InlineData("unknown-browser-permission")]
    [InlineData("invalid-stage")]
    [InlineData("null-mapping")]
    [InlineData("null-script")]
    [InlineData("null-grant")]
    [InlineData("null-mapping-host")]
    [InlineData("mapping-access")]
    [InlineData("mapping-missing-directory")]
    [InlineData("grant-missing-directory")]
    [InlineData("mapping-collides-with-entry")]
    public void MalformedEditorInputReturnsValidationError(string field)
    {
        var request = Online();
        request.Configuration.OpenMode = "Embedded";
        var config = request.Configuration;
        var runtime = config.Runtime;
        switch (field)
        {
            case "name": request.Name = " \t"; break;
            case "configuration": request.Configuration = null!; break;
            case "runtime": config.Runtime = null!; break;
            case "version": config.Version = 2; break;
            case "runtime-version": runtime.Version = 2; break;
            case "source-kind": config.SourceKind = "Unknown"; break;
            case "open-mode": config.OpenMode = "Unknown"; break;
            case "query-policy": request.QuerySourcePolicy = "999"; break;
            case "data-directory": config.UserDataFolder = "relative/browser"; break;
            case "current-navigation": runtime.CurrentNavigation = "Unknown"; break;
            case "new-window": runtime.NewWindowMode = "Unknown"; break;
            case "permissions": runtime.Permissions = null!; break;
            case "browser-permissions": runtime.BrowserPermissions = null!; break;
            case "trusted-origins": runtime.TrustedOrigins = null!; break;
            case "mappings": runtime.Mappings = null!; break;
            case "scripts": runtime.Scripts = null!; break;
            case "file-directories": runtime.FileDirectories = null!; break;
            case "unknown-permission": runtime.Permissions.Add("files.execute"); break;
            case "unknown-browser-permission": runtime.BrowserPermissions.Add("Unknown"); break;
            case "invalid-stage": runtime.Scripts.Add(new() { Stage = "Unknown" }); break;
            case "null-mapping": runtime.Mappings.Add(null!); break;
            case "null-script": runtime.Scripts.Add(null!); break;
            case "null-grant": runtime.FileDirectories.Add(null!); break;
            case "null-mapping-host": runtime.Mappings.Add(new() { Host = null!, Directory = root }); break;
            case "mapping-access": runtime.Mappings.Add(new() { Host = "assets.local", Directory = root, Access = "Unknown" }); break;
            case "mapping-missing-directory": runtime.Mappings.Add(new() { Host = "assets.local", Directory = Path.Combine(root, "missing") }); break;
            case "grant-missing-directory": runtime.FileDirectories.Add(new() { Directory = Path.Combine(root, "missing") }); break;
            case "mapping-collides-with-entry": runtime.Mappings.Add(new() { Host = WebsitePolicy.DefaultHost(request.Id), Directory = root }); break;
        }
        // Validation is called on untrusted bridge input and must not throw for null list items.
        Assert.NotEmpty(WebsitePolicy.Validate(request));
    }

    [Theory]
    [InlineData("DenyCors")]
    [InlineData("Deny")]
    [InlineData("Allow")]
    public void CompleteConfigurationRoundTripsAndRemovesOnlyObsoletePermissions(string access)
    {
        var request = Online();
        request.Configuration.OpenMode = "Embedded";
        request.Configuration.DebugEnabled = true;
        request.Configuration.Runtime = new()
        {
            CurrentNavigation = "ExternalUntrusted", NewWindowMode = "Current", AllowAutomaticPopups = true,
            Permissions = [.. WebsitePolicy.Permissions, "dialogs", "external"],
            BrowserPermissions = [.. WebsitePolicy.BrowserPermissions],
            TrustedOrigins = ["https://other.test", "http://localhost:3000"],
            Mappings = [new() { Host = "assets.local", Directory = root, Access = access }],
            FileDirectories = [new() { Directory = root, Read = true, Write = true }],
            Scripts = WebsitePolicy.Stages.Select(stage => new WebsiteScript { Stage = stage, Code = "console.log('中文')" }).ToList()
        };
        request = JsonSerializer.Deserialize<WebsiteEditorRequest>(JsonSerializer.Serialize(request))!;
        Assert.Empty(WebsitePolicy.Validate(request));
        Assert.Equal(WebsitePolicy.Permissions, request.Configuration.Runtime.Permissions);
        Assert.Equal(4, request.Configuration.Runtime.Scripts.Count);
        Assert.Equal(access, Assert.Single(request.Configuration.Runtime.Mappings).Access);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("assets.local-")]
    [InlineData("assets..local")]
    [InlineData("assets.local.")]
    [InlineData("a.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void RejectsInvalidDnsLabelsWithoutThrowing(string? host) => Assert.False(WebsitePolicy.IsValidHost(host!));

    [Theory]
    [InlineData("a.local")]
    [InlineData("my-assets.local")]
    [InlineData("ASSETS.LOCAL")]
    [InlineData("a.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void AcceptsValidDnsLabels(string host) => Assert.True(WebsitePolicy.IsValidHost(host));

    [Theory]
    [InlineData(".html", true)]
    [InlineData(".HTM", true)]
    [InlineData(".txt", false)]
    [InlineData(".html.exe", false)]
    public void LocalSourceValidationRequiresExistingHtml(string extension, bool valid)
    {
        var request = Online();
        request.Configuration.SourceKind = "LocalFile";
        request.Configuration.LocalFilePath = Path.Combine(root, "entry" + extension);
        Assert.NotEmpty(WebsitePolicy.Validate(request));
        File.WriteAllText(request.Configuration.LocalFilePath, "<html></html>");
        Assert.Equal(valid, WebsitePolicy.Validate(request).Length == 0);
    }
}
