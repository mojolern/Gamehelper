namespace FarmTracker
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;

    internal static class FarmCustomPrices
    {
        private static readonly object Gate = new();
        private static Dictionary<string, double> pricesDivine = new(StringComparer.OrdinalIgnoreCase);
        private static DateTime lastLoadUtc = DateTime.MinValue;

        public static int CustomPriceCount
        {
            get
            {
                lock (Gate)
                {
                    return pricesDivine.Count;
                }
            }
        }

        public static void ReloadIfNeeded(string pluginDir)
        {
            var path = Path.Combine(pluginDir, "custom_prices.txt");
            var writeUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            lock (Gate)
            {
                if (writeUtc <= lastLoadUtc)
                {
                    return;
                }

                pricesDivine = Load(path);
                lastLoadUtc = writeUtc == DateTime.MinValue ? DateTime.UtcNow : writeUtc;
            }
        }

        public static void ForceReload(string pluginDir)
        {
            var path = Path.Combine(pluginDir, "custom_prices.txt");
            lock (Gate)
            {
                pricesDivine = Load(path);
                lastLoadUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.UtcNow;
            }
        }

        public static bool TryGetDivine(string displayName, out double divine)
        {
            lock (Gate)
            {
                return pricesDivine.TryGetValue(displayName.Trim(), out divine) && divine > 0;
            }
        }

        private static Dictionary<string, double> Load(string path)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
            {
                return result;
            }

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                var name = line[..eq].Trim();
                var valueText = line[(eq + 1)..].Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                if (double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var divine) && divine > 0)
                {
                    result[name] = divine;
                }
            }

            return result;
        }
    }
}
