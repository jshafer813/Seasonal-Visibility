using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.SeasonalVisibility;

public class SeasonalVisibilityLibraryListener : IHostedService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;

    public SeasonalVisibilityLibraryListener(ILibraryManager libraryManager, IUserManager userManager)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemUpdated += OnItemUpdated;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemUpdated -= OnItemUpdated;
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

        var users = _userManager.Users.ToList();

        foreach (var rule in config.Rules)
        {
            if (!itemTags.Contains(rule.Tag, StringComparer.OrdinalIgnoreCase))
                continue;

            var startParts = rule.StartDate.Split('-');
            var endParts = rule.EndDate.Split('-');
            var startDate = new DateTime(today.Year, int.Parse(startParts[0]), int.Parse(startParts[1]));
            var endDate = new DateTime(today.Year, int.Parse(endParts[0]), int.Parse(endParts[1]));
            bool inSeason = today >= startDate && today <= endDate;

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
        }
    }
}
