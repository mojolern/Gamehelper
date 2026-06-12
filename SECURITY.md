# Security & trust

This document explains how installs and updates work, so you can decide whether to use the pre-built releases or build from source.

## Distribution model

| Channel | What you get |
|---------|----------------|
| [GitHub `main`](https://github.com/MordWraith/Gamehelper/tree/main) | Full source (clone or download ZIP) |
| [GitHub Releases](https://github.com/MordWraith/Gamehelper/releases) | Pre-built binaries (`GameHelperDownloader.exe`, full ZIP) |
| In-app auto-update | Same signed release ZIP as on GitHub Releases |

You do **not** have to use auto-update. Manual install from the full ZIP or a self-built `publish\` folder works the same.

## Auto-update (what it does)

1. The launcher downloads **`manifest.json`** and **`manifest.sig`** from GitHub Releases.
2. The manifest signature is checked with an **RSA public key embedded in the launcher** (`UpdateManifestVerifier`).
3. Each file in the manifest has a **SHA256** hash; mismatches abort the update.
4. The update is delivered as **one ZIP** (not arbitrary DLL URLs).
5. User data is **not** shipped in the update package:
   - `configs/` (core settings, `plugins.json`)
   - `Plugins/*/config/` (per-plugin settings)

You still trust the **maintainer** who signs releases — the same trust model as any overlay or game tool that is not independently audited.

## Build from source (maximum control)

```powershell
git clone https://github.com/MordWraith/Gamehelper.git
cd Gamehelper
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
```

Run `publish\GameHelper.exe`. No downloader or auto-update required.

## Reporting issues

Security or trust concerns: [GitHub Issues](https://github.com/MordWraith/Gamehelper/issues) (no DMs required).

Please include version (`VERSION.txt`), install method (downloader / ZIP / self-built), and whether auto-update was used.
