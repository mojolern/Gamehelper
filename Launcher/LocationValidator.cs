namespace Launcher
{
    using System;
    using System.IO;
    using System.Text.RegularExpressions;

    public static class LocationValidator
    {
        private static readonly Regex BadNameRegex = new(
            @"poe|path\s*of\s*exile|overlay|helper|hud|desktop",
            RegexOptions.IgnoreCase);

        public static bool IsGameHelperLocationGood(out string? message)
        {
            message = null;
            var directoryPath = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(directoryPath))
            {
                return true;
            }

            var pathMatch = BadNameRegex.Match(directoryPath);
            if (pathMatch.Success)
            {
                var folderName = pathMatch.Value;
                message = LauncherLocalization.L(
                    $"GameHelper is in a risky folder (\"{folderName}\"). Please move it to a less conspicuous folder.",
                    $"GameHelper liegt in einem riskanten Ordner (\"{folderName}\"). Bitte in einen unauffaelligen Ordner verschieben.");
                return false;
            }

            return true;
        }
    }
}
