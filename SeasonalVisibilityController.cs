using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SeasonalVisibility;

[ApiController]
[Route("SeasonalVisibility")]
public class SeasonalVisibilityController : ControllerBase
{
    private readonly string _pluginId = "1bad62ca-f2cb-4962-9d1e-b5737da03bfd";

    [HttpGet("config")]
    [AllowAnonymous]
    public ContentResult GetConfigPage()
    {
        var html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <title>Seasonal Visibility Config</title>
    <style>
        body { font-family: sans-serif; background: #101010; color: #ddd; padding: 2em; }
        h2 { color: #fff; }
        table { width: 100%; border-collapse: collapse; margin-bottom: 1em; }
        th { text-align: left; padding: 6px 8px; color: #aaa; border-bottom: 1px solid #333; }
        td { padding: 4px 8px; }
        input { background: #222; color: #ddd; border: 1px solid #444; padding: 4px 8px; border-radius: 4px; }
        button { cursor: pointer; padding: 8px 16px; border-radius: 4px; border: none; }
        .btn-add { background: #00a4dc; color: #fff; margin-bottom: 1em; }
        .btn-save { background: #00a4dc; color: #fff; width: 100%; padding: 12px; font-size: 1em; margin-top: 1em; }
        .btn-delete { background: none; color: #e05353; padding: 2px 10px; }
        .status { margin-top: 1em; color: #4CAF50; }
        .in-season { color: #4CAF50; }
        .out-season { color: #888; }
        p.desc { color: #aaa; font-size: 0.9em; }
    </style>
</head>
<body>
    <h2>🎄 Seasonal Visibility</h2>
    <p class=""desc"">Configure which tags hide content outside their season. Changes are applied by running the scheduled task or automatically at midnight.</p>

    <h3>Season Rules</h3>
    <p class=""desc"">Use MM-DD format for dates. Cross-year ranges supported (e.g. 12-01 to 01-05).</p>

    <button class=""btn-add"" onclick=""addRow('','','','')"">+ Add Rule</button>

    <table>
        <thead>
            <tr>
                <th>Tag</th>
                <th>Description</th>
                <th>Start (MM-DD)</th>
                <th>End (MM-DD)</th>
                <th>In Season?</th>
                <th></th>
            </tr>
        </thead>
        <tbody id=""rulesBody""></tbody>
    </table>

    <button class=""btn-save"" onclick=""save()"">Save</button>
    <div class=""status"" id=""status""></div>

    <script>
        var API = window.location.origin;
        var PLUGIN_ID = '" + _pluginId + @"';

        function getToken() {
            try {
                var creds = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{}');
                var servers = creds.Servers || [];
                return servers.length ? servers[0].AccessToken : '';
            } catch(e) { return ''; }
        }

        function apiGet(url) {
            return fetch(API + url, { headers: { 'X-MediaBrowser-Token': getToken() } }).then(r => r.json());
        }

        function apiPost(url, data) {
            return fetch(API + url, {
                method: 'POST',
                headers: { 'X-MediaBrowser-Token': getToken(), 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
        }

        function isInSeason(s, e) {
            var today = new Date();
            var t = (today.getMonth()+1)*100 + today.getDate();
            var p = s.split('-'); var sv = parseInt(p[0])*100+parseInt(p[1]);
            p = e.split('-'); var ev = parseInt(p[0])*100+parseInt(p[1]);
            return sv > ev ? (t >= sv || t <= ev) : (t >= sv && t <= ev);
        }

        function statusBadge(tag, s, e) {
            if (!tag || !s || !e || !/^\d{2}-\d{2}$/.test(s) || !/^\d{2}-\d{2}$/.test(e)) return '—';
            return isInSeason(s, e)
                ? '<span class=""in-season"">✔ In Season</span>'
                : '<span class=""out-season"">✘ Out of Season</span>';
        }

        function addRow(tag, desc, start, end) {
            var tbody = document.getElementById('rulesBody');
            var tr = document.createElement('tr');
            tr.innerHTML =
                '<td><input class=""rule-tag"" value=""' + (tag||'') + '"" placeholder=""e.g. christmas"" style=""width:110px""></td>' +
                '<td><input class=""rule-desc"" value=""' + (desc||'') + '"" placeholder=""e.g. Christmas Season"" style=""width:150px""></td>' +
                '<td><input class=""rule-start"" value=""' + (start||'') + '"" placeholder=""12-01"" style=""width:70px"" maxlength=""5""></td>' +
                '<td><input class=""rule-end"" value=""' + (end||'') + '"" placeholder=""01-05"" style=""width:70px"" maxlength=""5""></td>' +
                '<td class=""rule-status"">' + statusBadge(tag,start,end) + '</td>' +
                '<td><button class=""btn-delete"" onclick=""this.closest(\'tr\').remove()"">✕</button></td>';

            function upd() {
                tr.querySelector('.rule-status').innerHTML = statusBadge(
                    tr.querySelector('.rule-tag').value.trim(),
                    tr.querySelector('.rule-start').value.trim(),
                    tr.querySelector('.rule-end').value.trim()
                );
            }
            tr.querySelector('.rule-start').addEventListener('input', upd);
            tr.querySelector('.rule-end').addEventListener('input', upd);
            tr.querySelector('.rule-tag').addEventListener('input', upd);
            tbody.appendChild(tr);
        }

        function load() {
            apiGet('/Plugins/' + PLUGIN_ID + '/Configuration').then(function(config) {
                document.getElementById('rulesBody').innerHTML = '';
                (config.Rules || []).forEach(function(r) {
                    addRow(r.Tag, r.Description, r.StartDate, r.EndDate);
                });
            });
        }

        function save() {
            var rules = [];
            document.querySelectorAll('#rulesBody tr').forEach(function(tr) {
                var tag = tr.querySelector('.rule-tag').value.trim();
                if (tag) rules.push({
                    Tag: tag,
                    Description: tr.querySelector('.rule-desc').value.trim(),
                    StartDate: tr.querySelector('.rule-start').value.trim(),
                    EndDate: tr.querySelector('.rule-end').value.trim()
                });
            });
            apiGet('/Plugins/' + PLUGIN_ID + '/Configuration').then(function(config) {
                config.Rules = rules;
                apiPost('/Plugins/' + PLUGIN_ID + '/Configuration', config).then(function() {
                    var s = document.getElementById('status');
                    s.textContent = '✔ Saved. Run ""Apply Seasonal Visibility"" task to apply immediately.';
                    setTimeout(function(){ s.textContent=''; }, 5000);
                });
            });
        }

        load();
    </script>
</body>
</html>";
        return new ContentResult { Content = html, ContentType = "text/html" };
    }
}
