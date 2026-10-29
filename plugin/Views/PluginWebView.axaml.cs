using Ti.Avalonia.Shadcn.Controls;

namespace WebSearch.Views;

public partial class PluginWebView : ShadWindow
{
    private bool isInitialized;
    private bool closed;
    private Task? initializationTask;
    private WebSearchPlugin? plugin;
    private ServiceProvider? bridgeProvider;

    public PluginWebView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (isInitialized || closed)
        {
            return;
        }

        isInitialized = true;
        initializationTask = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var componentContext = WebSearchRegister.ComponentContextInstance;
            plugin = componentContext.Resolve<WebSearchPlugin>();
            var bridgeService = componentContext.Resolve<WebSearchJsService>();
            bridgeService.Initialize();
            var services = new ServiceCollection();
            services.AddSingleton(bridgeService);
            bridgeProvider = services.BuildServiceProvider();

            await WebView.EnsureCoreWebView2Async();
            if (closed) return;
            await WebView.InitializeBridgeAsync(services, bridgeProvider);
            if (closed) return;

#if DEBUG
            WebView.Source = new Uri("http://localhost:9000/");
            return;
#endif

            var indexHtml = Path.Combine(plugin.Context.PluginPath, "ui", "dist", "index.html");
            WebView.Source = new Uri(indexHtml);
        }
        catch (OperationCanceledException) when (closed) { }
        catch (Exception ex)
        {
            ReportFailure(ex, "Failed to initialize web search settings window.");
            if (!closed)
            {
                WebView.IsVisible = false;
                InitializationError.Text = "设置页加载失败，请关闭后重新打开。\n" + ex.Message;
                InitializationError.IsVisible = true;
            }
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        closed = true;
        Loaded -= OnLoaded;
        try { await WebView.StopAsync(); }
        catch (Exception ex) { ReportFailure(ex, "Failed to stop web search settings WebView."); }
        finally
        {
            try
            {
                if (initializationTask is not null) await initializationTask;
            }
            catch (Exception ex) { ReportFailure(ex, "Settings initialization ended during shutdown."); }
            finally
            {
                var provider = bridgeProvider;
                bridgeProvider = null;
                try { provider?.Dispose(); }
                catch (Exception ex) { ReportFailure(ex, "Failed to dispose settings bridge provider."); }
            }
        }
    }

    private void ReportFailure(Exception exception, string message)
    {
        // Error reporting must not escape a native window event, even during host shutdown.
        try { (plugin?.Context?.Logger ?? Serilog.Log.Logger).Warning(exception, message); }
        catch { }
    }
}
