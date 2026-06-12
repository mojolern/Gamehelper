// <copyright file="AhkKeySender.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger
{
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using ClickableTransparentOverlay.Win32;
    using GameHelper;
    using GameHelper.Utils;
    using Process = System.Diagnostics.Process;

    /// <summary>
    ///     AHK key sender: WM_KEYUP for skills/flasks (1.2.x). ESC uses keydown only (pause menu stays open).
    /// </summary>
    internal static class AhkKeySender
    {
        private const int WmKeydown = 0x100;
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

            if (key == VK.ESCAPE && !EscPressGuard.CanSend())
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
            var escMenuOpen = key == VK.ESCAPE;
            if (escMenuOpen)
            {
                EscPressGuard.MarkSent();
            }

            sendingMessage = Task.Run(() => SendToWindow(hwnd, key, escMenuOpen));
            ActivityLog.Write(
                "Input",
                $"{label}: key {key} sent to game (legacy{(escMenuOpen ? ", ESC keydown" : "")})");
            return true;
        }

        private static void SendToWindow(IntPtr hwnd, VK key, bool escMenuOpen)
        {
            if (escMenuOpen)
            {
                SendMessage(hwnd, WmKeydown, (int)key, 0);
                return;
            }

            SendMessage(hwnd, WmKeyup, (int)key, 0);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    }
}
