using System.Runtime.Loader;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SeasonalVisibility;

public class SeasonalVisibilityJsRegistrar : IHostedService
{
    private readonly ILogger<SeasonalVisibilityJsRegistrar> _logger;

    public SeasonalVisibilityJsRegistrar(ILogger<SeasonalVisibilityJsRegistrar> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Task.Run(async () =>
        {
            await Task.Delay(10000, cancellationToken).ConfigureAwait(false);
            RegisterWithJsInjector();
        }, cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        UnregisterFromJsInjector();
        return Task.CompletedTask;
    }

    private void RegisterWithJsInjector()
    {
        try
        {
            var jsInjectorAssembly = AssemblyLoadContext.All
                .SelectMany(x => x.Assemblies)
                .FirstOrDefault(x => x.FullName?.Contains("Jellyfin.Plugin.JavaScriptInjector") ?? false);

            if (jsInjectorAssembly == null)
            {
                _logger.LogInformation("SeasonalVisibility: JavaScript Injector not found.");
                return;
            }

            var pluginInterface = jsInjectorAssembly.GetType("Jellyfin.Plugin.JavaScriptInjector.PluginInterface");
            if (pluginInterface == null)
            {
                _logger.LogWarning("SeasonalVisibility: PluginInterface type not found.");
                return;
            }

            var plugin = Plugin.Instance;
            if (plugin == null) return;

            var registerMethod = pluginInterface.GetMethod("RegisterScript");
            if (registerMethod == null)
            {
                _logger.LogWarning("SeasonalVisibility: RegisterScript method not found.");
                return;
            }

            var payload = new Newtonsoft.Json.Linq.JObject
            {
                { "id", plugin.Id.ToString() + "-config-v3" },
                { "name", "Seasonal Visibility Config" },
                { "script", Plugin.ConfigScript },
                { "enabled", true },
                { "requiresAuthentication", true },
                { "pluginId", plugin.Id.ToString() },
                { "pluginName", plugin.Name },
                { "pluginVersion", "3.0.0.0" }
            };

            var result = registerMethod.Invoke(null, new object[] { payload });

            if (result is bool success && success)
                _logger.LogInformation("SeasonalVisibility: registered config script with JavaScript Injector.");
            else
                _logger.LogWarning("SeasonalVisibility: RegisterScript returned false. Result: {Result}", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeasonalVisibility: failed to register with JavaScript Injector.");
        }
    }

    private void UnregisterFromJsInjector()
    {
        try
        {
            var jsInjectorAssembly = AssemblyLoadContext.All
                .SelectMany(x => x.Assemblies)
                .FirstOrDefault(x => x.FullName?.Contains("Jellyfin.Plugin.JavaScriptInjector") ?? false);

            if (jsInjectorAssembly != null)
            {
                var pluginInterface = jsInjectorAssembly.GetType("Jellyfin.Plugin.JavaScriptInjector.PluginInterface");
                pluginInterface?.GetMethod("UnregisterAllScriptsFromPlugin")?.Invoke(null, new object[] { Plugin.Instance?.Id.ToString() ?? "" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeasonalVisibility: failed to unregister from JavaScript Injector.");
        }
    }
}
