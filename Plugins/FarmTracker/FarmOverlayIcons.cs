namespace FarmTracker
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Threading.Tasks;
    using GameHelper;
    using ImGuiNET;

    internal static class FarmOverlayIcons
    {
        private static readonly (string Key, string FileName, string Url)[] Sources =
        {
            ("Divine", "Divine.png", "https://web.poecdn.com//gen/image/WzI1LDE0LHsiZiI6IjJESXRlbXMvQ3VycmVuY3kvQ3VycmVuY3lNb2RWYWx1ZXMiLCJzY2FsZSI6MSwicmVhbG0iOiJwb2UyIn1d/2986e220b3/CurrencyModValues.png"),
            ("Exalt", "Exalt.png", "https://web.poecdn.com//gen/image/WzI1LDE0LHsiZiI6IjJESXRlbXMvQ3VycmVuY3kvQ3VycmVuY3lBZGRNb2RUb1JhcmUiLCJzY2FsZSI6MSwicmVhbG0iOiJwb2UyIn1d/ad7c366789/CurrencyAddModToRare.png"),
            ("Chaos", "Chaos.png", "https://web.poecdn.com//gen/image/WzI1LDE0LHsiZiI6IjJESXRlbXMvQ3VycmVuY3kvQ3VycmVuY3lEdWxhdGUiLCJzY2FsZSI6MSwicmVhbG0iOiJwb2UyIn1d/8b3e0a3f2c/ChaosOrb.png"),
            ("Map", "Map.png", "https://raw.githubusercontent.com/yokkenUA/LootTracker/main/icons/Map.png"),
            ("Time", "Time.png", "https://raw.githubusercontent.com/yokkenUA/LootTracker/main/icons/Time.png"),
            ("NormalMob", "NormalMob.png", "https://raw.githubusercontent.com/yokkenUA/LootTracker/main/icons/NormalMob.png"),
            ("MagicMob", "MagicMob.png", "https://raw.githubusercontent.com/yokkenUA/LootTracker/main/icons/MagicMob.png"),
            ("RareMob", "RareMob.png", "https://raw.githubusercontent.com/yokkenUA/LootTracker/main/icons/RareMob.png"),
            ("UniqueMob", "UniqueMob.png", "https://raw.githubusercontent.com/yokkenUA/LootTracker/main/icons/UniqueMob.png"),
        };

        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly Dictionary<string, IntPtr> Handles = new(StringComparer.OrdinalIgnoreCase);
        private static string iconsDir = string.Empty;
        private static bool downloadStarted;
        private static int reloadCooldown;

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add("User-Agent", "FarmTracker-GameHelper-Plugin");
            return client;
        }

        internal static void Load(string dllDirectory)
        {
            iconsDir = Path.Join(dllDirectory, "icons");
            Directory.CreateDirectory(iconsDir);

            if (!downloadStarted)
            {
                downloadStarted = true;
                Task.Run(EnsureIconsAsync);
            }

            ReloadIfNeeded(force: true);
        }

        internal static void ReloadIfNeeded(bool force = false)
        {
            if (!force)
            {
                if (--reloadCooldown > 0)
                {
                    return;
                }

                reloadCooldown = 180;
            }

            Handles.Clear();
            foreach (var (key, fileName, _) in Sources)
            {
                var path = Path.Join(iconsDir, fileName);
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    Core.Overlay.AddOrGetImagePointer(path, false, out var handle, out _, out _);
                    if (handle != IntPtr.Zero)
                    {
                        Handles[key] = handle;
                    }
                }
                catch
                {
                    // optional
                }
            }
        }

        internal static void Unload(string dllDirectory)
        {
            try
            {
                if (Directory.Exists(iconsDir))
                {
                    foreach (var (_, fileName, _) in Sources)
                    {
                        var path = Path.Join(iconsDir, fileName);
                        if (File.Exists(path))
                        {
                            Core.Overlay.RemoveImage(path);
                        }
                    }
                }
            }
            catch
            {
                // best effort
            }

            Handles.Clear();
            downloadStarted = false;
        }

        internal static bool TryDraw(string key, float size)
        {
            ReloadIfNeeded();
            if (!Handles.TryGetValue(key, out var handle) || handle == IntPtr.Zero)
            {
                return false;
            }

            ImGui.Image(handle, new System.Numerics.Vector2(size, size));
            return true;
        }

        private static async Task EnsureIconsAsync()
        {
            foreach (var (_, fileName, url) in Sources)
            {
                var path = Path.Join(iconsDir, fileName);
                if (File.Exists(path))
                {
                    continue;
                }

                try
                {
                    using var response = await Http.GetAsync(url).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    await File.WriteAllBytesAsync(path, await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false)).ConfigureAwait(false);
                }
                catch
                {
                    // optional
                }
            }

            reloadCooldown = 0;
        }
    }
}
