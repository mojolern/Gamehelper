namespace Launcher

{

    using Shared.UpdateSecurity;



    internal static class UpdateConfig

    {

        internal const string ChangelogHistoryFileName = "changelog-history.json";



        internal static string ManifestUrl => UpdateRepositoryConfig.ManifestUrl;



        internal static string ManifestSignatureUrl => UpdateRepositoryConfig.ManifestSignatureUrl;



        internal static string ChangelogHistoryUrl =>

            $"{UpdateRepositoryConfig.GitHubHost}/{UpdateRepositoryConfig.Repository}/releases/latest/download/{ChangelogHistoryFileName}";



        internal static string FileDownloadUrl(string version, string fileName) =>

            UpdateRepositoryConfig.FileDownloadUrl(version, fileName);



        internal static string ManifestSignatureUrlForVersion(string version)

        {

            var tag = version.StartsWith('v') ? version : $"v{version}";

            return $"{UpdateRepositoryConfig.GitHubHost}/{UpdateRepositoryConfig.Repository}/releases/download/{tag}/{UpdateRepositoryConfig.ManifestSignatureFileName}";

        }

    }

}


