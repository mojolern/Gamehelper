# AutoRitualPricer (RitualHelper)

AutoRitualPricer is a plugin for [GameHelper](https://github.com/Queuete/GameHelper) that automatically checks the prices of items inside the Path of Exile 2 Ritual UI using real-time data from **poe.ninja**.

## Features
- 💸 **Live Pricing**: Fetches item prices from `poe.ninja` / `poe2scout` in the background (Ritual currencies, Unique Armours, Accessories, and Charms).
- 🏷️ **Automatic Naming**: Item names are read directly from game memory — base types via the `Base` component, uniques via their icon art. No hover or copy needed; every recognized item is priced automatically.
- 🔎 **Pricing Diagnostics** (Advanced tab): label every tile with its rarity, the name read from memory, and the internal id, flagging anything that produced no price.

## Installation
1. Ensure you have [GameHelper](https://github.com/Queuete/GameHelper) installed.
2. Download or clone this repository into the `Plugins` folder of your GameHelper installation:
   `GameHelper/Plugins/RitualHelper/`
3. Compile the plugin using `dotnet build` or allow GameHelper to auto-compile it on startup.
4. Launch the game and GameHelper. The plugin will silently download the latest poe.ninja prices.

## How to Use
1. Open a Ritual Window in Path of Exile 2.
2. The plugin reads each reward directly from memory and shows its value (e.g., `1.5 Ex`, `0.5 Div`) on the tile.
3. Items the price source doesn't list show no label. Turn on **Diagnose Pricing** (Advanced tab) to see what name each tile resolved to.

## Requirements
- Path of Exile 2
- GameHelper
- .NET 10.0 SDK (for compilation)

## Note
This plugin currently uses Exalted Orbs (Ex) and Divine Orbs (Div) as standard currencies for pricing based on poe.ninja's PoE 2 economy data.
