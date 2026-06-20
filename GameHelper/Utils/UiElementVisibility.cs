// <copyright file="UiElementVisibility.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Utils
{
    using System;
    using GameOffsets.Objects.UiElement;

    internal static class UiElementVisibility
    {
        public static bool IsFlagVisible(IntPtr address)
        {
            if (address == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var reader = Core.Process.Handle;
                var off = reader.ReadMemory<UiElementBaseOffset>(address);
                return UiElementBaseFuncs.IsVisibleChecker(off.Flags);
            }
            catch
            {
                return false;
            }
        }
    }
}
