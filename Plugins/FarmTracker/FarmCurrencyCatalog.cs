namespace FarmTracker
{
    using System;
    using System.Collections.Generic;

    /// <summary>Maps PoE2 currency metadata basenames to scout/ninja display names.</summary>
    internal static class FarmCurrencyCatalog
    {
        private static readonly Dictionary<string, string> BasenameToItemName = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CurrencyModValues"] = "Divine Orb",
            ["CurrencyAddModToRare"] = "Exalted Orb",
            ["CurrencyAddModToRare2"] = "Greater Exalted Orb",
            ["CurrencyAddModToRare3"] = "Perfect Exalted Orb",
            ["CurrencyRerollRare"] = "Chaos Orb",
            ["CurrencyRerollRare2"] = "Greater Chaos Orb",
            ["CurrencyRerollRare3"] = "Perfect Chaos Orb",
            ["CurrencyDuplicate"] = "Mirror of Kalandra",
            ["CurrencyUpgradeToRare"] = "Orb of Alchemy",
            ["CurrencyUpgradeToMagic"] = "Orb of Transmutation",
            ["CurrencyUpgradeMagicToRare"] = "Regal Orb",
            ["CurrencyAddModToMagic"] = "Orb of Augmentation",
            ["CurrencyPortal"] = "Portal Scroll",
            ["CurrencyIdentification"] = "Scroll of Wisdom",
        };

        internal static bool TryResolveItemName(string basename, out string itemName)
        {
            itemName = string.Empty;
            if (string.IsNullOrWhiteSpace(basename))
            {
                return false;
            }

            if (BasenameToItemName.TryGetValue(basename, out var direct))
            {
                itemName = direct;
                return true;
            }

            // Leveled currency families: CurrencyRerollRare2 → try stem match + scout tier name via metaArt-style stem
            int s = basename.Length;
            while (s > 0 && basename[s - 1] >= '0' && basename[s - 1] <= '9')
            {
                s--;
            }

            if (s > 0 && s < basename.Length && BasenameToItemName.TryGetValue(basename[..s], out var stemName))
            {
                itemName = stemName;
                return true;
            }

            return false;
        }

        /// <summary>Fallback when API lookup fails — base currencies always have a known value.</summary>
        internal static bool TryGetBuiltinDivineValue(string basename, out double divine)
        {
            divine = 0;
            if (string.IsNullOrWhiteSpace(basename))
            {
                return false;
            }

            if (basename.Equals("CurrencyModValues", StringComparison.OrdinalIgnoreCase))
            {
                divine = 1.0;
                return true;
            }

            var chaosPerDiv = FarmPriceFetcher.GetChaosPerDivine();
            if (basename.Equals("CurrencyRerollRare", StringComparison.OrdinalIgnoreCase) && chaosPerDiv > 0)
            {
                divine = 1.0 / chaosPerDiv;
                return divine > 0;
            }

            if (basename.Equals("CurrencyAddModToRare", StringComparison.OrdinalIgnoreCase))
            {
                var rate = FarmPriceFetcher.DivineToExaltedRate;
                if (rate > 0)
                {
                    divine = 1.0 / rate;
                    return divine > 0;
                }
            }

            return false;
        }
    }
}
