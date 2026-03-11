using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SeasonalVisibility;

public class SeasonRule
{
    public string Tag { get; set; } = "";
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
}

public class PluginConfiguration : BasePluginConfiguration
{
    public List<SeasonRule> Rules { get; set; } = new()
    {
        new SeasonRule { Tag = "christmas", StartDate = "12-01", EndDate = "01-05" },
        new SeasonRule { Tag = "halloween", StartDate = "10-01", EndDate = "10-31" },
        new SeasonRule { Tag = "thanksgiving", StartDate = "11-15", EndDate = "11-30" },
        new SeasonRule { Tag = "summer", StartDate = "06-01", EndDate = "08-31" },
    };
}
