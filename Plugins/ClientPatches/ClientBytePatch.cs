namespace ClientPatches
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Runtime.InteropServices;

    internal class ClientBytePatch : IDisposable
    {
        private const uint ProcessQueryInformation = 0x0400;
        private const uint ProcessVmOperation = 0x0008;
        private const uint ProcessVmRead = 0x0010;
        private const uint ProcessVmWrite = 0x0020;
        private const uint PageExecuteReadWrite = 0x40;
        private const int ChunkSize = 256 * 1024;

        private readonly string displayName;
        private readonly byte?[] pattern;
        private readonly byte[] patchBytes;
        private readonly int targetOffset;

        private IntPtr handle = IntPtr.Zero;
        private uint pid;
        private IntPtr patchAddress = IntPtr.Zero;
        private byte[]? originalBytes;

        protected ClientBytePatch(string displayName, byte?[] pattern, int targetOffset, byte[] patchBytes)
        {
            this.displayName = displayName;
            this.pattern = pattern;
            this.targetOffset = targetOffset;
            this.patchBytes = patchBytes;
            this.Status = "Not scanned";
        }

        public PatchState State { get; private set; } = PatchState.NotScanned;

        public string Status { get; private set; }

        public bool Applied { get; private set; }

        public IntPtr PatchAddress => this.patchAddress;

        public bool Scan(uint targetPid)
        {
            this.ResetForPidIfNeeded(targetPid);
            if (!this.EnsureHandle(targetPid))
            {
                return false;
            }

            try
            {
                using var process = Process.GetProcessById((int)targetPid);
                var module = process.MainModule;
                if (module == null)
                {
                    return this.Fail("Main module unavailable");
                }

                var found = this.FindPattern(module.BaseAddress, module.ModuleMemorySize);
                if (found == IntPtr.Zero)
                {
                    this.patchAddress = IntPtr.Zero;
                    this.State = PatchState.PatternNotFound;
                    this.Status = $"{this.displayName} pattern not found";
                    return false;
                }

                this.patchAddress = found + this.targetOffset;
                this.State = this.Applied ? PatchState.Applied : PatchState.Ready;
                this.Status = $"{this.displayName} found at 0x{this.patchAddress.ToInt64():X}";
                return true;
            }
            catch (Exception ex)
            {
                return this.Fail($"Scan failed: {ex.Message}");
            }
        }

        public bool Apply(uint targetPid)
        {
            this.ResetForPidIfNeeded(targetPid);
            if (this.patchAddress == IntPtr.Zero && !this.Scan(targetPid))
            {
                return false;
            }

            if (!this.EnsureHandle(targetPid))
            {
                return false;
            }

            if (this.Applied)
            {
                this.State = PatchState.Applied;
                this.Status = $"{this.displayName} already applied at 0x{this.patchAddress.ToInt64():X}";
                return true;
            }

            this.originalBytes ??= this.ReadBytes(this.patchAddress, this.patchBytes.Length);
            if (this.originalBytes == null)
            {
                return this.Fail($"Failed to read original bytes at 0x{this.patchAddress.ToInt64():X}");
            }

            if (!this.WriteBytes(this.patchAddress, this.patchBytes))
            {
                return false;
            }

            this.Applied = true;
            this.State = PatchState.Applied;
            this.Status = $"{this.displayName} applied at 0x{this.patchAddress.ToInt64():X}";
            return true;
        }

        public bool Restore()
        {
            if (!this.Applied)
            {
                this.State = this.patchAddress == IntPtr.Zero ? PatchState.NotScanned : PatchState.Ready;
                this.Status = $"{this.displayName} is not active";
                return true;
            }

            if (this.patchAddress == IntPtr.Zero || this.originalBytes == null)
            {
                return this.Fail("Cannot restore; original bytes are missing");
            }

            if (!this.WriteBytes(this.patchAddress, this.originalBytes))
            {
                return false;
            }

            this.Applied = false;
            this.State = PatchState.Restored;
            this.Status = $"{this.displayName} restored";
            return true;
        }

        public void Dispose()
        {
            try
            {
                if (this.Applied)
                {
                    this.Restore();
                }
            }
            catch
            {
                // Best-effort cleanup. Do not throw from plugin shutdown.
            }

            this.CloseHandle();
        }

        private void ResetForPidIfNeeded(uint targetPid)
        {
            if (this.pid == targetPid)
            {
                return;
            }

            if (this.Applied)
            {
                this.Restore();
            }

            this.CloseHandle();
            this.pid = 0;
            this.patchAddress = IntPtr.Zero;
            this.originalBytes = null;
            this.Applied = false;
            this.State = PatchState.NotScanned;
            this.Status = "Not scanned";
        }

        private bool EnsureHandle(uint targetPid)
        {
            if (targetPid == 0)
            {
                return this.Fail("Game process is not attached");
            }

            if (this.handle != IntPtr.Zero && this.pid == targetPid)
            {
                return true;
            }

            this.CloseHandle();
            this.handle = OpenProcess(
                ProcessQueryInformation | ProcessVmRead | ProcessVmWrite | ProcessVmOperation,
                false,
                targetPid);
            if (this.handle == IntPtr.Zero)
            {
                return this.Fail($"OpenProcess failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            }

            this.pid = targetPid;
            return true;
        }

        private IntPtr FindPattern(IntPtr baseAddress, int moduleSize)
        {
            var overlap = this.pattern.Length - 1;
            var previousTail = Array.Empty<byte>();
            var offset = 0;

            while (offset < moduleSize)
            {
                var readSize = Math.Min(ChunkSize, moduleSize - offset);
                var chunkAddress = baseAddress + offset;
                var chunk = this.ReadBytes(chunkAddress, readSize);
                if (chunk == null || chunk.Length == 0)
                {
                    offset += readSize;
                    previousTail = Array.Empty<byte>();
                    continue;
                }

                var scanBuffer = new byte[previousTail.Length + chunk.Length];
                Buffer.BlockCopy(previousTail, 0, scanBuffer, 0, previousTail.Length);
                Buffer.BlockCopy(chunk, 0, scanBuffer, previousTail.Length, chunk.Length);

                var match = IndexOf(scanBuffer, this.pattern);
                if (match >= 0)
                {
                    var absoluteOffset = offset - previousTail.Length + match;
                    return baseAddress + absoluteOffset;
                }

                var tailLen = Math.Min(overlap, chunk.Length);
                previousTail = new byte[tailLen];
                Buffer.BlockCopy(chunk, chunk.Length - tailLen, previousTail, 0, tailLen);
                offset += readSize;
            }

            return IntPtr.Zero;
        }

        private static int IndexOf(byte[] haystack, byte?[] pattern)
        {
            if (haystack.Length < pattern.Length)
            {
                return -1;
            }

            for (var i = 0; i <= haystack.Length - pattern.Length; i++)
            {
                var matched = true;
                for (var j = 0; j < pattern.Length; j++)
                {
                    var expected = pattern[j];
                    if (expected.HasValue && haystack[i + j] != expected.Value)
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return i;
                }
            }

            return -1;
        }

        private byte[]? ReadBytes(IntPtr address, int count)
        {
            var buffer = new byte[count];
            return ReadProcessMemory(this.handle, address, buffer, count, out var read) && read == count
                ? buffer
                : null;
        }

        private bool WriteBytes(IntPtr address, byte[] bytes)
        {
            if (address == IntPtr.Zero || bytes.Length == 0)
            {
                return this.Fail("Invalid write target");
            }

            if (!VirtualProtectEx(this.handle, address, (UIntPtr)bytes.Length, PageExecuteReadWrite, out var oldProtect))
            {
                return this.Fail($"VirtualProtectEx failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            }

            var ok = WriteProcessMemory(this.handle, address, bytes, bytes.Length, out var written) && written == bytes.Length;
            VirtualProtectEx(this.handle, address, (UIntPtr)bytes.Length, oldProtect, out _);

            return ok || this.Fail($"WriteProcessMemory failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }

        private bool Fail(string message)
        {
            this.State = PatchState.Error;
            this.Status = message;
            return false;
        }

        private void CloseHandle()
        {
            if (this.handle == IntPtr.Zero)
            {
                return;
            }

            CloseHandle(this.handle);
            this.handle = IntPtr.Zero;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out int lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
