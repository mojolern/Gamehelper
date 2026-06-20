namespace GameHelper.Plugin

{

    using System;

    using System.Collections.Generic;



    /// <summary>Upstream authors and fork maintenance credits for bundled plugins.</summary>

    internal static class PluginCredits

    {

        private const string UnknownAuthor = "upstream (unknown)";

        private const string ForkMaintainer = "MordWraith";

        private const string ForkBasis = "Lafko / Gordin";



        private static readonly Dictionary<string, PluginCreditInfo> Credits = new(StringComparer.OrdinalIgnoreCase)

        {

            ["Atlas"] = new("Nekkoy", "yokkenUA/Atlas v0.1.3: chevron routes, hide available maps, expedition targets, content icons."),

            ["Radar"] = new("Gordin", "GameHelper2"),

            ["RitualHelper"] = new("caio", "AutoRitualPricer lineage"),

            ["RuneforgeHelper"] = new("Nekkoy", "GameHelper2 plugin ecosystem"),

            ["SekhemaHelper"] = new("Nekkoy", "Sekhema Trial path helper"),

            ["AutoPot"] = new("MordWraith", "written for this fork"),

            ["Autopot"] = new("MordWraith", "written for this fork"),

            ["AutoHotKeyTrigger"] = new("GameHelper2 upstream", "KronosDesign / community"),

            ["HealthBars"] = new("GameHelper2 upstream", "KronosDesign / community"),

            ["SimpleBars"] = new("Reynbow", "Reynbow/simplebars fork"),

            ["PreloadAlert"] = new("GameHelper2 upstream", "ExileAPI PreloadAlert concept"),

            ["AuraTracker"] = new("Skrip", "derekShaheen/AuraTracker"),

            ["MapKillCounter"] = new("MordWraith", "written for this fork"),

            ["AmanamuVoidAlert"] = new("1k4ru5g3", "POEFixer AmanamuVoidAlertPlugin port"),

            ["PlayerBuffBar"] = new("MordWraith", "written for this fork"),

            ["Hiveblood"] = new("MordWraith", "Genesis Tree Hiveblood tracker with inventory overlay (PoE2)."),

            ["FarmTracker"] = new("Senbry", "ported by MordWraith — farm session tracker (loot, maps, kills, div/h)"),

        };



        internal static string ForkByLine => $"Fork maintained by {ForkMaintainer} (basis: {ForkBasis})";



        internal static string GetOriginalAuthor(string pluginName) =>

            Credits.TryGetValue(pluginName, out var credit) ? credit.Author : UnknownAuthor;



        internal static string GetUpstreamNote(string pluginName) =>

            Credits.TryGetValue(pluginName, out var credit) ? credit.Notes : string.Empty;



        private readonly record struct PluginCreditInfo(string Author, string Notes);

    }

}


