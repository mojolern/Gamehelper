// <copyright file="UiElementTreeVisibility.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Utils
{
    using System;
    using System.Collections.Generic;
    using GameOffsets.Natives;
    using GameOffsets.Objects.UiElement;

    internal static class UiElementTreeVisibility
    {
        private const int MaxChildren = 5000;
        private const int MaxNodes = 256;

        public static bool HasVisibleDescendant(IntPtr rootAddress, int maxDepth = 4)
        {
            if (rootAddress == IntPtr.Zero || maxDepth < 0)
            {
                return false;
            }

            if (UiElementVisibility.IsFlagVisible(rootAddress))
            {
                return true;
            }

            var reader = Core.Process.Handle;
            var queue = new Queue<(IntPtr Address, int Depth)>();
            queue.Enqueue((rootAddress, 0));
            var visited = 0;

            while (queue.Count > 0 && visited < MaxNodes)
            {
                var (address, depth) = queue.Dequeue();
                visited++;

                if (depth >= maxDepth)
                {
                    continue;
                }

                UiElementBaseOffset element;
                try
                {
                    element = reader.ReadMemory<UiElementBaseOffset>(address);
                }
                catch
                {
                    continue;
                }

                var childCount = CountChildren(in element.ChildrensPtr);
                for (var i = 0; i < childCount; i++)
                {
                    var childAddress = ReadChildAddress(in element.ChildrensPtr, i);
                    if (childAddress == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (UiElementVisibility.IsFlagVisible(childAddress))
                    {
                        return true;
                    }

                    queue.Enqueue((childAddress, depth + 1));
                }
            }

            return false;
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
