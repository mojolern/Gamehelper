namespace Launcher

{

    using System;

    using System.IO;

    using System.Text;



    internal static class LauncherLog

    {

        private const long MaxLogBytes = 512 * 1024;



        private static readonly string LogPath = Path.Combine(

            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),

            "launcher.log");



        internal static void Write(string message)

        {

            try

            {

                TrimIfNeeded();

                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);

            }

            catch

            {

                // Logging darf Start nicht blockieren.

            }

        }



        private static void TrimIfNeeded()

        {

            if (!File.Exists(LogPath))

            {

                return;

            }



            var info = new FileInfo(LogPath);

            if (info.Length <= MaxLogBytes)

            {

                return;

            }



            var keepBytes = MaxLogBytes / 2;

            using var input = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            input.Seek(-keepBytes, SeekOrigin.End);

            var buffer = new byte[keepBytes];

            var read = input.Read(buffer, 0, buffer.Length);

            var text = Encoding.UTF8.GetString(buffer, 0, read);

            var firstLine = text.IndexOf('\n');

            if (firstLine >= 0 && firstLine < text.Length - 1)

            {

                text = text[(firstLine + 1)..];

            }



            File.WriteAllText(LogPath, text, Encoding.UTF8);

        }

    }

}


