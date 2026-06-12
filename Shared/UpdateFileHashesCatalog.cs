namespace Shared.UpdateSecurity
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Newtonsoft.Json.Linq;
    using System.Linq;

    /// <summary>
    ///     Persists verified file hashes from the last successful update.
    /// </summary>
    public static class UpdateFileHashesCatalog
    {
        public static void SaveFromManifest(string rootDir, JObject manifest)
        {
            if (UpdateZipPackage.TryRead(manifest, out var package))
            {
                SavePackageHash(rootDir, package.Hash);
                if (TryReadFileEntries(manifest, out var zipFileEntries))
                {
                    Save(rootDir, zipFileEntries);
                }

                return;
            }

            var files = manifest["files"] as JArray;
            if (files == null)
            {
                return;
            }

            var entries = files
                .Select(entry => new KeyValuePair<string, string>(
                    entry?["path"]?.ToString() ?? string.Empty,
                    entry?["hash"]?.ToString() ?? string.Empty))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value));
            Save(rootDir, entries);
        }

        public static void Save(string rootDir, IEnumerable<KeyValuePair<string, string>> entries)
        {
            var catalog = new JObject();
            foreach (var (path, hash) in entries)
            {
                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(hash))
                {
                    continue;
                }

                catalog[path.Replace('\\', '/')] = hash;
            }

            var filePath = GetCatalogPath(rootDir);
            var parent = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllText(filePath, catalog.ToString());
        }

        public static bool TryGetExpectedHash(string rootDir, string relativePath, out string hash)
        {
            hash = string.Empty;
            var catalogPath = GetCatalogPath(rootDir);
            if (!File.Exists(catalogPath))
            {
                return false;
            }

            try
            {
                var catalog = JObject.Parse(File.ReadAllText(catalogPath));
                var normalized = relativePath.Replace('\\', '/');
                var value = catalog[normalized]?.ToString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                hash = value;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void SavePackageHash(string rootDir, string packageHash)
        {
            if (string.IsNullOrWhiteSpace(packageHash))
            {
                return;
            }

            var filePath = Path.Combine(rootDir, "update.package.json");
            var json = new JObject { ["packageHash"] = packageHash };
            File.WriteAllText(filePath, json.ToString());
        }

        public const string CatalogFileName = "update.file-hashes.json";

        public static string GetCatalogPath(string rootDir) =>
            Path.Combine(rootDir, CatalogFileName);

        private static bool TryReadFileEntries(JObject manifest, out IEnumerable<KeyValuePair<string, string>> entries)
        {
            entries = Array.Empty<KeyValuePair<string, string>>();
            var files = manifest["files"] as JArray;
            if (files == null || files.Count == 0)
            {
                return false;
            }

            entries = files
                .Select(entry => new KeyValuePair<string, string>(
                    entry?["path"]?.ToString() ?? string.Empty,
                    entry?["hash"]?.ToString() ?? string.Empty))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value));
            return entries.Any();
        }
    }
}
