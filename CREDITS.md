# Credits

GameHelper is an **open-source fork** for Path of Exile 2, based on the [GameHelper2](https://github.com/Gordin/GameHelper2) ecosystem.

| What | Where |
|------|--------|
| **Source code** | https://github.com/MordWraith/Gamehelper (`main` branch) |
| **Pre-built releases** | https://github.com/MordWraith/Gamehelper/releases |
| **Trust / auto-update** | [SECURITY.md](SECURITY.md) |

**Maintainer:** [MordWraith](https://github.com/MordWraith)

If someone is missing from this list, please open an issue or contact the maintainer.

## In the application

Open **Plugins** in the GameHelper menu (top bar). The plugin table shows each plugin name and **Author** (original / upstream credit).

## Core & launcher

| Component | Credit | Notes |
|-----------|--------|-------|
| Fork basis | Lafko / Gordin | Starting point for this project (GameHelper2 lineage) |
| GameHelper2 base | [Gordin](https://github.com/Gordin/GameHelper2) and community | Overlay architecture, offsets, plugin host |
| GameHelper (original) | GameHelper (OwnedCore) | Early PoE helper lineage |
| Community maintainers | KronosDesign, arsenic2k, abevol, mm3141, others | GameHelper2 contributions |
| This fork | MordWraith | PoE2 updates, launcher, signed auto-update, plugins, publish pipeline |

## Bundled plugins

| Plugin | Author / upstream | Notes |
|--------|-------------------|-------|
| Atlas | Nekkoy / [yokkenUA](https://github.com/yokkenUA/Atlas) | GameHelper2 plugin ecosystem |
| Radar | Gordin | GameHelper2 |
| RitualHelper | caio | Based on AutoRitualPricer ([Queuete/GameHelper](https://github.com/Queuete/GameHelper) lineage) |
| RuneforgeHelper | Nekkoy / [yokkenUA](https://github.com/yokkenUA/RunecraftHelper) | Runeshape rewards overlay |
| SekhemaHelper | Nekkoy / [yokkenUA](https://github.com/yokkenUA/SekhemaHelper) | Sekhema Trial option helper |
| AutoPot | MordWraith | Written for this fork |
| AutoHotKeyTrigger | GameHelper2 upstream | Bundled with GameHelper2 |
| HealthBars | GameHelper2 upstream | Bundled with GameHelper2 |
| PreloadAlert | GameHelper2 upstream | Concept from [TehCheat/PreloadAlert](https://github.com/TehCheat/PreloadAlert) |
| AuraTracker | Skrip / [derekShaheen](https://github.com/derekShaheen/AuraTracker) | Nearby enemy list with HP/ES, buffs, DPS |
| MapKillCounter | MordWraith | Per-map monster kill counts |
| AmanamuVoidAlert | [1k4ru5g3](https://github.com/1k4ru5g3/AmanamuVoidAlertPlugin) | Abyss / Amanamu void cloud tracker |
| PlayerBuffBar | MordWraith | Player buff watchlist, charges, rage |

## Third-party libraries

Includes (non-exhaustive): ImGui.NET, ClickableTransparentOverlay, Newtonsoft.Json, Coroutine, GameOffsets, AsmResolver, Vortice, SixLabors.ImageSharp, and NuGet dependencies listed in each project's `.csproj`.

## License

Upstream GameHelper2 is GPLv3. This fork is distributed under the same license — see [LICENSE](LICENSE).
