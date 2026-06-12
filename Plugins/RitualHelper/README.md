# AutoRitualPricer (RitualHelper)

AutoRitualPricer is a plugin for [GameHelper](https://github.com/Queuete/GameHelper) that automatically checks the prices of items inside the Path of Exile 2 Ritual UI using real-time data from **poe.ninja**.

## Features
- 💸 **Live Pricing**: Fetches item prices from `poe.ninja` in the background (Ritual currencies, Unique Armours, Accessories, and Charms).
- 🟩 **Green/Red Indicators**: Automatically draws a bounding box over items in the Ritual window.
  - **Red Box**: The item's internal name has not been mapped yet.
  - **Green Box**: The item is mapped and the price is displayed.
- 📋 **Auto-Mapping (Ctrl+C)**: Hover over a red item in-game and press `Ctrl+C`. The plugin will instantly read the clipboard, map the internal name to the English unique/currency name, and update the box to green with the item's price.

## Installation
1. Ensure you have [GameHelper](https://github.com/Queuete/GameHelper) installed.
2. Download or clone this repository into the `Plugins` folder of your GameHelper installation:
   `GameHelper/Plugins/RitualHelper/`
3. Compile the plugin using `dotnet build` or allow GameHelper to auto-compile it on startup.
4. Launch the game and GameHelper. The plugin will silently download the latest poe.ninja prices.

## How to Use
1. Open a Ritual Window in Path of Exile 2.
2. The plugin will draw boxes over the items.
3. If a box is **Red**, hover your mouse over the item in-game and press `Ctrl + C`. The plugin will read the item name and bind it forever.
4. The box will turn **Green** and the value (e.g., `1.5 Ex`, `0.5 Div`) will appear in the bottom right corner of the item!

## Requirements
- Path of Exile 2
- GameHelper
- .NET 10.0 SDK (for compilation)

## Note
This plugin currently uses Exalted Orbs (Ex) and Divine Orbs (Div) as standard currencies for pricing based on poe.ninja's PoE 2 economy data.
