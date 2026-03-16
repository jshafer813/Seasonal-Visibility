using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.SeasonalVisibility;

public class PluginServiceRegistrar : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IScheduledTask, SeasonalVisibilityTask>();
        serviceCollection.AddSingleton<IHostedService, SeasonalVisibilityLibraryListener>();
        serviceCollection.AddSingleton<IHostedService, SeasonalVisibilityJsRegistrar>();
    }
}
