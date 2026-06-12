namespace Launcher
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using Newtonsoft.Json.Linq;
    using Shared.UpdateSecurity;

    internal static class UpdateService
    {
        private static readonly HttpClient HttpClient = new();
        internal static HttpClient SharedHttpClient => HttpClient;
        private static string? stagedVersion;
        private static string? stagedInstallDir;

        static UpdateService()
        {
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "GameHelper-Updater/1.1");
            HttpClient.Timeout = TimeSpan.FromMinutes(10);
        }

        internal static async Task<UpdateCheckResult> CheckForUpdateAsync(string appExePath, string installDir)
        {
            stagedInstallDir = installDir;
            var latestManifest = await DownloadManifestAsync();
            if (latestManifest == null)
            {
                LauncherLog.Write("Update check: manifest unavailable (download or signature verification failed).");
                return new UpdateCheckResult();
            }

            var currentVersion = GetCurrentVersion(appExePath);
            UpdateMigrationNotice.TryRead(latestManifest, out var latestMigration);
            var manifest = await ResolveManifestForClientAsync(latestManifest, currentVersion, latestMigration)
                ?? latestManifest;

            return EvaluateManifest(manifest, appExePath, installDir, currentVersion);
        }

        private static async Task<JObject?> ResolveManifestForClientAsync(
            JObject latestManifest,
            string currentVersion,
            UpdateMigrationNotice.Info? latestMigration)
        {
            if (latestMigration == null ||
                !UpdateMigrationNotice.AppliesToVersion(currentVersion, latestMigration) ||
                string.IsNullOrWhiteSpace(latestMigration.MaxAutoUpdateVersion))
            {
                return null;
            }

            var latestVersion = latestManifest["version"]?.ToString() ?? string.Empty;
            if (!VersionCompare.IsGreaterOrEqual(latestVersion, latestMigration.ManualInstallVersion))
            {
                return null;
            }

            if (!VersionCompare.IsGreater(latestMigration.MaxAutoUpdateVersion, currentVersion))
            {
                return null;
            }

            if (VersionCompare.EqualsNormalized(latestVersion, latestMigration.MaxAutoUpdateVersion))
            {
                var files = latestManifest["files"] as JArray;
                if (files != null && files.Count > 0)
                {
                    return null;
                }
            }

            LauncherLog.Write(
                $"Update check: bridge v{currentVersion} -> v{latestMigration.MaxAutoUpdateVersion} " +
                $"(latest release v{latestVersion} needs manual install from v{latestMigration.ManualInstallVersion}).");

            return await DownloadManifestForVersionAsync(latestMigration.MaxAutoUpdateVersion);
        }

        private static UpdateCheckResult EvaluateManifest(
            JObject manifest,
            string appExePath,
            string installDir,
            string currentVersion)
        {
            var remoteVersion = manifest["version"]?.ToString();
            if (string.IsNullOrEmpty(remoteVersion))
            {
                LauncherLog.Write("Update check: manifest has no version field.");
                return new UpdateCheckResult();
            }

            UpdateMigrationNotice.TryRead(manifest, out var migrationNotice);
            var migration = migrationNotice != null &&
                            UpdateMigrationNotice.AppliesToVersion(currentVersion, migrationNotice)
                ? migrationNotice
                : null;
            var remotePublished = manifest["published"]?.ToString() ?? string.Empty;
            var localState = UpdateStateHelper.Load(installDir);
            var remoteIsNewerVersion = IsNewerVersion(remoteVersion, currentVersion);

            if (!remoteIsNewerVersion && !UpdateStateHelper.IsRemoteManifestNewer(remotePublished, localState))
            {
                LauncherLog.Write(
                    $"Update check: no new release (installed v{currentVersion}, remote v{remoteVersion}, published {remotePublished}).");
                return new UpdateCheckResult { MigrationNotice = migration };
            }

            if (UpdateZipPackage.TryRead(manifest, out var zipPackage))
            {
                if (!remoteIsNewerVersion &&
                    !string.IsNullOrEmpty(localState?.PackageHash) &&
                    localState.PackageHash.Equals(zipPackage.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    LauncherLog.Write(
                        $"Update check: ZIP package already installed (v{remoteVersion}, hash match).");
                    UpdateStateHelper.Save(installDir, remotePublished, remoteVersion, zipPackage.Hash);
                    return new UpdateCheckResult { MigrationNotice = migration };
                }

                if (migration != null &&
                    VersionCompare.IsGreaterOrEqual(remoteVersion, migration.ManualInstallVersion) &&
                    UpdateMigrationNotice.AppliesToVersion(currentVersion, migration))
                {
                    LauncherLog.Write(
                        $"Update check: v{remoteVersion} requires manual install from v{currentVersion} (>= v{migration.ManualInstallVersion}).");
                    return new UpdateCheckResult { MigrationNotice = migration };
                }

                LauncherLog.Write(
                    $"Update check: v{currentVersion} -> v{remoteVersion}, full package {zipPackage.Name}.");

                return new UpdateCheckResult
                {
                    Offer = new UpdateOffer
                    {
                        CurrentVersion = currentVersion,
                        RemoteVersion = remoteVersion,
                        RemotePublished = remotePublished,
                        Manifest = manifest,
                        FilesToDownload = Array.Empty<UpdateFileEntry>(),
                        ZipPackage = zipPackage,
                        MigrationNotice = migration,
                        Changelog = ParseChangelog(manifest),
                    },
                    MigrationNotice = migration,
                };
            }

            var files = manifest["files"] as JArray;
            if (files == null || files.Count == 0)
            {
                LauncherLog.Write("Update check: manifest contains no package or files.");
                if (migration != null &&
                    VersionCompare.IsGreaterOrEqual(remoteVersion, migration.ManualInstallVersion))
                {
                    return new UpdateCheckResult { MigrationNotice = migration };
                }

                return new UpdateCheckResult { MigrationNotice = migration };
            }

            var toDownload = BuildFilesToDownload(manifest, installDir);
            if (toDownload.Count == 0)
            {
                LauncherLog.Write(
                    $"Update check: all {files.Count} manifest files already match (v{remoteVersion}).");
                UpdateStateHelper.Save(installDir, remotePublished, remoteVersion);
                return new UpdateCheckResult { MigrationNotice = migration };
            }

            LauncherLog.Write(
                $"Update check: v{currentVersion} -> v{remoteVersion}, {toDownload.Count} file(s) to download.");

            return new UpdateCheckResult
            {
                Offer = new UpdateOffer
                {
                    CurrentVersion = currentVersion,
                    RemoteVersion = remoteVersion,
                    RemotePublished = remotePublished,
                    Manifest = manifest,
                    FilesToDownload = toDownload,
                    MigrationNotice = migration,
                    Changelog = ParseChangelog(manifest),
                },
                MigrationNotice = migration,
            };
        }

        private static List<UpdateFileEntry> BuildFilesToDownload(JObject manifest, string installDir)
        {
            var toDownload = new List<UpdateFileEntry>();
            if (manifest["files"] is not JArray files)
            {
                return toDownload;
            }

            foreach (var entry in files)
            {
                var relativePath = entry["path"]?.ToString();
                var expectedHash = entry["hash"]?.ToString();
                if (string.IsNullOrEmpty(relativePath) || string.IsNullOrEmpty(expectedHash))
                {
                    continue;
                }

                if (!UpdatePathSecurity.TryResolvePath(installDir, relativePath, out var localPath))
                {
                    LauncherLog.Write($"Update: unsafe manifest path skipped: {relativePath}");
                    continue;
                }

                if (File.Exists(localPath) &&
                    ComputeSha256(localPath).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var packageName = entry["package"]?.ToString();
                if (string.IsNullOrEmpty(packageName))
                {
                    packageName = relativePath.Replace('\\', '/').Replace('/', '.');
                }

                toDownload.Add(new UpdateFileEntry
                {
                    RelativePath = relativePath,
                    ExpectedHash = expectedHash,
                    PackageName = packageName,
                });
            }

            return toDownload;
        }

        private static async Task<JObject?> DownloadManifestForVersionAsync(string version)
        {
            var tag = version.StartsWith('v') ? version : $"v{version}";
            var url =
                $"{UpdateRepositoryConfig.GitHubHost}/{UpdateRepositoryConfig.Repository}/releases/download/{tag}/{UpdateRepositoryConfig.ManifestFileName}";
            var content = await DownloadTextAsync(url);
            if (content == null)
            {
                LauncherLog.Write($"Bridge manifest download failed: {url}");
                return null;
            }

            var signatureUrl = UpdateConfig.ManifestSignatureUrlForVersion(version);
            var signature = await DownloadTextAsync(signatureUrl);
            if (!UpdateManifestVerifier.TryVerify(content, signature ?? string.Empty, out var verifyError))
            {
                LauncherLog.Write($"Bridge manifest signature rejected: {verifyError}");
                return null;
            }

            return JObject.Parse(content);
        }

        internal static async Task DownloadUpdateAsync(
            UpdateOffer offer,
            string installDir,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            stagedVersion = offer.RemoteVersion;
            stagedInstallDir = installDir;

            var tempDir = GetStagingDirectory(offer.RemoteVersion);
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }

            Directory.CreateDirectory(tempDir);

            if (offer.IsZipUpdate && offer.ZipPackage != null)
            {
                var zipPackage = offer.ZipPackage;
                progress?.Report(new DownloadProgress
                {
                    CompletedFiles = 0,
                    TotalFiles = 1,
                    CurrentFile = zipPackage.Name,
                });

                var zipPath = Path.Combine(tempDir, zipPackage.Name);
                var url = UpdateConfig.FileDownloadUrl(offer.RemoteVersion, zipPackage.Name);
                LauncherLog.Write($"Download package: {zipPackage.Name}");
                await DownloadFileAsync(url, zipPath, cancellationToken);
                VerifyDownloadedHash(zipPath, zipPackage.Hash, zipPackage.Name);
                UpdateZipPackage.ExtractToDirectory(zipPath, tempDir);

                try
                {
                    File.Delete(zipPath);
                }
                catch
                {
                    // ignore cleanup errors
                }

                UpdateFileHashesCatalog.SaveFromManifest(tempDir, offer.Manifest);
                var stagingManifest = Path.Combine(tempDir, "_staging.json");
                await File.WriteAllTextAsync(stagingManifest, offer.Manifest.ToString(), cancellationToken);
                UpdateStateHelper.Save(tempDir, offer.RemotePublished, offer.RemoteVersion, zipPackage.Hash);

                progress?.Report(new DownloadProgress
                {
                    CompletedFiles = 1,
                    TotalFiles = 1,
                    CurrentFile = string.Empty,
                });
                return;
            }

            var total = offer.FilesToDownload.Count;
            var completed = 0;
            foreach (var file in offer.FilesToDownload)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new DownloadProgress
                {
                    CompletedFiles = completed,
                    TotalFiles = total,
                    CurrentFile = file.RelativePath,
                });

                if (!UpdatePathSecurity.TryResolvePath(tempDir, file.RelativePath, out var destPath))
                {
                    throw new InvalidOperationException($"Unsafe update path: {file.RelativePath}");
                }

                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                var url = UpdateConfig.FileDownloadUrl(offer.RemoteVersion, file.PackageName);
                LauncherLog.Write($"Download: {file.PackageName}");
                await DownloadFileAsync(url, destPath, cancellationToken);
                VerifyDownloadedHash(destPath, file.ExpectedHash, file.RelativePath);
                completed++;
            }

            UpdateFileHashesCatalog.SaveFromManifest(tempDir, offer.Manifest);
            var legacyStagingManifest = Path.Combine(tempDir, "_staging.json");
            await File.WriteAllTextAsync(legacyStagingManifest, offer.Manifest.ToString(), cancellationToken);
            UpdateStateHelper.Save(tempDir, offer.RemotePublished, offer.RemoteVersion);

            progress?.Report(new DownloadProgress
            {
                CompletedFiles = total,
                TotalFiles = total,
                CurrentFile = string.Empty,
            });
        }

        internal static bool HasStagedUpdate =>
            !string.IsNullOrEmpty(stagedVersion) && !string.IsNullOrEmpty(stagedInstallDir);

        internal static void ApplyUpdateAndRestart()
        {
            if (!HasStagedUpdate)
            {
                throw new InvalidOperationException(
                    LauncherLocalization.L(
                        "No downloaded update is ready to install.",
                        "Kein heruntergeladenes Update zum Installieren bereit."));
            }

            var tempDir = GetStagingDirectory(stagedVersion!);
            var launcherPath = Process.GetCurrentProcess().MainModule?.FileName
                ?? Path.Combine(stagedInstallDir!, "GameHelper.exe");
            var launcherName = Path.GetFileNameWithoutExtension(launcherPath);
            var pid = Environment.ProcessId;
            var updateRoot = Path.Combine(Path.GetTempPath(), "GameHelperUpdate");
            var scriptPath = Path.Combine(Path.GetTempPath(), $"GameHelperUpdate-install-{pid}.bat");

            var script = string.Join(
                Environment.NewLine,
                "@echo off",
                ":wait",
                $"tasklist /FI \"PID eq {pid}\" 2>NUL | find \"{pid}\" >NUL",
                "if %ERRORLEVEL%==0 (timeout /t 1 /nobreak >nul & goto wait)",
                "timeout /t 1 /nobreak >nul",
                $"robocopy {QuoteBatchPath(tempDir)} {QuoteBatchPath(stagedInstallDir!)} /E /COPY:DAT /R:5 /W:2 /NFL /NDL /NJH /NJS /NC /NS /NP /XF _staging.json",
                $"start \"\" /D {QuoteBatchPath(stagedInstallDir!)} {QuoteBatchPath(launcherPath)}",
                $"rd /s /q {QuoteBatchPath(updateRoot)}",
                "del \"%~f0\"");
            File.WriteAllText(scriptPath, script);

            var started = Process.Start(new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                Arguments = $"/c {QuoteBatchPath(scriptPath)}",
                WorkingDirectory = Path.GetTempPath(),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            if (started == null)
            {
                throw new InvalidOperationException(
                    LauncherLocalization.L(
                        "Could not start the update installer.",
                        "Update-Installer konnte nicht gestartet werden."));
            }

            LauncherLog.Write($"Update install gestartet (PID {started.Id}) fuer {launcherName}");
        }

        private static string GetStagingDirectory(string version) =>
            Path.Combine(Path.GetTempPath(), "GameHelperUpdate", version);

        private static string QuoteBatchPath(string path) =>
            $"\"{path.Replace("\"", "\"\"")}\"";

        private static async Task<JObject?> DownloadManifestAsync()
        {
            var content = await DownloadTextAsync(UpdateConfig.ManifestUrl);
            if (content == null)
            {
                LauncherLog.Write($"Manifest download failed: {UpdateConfig.ManifestUrl}");
                return null;
            }

            var signature = await DownloadTextAsync(UpdateConfig.ManifestSignatureUrl);
            if (!UpdateManifestVerifier.TryVerify(content, signature ?? string.Empty, out var verifyError))
            {
                LauncherLog.Write($"Manifest signature rejected: {verifyError}");
                return null;
            }

            return JObject.Parse(content);
        }

        private static async Task<string?> DownloadTextAsync(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync();
        }

        private static void VerifyDownloadedHash(string filePath, string expectedHash, string relativePath)
        {
            var actualHash = ComputeSha256(filePath);
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch
                {
                    // ignore cleanup errors
                }

                throw new InvalidOperationException(
                    $"Hash mismatch for {relativePath} (expected {expectedHash}, got {actualHash}).");
            }
        }

        internal static string GetCurrentVersion(string exePath)
        {
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                var version = versionInfo.FileVersion;
                if (string.IsNullOrEmpty(version))
                {
                    return "0.0.0";
                }

                var parts = version.Split('.');
                return parts.Length >= 3 ? $"{parts[0]}.{parts[1]}.{parts[2]}" : version.TrimEnd('.', '0');
            }
            catch
            {
                return "0.0.0";
            }
        }

        private static bool IsNewerVersion(string latestVersion, string currentVersion)
        {
            try
            {
                var latest = latestVersion.TrimStart('v').Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
                var current = currentVersion.TrimStart('v').Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
                var maxLen = Math.Max(latest.Length, current.Length);
                for (var i = 0; i < maxLen; i++)
                {
                    var l = i < latest.Length ? latest[i] : 0;
                    var c = i < current.Length ? current[i] : 0;
                    if (l > c)
                    {
                        return true;
                    }

                    if (l < c)
                    {
                        return false;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
        {
            var fileLabel = Path.GetFileName(destinationPath);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} - {fileLabel}");
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var file = File.Create(destinationPath);
                await stream.CopyToAsync(file, cancellationToken);
            }
            catch (Exception ex) when (ex is not HttpRequestException)
            {
                throw new IOException($"Could not write {fileLabel}: {ex.Message}", ex);
            }
        }

        private static string ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(sha.ComputeHash(stream));
        }

        private static IReadOnlyList<string> ParseChangelog(JObject manifest)
        {
            var token = manifest["changelog"];
            if (token == null)
            {
                return DefaultChangelog(manifest["version"]?.ToString());
            }

            if (token is JArray array)
            {
                var lines = array
                    .Select(x => x?.ToString()?.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .ToList();
                return lines.Count > 0 ? lines : DefaultChangelog(manifest["version"]?.ToString());
            }

            var text = token.ToString();
            var split = text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();
            return split.Count > 0 ? split : DefaultChangelog(manifest["version"]?.ToString());
        }

        private static IReadOnlyList<string> DefaultChangelog(string? version)
        {
            var label = string.IsNullOrWhiteSpace(version) ? "This release" : $"Version {version}";
            return new[]
            {
                $"{label} includes improvements and bug fixes. || {label} enthaelt Verbesserungen und Fehlerbehebungen.",
                "Plugins and settings were updated. || Plugins und Einstellungen wurden aktualisiert.",
            };
        }
    }
}
