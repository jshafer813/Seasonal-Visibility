using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SeasonalVisibility;

public class Plugin : BasePlugin<PluginConfiguration>
{
    public static Plugin? Instance { get; private set; }

    public override string Name => "Seasonal Visibility";
    public override Guid Id => Guid.Parse("1bad62ca-f2cb-4962-9d1e-b5737da03bfd");
    public override string Description => "Hides and shows movies based on real-world seasons/holidays";

    public Plugin(IApplicationPaths appPaths, IXmlSerializer xmlSerializer)
        : base(appPaths, xmlSerializer)
    {
        Instance = this;
    }
}
