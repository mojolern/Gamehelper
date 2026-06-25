# RitualHelper

Community plugin for [GameHelper2](https://github.com/Gordin/GameHelper2) (Path of Exile 2).

Ritual reward **live pricing** from poe.ninja in the Ritual UI. Port maintained by **MordWraith** (original concept: **caio** / AutoRitualPricer). Build from source.

Bundled in [MordWraith/Gamehelper-Core](https://github.com/MordWraith/Gamehelper-Core).

## Features

- Live prices from poe.ninja / poe2scout (Ritual currencies, uniques, charms)
- Item names from game memory (base + unique via icon art)
- Pricing diagnostics overlay (Advanced tab)
- DE/EN settings via GameHelper localization

## Requirements

- GameHelper2 or Gamehelper-Core source tree
- **.NET 10 SDK** (`net10.0-windows`, x64)

## Build & install

```bash
git clone https://github.com/Gordin/GameHelper2.git
cd GameHelper2
git clone https://github.com/MordWraith/RitualHelper.git Plugins/RitualHelper
dotnet build Plugins/RitualHelper/RitualHelper.csproj -c Release
```

Enable **RitualHelper** in GameHelper -> Plugins.

## Config

| Path | Purpose |
|------|---------|
| `config/settings.txt` | JSON plugin settings |
| `item_names.json` | Item id to display name bridge (shipped with plugin) |

## Credits

- **caio** — AutoRitualPricer original
- **MordWraith** — Gamehelper-Core port and maintenance
