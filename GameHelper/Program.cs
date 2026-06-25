// <copyright file="Program.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Utils;

    /// <summary>
    ///     Class executed when the application starts.
    /// </summary>
    internal class Program
    {
        /// <summary>
        ///     function executed when the application starts.
        /// </summary>
        // Returns true if the launcher (GameHelper.App.exe) started us.
        // The launcher renames the exe before starting it, so our process name
        // differs from "GameHelper.exe" when we were launched via GameHelper.App.exe.
        private static bool WasStartedByLauncher()
        {
            var myExe = Path.GetFileName(Environment.ProcessPath ?? string.Empty);
            return !string.Equals(myExe, "GameHelper.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task Main()
        {
            // If the user started GameHelper.exe directly (not via launcher),
            // redirect to GameHelper.App.exe so they get update checks.
            if (!WasStartedByLauncher())
            {
                var launcherPath = Path.Combine(AppContext.BaseDirectory, "GameHelper.App.exe");
                if (File.Exists(launcherPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = launcherPath,
                        WorkingDirectory = AppContext.BaseDirectory,
                        UseShellExecute = true,
                    });
                    return;
                }
            }

            AppDomain.CurrentDomain.UnhandledException += (sender, exceptionArgs) =>
            {
                var errorText = "Program exited with message:\n " + exceptionArgs.ExceptionObject;
                File.AppendAllText("Error.log", $"{DateTime.Now:g} {errorText}\r\n{new string('-', 30)}\r\n");

                // Do NOT call Environment.Exit — it skips `using` Dispose and leaks
                // the SafeMemoryHandle (audit F-061). The runtime will terminate
                // the process naturally because IsTerminating == true for unhandled
                // exceptions on the main thread.
            };

            using (Core.Overlay = new GameOverlay(MiscHelper.GenerateRandomString()))
            {
                await Core.Overlay.Run();
            }
        }
    }
}