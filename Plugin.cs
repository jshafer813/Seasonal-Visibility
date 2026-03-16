using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SeasonalVisibility;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IDisposable
{
    public static Plugin? Instance { get; private set; }
    public override string Name => "Seasonal Visibility";
    public override Guid Id => Guid.Parse("1bad62ca-f2cb-4962-9d1e-b5737da03bfd");
    public override string Description => "Hides and shows movies based on real-world seasons/holidays";

    private readonly IUserManager _userManager;
    private readonly ILogger<Plugin> _logger;

    private const string ScriptId = "1bad62ca-f2cb-4962-9d1e-b5737da03bfd-config-script";

    private const string ConfigScript = """
(function () {
    var pluginId = '1bad62ca-f2cb-4962-9d1e-b5737da03bfd';

    function getEaster(year) {
        var a = year % 19, b = Math.floor(year / 100), c = year % 100;
        var d = Math.floor(b / 4), e = b % 4, f = Math.floor((b + 8) / 25);
        var g = Math.floor((b - f + 1) / 3), h = (19 * a + b - d - g + 15) % 30;
        var i = Math.floor(c / 4), k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = Math.floor((a + 11 * h + 22 * l) / 451);
        var month = Math.floor((h + l - 7 * m + 114) / 31);
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return { month: month, day: day };
    }

    function getNthWeekday(year, month, weekday, n) {
        if (n > 0) {
            var d = new Date(year, month - 1, 1);
            var diff = (weekday - d.getDay() + 7) % 7;
            d.setDate(1 + diff + (n - 1) * 7);
            return { month: d.getMonth() + 1, day: d.getDate() };
        } else {
            var d = new Date(year, month, 0);
            var diff = (d.getDay() - weekday + 7) % 7;
            d.setDate(d.getDate() - diff);
            return { month: d.getMonth() + 1, day: d.getDate() };
        }
    }

    function pad(n) { return n < 10 ? '0' + n : '' + n; }
    function toMMDD(obj) { return pad(obj.month) + '-' + pad(obj.day); }
    function addDays(mmdd, days) {
        var parts = mmdd.split('-');
        var d = new Date(new Date().getFullYear(), parseInt(parts[0]) - 1, parseInt(parts[1]) + days);
        return pad(d.getMonth() + 1) + '-' + pad(d.getDate());
    }

    var holidayDates = {
        'easter': function(year) { var e = getEaster(year); var base = toMMDD(e); return { start: addDays(base, -3), end: base }; },
        'thanksgiving': function(year) { var t = getNthWeekday(year, 11, 4, 4); var base = toMMDD(t); return { start: base, end: addDays(base, 3) }; },
        'halloween': function(year) { return { start: '10-01', end: '10-31' }; },
        'christmas': function(year) { return { start: '12-01', end: '01-05' }; },
        'mothers day': function(year) { var d = getNthWeekday(year, 5, 0, 2); var base = toMMDD(d); return { start: addDays(base, -7), end: base }; },
        'fathers day': function(year) { var d = getNthWeekday(year, 6, 0, 3); var base = toMMDD(d); return { start: addDays(base, -7), end: base }; },
        'valentines': function(year) { return { start: '02-07', end: '02-14' }; },
        'new year': function(year) { return { start: '12-26', end: '01-02' }; },
        'summer': function(year) { return { start: '06-01', end: '08-31' }; },
        'st patrick': function(year) { return { start: '03-10', end: '03-17' }; },
        'hanukkah': function(year) { return { start: '12-01', end: '12-14' }; }
    };

    function getSmartDates(tag) {
        var lower = tag.toLowerCase().trim();
        var year = new Date().getFullYear();
        for (var key in holidayDates) {
            if (lower.includes(key)) return holidayDates[key](year);
        }
        return null;
    }

    function isInSeason(startStr, endStr) {
        var today = new Date();
        var todayVal = (today.getMonth() + 1) * 100 + today.getDate();
        var parts = startStr.split('-');
        var startVal = parseInt(parts[0]) * 100 + parseInt(parts[1]);
        parts = endStr.split('-');
        var endVal = parseInt(parts[0]) * 100 + parseInt(parts[1]);
        if (startVal > endVal) return todayVal >= startVal || todayVal <= endVal;
        return todayVal >= startVal && todayVal <= endVal;
    }

    function addRuleRow(tag, description, start, end) {
        var tbody = document.getElementById('rulesBody');
        if (!tbody) return;
        var tr = document.createElement('tr');
        var statusLabel = (tag && start && end)
            ? (isInSeason(start, end) ? '<span style="color:#4CAF50;">✔ In Season</span>' : '<span style="color:#aaa;">✘ Out of Season</span>')
            : '—';
        tr.innerHTML =
            '<td style="padding:4px 8px;">' +
                '<input type="text" class="rule-tag emby-input" value="' + (tag||'') + '" placeholder="e.g. christmas" style="width:120px;" />' +
                '<button type="button" class="auto-dates-btn" title="Auto-fill dates" style="background:none;border:none;cursor:pointer;color:#00a4dc;font-size:1em;padding:2px 4px;">📅</button>' +
            '</td>' +
            '<td style="padding:4px 8px;"><input type="text" class="rule-description emby-input" value="' + (description||'') + '" placeholder="e.g. Christmas Season" style="width:160px;" /></td>' +
            '<td style="padding:4px 8px;"><input type="text" class="rule-start emby-input" value="' + (start||'') + '" placeholder="12-01" style="width:80px;" maxlength="5" /></td>' +
            '<td style="padding:4px 8px;"><input type="text" class="rule-end emby-input" value="' + (end||'') + '" placeholder="01-05" style="width:80px;" maxlength="5" /></td>' +
            '<td style="padding:4px 8px;" class="rule-status">' + statusLabel + '</td>' +
            '<td style="padding:4px 8px;"><button type="button" class="raised emby-button delete-btn" style="background:none;color:#e05353;padding:2px 10px;">✕</button></td>';
        function updateStatus() {
            var t = tr.querySelector('.rule-tag').value.trim();
            var s = tr.querySelector('.rule-start').value.trim();
            var e = tr.querySelector('.rule-end').value.trim();
            var cell = tr.querySelector('.rule-status');
            if (t && s && e && /^\d{2}-\d{2}$/.test(s) && /^\d{2}-\d{2}$/.test(e)) {
                cell.innerHTML = isInSeason(s, e) ? '<span style="color:#4CAF50;">✔ In Season</span>' : '<span style="color:#aaa;">✘ Out of Season</span>';
            } else { cell.innerHTML = '—'; }
        }
        tr.querySelector('.auto-dates-btn').addEventListener('click', function () {
            var tagVal = tr.querySelector('.rule-tag').value.trim();
            var dates = getSmartDates(tagVal);
            if (dates) { tr.querySelector('.rule-start').value = dates.start; tr.querySelector('.rule-end').value = dates.end; updateStatus(); }
            else { alert('No smart dates for "' + tagVal + '". Supported: easter, thanksgiving, halloween, christmas, mothers day, fathers day, valentines, new year, summer, st patrick, hanukkah'); }
        });
        tr.querySelector('.rule-start').addEventListener('input', updateStatus);
        tr.querySelector('.rule-end').addEventListener('input', updateStatus);
        tr.querySelector('.rule-tag').addEventListener('input', updateStatus);
        tr.querySelector('.delete-btn').addEventListener('click', function () { tr.remove(); });
        tbody.appendChild(tr);
    }

    function loadConfig() {
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            document.getElementById('rulesBody').innerHTML = '';
            (config.Rules || []).forEach(function (r) { addRuleRow(r.Tag, r.Description, r.StartDate, r.EndDate); });
        });
    }

    function triggerTask() {
        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('ScheduledTasks') }).then(function (tasks) {
            var task = tasks.find(function (t) { return t.Name === 'Apply Seasonal Visibility'; });
            if (task) ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('ScheduledTasks/Running/' + task.Id) });
        });
    }

    function saveConfig() {
        var rules = [];
        document.querySelectorAll('#rulesBody tr').forEach(function (tr) {
            var tag = tr.querySelector('.rule-tag').value.trim();
            if (tag) rules.push({ Tag: tag, Description: tr.querySelector('.rule-description').value.trim(), StartDate: tr.querySelector('.rule-start').value.trim(), EndDate: tr.querySelector('.rule-end').value.trim() });
        });
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            config.Rules = rules;
            ApiClient.updatePluginConfiguration(pluginId, config).then(function () {
                triggerTask();
                var status = document.getElementById('saveStatus');
                if (status) { status.style.color = '#4CAF50'; status.textContent = '✔ Saved & applied.'; setTimeout(function () { status.textContent = ''; }, 5000); }
            });
        });
    }

    function initPage() {
        var addBtn = document.getElementById('addRuleBtn');
        var saveBtn = document.getElementById('saveBtn');
        if (addBtn) addBtn.addEventListener('click', function () { addRuleRow('', '', '', ''); });
        if (saveBtn) saveBtn.addEventListener('click', function () { saveConfig(); });
        loadConfig();
    }

    document.addEventListener('viewshow', function (e) {
        var page = e.target;
        if (!page || page.id !== 'SeasonalVisibilityConfigPage') return;
        initPage();
    }, true);
})();
""";

    public Plugin(IApplicationPaths appPaths, IXmlSerializer xmlSerializer, IUserManager userManager, ILogger<Plugin> logger)
        : base(appPaths, xmlSerializer)
    {
        Instance = this;
        _userManager = userManager;
        _logger = logger;
        RegisterWithJsInjector();
    }

    private void RegisterWithJsInjector()
    {
        try
        {
            var jsInjectorAssembly = AssemblyLoadContext.All
                .SelectMany(x => x.Assemblies)
                .FirstOrDefault(x => x.FullName?.Contains("Jellyfin.Plugin.JavaScriptInjector") ?? false);

            if (jsInjectorAssembly == null)
            {
                _logger.LogInformation("JavaScript Injector not found — config UI will not be available.");
                return;
            }

            var pluginInterface = jsInjectorAssembly.GetType("Jellyfin.Plugin.JavaScriptInjector.PluginInterface");
            if (pluginInterface == null)
            {
                _logger.LogWarning("JavaScript Injector PluginInterface type not found.");
                return;
            }

            var payload = new Newtonsoft.Json.Linq.JObject
            {
                { "id", ScriptId },
                { "name", "Seasonal Visibility Config" },
                { "script", ConfigScript },
                { "requiresAuthentication", true }
            };

            var result = pluginInterface.GetMethod("RegisterScript")?.Invoke(null, new object[] { Id.ToString(), payload });
            if (result is bool success && success)
                _logger.LogInformation("Seasonal Visibility: registered config script with JavaScript Injector.");
            else
                _logger.LogWarning("Seasonal Visibility: RegisterScript returned false.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Seasonal Visibility: failed to register with JavaScript Injector.");
        }
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace),
                EnableInMainMenu = true,
                MenuSection = "server",
                MenuIcon = "event"
            }
        };
    }

    public void Dispose()
    {
        try
        {
            var jsInjectorAssembly = AssemblyLoadContext.All
                .SelectMany(x => x.Assemblies)
                .FirstOrDefault(x => x.FullName?.Contains("Jellyfin.Plugin.JavaScriptInjector") ?? false);

            if (jsInjectorAssembly != null)
            {
                var pluginInterface = jsInjectorAssembly.GetType("Jellyfin.Plugin.JavaScriptInjector.PluginInterface");
                pluginInterface?.GetMethod("UnregisterAllScriptsFromPlugin")?.Invoke(null, new object[] { Id.ToString() });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Seasonal Visibility: failed to unregister from JavaScript Injector.");
        }

        var config = Configuration;
        var users = _userManager.Users.ToList();
        foreach (var user in users)
        {
            var userDto = _userManager.GetUserDto(user);
            if (userDto.Policy?.IsAdministrator == true) continue;
            var policy = userDto.Policy;
            if (policy?.BlockedTags == null) continue;
            var seasonalTags = config.Rules.Select(r => r.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var cleanedTags = policy.BlockedTags.Where(t => !seasonalTags.Contains(t)).ToArray();
            if (cleanedTags.Length != policy.BlockedTags.Length)
            {
                policy.BlockedTags = cleanedTags;
                _userManager.UpdatePolicyAsync(user.Id, policy).GetAwaiter().GetResult();
            }
        }
        GC.SuppressFinalize(this);
    }
}
