using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SeasonalVisibility;

public class SeasonRule
{
    // Core
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public List<string> Tags { get; set; } = new();
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";

    // Metadata
    public string Description { get; set; } = "";

    // Control
    public bool Enabled { get; set; } = true;

    // Collections
    public List<string> CollectionIds { get; set; } = new();
}

public class ActivityLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string RuleId { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Action { get; set; } = ""; // "blocked" or "unblocked"
    public string Username { get; set; } = "";
}

public class PluginConfiguration : BasePluginConfiguration
{
    public List<SeasonRule> Rules { get; set; } = new();

    // Activity log — capped at 200 entries to avoid bloat
    public List<ActivityLogEntry> ActivityLog { get; set; } = new();
    public int ActivityLogMaxEntries { get; set; } = 200;
}
