namespace Launcher
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Newtonsoft.Json;

    public static class TemporaryFileManager
    {
        private const string StoreFileName = "tempFileLocations.dat";

        public static void AddFile(string path)
        {
            var fileList = GetFileList();
            WriteFileList(fileList.Append(Path.GetRelativePath(GetDirectoryPath(), path)));
        }

        public static void Purge()
        {
            var fileList = GetFileList();
            var directoryPath = GetDirectoryPath();
            foreach (var file in fileList.Select(x => Path.Join(directoryPath, x)))
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }

            WriteFileList(Array.Empty<string>());
        }

        private static IReadOnlyList<string> GetFileList()
        {
            try
            {
                var tempFileName = GetFullTemporaryFileName();
                if (!File.Exists(tempFileName))
                {
                    return Array.Empty<string>();
                }

                var fileContent = File.ReadAllText(tempFileName);
                return JsonConvert.DeserializeObject<List<string>>(fileContent) ?? (IReadOnlyList<string>)Array.Empty<string>();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return Array.Empty<string>();
            }
        }

        private static void WriteFileList(IEnumerable<string> list)
        {
            File.WriteAllText(GetFullTemporaryFileName(), JsonConvert.SerializeObject(list.Distinct()));
        }

        private static string GetFullTemporaryFileName() => Path.Join(GetDirectoryPath(), StoreFileName);

        private static string GetDirectoryPath() =>
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
