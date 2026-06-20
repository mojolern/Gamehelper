namespace FarmTracker
{
    using System;
    using System.Numerics;
    using System.Runtime.InteropServices;
    using GameHelper;

    /// <summary>Resolves the in-game experience bar rect via UI fingerprint walk.</summary>
    internal sealed class FarmExperienceBarAnchor
    {
        private static readonly uint[] Fingerprints = { 0x005026F1, 0x004426F3 };

        private const int UiChildrenOffset = 0x10;
        private const int UiSelfOffset = 0x08;
        private const int UiFlagsOffset = 0x180;
        private const int UiRelativePositionOffset = 0x118;
        private const int UiPositionModifierOffset = 0xF0;
        private const int UiParentPtrOffset = 0xB8;
        private const int UiLocalScaleOffset = 0x130;
        private const int UiScaleIndexOffset = 0x18A;
        private const int UiUnscaledSizeOffset = 0x288;
        private const uint UiIsVisibleMask = 0x800;
        private const uint UiShouldModifyPosMask = 0x400;
        private const int UiNodeReadSize = 0x290;

        private IntPtr processHandle = IntPtr.Zero;
        private int handlePid;
        private IntPtr resolvedExpBar = IntPtr.Zero;
        private DateTime nextResolveUtc = DateTime.MinValue;

        internal bool TryGetRect(out Vector2 pos, out Vector2 size)
        {
            pos = default;
            size = default;
            if (!this.EnsureProcess())
            {
                return false;
            }

            var addr = this.GetAddress();
            if (addr == IntPtr.Zero || !this.TryReadUiNode(addr, out var el) || (el.Flags & UiIsVisibleMask) == 0)
            {
                return false;
            }

            size = this.ScaledSize(in el);
            if (size.X <= 1f || size.Y <= 1f || !this.TryScreenPosition(in el, out pos))
            {
                return false;
            }

            return !float.IsNaN(pos.X) && !float.IsNaN(pos.Y);
        }

        internal void Reset()
        {
            if (this.processHandle != IntPtr.Zero)
            {
                CloseHandle(this.processHandle);
                this.processHandle = IntPtr.Zero;
            }

            this.handlePid = 0;
            this.resolvedExpBar = IntPtr.Zero;
        }

        private IntPtr GetAddress()
        {
            if (this.resolvedExpBar != IntPtr.Zero)
            {
                if (this.ReadPtr(this.resolvedExpBar + UiSelfOffset) == this.resolvedExpBar &&
                    this.TryReadFlags(this.resolvedExpBar, out var f) &&
                    (f & ~UiIsVisibleMask) == (Fingerprints[^1] & ~UiIsVisibleMask))
                {
                    return this.resolvedExpBar;
                }

                this.resolvedExpBar = IntPtr.Zero;
            }

            if (DateTime.UtcNow < this.nextResolveUtc)
            {
                return IntPtr.Zero;
            }

            this.nextResolveUtc = DateTime.UtcNow.AddMilliseconds(500);
            var gameUi = Core.States.InGameStateObject?.GameUi?.Address ?? IntPtr.Zero;
            if (gameUi == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            this.resolvedExpBar = this.WalkFp(gameUi, 0);
            return this.resolvedExpBar;
        }

        private IntPtr WalkFp(IntPtr parent, int step)
        {
            if (step == Fingerprints.Length)
            {
                return this.IsExperienceBar(parent) ? parent : IntPtr.Zero;
            }

            if (!this.TryReadStdVector(parent + UiChildrenOffset, out var first, out var last))
            {
                return IntPtr.Zero;
            }

            long n = ((long)last - (long)first) / 8;
            if (n <= 0 || n > 4000)
            {
                return IntPtr.Zero;
            }

            uint target = Fingerprints[step] & ~UiIsVisibleMask;
            for (int pass = 0; pass < 2; pass++)
            {
                bool wantVisible = pass == 0;
                for (int i = 0; i < n; i++)
                {
                    var child = this.ReadPtr(first + (nint)(i * 8));
                    if (child == IntPtr.Zero || !this.TryReadFlags(child, out var flags))
                    {
                        continue;
                    }

                    if ((flags & ~UiIsVisibleMask) != target)
                    {
                        continue;
                    }

                    if (((flags & UiIsVisibleMask) != 0) != wantVisible)
                    {
                        continue;
                    }

                    var deeper = this.WalkFp(child, step + 1);
                    if (deeper != IntPtr.Zero)
                    {
                        return deeper;
                    }
                }
            }

            return IntPtr.Zero;
        }

        private bool IsExperienceBar(IntPtr addr)
        {
            if (!this.TryReadUiNode(addr, out var el) || (el.Flags & UiIsVisibleMask) == 0)
            {
                return false;
            }

            var size = this.ScaledSize(in el);
            return size.X > 50f && size.Y > 2f;
        }

        private struct UiNode
        {
            public uint Flags;
            public Vector2 RelativePosition;
            public Vector2 PositionModifier;
            public IntPtr ParentPtr;
            public float LocalScaleMultiplier;
            public byte ScaleIndex;
            public Vector2 UnscaledSize;
        }

        private static (float W, float H) UiScaleValue(byte index, float multiplier)
        {
            var io = ImGuiNET.ImGui.GetIO();
            float v1 = io.DisplaySize.X / 2560f;
            float v2 = io.DisplaySize.Y / 1600f;
            float w = multiplier;
            float h = multiplier;
            switch (index)
            {
                case 1: w *= v1; h *= v1; break;
                case 2: w *= v2; h *= v2; break;
                case 3: w *= v1; h *= v2; break;
            }

            return (w, h);
        }

        private Vector2 ScaledSize(in UiNode el)
        {
            var (w, h) = UiScaleValue(el.ScaleIndex, el.LocalScaleMultiplier);
            return new Vector2(el.UnscaledSize.X * w, el.UnscaledSize.Y * h);
        }

        private bool TryScreenPosition(in UiNode el, out Vector2 screen)
        {
            if (!this.TryGetUnscaledPosition(in el, 0, out var p))
            {
                screen = default;
                return false;
            }

            var (w, h) = UiScaleValue(el.ScaleIndex, el.LocalScaleMultiplier);
            screen = new Vector2(p.X * w, p.Y * h);
            return true;
        }

        private bool TryGetUnscaledPosition(in UiNode el, int depth, out Vector2 pos)
        {
            var local = el.RelativePosition;
            if (el.ParentPtr == IntPtr.Zero || depth >= 64)
            {
                pos = local;
                return true;
            }

            if (!this.TryReadUiNode(el.ParentPtr, out var parent))
            {
                pos = local;
                return false;
            }

            if (!this.TryGetUnscaledPosition(in parent, depth + 1, out var parentPos))
            {
                pos = local;
                return false;
            }

            if ((el.Flags & UiShouldModifyPosMask) != 0)
            {
                parentPos += parent.PositionModifier;
            }

            if (parent.ScaleIndex == el.ScaleIndex &&
                Math.Abs(parent.LocalScaleMultiplier - el.LocalScaleMultiplier) < 0.0001f)
            {
                pos = parentPos + local;
                return true;
            }

            var (psw, psh) = UiScaleValue(parent.ScaleIndex, parent.LocalScaleMultiplier);
            var (msw, msh) = UiScaleValue(el.ScaleIndex, el.LocalScaleMultiplier);
            pos = new Vector2(
                parentPos.X * psw / msw + local.X,
                parentPos.Y * psh / msh + local.Y);
            return true;
        }

        private bool EnsureProcess()
        {
            int pid = (int)Core.Process.Pid;
            if (pid == 0)
            {
                this.Reset();
                return false;
            }

            if (pid == this.handlePid && this.processHandle != IntPtr.Zero)
            {
                return true;
            }

            this.Reset();
            this.processHandle = OpenProcess(0x0010 | 0x0400, false, pid);
            if (this.processHandle == IntPtr.Zero)
            {
                return false;
            }

            this.handlePid = pid;
            return true;
        }

        private bool TryReadFlags(IntPtr addr, out uint flags)
        {
            flags = 0;
            var buf = new byte[4];
            if (!ReadProcessMemory(this.processHandle, addr + UiFlagsOffset, buf, 4, out _))
            {
                return false;
            }

            flags = BitConverter.ToUInt32(buf, 0);
            return true;
        }

        private bool TryReadUiNode(IntPtr addr, out UiNode node)
        {
            node = default;
            ulong u = (ulong)addr;
            if (u < 0x10000 || u > 0x7FFFFFFFFFFF)
            {
                return false;
            }

            var buf = new byte[UiNodeReadSize];
            if (!ReadProcessMemory(this.processHandle, addr, buf, (uint)UiNodeReadSize, out var got) || got < UiNodeReadSize)
            {
                return false;
            }

            node.Flags = BitConverter.ToUInt32(buf, UiFlagsOffset);
            node.RelativePosition = new Vector2(
                BitConverter.ToSingle(buf, UiRelativePositionOffset),
                BitConverter.ToSingle(buf, UiRelativePositionOffset + 4));
            node.PositionModifier = new Vector2(
                BitConverter.ToSingle(buf, UiPositionModifierOffset),
                BitConverter.ToSingle(buf, UiPositionModifierOffset + 4));
            node.ParentPtr = (IntPtr)BitConverter.ToInt64(buf, UiParentPtrOffset);
            node.LocalScaleMultiplier = BitConverter.ToSingle(buf, UiLocalScaleOffset);
            node.ScaleIndex = buf[UiScaleIndexOffset];
            node.UnscaledSize = new Vector2(
                BitConverter.ToSingle(buf, UiUnscaledSizeOffset),
                BitConverter.ToSingle(buf, UiUnscaledSizeOffset + 4));
            return true;
        }

        private IntPtr ReadPtr(IntPtr addr)
        {
            var buf = new byte[8];
            if (!ReadProcessMemory(this.processHandle, addr, buf, 8, out _))
            {
                return IntPtr.Zero;
            }

            return (IntPtr)BitConverter.ToInt64(buf, 0);
        }

        private bool TryReadStdVector(IntPtr addr, out IntPtr first, out IntPtr last)
        {
            first = IntPtr.Zero;
            last = IntPtr.Zero;
            var buf = new byte[16];
            if (!ReadProcessMemory(this.processHandle, addr, buf, 16, out _))
            {
                return false;
            }

            first = (IntPtr)BitConverter.ToInt64(buf, 0);
            last = (IntPtr)BitConverter.ToInt64(buf, 8);
            return first != IntPtr.Zero && (long)last >= (long)first;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            uint dwSize,
            out int lpNumberOfBytesRead);
    }
}
