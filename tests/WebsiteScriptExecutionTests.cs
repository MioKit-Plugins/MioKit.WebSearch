using System.Diagnostics;
using System.Text.Json;
using WebSearch.Models;
using WebSearch.Services;

namespace MioKit.WebSearch.Tests;

public sealed class WebsiteScriptExecutionTests
{
    [Theory]
    [InlineData("lifecycle")]
    [InlineData("already-interactive")]
    [InlineData("already-complete")]
    [InlineData("script-failure")]
    [InlineData("disabled-script")]
    [InlineData("close-failure")]
    [InlineData("iframe")]
    [InlineData("untrusted")]
    [InlineData("extra-origin")]
    [InlineData("about-blank")]
    [InlineData("inherited-blank")]
    [InlineData("inherited-blank-api-contract")]
    [InlineData("inherited-blank-untrusted")]
    [InlineData("inherited-blank-redirect")]
    [InlineData("local-file")]
    [InlineData("local-sibling")]
    [InlineData("api-contract")]
    [InlineData("api-error")]
    [InlineData("template-tokens")]
    public async Task GeneratedScriptExecutesAgainstTheRealEmbeddedBridge(string scenario)
    {
        var config = new WebsiteConfiguration();
        config.Runtime.TrustedOrigins.Add("https://extra.test:8443");
        foreach (var stage in WebsitePolicy.Stages)
            config.Runtime.Scripts.Add(new()
            {
                Stage = stage, Name = stage,
                Code = $"window.trace.push('{stage}:start'); await Mio.storage.set('{stage}', true); window.trace.push('{stage}:end');"
            });
        if (scenario is "local-file" or "local-sibling")
        {
            config.SourceKind = "LocalFile";
            config.MapLocalDirectory = false;
            config.LocalFilePath = Path.Combine(Path.GetTempPath(), "websearch-script-root", "中文 #.html");
        }
        if (scenario is "script-failure" or "close-failure")
            config.Runtime.Scripts.Insert(0, new()
            {
                Stage = scenario == "close-failure" ? "Closing" : "DocumentCreated",
                Name = "failing 中文", Code = "throw new Error('expected failure')"
            });
        if (scenario == "disabled-script")
            config.Runtime.Scripts.Insert(0, new() { Enabled = false, Stage = "DocumentCreated", Code = "throw new Error('disabled script ran')" });
        if (scenario == "template-tokens")
            config.Runtime.Scripts.Add(new()
            {
                Stage = "DocumentCreated", Name = "BRIDGE ORIGINS",
                Code = "window.tokens = 'ORIGINS LOCALROOT SCRIPTS BRIDGE </script> 中文';"
            });

        var start = new ProcessStartInfo("node")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "JavaScript", "website-script.cjs"));
        using var process = Process.Start(start)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new
            {
                scenario, script = WebsiteScriptBuilder.Build(config, "script-test", "https://entry.test/"),
                entry = WebsitePolicy.Entry(config, "script-test", "https://entry.test/")
            }));
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, await output + await errors);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
