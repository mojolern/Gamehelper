// <copyright file="EndgameAtlasPanelDetector.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Utils
{
    using System;
    using GameOffsets.Natives;
    using GameOffsets.Objects.UiElement;

    /// <summary>
    ///     Detects the PoE2 endgame atlas panel via fingerprint backtracking on the GameUi tree.
    ///     Logic mirrors Plugins/Atlas (verified 0.5.x, 2026-06).
    /// </summary>
    internal static class EndgameAtlasPanelDetector
    {
        private const uint AtlasPanelFp = 0x00562EF5;
        private const uint AtlasGateFp = 0x00502EF1;
        private const uint AtlasNodeListFp = 0x00502EF3;
        private const uint AtlasNodeFp = 0x00542EF3;
        private const uint IsVisibleMask = 0x800u;
        private const int MinAtlasNodes = 20;
        private const int MinVisibleAtlasNodes = 1;
        private const int MaxChildren = 10000;
        private const int KbMouseGateStep = 1;
        private const int ControllerGateStep = 4;

        private static readonly uint[] KbMouseChain = { AtlasPanelFp, AtlasGateFp, AtlasNodeListFp };
        private static readonly uint[] ControllerChain =
            { AtlasGateFp, AtlasNodeFp, AtlasGateFp, AtlasPanelFp, AtlasGateFp, AtlasNodeListFp };

        public static bool IsConfidentlyOpen(IntPtr gameUiAddress, bool minimapLive)
        {
            if (gameUiAddress == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (HasStrongOpenSignal(gameUiAddress))
                {
                    return true;
                }

                if (minimapLive)
                {
                    return false;
                }

                return IsOpen(gameUiAddress);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        ///     Gate or visible UI path only — safe while the in-zone Tab overlay is up.
        /// </summary>
        public static bool HasStrongOpenSignal(IntPtr gameUiAddress)
        {
            if (gameUiAddress == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (IsVisibleGateOpen(gameUiAddress))
                {
                    return true;
                }

                var visiblePathPanel = FindPanelAddress(gameUiAddress, visiblePathOnly: true);
                return visiblePathPanel != IntPtr.Zero && PanelLooksOpen(visiblePathPanel);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsOpen(IntPtr gameUiAddress)
        {
            if (gameUiAddress == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (IsVisibleGateOpen(gameUiAddress))
                {
                    return true;
                }

                var visiblePathPanel = FindPanelAddress(gameUiAddress, visiblePathOnly: true);
                if (visiblePathPanel != IntPtr.Zero && PanelLooksOpen(visiblePathPanel))
                {
                    return true;
                }

                var panelAddr = FindPanelAddress(gameUiAddress, visiblePathOnly: false);
                if (panelAddr == IntPtr.Zero)
                {
                    return false;
                }

                var reader = Core.Process.Handle;
                var panel = reader.ReadMemory<UiElementBaseOffset>(panelAddr);
                if (UiElementBaseFuncs.IsVisibleChecker(panel.Flags))
                {
                    return true;
                }

                if (!PanelLooksOpen(panelAddr))
                {
                    return false;
                }

                return CountVisibleAtlasNodeChildren(in panel.ChildrensPtr) >= MinVisibleAtlasNodes;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsVisibleGateOpen(IntPtr gameUiAddress)
        {
            var controller = Core.GHSettings.EnableControllerMode;
            var (primary, primaryGate, secondary, secondaryGate) = controller
                ? (ControllerChain, ControllerGateStep, KbMouseChain, KbMouseGateStep)
                : (KbMouseChain, KbMouseGateStep, ControllerChain, ControllerGateStep);

            return IsVisibleGateOnChain(gameUiAddress, primary, primaryGate) ||
                IsVisibleGateOnChain(gameUiAddress, secondary, secondaryGate);
        }

        private static bool IsVisibleGateOnChain(IntPtr gameUiAddress, uint[] chain, int gateStep)
        {
            var gateAddr = FindVisibleGateAddress(gameUiAddress, chain, gateStep, 0);
            return gateAddr != IntPtr.Zero && UiElementVisibility.IsFlagVisible(gateAddr);
        }

        private static IntPtr FindVisibleGateAddress(IntPtr parentAddr, uint[] fps, int gateStep, int step)
        {
            if (parentAddr == IntPtr.Zero || step >= fps.Length)
            {
                return IntPtr.Zero;
            }

            var reader = Core.Process.Handle;
            var parent = reader.ReadMemory<UiElementBaseOffset>(parentAddr);
            var childCount = CountChildren(in parent.ChildrensPtr);
            if (childCount <= 0 || childCount > MaxChildren)
            {
                return IntPtr.Zero;
            }

            var target = fps[step] & ~IsVisibleMask;
            for (var i = 0; i < childCount; i++)
            {
                var childAddr = ReadChildAddress(in parent.ChildrensPtr, i);
                if (childAddr == IntPtr.Zero)
                {
                    continue;
                }

                var child = reader.ReadMemory<UiElementBaseOffset>(childAddr);
                var flags = child.Flags;
                if ((flags & ~IsVisibleMask) != target)
                {
                    continue;
                }

                if ((flags & IsVisibleMask) == 0)
                {
                    continue;
                }

                if (step == gateStep)
                {
                    return childAddr;
                }

                if (step == gateStep - 1 && !UiElementBaseFuncs.IsVisibleChecker(flags))
                {
                    continue;
                }

                var deeper = FindVisibleGateAddress(childAddr, fps, gateStep, step + 1);
                if (deeper != IntPtr.Zero)
                {
                    return deeper;
                }
            }

            return IntPtr.Zero;
        }

        private static bool PanelLooksOpen(IntPtr panelAddr)
        {
            var reader = Core.Process.Handle;
            var panel = reader.ReadMemory<UiElementBaseOffset>(panelAddr);
            var nodeCount = CountChildren(in panel.ChildrensPtr);
            return nodeCount >= MinAtlasNodes && nodeCount <= MaxChildren;
        }

        private static IntPtr FindPanelAddress(IntPtr gameUiAddress, bool visiblePathOnly)
        {
            var controller = Core.GHSettings.EnableControllerMode;
            var (primary, primaryGate, secondary, secondaryGate) = controller
                ? (ControllerChain, ControllerGateStep, KbMouseChain, KbMouseGateStep)
                : (KbMouseChain, KbMouseGateStep, ControllerChain, ControllerGateStep);

            var addr = WalkFp(gameUiAddress, primary, primaryGate, 0, visiblePathOnly);
            return addr != IntPtr.Zero ? addr : WalkFp(gameUiAddress, secondary, secondaryGate, 0, visiblePathOnly);
        }

        private static IntPtr WalkFp(IntPtr parentAddr, uint[] fps, int gateStep, int step, bool visiblePathOnly)
        {
            if (step == fps.Length)
            {
                return parentAddr;
            }

            var reader = Core.Process.Handle;
            var parent = reader.ReadMemory<UiElementBaseOffset>(parentAddr);
            var childCount = CountChildren(in parent.ChildrensPtr);
            if (childCount <= 0 || childCount > MaxChildren)
            {
                return IntPtr.Zero;
            }

            var target = fps[step] & ~IsVisibleMask;
            var passCount = visiblePathOnly ? 1 : 2;

            for (var pass = 0; pass < passCount; pass++)
            {
                var wantVisible = pass == 0;
                for (var i = 0; i < childCount; i++)
                {
                    var childAddr = ReadChildAddress(in parent.ChildrensPtr, i);
                    if (childAddr == IntPtr.Zero)
                    {
                        continue;
                    }

                    var child = reader.ReadMemory<UiElementBaseOffset>(childAddr);
                    var flags = child.Flags;
                    if ((flags & ~IsVisibleMask) != target)
                    {
                        continue;
                    }

                    var visible = (flags & IsVisibleMask) != 0;
                    if (visible != wantVisible)
                    {
                        continue;
                    }

                    if (step == gateStep && !visible)
                    {
                        continue;
                    }

                    var deeper = WalkFp(childAddr, fps, gateStep, step + 1, visiblePathOnly);
                    if (deeper != IntPtr.Zero)
                    {
                        return deeper;
                    }
                }
            }

            return IntPtr.Zero;
        }

        private static int CountVisibleAtlasNodeChildren(in StdVector vector)
        {
            var count = CountChildren(in vector);
            if (count <= 0)
            {
                return 0;
            }

            var reader = Core.Process.Handle;
            var target = AtlasNodeFp & ~IsVisibleMask;
            var sampleCount = Math.Min(count, 128);
            var visibleCount = 0;
            for (var i = 0; i < sampleCount; i++)
            {
                var childAddr = ReadChildAddress(in vector, i);
                if (childAddr == IntPtr.Zero)
                {
                    continue;
                }

                var child = reader.ReadMemory<UiElementBaseOffset>(childAddr);
                if ((child.Flags & ~IsVisibleMask) != target)
                {
                    continue;
                }

                if ((child.Flags & IsVisibleMask) != 0)
                {
                    visibleCount++;
                }
            }

            return visibleCount;
        }

        private static int CountChildren(in StdVector vector)
        {
            if (vector.First == IntPtr.Zero || vector.Last == IntPtr.Zero)
            {
                return 0;
            }

            var bytes = vector.Last.ToInt64() - vector.First.ToInt64();
            if (bytes <= 0)
            {
                return 0;
            }

            var stride = IntPtr.Size;
            if ((bytes % stride) != 0)
            {
                return 0;
            }

            var count = bytes / stride;
            if (count <= 0 || count > MaxChildren)
            {
                return 0;
            }

            return (int)count;
        }

        private static IntPtr ReadChildAddress(in StdVector vector, int index)
        {
            var count = CountChildren(in vector);
            if ((uint)index >= (uint)count)
            {
                return IntPtr.Zero;
            }

            var reader = Core.Process.Handle;
            var slot = IntPtr.Add(vector.First, index * IntPtr.Size);
            return reader.ReadMemory<IntPtr>(slot);
        }
    }
}
