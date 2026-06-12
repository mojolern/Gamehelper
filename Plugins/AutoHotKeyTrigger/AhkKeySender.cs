// <copyright file="AhkKeySender.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger
{
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using Process = System.Diagnostics.Process;
    using System.Threading.Tasks;
    using ClickableTransparentOverlay.Win32;
    using GameHelper;
    using GameHelper.Utils;

    /// <summary>
    ///     AHK-only key sender matching GameHelper v1.2.5 behaviour (WM_KEYUP, no skill-key blocking).
    ///     Keeps Autopot and other plugins on the current core input path.
    /// </summary>
    internal static class AhkKeySender
    {
        private const int WmKeyup = 0x101;

        private static readonly Random Rand = new();
        private static readonly Stopwatch DelayBetweenKeys = Stopwatch.StartNew();
        private static Task? sendingMessage;

        internal static bool SendKey(VK key, string? source = null)
        {
            var label = string.IsNullOrWhiteSpace(source) ? "AHK" : source.Trim();

            if (Core.GHSettings.EnableControllerMode)
            {
                return false;
            }

            if (sendingMessage != null && !sendingMessage.IsCompleted)
            {
                return false;
            }

            if (DelayBetweenKeys.ElapsedMilliseconds >= Core.GHSettings.KeyPressTimeout + Rand.Next() % 10)
            {
                DelayBetweenKeys.Restart();
            }
            else
            {
                return false;
            }

            if (Core.Process.Pid == 0)
            {
                ActivityLog.Write("Input", $"{label}: key {key} not sent (game not loaded)");
                return false;
            }

            IntPtr hwnd;
            try
            {
                hwnd = Process.GetProcessById((int)Core.Process.Pid).MainWindowHandle;
            }
            catch (Exception)
            {
                return false;
            }

            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            sendingMessage = Task.Run(() => SendMessage(hwnd, WmKeyup, (int)key, 0));
            ActivityLog.Write("Input", $"{label}: key {key} sent to game (legacy)");
            return true;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    }
}
