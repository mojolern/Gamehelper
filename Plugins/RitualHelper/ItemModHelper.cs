using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;

namespace RitualHelper
{
    internal static class ItemModHelper
    {
        public static List<string> GetModLines(Item item)
        {
            var lines = new List<string>();
            if (item == null) return lines;

            if (item.TryGetComponent<Mods>(out var modsComponent))
            {
                AddModGroup(lines, modsComponent.ImplicitMods);
                AddModGroup(lines, modsComponent.ExplicitMods);
                AddModGroup(lines, modsComponent.EnchantMods);
            }

            if (item.TryGetComponent<ObjectMagicProperties>(out var magicProps))
                AddModGroup(lines, magicProps.Mods);

            return lines;
        }

        public static Item? ReadFreshItem(IntPtr itemAddress)
        {
            if (itemAddress == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Activator.CreateInstance(
                    typeof(Item),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new object[] { itemAddress },
                    null) as Item;
            }
            catch
            {
                return null;
            }
        }

        public static List<string> MergeModLines(IReadOnlyList<string> memoryMods, IReadOnlyList<string> clipboardMods)
        {
            var merged = new List<string>();
            if (clipboardMods != null)
            {
                foreach (var mod in clipboardMods)
                {
                    if (!string.IsNullOrWhiteSpace(mod))
                        merged.Add(mod);
                }
            }

            if (memoryMods == null) return merged;

            foreach (var mod in memoryMods)
            {
                if (string.IsNullOrWhiteSpace(mod)) continue;
                if (merged.Exists(existing => string.Equals(existing, mod, StringComparison.OrdinalIgnoreCase)))
                    continue;
                merged.Add(mod);
            }

            return merged;
        }

        private static void AddModGroup(List<string> lines, List<(string name, (float value0, float value1) values)> mods)
        {
            foreach (var (name, values) in mods)
            {
                var formatted = FormatModLine(name, values);
                if (!string.IsNullOrWhiteSpace(formatted))
                    lines.Add(formatted);
            }
        }

        private static string FormatModLine(string template, (float value0, float value1) values)
        {
            if (string.IsNullOrWhiteSpace(template)) return string.Empty;

            var line = template;
            if (!float.IsNaN(values.value0))
            {
                line = line.Replace("{0}", FormatNumber(values.value0), StringComparison.Ordinal);
                if (!float.IsNaN(values.value1))
                    line = line.Replace("{1}", FormatNumber(values.value1), StringComparison.Ordinal);
            }

            return line.Trim();
        }

        private static string FormatNumber(float value)
        {
            if (Math.Abs(value - MathF.Round(value)) < 0.001f)
                return ((int)MathF.Round(value)).ToString(CultureInfo.InvariantCulture);
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
