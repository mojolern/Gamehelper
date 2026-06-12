namespace Downloader
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Windows.Forms;

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var force = false;
            string? targetDir = null;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg is "--force" or "-f")
                {
                    force = true;
                    continue;
                }

                if (arg is "--help" or "-h" or "/?")
                {
                    PrintHelp();
                    return 0;
                }

                if (arg is "--target" or "-t")
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine(DownloaderLocalization.B(
                            "Missing value for --target",
                            "Fehlender Wert fuer --target"));
                        return 1;
                    }

                    targetDir = args[++i];
                    continue;
                }

                if (!arg.StartsWith('-'))
                {
                    targetDir = arg;
                }
            }

            if (string.IsNullOrWhiteSpace(targetDir))
            {
                targetDir = PromptForTargetDirectory()
                    ?? Path.Combine(AppContext.BaseDirectory, "GameHelper");
            }

            Console.WriteLine("=== GameHelper Download ===");
            Console.WriteLine($"{DownloaderLocalization.B("Target folder", "Zielordner")}: {Path.GetFullPath(targetDir)}");
            Console.WriteLine();

            var service = new GameHelperDownloadService();
            var progress = new Progress<string>(line => Console.WriteLine(line));

            DownloadResult result;
            try
            {
                result = service.DownloadAsync(targetDir, force, progress, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{DownloaderLocalization.B("Error", "Fehler")}: {ex.Message}");
                Console.ResetColor();
                WaitForKey();
                return 1;
            }

            Console.WriteLine();
            if (result.ExitCode != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(result.Message);
                Console.ResetColor();
                WaitForKey();
                return result.ExitCode;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(DownloaderLocalization.B(
                "Done. GameHelper is installed in:",
                "Fertig. GameHelper liegt in:"));
            Console.WriteLine($"  {result.TargetDir}");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine(DownloaderLocalization.B("Start with:", "Starten mit:"));
            Console.WriteLine($"  {Path.Combine(result.TargetDir!, "GameHelper.exe")}");
            WaitForKey();
            return 0;
        }

        private static string? PromptForTargetDirectory()
        {
            try
            {
                Application.EnableVisualStyles();
                using var dialog = new FolderBrowserDialog
                {
                    Description = DownloaderLocalization.B(
                        "Choose target folder for GameHelper",
                        "Zielordner fuer GameHelper waehlen"),
                    UseDescriptionForTitle = true,
                    SelectedPath = AppContext.BaseDirectory,
                };

                return dialog.ShowDialog() == DialogResult.OK
                    ? dialog.SelectedPath
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("GameHelperDownloader");
            Console.WriteLine();
            Console.WriteLine(DownloaderLocalization.B("Usage:", "Verwendung:"));
            Console.WriteLine("  GameHelperDownloader.exe [target folder] [--force]");
            Console.WriteLine("  GameHelperDownloader.exe --target \"D:\\Games\\GameHelper\"");
            Console.WriteLine();
            Console.WriteLine(DownloaderLocalization.B(
                "Without target folder: folder picker dialog, otherwise .\\GameHelper",
                "Ohne Zielordner: Ordnerauswahl-Dialog, sonst .\\GameHelper"));
        }

        private static void WaitForKey()
        {
            if (!Environment.UserInteractive)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine(DownloaderLocalization.B(
                "Press Enter to exit ...",
                "Enter zum Beenden ..."));
            Console.ReadLine();
        }
    }
}
