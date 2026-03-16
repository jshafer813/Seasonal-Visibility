using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
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

        int processed = 0;
        foreach (var rule in config.Rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryIsInSeason(today, rule.StartDate, rule.EndDate, out bool inSeason))
            {
                _logger.LogWarning("SeasonalVisibility: Skipping rule with invalid dates — Tag: '{Tag}', Start: '{Start}', End: '{End}'", rule.Tag, rule.StartDate, rule.EndDate);
                progress.Report(++processed * 100.0 / config.Rules.Count);
                continue;
            }

            _logger.LogInformation("SeasonalVisibility: Tag '{Tag}' is {Status}", rule.Tag, inSeason ? "IN season" : "OUT of season");

            foreach (var user in users)
            {
                var userDto = _userManager.GetUserDto(user);
                if (userDto.Policy?.IsAdministrator == true)
                    continue;

                var policy = userDto.Policy ?? new UserPolicy();
                var blockedTags = (policy.BlockedTags ?? Array.Empty<string>()).ToList();

                if (!inSeason)
                {
                    if (!blockedTags.Contains(rule.Tag, StringComparer.OrdinalIgnoreCase))
                    {
                        blockedTags.Add(rule.Tag);
                        policy.BlockedTags = blockedTags.ToArray();
                        await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);
                        _logger.LogInformation("SeasonalVisibility: Blocked tag '{Tag}' for user '{User}'", rule.Tag, user.Username);
                    }
                }
                else
                {
                    if (blockedTags.Contains(rule.Tag, StringComparer.OrdinalIgnoreCase))
                    {
                        policy.BlockedTags = blockedTags
                            .Where(t => !t.Equals(rule.Tag, StringComparison.OrdinalIgnoreCase))
                            .ToArray();
                        await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);
                        _logger.LogInformation("SeasonalVisibility: Unblocked tag '{Tag}' for user '{User}'", rule.Tag, user.Username);
                    }
                }
            }
            progress.Report(++processed * 100.0 / config.Rules.Count);
        }

        _logger.LogInformation("SeasonalVisibility: Task complete");
    }

    public static bool TryIsInSeason(DateTime today, string start, string end, out bool inSeason)
    {
        inSeason = false;
        try
        {
            var startParts = start.Split('-');
            var endParts = end.Split('-');
            if (startParts.Length != 2 || endParts.Length != 2)
                return false;

            int startMonth = int.Parse(startParts[0]);
            int startDay = int.Parse(startParts[1]);
            int endMonth = int.Parse(endParts[0]);
            int endDay = int.Parse(endParts[1]);

            if (startMonth < 1 || startMonth > 12 || endMonth < 1 || endMonth > 12)
                return false;
            if (startDay < 1 || startDay > 31 || endDay < 1 || endDay > 31)
                return false;

            var startDate = new DateTime(today.Year, startMonth, startDay);
            var endDate = new DateTime(today.Year, endMonth, endDay);

            inSeason = startDate > endDate
                ? today >= startDate || today <= endDate
                : today >= startDate && today <= endDate;

            return true;
        }
        catch
        {
            return false;
        }
    }

    // Keep old signature for compatibility
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
