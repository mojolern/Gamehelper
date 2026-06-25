namespace Launcher
{
    using System;
    using System.IO;

    internal static class LegacyPluginCleanup
    {
        private static readonly string[] LegacyFolders =
        [
            "RuneforgeHelper",
            "FarmTracker",
            "MapKillCounter",
        ];

        internal static void Apply(string installDir)
        {
            var pluginsRoot = Path.Combine(installDir, "Plugins");
            if (!Directory.Exists(pluginsRoot))
            {
                return;
            }

            foreach (var name in LegacyFolders)
            {
                var path = Path.Combine(pluginsRoot, name);
                if (!Directory.Exists(path))
                {
                    continue;
                }

                try
                {
                    Directory.Delete(path, recursive: true);
                    LauncherLog.Write($"LegacyCleanup: entfernt {name}");
                }
                catch (Exception ex)
                {
                    LauncherLog.Write($"LegacyCleanup: Fehler beim Entfernen von {name}: {ex.Message}");
                }
            }
        }
    }
}
