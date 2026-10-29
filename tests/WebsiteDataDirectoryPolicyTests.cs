using WebSearch.Services;
using WebSearch.Models;

namespace MioKit.WebSearch.Tests;

public sealed partial class WebsitePolicyTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData(null)]
    public void UnsetDataDirectorySurvivesModeSwitchesInDebuggerLayout(string? value)
    {
        // Miokit.Debugger --path <plugin output> puts app_data inside PluginPath.
        var pluginPath = Path.Combine(root, "plugin", "bin", "Debug", "net10.0-windows10.0.19041.0");
        var host = Path.Combine(pluginPath, "app_data");
        var data = Path.Combine(host, "plugin_data", "miokit.websearch", "data");
        var request = Online();
        request.Configuration.UserDataFolder = value!;
        Assert.Empty(WebsitePolicy.Validate(request));
        var expected = WebsitePolicy.DataDirectory(request.Configuration, request.Id, data);
        for (var iteration = 0; iteration < 3; iteration++)
        {
            request.Configuration.OpenMode = "Embedded";
            Assert.Empty(WebsitePolicy.Validate(request));
            var path = WebsitePolicy.DataDirectory(request.Configuration, request.Id, data);
            WebsiteDataDirectoryPolicy.ValidateCustomLocation(request.Configuration.UserDataFolder,
                pluginPath, data, host, Path.Combine(host, "jsruntime"));
            Assert.Equal(expected, path);
            WebsiteDataDirectoryPolicy.ValidateIndependent(path,
                WebsitePolicy.DataDirectory(new(), "other-site", data));
            request.Configuration.OpenMode = "External";
            Assert.Empty(WebsitePolicy.Validate(request));
        }
        Assert.Equal(value, request.Configuration.UserDataFolder);
        Assert.False(Directory.Exists(expected));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Assets", false)]
    [InlineData("app_data/webview2/profile", false)]
    [InlineData("app_data/jsruntime/profile", false)]
    [InlineData("../custom-browser", true)]
    public void ExplicitDataDirectoryStillValidatesReservedLocationsInDebuggerLayout(string relative, bool valid)
    {
        var pluginPath = Path.Combine(root, "plugin-output");
        var host = Path.Combine(pluginPath, "app_data");
        var data = Path.Combine(host, "plugin_data", "miokit.websearch", "data");
        var config = new WebsiteConfiguration
        {
            OpenMode = "Embedded", UserDataFolder = Path.GetFullPath(Path.Combine(pluginPath, relative))
        };
        void Validate() => WebsiteDataDirectoryPolicy.ValidateCustomLocation(config.UserDataFolder,
            pluginPath, data, host, Path.Combine(host, "jsruntime"));
        if (valid) Validate();
        else Assert.Throws<InvalidOperationException>(Validate);
        config.UserDataFolder = "";
        Validate();
        Assert.True(WebsitePolicy.IsWithin(WebsitePolicy.DataDirectory(config, "site", data), data));
    }

    [Theory]
    [InlineData("host", true)]
    [InlineData("host/plugin_data/websearch/data", true)]
    [InlineData("host/plugin_data/websearch/data/websites", false)]
    [InlineData("custom", false)]
    public void CustomBrowserDirectoryTrustsHostDataAncestorsButRejectsLinksBelowOrOutside(string linkedLocation, bool valid)
    {
        var host = Path.Combine(root, "host");
        var data = Path.Combine(host, "plugin_data", "websearch", "data");
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, linkedLocation);
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            Arguments = "/c mklink /J \"" + link + "\" \"" + target + "\"",
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        try
        {
            var config = new WebsiteConfiguration { OpenMode = "External" };
            Assert.Empty(WebsitePolicy.Validate(new() { Name = "Site", UrlTemplate = "https://example.test", Configuration = config }));
            config.OpenMode = "Embedded";
            if (linkedLocation == "custom") config.UserDataFolder = Path.Combine(link, "browser");
            var path = WebsitePolicy.DataDirectory(config, "site", data);
            void Validate() => WebsiteDataDirectoryPolicy.ValidateCustomLocation(path, Path.Combine(root, "plugins", "websearch"),
                data, host, Path.Combine(host, "jsruntime"));
            Assert.False(WebsitePolicy.HasNoReparsePoints(path));
            if (valid) Validate();
            else Assert.Throws<InvalidOperationException>(Validate);
            Assert.False(Directory.Exists(path));
        }
        finally { Directory.Delete(link); } // Remove only the test-owned junction, without traversing its target.
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData(null)]
    public void UnsetDataDirectoryUsesAnIndependentDefaultInTheRealHostLayout(string? value)
    {
        var host = Path.Combine(root, "host");
        var data = Path.Combine(host, "plugin_data", "websearch", "data");
        var config = new WebsiteConfiguration { OpenMode = "Embedded", UserDataFolder = value! };
        var path = WebsitePolicy.DataDirectory(config, "site", data);
        WebsiteDataDirectoryPolicy.ValidateCustomLocation(config.UserDataFolder, Path.Combine(host, "plugins", "websearch"), data,
            host, Path.Combine(host, "jsruntime"));
        Assert.True(WebsitePolicy.IsWithin(path, data));
        Assert.NotEqual(path, WebsitePolicy.DataDirectory(config, "another-site", data));
        Assert.False(Directory.Exists(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/profile")]
    [InlineData("invalid\0path")]
    public async Task ExternalDataDirectoryValidationDoesNotAccessHostOrPaths(string path)
    {
        var service = new WebsiteWindowService(null!, null!, null!);
        await service.ValidateDataDirectoryAsync("site", new() { UserDataFolder = path });
    }

    [Theory]
    [InlineData("host")]
    [InlineData("host/..")]
    [InlineData("host/plugins/websearch")]
    [InlineData("host/plugins/websearch/nested")]
    [InlineData("host/data/websearch")]
    [InlineData("host/data/websearch/")]
    [InlineData("HOST/DATA/WEBSEARCH")]
    [InlineData("host/webview2")]
    [InlineData("host/webview2/profile")]
    [InlineData("host/jsruntime")]
    [InlineData("host/jsruntime/profile")]
    public void BrowserDirectoryCannotUseReservedHostLocations(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        Assert.Throws<InvalidOperationException>(() => ValidateBrowserDirectory(path));
    }

    [Theory]
    [InlineData("host/data/websearch/websites/site/browser")]
    [InlineData("custom-browser")]
    [InlineData("host/webview2-other")]
    public void BrowserDirectoryCanUseNodeDefaultOrIndependentCustomLocations(string relative) =>
        ValidateBrowserDirectory(Path.GetFullPath(Path.Combine(root, relative)));

    [Fact]
    public void BrowserDirectoryCannotUseAnExistingFile()
    {
        var path = Path.Combine(root, "file.txt"); File.WriteAllText(path, "existing");
        Assert.Throws<InvalidOperationException>(() => ValidateBrowserDirectory(path));
        Assert.Equal("existing", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("browser", "browser", false)]
    [InlineData("browser", "BROWSER/", false)]
    [InlineData("browser", "browser/nested", false)]
    [InlineData("browser/nested", "browser", false)]
    [InlineData("browser", "browser-other", true)]
    [InlineData("browser/a", "browser/b", true)]
    public void BrowserDirectoriesAreComparedInBothDirectionsWithPathBoundaries(string first, string second, bool valid)
    {
        var path = Path.Combine(root, first); var other = Path.Combine(root, second);
        if (valid) WebsiteDataDirectoryPolicy.ValidateIndependent(path, other);
        else Assert.Throws<InvalidOperationException>(() => WebsiteDataDirectoryPolicy.ValidateIndependent(path, other));
    }

    private void ValidateBrowserDirectory(string path) => WebsiteDataDirectoryPolicy.ValidateCustomLocation(path,
        Path.Combine(root, "host", "plugins", "websearch"), Path.Combine(root, "host", "data", "websearch"),
        Path.Combine(root, "host"), Path.Combine(root, "host", "jsruntime"));
}
