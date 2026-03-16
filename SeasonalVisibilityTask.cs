using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SeasonalVisibility;

public class SeasonalVisibilityTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<SeasonalVisibilityTask> _logger;

    public string Name => "Apply Seasonal Visibility";
    public string Key => "SeasonalVisibility";
    public string Description => "Hides or shows movies and TV shows based on seasonal tags and today's date.";
    public string Category => "Seasonal Visibility";

    public SeasonalVisibilityTask(ILibraryManager libraryManager, IUserManager userManager, ILogger<SeasonalVisibilityTask> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;
        var today = DateTime.Now;
        var users = _userManager.Users.ToList();

        _logger.LogInformation("SeasonalVisibility: Starting task for {UserCount} users and {RuleCount} rules", users.Count, config.Rules.Count);

        // Conflict detection
        var tagRuleMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in config.Rules.Where(r => r.Enabled))
        {
            foreach (var tag in rule.Tags)
            {
                if (!tagRuleMap.ContainsKey(tag)) tagRuleMap[tag] = new List<string>();
                tagRuleMap[tag].Add(rule.Id);
            }
        }
        foreach (var kvp in tagRuleMap.Where(k => k.Value.Count > 1))
            _logger.LogWarning("SeasonalVisibility: Conflict — tag '{Tag}' is used in {Count} rules simultaneously", kvp.Key, kvp.Value.Count);

        int processed = 0;
        foreach (var rule in config.Rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!rule.Enabled)
            {
                _logger.LogInformation("SeasonalVisibility: Skipping disabled rule '{Description}'", rule.Description);
                progress.Report(++processed * 100.0 / config.Rules.Count);
                continue;
            }

            if (!TryIsInSeason(today, rule.StartDate, rule.EndDate, out bool inSeason))
            {
                _logger.LogWarning("SeasonalVisibility: Skipping rule with invalid dates — Start: '{Start}', End: '{End}'", rule.StartDate, rule.EndDate);
                progress.Report(++processed * 100.0 / config.Rules.Count);
                continue;
            }

            // Process tags
            foreach (var tag in rule.Tags)
            {
                _logger.LogInformation("SeasonalVisibility: Tag '{Tag}' is {Status}", tag, inSeason ? "IN season" : "OUT of season");
                await ApplyTagToUsers(config, rule.Id, tag, inSeason, users).ConfigureAwait(false);
            }

            // Process collections
            foreach (var collectionId in rule.CollectionIds)
            {
                if (!Guid.TryParse(collectionId, out var collectionGuid)) continue;

                var collection = _libraryManager.GetItemById(collectionGuid);
                if (collection == null)
                {
                    _logger.LogWarning("SeasonalVisibility: Collection '{Id}' not found", collectionId);
                    continue;
                }

                _logger.LogInformation("SeasonalVisibility: Collection '{Name}' is {Status}", collection.Name, inSeason ? "IN season" : "OUT of season");

                // Get all items in the collection
                var collectionFolder = collection as MediaBrowser.Controller.Entities.Folder;
                var collectionItems = collectionFolder?.GetChildren(null, true) ?? Enumerable.Empty<MediaBrowser.Controller.Entities.BaseItem>();

                // Use a synthetic tag based on collection ID to block/unblock
                var syntheticTag = $"__sv_collection_{collectionId}";
                foreach (var item in collectionItems)
                {
                    await ApplyTagToUsers(config, rule.Id, syntheticTag, inSeason, users).ConfigureAwait(false);
                }
            }

            progress.Report(++processed * 100.0 / config.Rules.Count);
        }

        Plugin.Instance.SaveConfiguration();
        _logger.LogInformation("SeasonalVisibility: Task complete");
    }

    private async Task ApplyTagToUsers(PluginConfiguration config, string ruleId, string tag, bool inSeason, IEnumerable<Jellyfin.Database.Implementations.Entities.User> users)
    {
        foreach (var user in users)
        {
            var userDto = _userManager.GetUserDto(user);
            if (userDto.Policy?.IsAdministrator == true) continue;

            var policy = userDto.Policy ?? new UserPolicy();
            var blockedTags = (policy.BlockedTags ?? Array.Empty<string>()).ToList();

            if (!inSeason)
            {
                if (!blockedTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    blockedTags.Add(tag);
                    policy.BlockedTags = blockedTags.ToArray();
                    await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);
                    _logger.LogInformation("SeasonalVisibility: Blocked '{Tag}' for user '{User}'", tag, user.Username);
                    AddActivityLog(config, ruleId, tag, "blocked", user.Username);
                }
            }
            else
            {
                if (blockedTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    policy.BlockedTags = blockedTags
                        .Where(t => !t.Equals(tag, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);
                    _logger.LogInformation("SeasonalVisibility: Unblocked '{Tag}' for user '{User}'", tag, user.Username);
                    AddActivityLog(config, ruleId, tag, "unblocked", user.Username);
                }
            }
        }
    }

    private static void AddActivityLog(PluginConfiguration config, string ruleId, string tag, string action, string username)
    {
        config.ActivityLog.Add(new ActivityLogEntry
        {
            Timestamp = DateTime.UtcNow,
            RuleId = ruleId,
            Tag = tag,
            Action = action,
            Username = username
        });
        while (config.ActivityLog.Count > config.ActivityLogMaxEntries)
            config.ActivityLog.RemoveAt(0);
    }

    public static bool TryIsInSeason(DateTime today, string start, string end, out bool inSeason)
    {
        inSeason = false;
        try
        {
            var startParts = start.Split('-');
            var endParts = end.Split('-');
            if (startParts.Length != 2 || endParts.Length != 2) return false;

            int startMonth = int.Parse(startParts[0]);
            int startDay = int.Parse(startParts[1]);
            int endMonth = int.Parse(endParts[0]);
            int endDay = int.Parse(endParts[1]);

            if (startMonth < 1 || startMonth > 12 || endMonth < 1 || endMonth > 12) return false;
            if (startDay < 1 || startDay > 31 || endDay < 1 || endDay > 31) return false;

            var startDate = new DateTime(today.Year, startMonth, startDay);
            var endDate = new DateTime(today.Year, endMonth, endDay);

            inSeason = startDate > endDate
                ? today >= startDate || today <= endDate
                : today >= startDate && today <= endDate;

            return true;
        }
        catch { return false; }
    }

    public static bool IsInSeason(DateTime today, string start, string end)
    {
        TryIsInSeason(today, start, end, out bool result);
        return result;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
    {
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(0).Ticks
        }
    };
}
