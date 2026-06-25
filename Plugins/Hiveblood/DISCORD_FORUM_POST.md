# Discord forum — first post (copy into `#plugins`)

**Title:** `Hiveblood`

**Tags:** `community`

---

## Paragraph post (same style as FarmTracker thread opener)

```
https://github.com/MordWraith/Hiveblood

GameHelper2 plugin that tracks Hiveblood in Path of Exile 2: syncs your total from the Genesis Tree UI and adds +N gains from Breach run popups. Configurable overlay (inventory anchor or draggable position dummy), optional session-gain line, and a cap warning (orange blink near 100k — also visible when inventory is closed). Visit the Genesis Tree once to calibrate.

Written by MordWraith for GameHelper2 — read-only overlay, no input automation.

Build from source only (no prebuilt DLL): clone into your GameHelper2 tree as Plugins/Hiveblood, then dotnet build GameHelper and the plugin. Enable it under GameHelper -> Plugins. Also bundled in my Gamehelper releases.

Compatible with Gordin GameHelper2 main (.NET 10, net10.0-windows x64) and community forks on the same plugin API / target.

Feedback and feature requests: this thread. Crashes or GameHelper install issues: help with tag plugin.

Community third-party plugin — not affiliated with Grinding Gear Games. Use at your own risk; only build from the linked source.
```

---

## Forum template (structured)

**Author:** MordWraith  
**Source:** https://github.com/MordWraith/Hiveblood  
**Also in:** [MordWraith/Gamehelper](https://github.com/MordWraith/Gamehelper)  
### What it does

- Syncs Hiveblood total from **Genesis Tree** UI
- Adds **+N** from Breach popup text between tree visits
- Overlay at inventory (or fixed position via drag dummy)
- **Cap warning** — blinking orange text from configurable threshold (default 95k)

### Install

**Option A:** [Gamehelper releases](https://github.com/MordWraith/Gamehelper) → enable Hiveblood  
**Option B:** clone into `Plugins/Hiveblood`, build GameHelper + plugin (see README)

### v1.0.0

- Initial release: tree sync, breach popups, cap warning, position dummy

### Disclaimer

Community third-party plugin. Use at your own risk.
