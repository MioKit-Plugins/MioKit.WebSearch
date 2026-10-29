using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;
using Ti.Avalonia.Shadcn.Controls;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using MioKit.Extensions.Extensions;

namespace WebSearch.Views;

public partial class WebsiteWindow : ShadWindow
{
    private WebsiteWindowService service = null!;
    private WebsiteNode node = null!;
    private WebsiteConfiguration config = null!;
    private string onlineUrl = "";
    private string currentAddress = "";
    private bool closed;
    private bool forceClose;
    private bool closeInProgress;
    private long epoch;
    private bool capabilitiesRevoked;
    private CancellationTokenSource? iconCancellation;
    private ServiceBridge? bridge;
    private ServiceProvider? provider;
    private WebsiteBridgeTransport? transport;
    private string? blockedPopupUrl;
    private readonly WebsiteFileGrantSession fileGrants = new();
    public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Stopped => stopped.Task;
    public string NodeId => node.Id;
    internal string DataDirectory => Browser.DataDirectory;
    public bool RequiresReopen => capabilitiesRevoked;
    public CoreWebView2? Core => Browser.CoreWebView2;
    public WebsiteWindow()
    {
        InitializeComponent();
        Loaded += LoadedWindow;
        Closing += ClosingWindow;
        Opened += (_, _) => RunWindowAction(() =>
        {
            this.SetAppUserModelId(WebSearchConst.PluginId);
            _ = RefreshIconAsync();
        }, "初始化窗口失败");
        Closed += ClosedWindow;
    }
    public void Configure(WebsiteWindowService owner, WebsiteNode website, WebsiteConfiguration configuration,
        string url, CoreWebView2Environment? environment, string dataDirectory)
    {
        service = owner; node = website; config = configuration; onlineUrl = url;
        WindowManager.SetOwnerId(this, WebSearchConst.PluginId);
        node.PropertyChanged += OnNodeIconChanged;
        Title = website.Name ?? "网页";
        Browser.Configuration = config;
        Browser.WebsiteHost = WebsitePolicy.Host(config, node.Id);
        Browser.DataDirectory = dataDirectory;
        Browser.SharedEnvironment = environment;
        Browser.PopupRequested = OpenPopupAsync;
        Browser.ErrorReported = ex => ReportFailure(ex, "打开弹窗失败");
        service.Register(this);
    }
    private void OnNodeIconChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WebsiteNode.Name))
            Dispatcher.UIThread.Post(() => RunWindowAction(() => { if (!closed) RefreshTitle(); }, "更新窗口标题失败"));
        if (e.PropertyName is "IconRevision" or "CustomIconId" or "SourceKind" or "UrlTemplate")
            Dispatcher.UIThread.Post(() => { if (!closed && IsVisible) _ = RefreshIconAsync(); });
    }
    public async Task RefreshIconAsync()
    {
        CancellationToken ct = default;
        try
        {
            if (closed || !IsVisible) return;
            DisposeIconCancellation();
            iconCancellation = new CancellationTokenSource();
            ct = iconCancellation.Token;
            // One ordered writer: plugin fallback, then the website's effective icon.
            await service.ApplyPluginIconAsync(this, ct);
            using var lease = await Task.Run(async () => await service.OpenIconAsync(node, ct), ct);
            if (!closed && !ct.IsCancellationRequested && lease?.Image is Bitmap bitmap)
                Icon = new WindowIcon(bitmap);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { ReportFailure(ex, "更新窗口图标失败", showToUser: false); }
    }
    private async void LoadedWindow(object? sender, RoutedEventArgs e)
    {
        Loaded -= LoadedWindow;
        try
        {
            await Browser.EnsureCoreWebView2Async();
            if (closed || Core is not { } core) return;
            core.Settings.AreDevToolsEnabled = config.DebugEnabled;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            if (config.SourceKind == "LocalFile" && config.MapLocalDirectory)
                core.SetVirtualHostNameToFolderMapping(Browser.AppName, Path.GetDirectoryName(config.LocalFilePath)!, CoreWebView2HostResourceAccessKind.DenyCors);
            foreach (var m in config.Runtime.Mappings.Where(m => m.Enabled))
                core.SetVirtualHostNameToFolderMapping(m.Host, m.Directory, Enum.Parse<CoreWebView2HostResourceAccessKind>(m.Access));
            core.NavigationStarting += NavigationStarting;
            core.ContentLoading += ContentLoading;
            core.SourceChanged += SourceChanged;
            core.NavigationCompleted += NavigationCompleted;
            core.PermissionRequested += PermissionRequested;
            core.WindowCloseRequested += (_, _) => RunWindowAction(Close, "关闭窗口失败");
            core.DownloadStarting += (_, args) => RunWindowAction(() =>
            { args.Cancel = true; SetStatus("请使用已授权的文件功能保存，或在外部浏览器下载。", showToUser: true); }, "处理下载请求失败");
            await core.AddScriptToExecuteOnDocumentCreatedAsync(WebsiteScriptBuilder.Build(config, node.Id, onlineUrl));
            Ready.TrySetResult();
            SetStatus("就绪");
        }
        catch (Exception ex)
        {
            Ready.TrySetException(ex);
            ReportFailure(ex, "初始化失败，可关闭后重新打开");
        }
    }
    public async Task NavigateAsync(string url)
    {
        await Ready.Task;
        if (!closed) Core?.Navigate(url);
    }
    private bool Trusted(string address) => WebsitePolicy.IsTrusted(config, node.Id, onlineUrl, address);
    private void NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        try
        {
            if (closed) { e.Cancel = true; return; }
            if (!WebsiteNavigationPolicy.Allows(config, e.Uri)) { e.Cancel = true; SetStatus("已阻止不支持的地址。", showToUser: true); return; }
            if (WebsiteNavigationPolicy.OpensExternally(config, node.Id, onlineUrl, e.Uri))
            {
                e.Cancel = true;
                RunWindowAction(() => service.OpenExternal(e.Uri), "打开外部浏览器失败");
                return;
            }
            epoch++;
            DisposeDocument();
            blockedPopupUrl = null;
            SetPopupVisible(false);
            SetStatus("正在加载…");
        }
        catch (Exception ex)
        {
            epoch++;
            DisposeDocument();
            RunWindowAction(() => e.Cancel = true, "取消导航失败");
            ReportFailure(ex, "导航失败");
        }
    }
    private async void ContentLoading(object? sender, CoreWebView2ContentLoadingEventArgs e)
    {
        try
        {
            DisposeDocument();
            if (closed || Core is not { } core || !Trusted(core.Source)) return;
            currentAddress = core.Source;
            var documentEpoch = epoch;
            bool Active() => !closed && documentEpoch == epoch && !capabilitiesRevoked;
            var context = new WebsitePageContext(node, config, onlineUrl, () => currentAddress, Active, this, fileGrants);
            var services = new ServiceCollection();
            services.AddSingleton(service.CreateCapabilities(context));
            provider = services.BuildServiceProvider();
            transport = new(core, Active, Trusted, Log, Dispatcher);
            await transport.InitializeAsync();
            if (!Active()) { DisposeDocument(); return; }
            bridge = new ServiceBridge(transport, provider, services);
        }
        catch (Exception ex)
        {
            DisposeDocument();
            ReportFailure(ex, "初始化网页增强功能失败");
        }
    }
    private void SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        RunWindowAction(() =>
        {
            if (closed) return;
            if (Core is not { } core) return;
            currentAddress = core.Source;
        }, "更新导航状态失败");
    }
    private void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        RunWindowAction(() =>
        {
            if (closed) return;
            RefreshTitle();
            SetStatus(e.IsSuccess ? (Trusted(currentAddress) ? "已授权页面 · 增强功能可用" : "普通浏览 · 未授权增强功能") : "加载失败，可按 Ctrl+R 刷新重试。", showToUser: !e.IsSuccess);
        }, "更新页面状态失败");
    }
    private void RefreshTitle()
    {
        var documentTitle = Core?.DocumentTitle;
        Title = (node.Name ?? "网页") + (string.IsNullOrWhiteSpace(documentTitle) ? "" : " · " + documentTitle);
    }
    private void PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        RunWindowAction(() =>
        {
            // Default to denial if reading the current authorization fails.
            e.State = CoreWebView2PermissionState.Deny;
            e.SavesInProfile = false;
            e.Handled = true;
            var allow = !closed && !capabilitiesRevoked && Trusted(e.Uri) && (node.GetRuntimeOptions()?.BrowserPermissions ?? []).Contains(e.PermissionKind.ToString());
            if (allow) e.State = CoreWebView2PermissionState.Allow;
        }, "处理网页权限失败");
    }
    private async Task OpenPopupAsync(CoreWebView2NewWindowRequestedEventArgs e)
    {
        var runtime = node.GetRuntimeOptions() ?? config.Runtime;
        switch (WebsiteNavigationPolicy.Popup(config, runtime, e.Uri, e.IsUserInitiated, capabilitiesRevoked))
        {
            case WebsitePopupDecision.Revoked: SetStatus("设置已改变，请重新打开后再使用网页弹窗。", showToUser: true); return;
            case WebsitePopupDecision.Unsupported: SetStatus("已阻止不支持的弹窗地址。", showToUser: true); return;
            case WebsitePopupDecision.AutomaticBlocked:
                blockedPopupUrl = e.Uri;
                SetPopupVisible(true);
                return;
            case WebsitePopupDecision.Block: SetStatus("已按网站设置阻止新窗口。", showToUser: e.IsUserInitiated); return;
            case WebsitePopupDecision.Current: Core?.Navigate(e.Uri); return;
            case WebsitePopupDecision.External: service.OpenExternal(e.Uri); return;
        }
        if (Core is not { } opener) return;
        var popup = service.CreatePopup(node, config, onlineUrl, opener.Environment,
            e.WindowFeatures.HasSize ? (e.WindowFeatures.Width, e.WindowFeatures.Height) : null);
        try
        {
            await popup.Ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
            if (closed || capabilitiesRevoked || popup.closed || popup.capabilitiesRevoked || popup.Core is null) { popup.ForceClose(); return; }
            e.NewWindow = popup.Core;
        }
        catch { popup.ForceClose(); throw; }
    }
    private void OnAllowPopup(object? sender, RoutedEventArgs e) => _ = AllowPopupAsync();
    private async Task AllowPopupAsync()
    {
        WebsiteWindow? popup = null;
        try
        {
            if (closed || capabilitiesRevoked) return;
            // The original window.open has already been blocked; retrying a URL cannot restore its opener.
            SetPopupVisible(false);
            if (blockedPopupUrl is { } url && Core is { } core)
            {
                popup = service.CreatePopup(node, config, onlineUrl, core.Environment, null);
                await popup.NavigateAsync(url).WaitAsync(TimeSpan.FromSeconds(20));
                if (closed || capabilitiesRevoked) { popup.ForceClose(); return; }
                SetStatus("已打开被拦截的地址；若为登录弹窗，请在原网页重新点击登录。");
            }
        }
        catch (Exception ex)
        {
            popup?.ForceClose();
            ReportFailure(ex, "打开弹窗失败");
        }
    }
    private void OnAlwaysAllowPopup(object? sender, RoutedEventArgs e)
    {
        RunWindowAction(() =>
        {
            if (closed || capabilitiesRevoked) return;
            var runtime = node.GetRuntimeOptions() ?? new();
            // Replace the value to notify EAV listeners instead of mutating the cached instance.
            runtime = System.Text.Json.JsonSerializer.Deserialize<WebsiteRuntimeOptions>(System.Text.Json.JsonSerializer.Serialize(runtime))!;
            runtime.AllowAutomaticPopups = true;
            node.SetRuntimeOptions(runtime);
            OnAllowPopup(sender, e);
        }, "更新弹窗设置失败");
    }
    private async void ClosingWindow(object? sender, WindowClosingEventArgs e)
    {
        if (forceClose) return;
        e.Cancel = true;
        if (closeInProgress) return;
        closeInProgress = true;
        try { if (transport is not null) await transport.RunClosingAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception ex) { ReportFailure(ex, "执行关闭脚本失败"); }
        finally { ForceClose(); }
    }
    public void ForceClose() { forceClose = true; RunWindowAction(Close, "关闭窗口失败"); }

    private async void ClosedWindow(object? sender, EventArgs e)
    {
        closed = true; epoch++;
        RunWindowAction(() => NoticePopup.IsOpen = false, "关闭网页提示失败");
        Loaded -= LoadedWindow;
        DisposeIconCancellation();
        RunWindowAction(() => { if (node is not null) node.PropertyChanged -= OnNodeIconChanged; }, "解除窗口订阅失败");
        Ready.TrySetCanceled();
        DisposeDocument();
        RunWindowAction(() => service?.Unregister(this), "清理窗口登记失败");
        try { await Browser.StopAsync(); }
        catch (Exception ex) { ReportFailure(ex, "停止网页窗口失败"); }
        finally { stopped.TrySetResult(); }
    }

    private void DisposeIconCancellation()
    {
        var cancellation = iconCancellation;
        iconCancellation = null;
        if (cancellation is null) return;
        RunWindowAction(cancellation.Cancel, "取消窗口图标加载失败");
        RunWindowAction(cancellation.Dispose, "清理窗口图标任务失败");
    }
    public void RevokeCapabilities()
    {
        capabilitiesRevoked = true;
        fileGrants.Clear();
        blockedPopupUrl = null;
        SetPopupVisible(false);
        SetStatus("权限或运行配置已更改，重新打开窗口后生效。", showToUser: true);
    }
    public void SetStatus(string message, bool showToUser = false)
    {
        if (capabilitiesRevoked)
        {
            message = "权限或运行配置已更改，重新打开窗口后生效。";
            showToUser = true;
        }
        StatusText.Text = message;
        StatusText.IsVisible = showToUser;
        RefreshNotice();
    }
    private void SetPopupVisible(bool visible)
    {
        PopupBar.IsVisible = visible;
        RefreshNotice();
    }
    private void RefreshNotice() => NoticePopup.IsOpen = !closed && (StatusText.IsVisible || PopupBar.IsVisible);
    public void Log(string message) => service.LogMessage(node.Id, message);
    private void DisposeDocument()
    {
        var oldTransport = transport; var oldBridge = bridge; var oldProvider = provider;
        transport = null; bridge = null; provider = null;
        RunWindowAction(() => oldTransport?.Dispose(), "清理网页通信失败");
        RunWindowAction(() => oldBridge?.Dispose(), "清理网页桥接失败");
        RunWindowAction(() => oldProvider?.Dispose(), "清理网页服务失败");
    }
    private void RunWindowAction(Action action, string message)
    {
        try { action(); }
        catch (Exception ex) { ReportFailure(ex, message); }
    }
    private void ReportFailure(Exception exception, string message, bool showToUser = true)
    {
        // Reporting must remain safe after native WebView resources or the plugin scope stop.
        try { service?.Log(exception, message); }
        catch { }
        try { if (!closed && showToUser) SetStatus(message + "：" + exception.Message, showToUser: true); }
        catch { }
    }
}

