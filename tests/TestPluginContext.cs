using MioKit.Sdk;
using Serilog;
using WebSearch;

namespace MioKit.WebSearch.Tests;

internal sealed class TestPluginContext(string directory) : IPluginContext
{
    public IPluginMetadata Metadata => null!;
    public ILogger Logger { get; } = new LoggerConfiguration().CreateLogger();
    public PluginState RuntimeState => PluginState.Running;
    public string PluginPath => directory;
    public string PluginDataPath => directory;
    public IPluginIconService Icons => null!;

    public static WebSearchPlugin CreatePlugin(string directory)
    {
        var plugin = new WebSearchPlugin();
        typeof(PluginBase).GetProperty(nameof(PluginBase.Context))!.SetValue(plugin, new TestPluginContext(directory));
        return plugin;
    }
}
