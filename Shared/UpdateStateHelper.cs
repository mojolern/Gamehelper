namespace Shared.UpdateSecurity
{
    using System;
    using System.IO;
    using Newtonsoft.Json.Linq;

    internal static class UpdateStateHelper
    {
        internal sealed class UpdateState
        {
            public string LastPublished { get; set; } = string.Empty;

            public string LastVersion { get; set; } = string.Empty;

            public string PackageHash { get; set; } = string.Empty;
        }

        internal static UpdateState? Load(string installDir)
        {
            var path = Path.Combine(installDir, "update.state.json");
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var json = JObject.Parse(File.ReadAllText(path));
                return new UpdateState
                {
                    LastPublished = json["lastPublished"]?.ToString() ?? string.Empty,
                    LastVersion = json["lastVersion"]?.ToString() ?? string.Empty,
                    PackageHash = json["packageHash"]?.ToString() ?? string.Empty,
                };
            }
            catch
            {
                return null;
            }
        }

        internal static void Save(string installDir, string published, string version, string? packageHash = null)
        {
            if (string.IsNullOrEmpty(published))
            {
                return;
            }

            var path = Path.Combine(installDir, "update.state.json");
            var json = new JObject
            {
                ["lastPublished"] = published,
                ["lastVersion"] = version,
            };
            if (!string.IsNullOrWhiteSpace(packageHash))
            {
                json["packageHash"] = packageHash;
            }

            File.WriteAllText(path, json.ToString());
        }

        internal static bool IsRemoteManifestNewer(string remotePublished, UpdateState? localState)
        {
            if (string.IsNullOrEmpty(remotePublished))
            {
                return localState == null;
            }

            if (localState == null || string.IsNullOrEmpty(localState.LastPublished))
            {
                return true;
            }

            if (!DateTime.TryParse(remotePublished, out var remoteTime) ||
                !DateTime.TryParse(localState.LastPublished, out var localTime))
            {
                return !string.Equals(remotePublished, localState.LastPublished, StringComparison.Ordinal);
            }

            return remoteTime > localTime;
        }
    }
}
