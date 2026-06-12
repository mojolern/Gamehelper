namespace Downloader



{

    using Shared.UpdateSecurity;



    internal static class DownloadConfig

    {

        internal static string ManifestUrl => UpdateRepositoryConfig.ManifestUrl;



        internal static string ManifestSignatureUrl => UpdateRepositoryConfig.ManifestSignatureUrl;



        internal static string FileDownloadUrl(string version, string packageFileName) =>

            UpdateRepositoryConfig.FileDownloadUrl(version, packageFileName);

    }

}


