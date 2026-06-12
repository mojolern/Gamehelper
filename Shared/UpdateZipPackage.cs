namespace Shared.UpdateSecurity
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using Newtonsoft.Json.Linq;

    /// <summary>
    ///     ZIP-based full-package updates (manifest "package" field).
    /// </summary>
    public static class UpdateZipPackage
    {
        public sealed class PackageInfo
        {
            public required string Name { get; init; }

            public required string Hash { get; init; }

            public long Size { get; init; }
        }

        public static bool TryRead(JObject manifest, out PackageInfo package)
        {
            package = null!;
            var token = manifest["package"];
            if (token is not JObject obj)
            {
                return false;
            }

            var name = obj["name"]?.ToString();
            var hash = obj["hash"]?.ToString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(hash))
            {
                return false;
            }

            long size = 0;
            if (obj["size"] != null && long.TryParse(obj["size"]!.ToString(), out var parsed))
            {
                size = parsed;
            }

            package = new PackageInfo
            {
                Name = name,
                Hash = hash,
                Size = size,
            };
            return true;
        }

        public static bool UsesZipDistribution(JObject manifest) =>
            string.Equals(manifest["distribution"]?.ToString(), "zip", StringComparison.OrdinalIgnoreCase) ||
            manifest["package"] is JObject;

        public static void ExtractToDirectory(string zipPath, string destinationDirectory)
        {
            if (!File.Exists(zipPath))
            {
                throw new FileNotFoundException("Update package not found.", zipPath);
            }

            Directory.CreateDirectory(destinationDirectory);
            var destRoot = Path.GetFullPath(destinationDirectory);
            if (!destRoot.EndsWith(Path.DirectorySeparatorChar))
            {
                destRoot += Path.DirectorySeparatorChar;
            }

            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                if (!UpdatePathSecurity.TryResolvePath(destinationDirectory, relativePath, out var destPath))
                {
                    throw new InvalidOperationException($"Unsafe path in update package: {entry.FullName}");
                }

                var destFull = Path.GetFullPath(destPath);
                if (!destFull.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Unsafe path in update package: {entry.FullName}");
                }

                var parent = Path.GetDirectoryName(destFull);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                entry.ExtractToFile(destFull, overwrite: true);
            }
        }
    }
}
