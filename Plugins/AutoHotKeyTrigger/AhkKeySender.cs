// <copyright file="AhkKeySender.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger
{
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using ClickableTransparentOverlay.Win32;
    using GameHelper;
    using GameHelper.Utils;
    using Process = System.Diagnostics.Process;

    /// <summary>
    ///     AHK-only key sender — matches GameHelper v1.2.4/v1.3.1 (WM_KEYUP via SendMessage).
    ///     Kept separate because core MiscHelper.KeyUp now uses SendInput full taps.
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

            if (DelayBetweenKeys.ElapsedMilliseconds < Core.GHSettings.KeyPressTimeout + Rand.Next() % 10)
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

            DelayBetweenKeys.Restart();
            sendingMessage = Task.Run(() => SendMessage(hwnd, WmKeyup, (int)key, 0));
            ActivityLog.Write("Input", $"{label}: key {key} sent to game");
            return true;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    }
}
