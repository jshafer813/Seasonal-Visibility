using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SeasonalVisibility;

public class SeasonalVisibilityLibraryListener : IHostedService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<SeasonalVisibilityLibraryListener> _logger;

    public SeasonalVisibilityLibraryListener(ILibraryManager libraryManager, IUserManager userManager, ILogger<SeasonalVisibilityLibraryListener> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemUpdated += OnItemUpdated;
        _logger.LogInformation("SeasonalVisibility: Library listener started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _logger.LogInformation("SeasonalVisibility: Library listener stopped");
        return Task.CompletedTask;
    }

    private async void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        try
        {
            var config = Plugin.Instance!.Configuration;
            var today = DateTime.Now;
            var item = e.Item;
            var itemTags = item.Tags ?? Array.Empty<string>();

            // Get all seasonal tags from enabled rules
            var seasonalTags = config.Rules
                .Where(r => r.Enabled)
                .SelectMany(r => r.Tags)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!itemTags.Any(t => seasonalTags.Contains(t)))
                return;

            _logger.LogInformation("SeasonalVisibility: Detected tag update on '{Item}', reapplying visibility", item.Name);

            var users = _userManager.Users.ToList();

            foreach (var rule in config.Rules.Where(r => r.Enabled))
            {
                if (!rule.Tags.Any(t => itemTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                    continue;

                if (!SeasonalVisibilityTask.TryIsInSeason(today, rule.StartDate, rule.EndDate, out bool inSeason))
                {
                    _logger.LogWarning("SeasonalVisibility: Skipping rule with invalid dates — Start: '{Start}', End: '{End}'", rule.StartDate, rule.EndDate);
                    continue;
                }

                foreach (var tag in rule.Tags)
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
                                _logger.LogInformation("SeasonalVisibility: Blocked tag '{Tag}' for user '{User}'", tag, user.Username);
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
                                _logger.LogInformation("SeasonalVisibility: Unblocked tag '{Tag}' for user '{User}'", tag, user.Username);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeasonalVisibility: Unhandled exception in OnItemUpdated");
        }
    }
}
