# 🎄 Seasonal Visibility

Automatically hide and reveal movies & TV shows in Jellyfin based on real-world seasons and holidays.

Tag your Christmas movies with `christmas`, Halloween films with `halloween`, and they'll disappear from non-admin users outside of that season — and reappear automatically when the season arrives.

---

## ✨ Features

- Automatic seasonal hiding and revealing
- Instant updates when tags are added or removed
- Cross-year season support (e.g. Dec 1 → Jan 5)
- TV show support
- Admin bypass — admins always see everything
- Clean uninstall — all blocked tags removed automatically
- Interactive config UI with live In Season / Out of Season status
- Smart holiday date auto-fill for Easter, Thanksgiving, and more
- Auto-apply on save
- Auto-registers config script with JavaScript Injector — no manual setup required

---

## 📦 Installation

### Step 1 — Install dependencies

**File Transformation** (required by JavaScript Injector):
1. Go to **Dashboard → Plugins → Repositories** and add:
```
https://www.iamparadox.dev/jellyfin/plugins/manifest.json
```
2. Install **File Transformation** from the Catalog and restart Jellyfin.

**JavaScript Injector** (required for config UI):
1. Go to **Dashboard → Plugins → Repositories** and add:
```
https://raw.githubusercontent.com/n00bcodr/jellyfin-plugins/main/10.11/manifest.json
```
2. Install **JavaScript Injector** from the Catalog and restart Jellyfin.

---

### Step 2 — Install Seasonal Visibility

1. Go to **Dashboard → Plugins → Repositories** and add:
```
https://raw.githubusercontent.com/jshafer813/Seasonal-Visibility/main/manifest.json
```
2. Install **Seasonal Visibility** from the Catalog and restart Jellyfin.
3. The config UI script is automatically registered with JavaScript Injector — no manual setup needed.
4. Navigate to **Dashboard → Plugins → Seasonal Visibility** to manage your rules.

---

## 🏷️ How It Works

Tag any movie or TV show in Jellyfin with a season tag (e.g. `christmas`, `halloween`). The plugin will automatically hide it from non-admin users outside of that season and reveal it when the season arrives.

Dates can be entered manually in MM-DD format, or use the 📅 button to auto-fill the correct dates for the current year.

**Supported smart date holidays:** easter, thanksgiving, halloween, christmas, mothers day, fathers day, valentines, new year, summer, st patrick, hanukkah

---

## ⚙️ Requirements

- Jellyfin 10.11.6+
- [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) by IAmParadox27
- [JavaScript Injector](https://github.com/n00bcodr/Jellyfin-JavaScript-Injector) by n00bcodr

---

## 📜 Changelog

- **v3.0.0** — Multiple tags per rule, enable/disable toggle, conflict detection, preview mode, tag suggestions dropdown, activity log, collections support, security fixes
- **v2.2.0** — Security fixes: XSS protection, date validation, async exception handling
- **v2.1.0** — Auto-registers config script with JavaScript Injector on install
- **v2.0.0** — Interactive config UI, smart holiday date auto-fill, auto-apply on save
- **v1.0.5** — TV show support, logging, GitHub Actions automated releases
- **v1.0.4** — Unblock on uninstall, checksum verification
- **v1.0.3** — Cross-year season support
- **v1.0.2** — Live library listener for instant visibility changes
- **v1.0.1** — Admin users skipped
- **v1.0.0** — Initial release

---

> ⚠️ **Disclaimer:** This plugin was developed with the some assistance of AI
