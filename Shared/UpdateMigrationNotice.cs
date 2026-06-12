namespace Shared.UpdateSecurity
{
    using System;
    using System.IO;
    using System.Linq;
    using Newtonsoft.Json.Linq;

    /// <summary>
    ///     Optional manifest hint that a future version requires manual installation.
    /// </summary>
    public static class UpdateMigrationNotice
    {
        public sealed class Info
        {
            public required string ManualInstallVersion { get; init; }

            /// <summary>Last version reachable via auto-update (e.g. 1.1.10 before manual 1.2+).</summary>
            public string MaxAutoUpdateVersion { get; init; } = string.Empty;

            public required string MessageEn { get; init; }

            public required string MessageDe { get; init; }
        }

        private const string DismissFileName = "migration-notice.dismissed";

        public static bool TryRead(JObject manifest, out Info notice)
        {
            notice = null!;
            var token = manifest["migration"];
            if (token is not JObject obj)
            {
                return false;
            }

            var targetVersion = obj["manualInstallVersion"]?.ToString();
            if (string.IsNullOrWhiteSpace(targetVersion))
            {
                return false;
            }

            var maxAutoVersion = obj["maxAutoUpdateVersion"]?.ToString()?.Trim() ?? string.Empty;

            var messageEn = obj["messageEn"]?.ToString();
            var messageDe = obj["messageDe"]?.ToString();
            var combined = obj["message"]?.ToString();
            if (!string.IsNullOrWhiteSpace(combined))
            {
                var parts = combined.Split(new[] { " || " }, 2, StringSplitOptions.None);
                if (string.IsNullOrWhiteSpace(messageEn))
                {
                    messageEn = parts[0].Trim();
                }

                if (string.IsNullOrWhiteSpace(messageDe))
                {
                    messageDe = parts.Length > 1 ? parts[1].Trim() : messageEn;
                }
            }

            messageEn ??= string.Empty;
            messageDe ??= string.IsNullOrWhiteSpace(messageEn) ? string.Empty : messageEn;

            notice = new Info
            {
                ManualInstallVersion = targetVersion.TrimStart('v'),
                MaxAutoUpdateVersion = maxAutoVersion.TrimStart('v'),
                MessageEn = messageEn,
                MessageDe = messageDe,
            };
            return true;
        }

        public static bool AppliesToVersion(string currentVersion, Info notice)
        {
            return VersionCompare.IsLess(currentVersion, notice.ManualInstallVersion);
        }

        public static bool ShouldShow(string installDir, Info notice, string currentVersion)
        {
            if (!AppliesToVersion(currentVersion, notice))
            {
                return false;
            }

            var dismissedPath = Path.Combine(installDir, DismissFileName);
            if (!File.Exists(dismissedPath))
            {
                return true;
            }

            try
            {
                var dismissedFor = File.ReadAllText(dismissedPath).Trim();
                return !string.Equals(
                    NormalizeVersion(dismissedFor),
                    NormalizeVersion(notice.ManualInstallVersion),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }

        public static void Dismiss(string installDir, Info notice)
        {
            var dismissedPath = Path.Combine(installDir, DismissFileName);
            File.WriteAllText(dismissedPath, NormalizeVersion(notice.ManualInstallVersion));
        }

        private static string NormalizeVersion(string version) =>
            VersionCompare.Normalize(version);
    }
}
