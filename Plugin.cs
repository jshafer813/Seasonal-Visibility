using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SeasonalVisibility;

public class Plugin : BasePlugin<PluginConfiguration>, IDisposable
{
    public static Plugin? Instance { get; private set; }
    public override string Name => "Seasonal Visibility";
    public override Guid Id => Guid.Parse("1bad62ca-f2cb-4962-9d1e-b5737da03bfd");
    public override string Description => "Hides and shows movies based on real-world seasons/holidays";

    private readonly IUserManager _userManager;

    public Plugin(IApplicationPaths appPaths, IXmlSerializer xmlSerializer, IUserManager userManager)
        : base(appPaths, xmlSerializer)
    {
        Instance = this;
        _userManager = userManager;
    }

    public void Dispose()
    {
        // When plugin is uninstalled or disabled, remove all seasonal blocked tags from all users
        var config = Configuration;
        var users = _userManager.Users.ToList();

        foreach (var user in users)
        {
            var userDto = _userManager.GetUserDto(user);
            if (userDto.Policy?.IsAdministrator == true)
                continue;

            var policy = userDto.Policy;
            if (policy?.BlockedTags == null)
                continue;

            var seasonalTags = config.Rules.Select(r => r.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var cleanedTags = policy.BlockedTags
                .Where(t => !seasonalTags.Contains(t))
                .ToArray();

            if (cleanedTags.Length != policy.BlockedTags.Length)
            {
                policy.BlockedTags = cleanedTags;
                _userManager.UpdatePolicyAsync(user.Id, policy).GetAwaiter().GetResult();
            }
        }

        GC.SuppressFinalize(this);
    }
}
