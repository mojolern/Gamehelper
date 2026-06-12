namespace Launcher
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;

    /// <summary>
    ///     Resolves bilingual changelog lines from manifest / release-notes.txt.
    ///     Format per line: <c>English text || German text</c>
    /// </summary>
    internal static class ChangelogLocalization
    {
        private static readonly Regex BilingualSeparator =
            new(@"\s*\|\|\s*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, string> KnownGermanLines =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Improvements and bug fixes in this version."] =
                    "Verbesserungen und Fehlerbehebungen in dieser Version.",
                ["Plugins and settings were updated."] =
                    "Plugins und Einstellungen wurden aktualisiert.",
                ["Improvements and bug fixes."] =
                    "Verbesserungen und Fehlerbehebungen.",
            };

        internal static bool LooksBilingual(string line) =>
            !string.IsNullOrWhiteSpace(line) && BilingualSeparator.IsMatch(line.Trim());

        internal static string ResolveLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            var trimmed = line.Trim();
            var parts = BilingualSeparator.Split(trimmed, 2);
            if (parts.Length >= 2)
            {
                var english = parts[0].Trim();
                var german = parts[1].Trim();
                if (!string.IsNullOrEmpty(english) && !string.IsNullOrEmpty(german))
                    return LauncherLocalization.L(english, german);
            }

            if (LauncherLocalization.Language == LauncherLanguage.German &&
                TryTranslateKnownLine(trimmed, out var germanLine))
            {
                return germanLine;
            }

            return trimmed;
        }

        private static bool TryTranslateKnownLine(string line, out string german)
        {
            if (KnownGermanLines.TryGetValue(line, out german!))
                return true;

            const string prefix = "Version ";
            const string suffix = " includes improvements and bug fixes.";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                line.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var version = line.Substring(prefix.Length, line.Length - prefix.Length - suffix.Length).Trim();
                german = $"Version {version} enthaelt Verbesserungen und Fehlerbehebungen.";
                return true;
            }

            german = string.Empty;
            return false;
        }

        internal static IReadOnlyList<string> ResolveLines(IEnumerable<string> lines) =>
            lines.Select(ResolveLine).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

        /// <summary>English half for GitHub release body (manifest keeps full bilingual lines).</summary>
        internal static string EnglishHalf(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            var trimmed = line.Trim();
            var parts = BilingualSeparator.Split(trimmed, 2);
            return parts.Length < 2 ? trimmed : parts[0].Trim();
        }
    }
}
