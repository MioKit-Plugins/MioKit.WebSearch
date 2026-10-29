namespace WebSearch;

public sealed class WebSearchRegister : RegisterBase<WebSearchRegister>
{
    public override void RegisterService(ContainerBuilder builder, IServiceCollection services)
    {
        var plugin = new WebSearchPlugin();

        builder.RegisterInstance(plugin)
            .As<IPlugin>()
            .Keyed<IPlugin>(WebSearchConst.PluginId)
            .As(plugin.GetType());

        builder.RegisterType<BrowserCatalogService>().SingleInstance();
        builder.RegisterType<BrowserIndexService>().SingleInstance();
        builder.RegisterType<BrowserLaunchService>().SingleInstance();
        builder.RegisterType<WebsiteStorageService>().SingleInstance();
        builder.RegisterType<WebsiteCapabilitiesJsService>().AsSelf().InstancePerDependency();
        builder.RegisterType<WebsiteWindowService>().AsSelf().As<IPluginLifecycleComponent>().SingleInstance();
        builder.RegisterType<FaviconService>().AsSelf().As<IPluginLifecycleComponent>().SingleInstance();
        builder.RegisterType<WebsiteIconService>().SingleInstance();
        builder.RegisterType<BrowserIndexLifecycle>()
            .As<IPluginLifecycleComponent>()
            .SingleInstance();
        builder.RegisterType<WebsiteIconLifecycle>()
            .As<IPluginLifecycleComponent>()
            .SingleInstance();

        builder.RegisterType<WebSearchJsService>().AsSelf().As<IPluginLifecycleComponent>().SingleInstance();
    }
}
