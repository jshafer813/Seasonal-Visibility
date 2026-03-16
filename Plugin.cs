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

    public const string ConfigScript = """
(function () {
    var pluginId = '1bad62ca-f2cb-4962-9d1e-b5737da03bfd';
    var allLibraryTags = [];

    function escHtml(str) {
        return String(str || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

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
        try {
            var today = new Date();
            var todayVal = (today.getMonth() + 1) * 100 + today.getDate();
            var parts = startStr.split('-');
            var startVal = parseInt(parts[0]) * 100 + parseInt(parts[1]);
            parts = endStr.split('-');
            var endVal = parseInt(parts[0]) * 100 + parseInt(parts[1]);
            if (startVal > endVal) return todayVal >= startVal || todayVal <= endVal;
            return todayVal >= startVal && todayVal <= endVal;
        } catch(e) { return false; }
    }

    function fetchLibraryTags() {
        var userId = ApiClient.getCurrentUserId();
        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('Items', { Recursive: true, IncludeItemTypes: 'Movie,Series', Fields: 'Tags', Limit: 2000, UserId: userId }) }).then(function(result) {
            var tagSet = {};
            (result.Items || []).forEach(function(item) {
                (item.Tags || []).forEach(function(t) { tagSet[t] = true; });
            });
            allLibraryTags = Object.keys(tagSet).sort();
        }).catch(function() {});
    }

    function showTagDropdown(input, tagsCell) {
        var existing = tagsCell.querySelector('.sv-tag-dropdown');
        if (existing) existing.remove();

        var val = input.value.split(',').pop().trim().toLowerCase();
        var already = input.value.split(',').map(function(x){ return x.trim().toLowerCase(); });
        var matches = allLibraryTags.filter(function(t) {
            return t.toLowerCase().includes(val) && !already.includes(t.toLowerCase());
        }).slice(0, 10);

        if (!matches.length) return;

        var dd = document.createElement('div');
        dd.className = 'sv-tag-dropdown';
        dd.style.cssText = 'position:absolute;background:#1e1e1e;border:1px solid #444;border-radius:4px;z-index:9999;max-height:200px;overflow-y:auto;min-width:160px;box-shadow:0 4px 12px rgba(0,0,0,0.5);';

        matches.forEach(function(tag) {
            var item = document.createElement('div');
            item.textContent = tag;
            item.style.cssText = 'padding:6px 12px;cursor:pointer;color:#ddd;font-size:0.9em;';
            item.addEventListener('mouseenter', function() { item.style.background = '#00a4dc'; item.style.color = '#fff'; });
            item.addEventListener('mouseleave', function() { item.style.background = ''; item.style.color = '#ddd'; });
            item.addEventListener('mousedown', function(e) {
                e.preventDefault();
                var parts = input.value.split(',').map(function(x){ return x.trim(); }).filter(Boolean);
                parts.pop();
                parts.push(tag);
                input.value = parts.join(', ');
                dd.remove();
                input.focus();
            });
            dd.appendChild(item);
        });

        tagsCell.style.position = 'relative';
        tagsCell.appendChild(dd);
        input.addEventListener('blur', function() { setTimeout(function() { if (dd.parentNode) dd.remove(); }, 150); }, { once: true });
    }

    function addRuleRow(ruleId, tags, description, start, end, enabled) {
        var tbody = document.getElementById('rulesBody');
        if (!tbody) return;
        if (enabled === undefined) enabled = true;
        var tagsStr = Array.isArray(tags) ? tags.join(', ') : (tags || '');
        var tr = document.createElement('tr');
        tr.dataset.ruleId = ruleId || ('rule-' + Date.now());
        if (!enabled) tr.style.opacity = '0.5';

        var statusLabel = '—';
        if (tagsStr && start && end) {
            statusLabel = isInSeason(start, end)
                ? '<span style="color:#4CAF50;">✔ In Season</span>'
                : '<span style="color:#aaa;">✘ Out of Season</span>';
        }

        tr.innerHTML =
            '<td style="padding:4px 8px;min-width:180px;">' +
                '<div style="display:flex;align-items:center;gap:4px;">' +
                    '<input type="text" class="rule-tags emby-input" value="' + escHtml(tagsStr) + '" placeholder="christmas, xmas" style="width:140px;" />' +
                    '<button type="button" class="auto-dates-btn" title="Auto-fill dates" style="background:none;border:none;cursor:pointer;color:#00a4dc;font-size:1em;padding:2px;">📅</button>' +
                '</div>' +
            '</td>' +
            '<td style="padding:4px 8px;">' +
                '<input type="text" class="rule-description emby-input" value="' + escHtml(description) + '" placeholder="e.g. Christmas Season" style="width:150px;" />' +
            '</td>' +
            '<td style="padding:4px 8px;">' +
                '<input type="text" class="rule-start emby-input" value="' + escHtml(start) + '" placeholder="12-01" style="width:70px;" maxlength="5" />' +
            '</td>' +
            '<td style="padding:4px 8px;">' +
                '<input type="text" class="rule-end emby-input" value="' + escHtml(end) + '" placeholder="01-05" style="width:70px;" maxlength="5" />' +
            '</td>' +
            '<td style="padding:4px 8px;" class="rule-status">' + statusLabel + '</td>' +
            '<td style="padding:4px 8px;text-align:center;">' +
                '<input type="checkbox" class="rule-enabled" ' + (enabled ? 'checked' : '') + ' title="Enable/disable rule" style="width:18px;height:18px;cursor:pointer;" />' +
            '</td>' +
            '<td style="padding:4px 8px;">' +
                '<button type="button" class="preview-btn raised emby-button" style="background:#333;color:#ddd;padding:2px 8px;font-size:0.8em;">Preview</button>' +
            '</td>' +
            '<td style="padding:4px 8px;">' +
                '<button type="button" class="raised emby-button delete-btn" style="background:none;color:#e05353;padding:2px 10px;">✕</button>' +
            '</td>';

        var tagsInput = tr.querySelector('.rule-tags');
        var tagsCell = tr.querySelector('td:first-child');

        function updateStatus() {
            var s = tr.querySelector('.rule-start').value.trim();
            var e = tr.querySelector('.rule-end').value.trim();
            var t = tagsInput.value.trim();
            var cell = tr.querySelector('.rule-status');
            if (t && s && e && /^\d{2}-\d{2}$/.test(s) && /^\d{2}-\d{2}$/.test(e)) {
                cell.innerHTML = isInSeason(s, e)
                    ? '<span style="color:#4CAF50;">✔ In Season</span>'
                    : '<span style="color:#aaa;">✘ Out of Season</span>';
            } else { cell.innerHTML = '—'; }
        }

        tagsInput.addEventListener('input', function() {
            updateStatus();
            showTagDropdown(tagsInput, tagsCell);
        });
        tagsInput.addEventListener('focus', function() {
            showTagDropdown(tagsInput, tagsCell);
        });

        tr.querySelector('.auto-dates-btn').addEventListener('click', function() {
            var firstTag = tagsInput.value.split(',')[0].trim();
            var dates = getSmartDates(firstTag);
            if (dates) {
                tr.querySelector('.rule-start').value = dates.start;
                tr.querySelector('.rule-end').value = dates.end;
                updateStatus();
            } else {
                alert('No smart dates for "' + escHtml(firstTag) + '". Supported: easter, thanksgiving, halloween, christmas, mothers day, fathers day, valentines, new year, summer, st patrick, hanukkah');
            }
        });

        tr.querySelector('.rule-start').addEventListener('input', updateStatus);
        tr.querySelector('.rule-end').addEventListener('input', updateStatus);

        tr.querySelector('.rule-enabled').addEventListener('change', function() {
            tr.style.opacity = this.checked ? '1' : '0.5';
        });

        tr.querySelector('.preview-btn').addEventListener('click', function() {
            var tags = tagsInput.value.split(',').map(function(t){ return t.trim(); }).filter(Boolean);
            var s = tr.querySelector('.rule-start').value.trim();
            var e = tr.querySelector('.rule-end').value.trim();
            var desc = tr.querySelector('.rule-description').value.trim();
            if (!tags.length || !s || !e) { alert('Fill in tags and dates first.'); return; }
            var inSeason = isInSeason(s, e);
            ApiClient.ajax({
                type: 'GET',
                url: ApiClient.getUrl('Items', { Tags: tags, Recursive: true, IncludeItemTypes: 'Movie,Series', Fields: 'Name' })
            }).then(function(result) {
                var items = result.Items || [];
                var msg = '"' + escHtml(desc || tags.join(', ')) + '" — ' + (inSeason ? '✔ IN season' : '✘ OUT of season') + '\n\n';
                msg += items.length + ' item(s) affected:\n';
                items.slice(0, 20).forEach(function(item) { msg += '  • ' + item.Name + '\n'; });
                if (items.length > 20) msg += '  ...and ' + (items.length - 20) + ' more';
                alert(msg);
            }).catch(function() { alert('Could not fetch preview.'); });
        });

        tr.querySelector('.delete-btn').addEventListener('click', function() { tr.remove(); });
        tbody.appendChild(tr);
    }

    function loadConfig() {
        fetchLibraryTags();
        ApiClient.getPluginConfiguration(pluginId).then(function(config) {
            document.getElementById('rulesBody').innerHTML = '';
            (config.Rules || []).forEach(function(r) {
                try {
                    var tags = r.Tags && r.Tags.length ? r.Tags : (r.Tag ? [r.Tag] : []);
                    addRuleRow(r.Id, tags, r.Description, r.StartDate, r.EndDate, r.Enabled !== false);
                } catch(err) {
                    console.error('SeasonalVisibility: addRuleRow failed', err, r);
                }
            });
            renderActivityLog(config.ActivityLog || []);
        }).catch(function(err) {
            console.error('SeasonalVisibility: loadConfig failed', err);
        });
    }

    function renderActivityLog(log) {
        var container = document.getElementById('activityLog');
        if (!container) return;
        if (!log.length) { container.innerHTML = '<p style="color:#888;font-size:0.9em;">No activity yet.</p>'; return; }
        var html = '<table style="width:100%;border-collapse:collapse;font-size:0.85em;">' +
            '<thead><tr>' +
            '<th style="text-align:left;padding:4px 8px;color:#aaa;border-bottom:1px solid #333;">Time</th>' +
            '<th style="text-align:left;padding:4px 8px;color:#aaa;border-bottom:1px solid #333;">Tag</th>' +
            '<th style="text-align:left;padding:4px 8px;color:#aaa;border-bottom:1px solid #333;">Action</th>' +
            '<th style="text-align:left;padding:4px 8px;color:#aaa;border-bottom:1px solid #333;">User</th>' +
            '</tr></thead><tbody>';
        log.slice().reverse().slice(0, 50).forEach(function(entry) {
            var ts = new Date(entry.Timestamp).toLocaleString();
            var color = entry.Action === 'unblocked' ? '#4CAF50' : '#e05353';
            html += '<tr>' +
                '<td style="padding:3px 8px;color:#888;">' + escHtml(ts) + '</td>' +
                '<td style="padding:3px 8px;color:#ddd;">' + escHtml(entry.Tag) + '</td>' +
                '<td style="padding:3px 8px;color:' + color + ';">' + escHtml(entry.Action) + '</td>' +
                '<td style="padding:3px 8px;color:#ddd;">' + escHtml(entry.Username) + '</td>' +
                '</tr>';
        });
        html += '</tbody></table>';
        container.innerHTML = html;
    }

    function triggerTask() {
        ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('ScheduledTasks') }).then(function(tasks) {
            var taskList = Array.isArray(tasks) ? tasks : (tasks.Items || []);
            var task = taskList.find(function(t) { return t.Name === 'Apply Seasonal Visibility'; });
            if (task) ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('ScheduledTasks/Running/' + task.Id) });
        });
    }

    function saveConfig() {
        var rules = [];
        var tagMap = {};
        var hasConflict = false;

        document.querySelectorAll('#rulesBody tr[data-rule-id]').forEach(function(tr) {
            var tagsRaw = tr.querySelector('.rule-tags').value.trim();
            var tags = tagsRaw.split(',').map(function(t){ return t.trim(); }).filter(Boolean);
            if (!tags.length) return;

            var enabled = tr.querySelector('.rule-enabled').checked;
            if (enabled) {
                tags.forEach(function(tag) {
                    var lower = tag.toLowerCase();
                    tagMap[lower] = (tagMap[lower] || 0) + 1;
                    if (tagMap[lower] > 1) hasConflict = true;
                });
            }

            rules.push({
                Id: tr.dataset.ruleId,
                Tags: tags,
                Description: tr.querySelector('.rule-description').value.trim(),
                StartDate: tr.querySelector('.rule-start').value.trim(),
                EndDate: tr.querySelector('.rule-end').value.trim(),
                Enabled: enabled,
                CollectionIds: []
            });
        });

        if (hasConflict) {
            var conflictTags = Object.keys(tagMap).filter(function(t){ return tagMap[t] > 1; });
            if (!confirm('⚠️ Conflict detected: tag(s) "' + conflictTags.join(', ') + '" appear in multiple enabled rules. Save anyway?')) return;
        }

        ApiClient.getPluginConfiguration(pluginId).then(function(config) {
            config.Rules = rules;
            ApiClient.updatePluginConfiguration(pluginId, config).then(function() {
                triggerTask();
                var status = document.getElementById('saveStatus');
                if (status) {
                    status.style.color = '#4CAF50';
                    status.textContent = '✔ Saved & applied.';
                    setTimeout(function() { status.textContent = ''; loadConfig(); }, 3000);
                }
            });
        });
    }

    function initPage() {
        var addBtn = document.getElementById('addRuleBtn');
        var saveBtn = document.getElementById('saveBtn');
        if (addBtn) addBtn.addEventListener('click', function() { addRuleRow('rule-' + Date.now(), [], '', '', '', true); });
        if (saveBtn) saveBtn.addEventListener('click', saveConfig);
        loadConfig();
    }

    var pageInitialized = false;

    function safeInitPage() {
        if (pageInitialized) return;
        pageInitialized = true;
        initPage();
    }

    document.addEventListener('viewshow', function(e) {
        var page = e.target;
        if (!page || page.id !== 'SeasonalVisibilityConfigPage') return;
        pageInitialized = false;
        safeInitPage();
    }, true);

    // Handle case where page is already loaded when script runs
    var alreadyLoaded = document.getElementById('SeasonalVisibilityConfigPage');
    if (alreadyLoaded && alreadyLoaded.classList.contains('mainAnimatedPage')) {
        safeInitPage();
    }
})();
""";

    public Plugin(IApplicationPaths appPaths, IXmlSerializer xmlSerializer, IUserManager userManager, ILogger<Plugin> logger)
        : base(appPaths, xmlSerializer)
    {
        Instance = this;
        _userManager = userManager;
        _logger = logger;

        // Seed default rules only on first install
        if (Configuration.Rules.Count == 0)
        {
            Configuration.Rules.Add(new SeasonRule { Tags = new System.Collections.Generic.List<string> { "christmas" }, Description = "Christmas Season", StartDate = "12-01", EndDate = "01-05" });
            Configuration.Rules.Add(new SeasonRule { Tags = new System.Collections.Generic.List<string> { "halloween" }, Description = "Halloween Season", StartDate = "10-01", EndDate = "10-31" });
            Configuration.Rules.Add(new SeasonRule { Tags = new System.Collections.Generic.List<string> { "thanksgiving" }, Description = "Thanksgiving Season", StartDate = "11-15", EndDate = "11-30" });
            Configuration.Rules.Add(new SeasonRule { Tags = new System.Collections.Generic.List<string> { "summer" }, Description = "Summer Season", StartDate = "06-01", EndDate = "08-31" });
            SaveConfiguration();
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
        var config = Configuration;
        var users = _userManager.Users.ToList();
        foreach (var user in users)
        {
            var userDto = _userManager.GetUserDto(user);
            if (userDto.Policy?.IsAdministrator == true) continue;
            var policy = userDto.Policy;
            if (policy?.BlockedTags == null) continue;
            var seasonalTags = config.Rules.SelectMany(r => r.Tags)
                .Concat(config.Rules.SelectMany(r => r.CollectionIds).Select(id => $"__sv_collection_{id}"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
