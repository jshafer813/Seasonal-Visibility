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
        var config = Plugin.Instance!.Configuration;
        var today = DateTime.Now;
        var item = e.Item;
        var itemTags = item.Tags ?? Array.Empty<string>();

        var seasonalTags = config.Rules.Select(r => r.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!itemTags.Any(t => seasonalTags.Contains(t)))
            return;

        _logger.LogInformation("SeasonalVisibility: Detected tag update on '{Item}', reapplying visibility", item.Name);

        var users = _userManager.Users.ToList();

        foreach (var rule in config.Rules)
        {
            if (!itemTags.Contains(rule.Tag, StringComparer.OrdinalIgnoreCase))
                continue;

            bool inSeason = SeasonalVisibilityTask.IsInSeason(today, rule.StartDate, rule.EndDate);

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
        }
    }
}
