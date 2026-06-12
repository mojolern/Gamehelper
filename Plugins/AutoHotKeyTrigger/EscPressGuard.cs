// <copyright file="EscPressGuard.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger
{
    using System.Diagnostics;
    using GameHelper;
    using GameHelper.RemoteEnums;

    internal static class EscPressGuard
    {
        private static readonly Stopwatch SinceLastEsc = new();
        private const double MinSecondsBetweenEsc = 2.0;

        internal static bool CanSend()
        {
            if (Core.States.GameCurrentState == GameStateTypes.EscapeState)
            {
                return false;
            }

            return !SinceLastEsc.IsRunning || SinceLastEsc.Elapsed.TotalSeconds >= MinSecondsBetweenEsc;
        }

        internal static void MarkSent()
        {
            SinceLastEsc.Restart();
        }

        internal static void Reset()
        {
            SinceLastEsc.Reset();
        }
    }
}
