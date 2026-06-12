namespace Shared.UpdateSecurity
{
    using System;
    using System.IO;
    using System.Linq;

    /// <summary>
    ///     Validates manifest-relative paths stay inside a root directory.
    /// </summary>
    public static class UpdatePathSecurity
    {
        public static bool TryResolvePath(string rootDir, string relativePath, out string fullPath)
        {
            fullPath = string.Empty;
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            var normalized = relativePath.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(normalized))
            {
                return false;
            }

            if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(static segment => segment == ".."))
            {
                return false;
            }

            var rootFull = NormalizeRoot(rootDir);
            var combined = Path.GetFullPath(Path.Combine(rootFull, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            fullPath = combined;
            return true;
        }

        public static bool IsPathInsideRoot(string rootDir, string candidatePath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                return false;
            }

            var rootFull = NormalizeRoot(rootDir);
            var candidateFull = Path.GetFullPath(candidatePath);
            return candidateFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeRoot(string rootDir)
        {
            var rootFull = Path.GetFullPath(rootDir);
            if (!rootFull.EndsWith(Path.DirectorySeparatorChar))
            {
                rootFull += Path.DirectorySeparatorChar;
            }

            return rootFull;
        }
    }
}
