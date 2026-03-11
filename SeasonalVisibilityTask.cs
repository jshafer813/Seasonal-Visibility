using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Users;

namespace Jellyfin.Plugin.SeasonalVisibility;

public class SeasonalVisibilityTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;

    public string Name => "Apply Seasonal Visibility";
    public string Key => "SeasonalVisibility";
    public string Description => "Hides or shows movies based on seasonal tags and today's date.";
    public string Category => "Seasonal Visibility";

    public SeasonalVisibilityTask(ILibraryManager libraryManager, IUserManager userManager)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;
        var today = DateTime.Now;
        var users = _userManager.Users.ToList();

        int processed = 0;
        foreach (var rule in config.Rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool inSeason = IsInSeason(today, rule.StartDate, rule.EndDate);

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
                    }
                }
            }
            progress.Report(++processed * 100.0 / config.Rules.Count);
        }
    }

    public static bool IsInSeason(DateTime today, string start, string end)
    {
        var startParts = start.Split('-');
        var endParts = end.Split('-');
        int startMonth = int.Parse(startParts[0]);
        int startDay = int.Parse(startParts[1]);
        int endMonth = int.Parse(endParts[0]);
        int endDay = int.Parse(endParts[1]);

        var startDate = new DateTime(today.Year, startMonth, startDay);
        var endDate = new DateTime(today.Year, endMonth, endDay);

        // Cross-year season e.g. Dec 20 - Jan 5
        if (startDate > endDate)
        {
            // We're in season if today is after start OR before end
            return today >= startDate || today <= endDate;
        }

        return today >= startDate && today <= endDate;
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
