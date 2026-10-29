using Lucide.Avalonia;
using Windows.ApplicationModel.DataTransfer;

namespace WebSearch.Actions;

internal static class UrlResultActions
{
    public static IEnumerable<ISearchResultAction> Create(SearchResult result)
    {
        if (result.Payload is not OpenUrlPayload)
        {
            return [];
        }

        var actions = new List<ISearchResultAction> { CopyLinkAction.Instance };
        var catalog = WebSearchRegister.ComponentContextInstance.Resolve<BrowserCatalogService>();
        foreach (var browser in catalog.GetBrowserOptions())
        {
            if (browser.IsAvailable && browser.Id != BrowserIds.SystemDefault)
            {
                actions.Add(new OpenWithBrowserAction(browser.Id, browser.Name));
            }
        }

        return actions;
    }
}

internal interface IClipboardTextService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken);
}

internal sealed class WebSearchClipboardTextService : IClipboardTextService
{
    public Task SetTextAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plugin = WebSearchRegister.ComponentContextInstance.Resolve<WebSearchPlugin>();
        return StaTask.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }, plugin.Context.Logger);
    }
}

internal sealed class CopyLinkAction : SearchResultActionBase
{
    public static CopyLinkAction Instance { get; } = new(new WebSearchClipboardTextService());

    private readonly IClipboardTextService _clipboard;

    internal CopyLinkAction(IClipboardTextService clipboard)
    {
        _clipboard = clipboard;
        Text = "复制链接";
        Group = "网页";
        Icon = LucideIconKind.ClipboardCopy;
    }

    public override async Task ExecuteAsync(
        SearchResult result,
        InvokeContext context,
        CancellationToken cancellationToken)
    {
        if (result.Payload is not OpenUrlPayload payload)
        {
            return;
        }

        await _clipboard.SetTextAsync(payload.Url, cancellationToken).ConfigureAwait(false);
        await MioIoc.Resolve<ISearchBoxWindow>().HideWindowAsync().ConfigureAwait(false);
    }
}

internal sealed class OpenWithBrowserAction : SearchResultActionBase
{
    private readonly string _browserId;

    public OpenWithBrowserAction(string browserId, string browserName)
    {
        _browserId = browserId;
        Text = $"使用 {browserName} 打开";
        Group = "网页";
    }

    public override async Task ExecuteAsync(
        SearchResult result,
        InvokeContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Payload is not OpenUrlPayload payload)
        {
            return;
        }

        WebSearchRegister.ComponentContextInstance.Resolve<BrowserLaunchService>()
            .Open(payload.Url, _browserId);
        await MioIoc.Resolve<ISearchBoxWindow>().HideWindowAsync().ConfigureAwait(false);
    }
}
