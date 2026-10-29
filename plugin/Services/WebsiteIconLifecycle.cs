namespace WebSearch.Services;

public sealed class WebsiteIconLifecycle(
    WebSearchPlugin plugin,
    FaviconService favicon) :
    IPluginLifecycleComponent,
    IMioEventHandler<MioPropertyChangedEventMessage>
{
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        MioAppContext.Current.EventBus.RegisterEventHandle<MioPropertyChangedEventMessage>(this);
        foreach (var website in plugin.WebsiteGroup.Children.OfType<WebsiteNode>())
        {
            if (website.GetSourceKind() == "LocalFile" || !string.IsNullOrEmpty(website.GetCustomIconId())) continue;
            var origin = WebsiteUrlResolver.GetOrigin(website.UrlTemplate);
            if (origin is not null && favicon.TryGetPath(origin) is null)
            {
                favicon.RequestRefresh(origin);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        MioAppContext.Current.EventBus.UnregisterEventHandle<MioPropertyChangedEventMessage>(this);
        return Task.CompletedTask;
    }

    public Task HandleAsync(
        MioPropertyChangedEventMessage eventModel,
        CancellationToken cancellationToken)
    {
        if (eventModel.Object is WebsiteNode website
            && website.IsAttachRootTree
            && (ReferenceEquals(eventModel.AttachProperty, WebsiteExtension.UrlTemplateProperty)
                || ReferenceEquals(eventModel.AttachProperty, EmbeddedWebsiteExtension.SourceKindProperty)
                || ReferenceEquals(eventModel.AttachProperty, WebsiteExtension.CustomIconIdProperty)))
        {
            website.InvalidateIcon();
            if (website.GetSourceKind() != "LocalFile" && string.IsNullOrEmpty(website.GetCustomIconId()))
                favicon.RequestRefresh(website);
        }

        return Task.CompletedTask;
    }
}
