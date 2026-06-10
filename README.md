# GameHelper

GameHelper is a Windows x64 .NET overlay for **Path of Exile 2** with a plugin architecture. The launcher (`GameHelper.exe`) checks for updates, starts the overlay (`GameHelper.App.exe`), reads data from the running game process, and loads plugins from the `Plugins` folder.

**Fork maintained by Lafko** ([MordWraith](https://github.com/MordWraith)) — based on the [GameHelper2](https://github.com/MordWraith/Gamehelper) ecosystem, adapted for friends.

## Repository vs releases

| Location | What you get |
|----------|----------------|
| **[GitHub Releases](https://github.com/MordWraith/Gamehelper/releases)** | Pre-built binaries (recommended for players) |
| **`main` branch (this repo)** | Source code when the maintainer has pushed it (`scripts/push-github-source.ps1`) |
| **In-app → Plugins** | Plugin list with **Author** column (upstream credits) |

This is **not** a source-only repository: releases are a **binary distribution**. The project can still be built from source when the code is on `main`. See [CREDITS.md](CREDITS.md) for authors and upstream projects.

## Download (players)

| What | Link |
|------|------|
| **Installer (recommended)** | https://github.com/MordWraith/Gamehelper/releases/latest/download/GameHelperDownloader.exe |
| Full ZIP (manual install) | https://github.com/MordWraith/Gamehelper/releases/latest/download/GameHelper-*-full.zip |
| All releases | https://github.com/MordWraith/Gamehelper/releases/latest |

Run `GameHelperDownloader.exe` in an **empty folder**. Requires [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

### Release assets (ZIP-only)

Each release contains a small set of files (~11 MB total):

| Release asset | Purpose |
|---------------|---------|
| `GameHelperDownloader.exe` | One-file fresh install |
| `GameHelper-*-full.zip` | Full install / auto-update package |
| `manifest.json` + `manifest.sig` | Signed update metadata and integrity |
| `changelog-history.json` | In-app changelog history |

The launcher and downloader fetch **one ZIP** per update instead of dozens of individual DLLs.

## Credits

- **In-app:** Menu → **Plugins** → see the **Author** column.
- **In repo:** [CREDITS.md](CREDITS.md)

Please report missing attributions via GitHub Issues.

## Build from source (developers)

### Required tools

- [Visual Studio](https://visualstudio.microsoft.com/downloads/) with **.NET desktop development**
- [.NET 10 SDK for Windows x64](https://dotnet.microsoft.com/download/dotnet/10.0)
- Clone or download this repository (after source is on `main`)

Open [`GameOverlay.sln`](GameOverlay.sln) — not a single `.csproj` only.

### Build

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
```

Output: `publish\` (runnable folder with `GameHelper.exe`).

### Publish source to GitHub (maintainer)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\push-github-source.ps1
```

Binaries are published separately:

```powershell
powershell -ExecutionPolicy Bypass -File rebuild-and-publish.ps1
```

## Project layout

| Path | Role |
|------|------|
| `GameHelper/` | Overlay (`GameHelper.App.exe`) |
| `Launcher/` | Launcher & updater (`GameHelper.exe`) |
| `Downloader/` | Standalone installer EXE |
| `GameOffsets/` | Game structure offsets |
| `Plugins/` | Atlas, Radar, AutoPot, … |
| `scripts/` | Build & publish automation |
| `CREDITS.md` | Attribution list |

Target: `net10.0-windows`, `win-x64`.

## Solution projects

- `GameHelper` — main overlay
- `Launcher` — updater entry point
- `Downloader` — public installer
- `GameOffsets` — offsets
- Plugins: `Atlas`, `AutoPot`, `AutoHotKeyTrigger`, `HealthBars`, `PreloadAlert`, `Radar`, `RitualHelper`, `RuneforgeHelper`

## Run

1. Open `publish\` or `GameHelper\bin\Release\net10.0-windows\win-x64\`
2. Start **`GameHelper.exe`** (not `GameHelper.App.exe` directly)
3. Run with the same privilege level as the game (admin if PoE is admin)

## Runtime data (not in git)

```
configs\core_settings.json
configs\plugins.json
Plugins\<Name>\config\
```

## Troubleshooting

**`net10.0-windows` not supported** — Install .NET 10 SDK and update Visual Studio.

**Plugins missing after build** — Use **Rebuild Solution**, not Build Project.

**Update pulled wrong version** — Install into an **empty** folder; check `VERSION.txt`.

**Overlay does not attach** — Match admin elevation with the game.

## Links

- [Visual Studio](https://visualstudio.microsoft.com/downloads/)
- [.NET 10 downloads](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Upstream GameHelper2](https://github.com/MordWraith/Gamehelper)
