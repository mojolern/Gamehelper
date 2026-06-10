# Credits

GameHelper is a **private fork** for friends, based on the [GameHelper2](https://github.com/MordWraith/Gamehelper) ecosystem (Path of Exile 2 overlay). This repository publishes **pre-built binaries** on [GitHub Releases](https://github.com/MordWraith/Gamehelper/releases). The full source tree lives in the maintainer's working copy and can be pushed to the `main` branch with `scripts/push-github-source.ps1`.

**Fork maintained by:** Lafko / [MordWraith](https://github.com/MordWraith)

If someone is missing from this list, please open an issue or contact the maintainer.

## In the application

Open **Plugins** in the GameHelper menu (top bar). The plugin table shows each plugin name and **Author** (original / upstream credit).

## Core & launcher

| Component | Credit | Notes |
|-----------|--------|-------|
| GameHelper2 base | [Gordin](https://github.com/MordWraith/Gamehelper) and community | Overlay architecture, offsets, plugin host |
| GameHelper (original) | GameHelper (OwnedCore) | Early PoE helper lineage |
| Community maintainers | KronosDesign, arsenic2k, abevol, mm3141, others | GameHelper2 contributions |
| This fork | Lafko / MordWraith | PoE2 updates, launcher, auto-update, plugins, publish pipeline |

## Bundled plugins

| Plugin | Author / upstream | Notes |
|--------|-------------------|-------|
| Atlas | Nekkoy | GameHelper2 plugin ecosystem |
| Radar | Gordin | GameHelper2 |
| RitualHelper | caio | Based on AutoRitualPricer ([Queuete/GameHelper](https://github.com/MordWraith/Gamehelper) lineage) |
| RuneforgeHelper | Nekkoy / [yokkenUA](https://github.com/MordWraith/Gamehelper) | Runeshape rewards overlay; based on RunecraftHelper |
| SekhemaHelper | Nekkoy | Sekhema Trial option helper; [yokkenUA/SekhemaHelper](https://github.com/MordWraith/Gamehelper) |
| AutoPot | MordWraith | Written for this fork |
| AutoHotKeyTrigger | GameHelper2 upstream | Bundled with GameHelper2 (KronosDesign / community) |
| HealthBars | GameHelper2 upstream | Bundled with GameHelper2 |
| PreloadAlert | GameHelper2 upstream | PoE preload alerts; concept from [TehCheat/PreloadAlert](https://github.com/MordWraith/Gamehelper) (ExileAPI) |

## Third-party libraries

Includes (non-exhaustive): ImGui.NET, ClickableTransparentOverlay, Newtonsoft.Json, Coroutine, GameOffsets, AsmResolver, Vortice, SixLabors.ImageSharp, and NuGet dependencies listed in each project's `.csproj`.

## License

Upstream GameHelper2 is GPLv3. This fork follows the same obligations where applicable. See `LICENSE` when present in the source tree.
