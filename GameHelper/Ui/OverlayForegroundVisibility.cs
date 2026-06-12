// <copyright file="OverlayForegroundVisibility.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Ui
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using Coroutine;
    using CoroutineEvents;

    /// <summary>
    ///     Hides the overlay HWND while the game is not in the foreground.
    /// </summary>
    public static class OverlayForegroundVisibility
    {
        private static bool overlayHiddenForBackground;
        private static bool lastHideSetting;
        private static bool lastForeground;
        private static uint lastPid;

        /// <summary>
        ///     Initializes the co-routines.
        /// </summary>
        internal static void InitializeCoroutines()
        {
            CoroutineHandler.Start(SyncOverlayVisibilityCoroutine());
        }

        private static IEnumerator<Wait> SyncOverlayVisibilityCoroutine()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnRender);

                var hideSetting = Core.GHSettings.HideOverlayWhenGameInBackground;
                var pid = Core.Process.Pid;
                var foreground = Core.Process.Foreground;
                if (hideSetting == lastHideSetting && pid == lastPid && foreground == lastForeground)
                {
                    continue;
                }

                lastHideSetting = hideSetting;
                lastPid = pid;
                lastForeground = foreground;
                ApplyVisibility();
            }
        }

        private static void ApplyVisibility()
        {
            if (Core.Overlay?.window == null)
            {
                return;
            }

            var shouldHide = Core.GHSettings.HideOverlayWhenGameInBackground
                && Core.Process.Pid != 0
                && !Core.Process.Foreground;

            if (shouldHide == overlayHiddenForBackground)
            {
                return;
            }

            overlayHiddenForBackground = shouldHide;
            ShowWindow(Core.Overlay.window.Handle, shouldHide ? SwHide : SwShow);
        }

        private const int SwHide = 0;
        private const int SwShow = 5;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
