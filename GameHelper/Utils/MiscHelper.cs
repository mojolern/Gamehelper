// <copyright file="MiscHelper.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Utils
{
    using ClickableTransparentOverlay.Win32;
    using CTOUtils = ClickableTransparentOverlay.Win32.Utils;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    ///     Util class to send keyboard/mouse keys to the game.
    /// </summary>
    public static class MiscHelper
    {
        private const int WmKeyup = 0x101;
        private const int WmChar = 0x102;
        private const uint CfUnicodeText = 13;
        private const uint GmemMoveable = 2;
        private const uint InputKeyboard = 1;
        private const uint KeyeventfKeyup = 0x0002;
        private const ushort VkReturn = 0x0D;
        private const ushort VkControl = 0x11;
        private const ushort VkV = 0x56;
        private const int ChatOpenDelayMs = 50;
        private const int ChatPasteDelayMs = 25;
        private const int ChatSendDelayMs = 10;
        private const int ChatFocusDelayMs = 10;

        private static readonly Random Rand = new();
        private static readonly Stopwatch DelayBetweenKeys = Stopwatch.StartNew();
        private static readonly object KeySendLock = new();
        private static Task? chatSendTask;
        private static bool chatSequenceReserved;

        private static readonly VK[] GameplayKeys =
        {
            VK.KEY_1, VK.KEY_2, VK.KEY_3, VK.KEY_4, VK.KEY_5,
            VK.KEY_6, VK.KEY_7, VK.KEY_8, VK.KEY_9, VK.KEY_0,
            VK.KEY_Q, VK.KEY_W, VK.KEY_E, VK.KEY_R, VK.KEY_T,
            VK.KEY_F, VK.SPACE,
        };

        internal static void ActiveSkillGemDataParser(
            uint unknownIdAndEquipmentInfo,
            out bool isUserEquipped,
            out byte Unknown0,
            out byte socketIndex,
            out byte linkId,
            out byte inventoryName,
            out uint activeSkillGemUnknownId)
        {
            activeSkillGemUnknownId = unknownIdAndEquipmentInfo >> 0x10;
            unknownIdAndEquipmentInfo &= 0x0000FFFF;

            inventoryName = (byte)((unknownIdAndEquipmentInfo & 0x007F) + 1);
            unknownIdAndEquipmentInfo >>= 0x07;

            linkId = (byte)(unknownIdAndEquipmentInfo & 0x07);
            unknownIdAndEquipmentInfo >>= 0x03;

            socketIndex = (byte)(unknownIdAndEquipmentInfo & 0x07);
            unknownIdAndEquipmentInfo >>= 0x03;

            Unknown0 = (byte)(unknownIdAndEquipmentInfo & 0x03);
            unknownIdAndEquipmentInfo >>= 0x02;

            isUserEquipped = unknownIdAndEquipmentInfo > 0;
        }

        internal static bool TryConvertStringToImGuiGlyphRanges(string data, out ushort[] ranges)
        {
            if (string.IsNullOrEmpty(data))
            {
                ranges = Array.Empty<ushort>();
                return false;
            }

            var intsInHex = data.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            ranges = new ushort[intsInHex.Length];
            for (var i = 0; i < intsInHex.Length; i++)
            {
                try
                {
                    ranges[i] = (ushort)Convert.ToInt32(intsInHex[i], 16);
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return ranges[^1] == 0x00;
        }

        /// <summary>
        ///     Utility function that returns randomly generated string.
        /// </summary>
        /// <returns>randomly generated string.</returns>
        internal static string GenerateRandomString()
        {
            //more common letters!
            const string characters = "qwertyuiopasdfghjklzxcvbnm" + "eioadfc";
            var random = new Random();

            char GetRandomCharacter()
            {
                return characters[random.Next(0, characters.Length)];
            }

            string GetWord()
            {
                return char.ToUpperInvariant(GetRandomCharacter()) +
                       new string(Enumerable.Range(0, random.Next(5, 10))
                                            .Select(_ => GetRandomCharacter())
                                            .ToArray());
            }

            return string.Join(' ', Enumerable.Range(0, random.Next(1, 4)).Select(_ => GetWord()));
        }

        /// <summary>
        ///     Presses a key in the game by posting a single <c>WM_KEYUP</c> message.
        ///     <para>
        ///     The game registers an activation on every key-transition message it receives, so a
        ///     full down+up tap (whether via <c>SendMessage</c> or <c>SendInput</c>) fires the bound
        ///     action twice. Sending only the key-up — the long-standing GameHelper2 upstream
        ///     behaviour — produces exactly one activation. There is a hard delay between presses so
        ///     the game doesn't kick us for sending too many.
        ///     </para>
        /// </summary>
        /// <param name="key">key to press.</param>
        /// <param name="source">optional label for the activity log (e.g. plugin/rule name).</param>
        /// <returns><see langword="true"/> when the key was sent to the focused game window.</returns>
        public static bool KeyUp(VK key, string? source = null)
        {
            var label = string.IsNullOrWhiteSpace(source) ? "GameHelper" : source.Trim();

            if (Core.GHSettings.EnableControllerMode)
            {
                return false;
            }

            if (chatSequenceReserved)
            {
                return false;
            }

            if (Core.Process.Address == IntPtr.Zero)
            {
                ActivityLog.Write("Input", $"{label}: key {key} not sent (game not loaded)");
                return false;
            }

            if (!Core.Process.Foreground)
            {
                return false;
            }

            var hwnd = Core.Process.Information.MainWindowHandle;
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            if (IsBlockingPhysicalKeyHeld(key))
            {
                return false;
            }

            lock (KeySendLock)
            {
                if (DelayBetweenKeys.ElapsedMilliseconds < Core.GHSettings.KeyPressTimeout + Rand.Next() % 10)
                {
                    return false;
                }

                SendMessage(hwnd, WmKeyup, (int)key, 0);
                DelayBetweenKeys.Restart();
            }

            ActivityLog.Write("Input", $"{label}: key {key} sent to game");
            return true;
        }

        internal static bool IsChatSequenceRunning => chatSequenceReserved;

        /// <summary>
        ///     Opens chat and sends a slash command to the game.
        /// </summary>
        /// <param name="command">chat command without leading slash (e.g. <c>hideout</c>).</param>
        /// <param name="source">optional label for the activity log.</param>
        /// <returns><see langword="true"/> when the command sequence was started.</returns>
        public static bool TrySendChatCommand(string command, string? source = null)
        {
            var label = string.IsNullOrWhiteSpace(source) ? "GameHelper" : source.Trim();

            if (string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            if (Core.GHSettings.EnableControllerMode || chatSequenceReserved)
            {
                return false;
            }

            if (chatSendTask != null && !chatSendTask.IsCompleted)
            {
                return false;
            }

            if (DelayBetweenKeys.ElapsedMilliseconds < Core.GHSettings.KeyPressTimeout + Rand.Next() % 10)
            {
                return false;
            }

            if (Core.Process.Address == IntPtr.Zero)
            {
                ActivityLog.Write("Input", $"{label}: chat command not sent (game not loaded)");
                return false;
            }

            DelayBetweenKeys.Restart();
            chatSequenceReserved = true;
            var hwnd = Core.Process.Information.MainWindowHandle;
            var chatText = "/" + command.TrimStart('/');
            chatSendTask = Task.Run(() =>
            {
                var savedClipboard = TryReadClipboardTextWithRetry();
                try
                {
                    if (TrySendChatCommandViaSendInput(hwnd, chatText))
                    {
                        ActivityLog.Write("Input", $"{label}: chat command {chatText} sent to game (paste)");
                    }
                    else
                    {
                        SendChatCommandViaWmChar(hwnd, chatText);
                        ActivityLog.Write("Input", $"{label}: chat command {chatText} sent to game (typed)");
                    }
                }
                finally
                {
                    if (savedClipboard != null)
                    {
                        TrySetClipboardTextWithRetry(savedClipboard);
                    }

                    chatSequenceReserved = false;
                }

                return IntPtr.Zero;
            });

            return true;
        }

        private static bool TrySendChatCommandViaSendInput(IntPtr hwnd, string chatText)
        {
            if (!TrySetClipboardTextWithRetry(chatText))
            {
                return false;
            }

            FocusGameWindow(hwnd);
            Thread.Sleep(ChatFocusDelayMs);

            if (!SendInputKeyTap(VkReturn))
            {
                return false;
            }

            Thread.Sleep(ChatOpenDelayMs);

            if (!SendInputCtrlChord(VkV))
            {
                return false;
            }

            Thread.Sleep(ChatPasteDelayMs);

            return SendInputKeyTap(VkReturn);
        }

        private static void SendChatCommandViaWmChar(IntPtr hwnd, string chatText)
        {
            SendGameKeyUp(hwnd, VK.RETURN);
            Thread.Sleep(ChatOpenDelayMs);
            foreach (var c in chatText)
            {
                SendGameChar(hwnd, c);
            }

            Thread.Sleep(ChatSendDelayMs);
            SendGameKeyUp(hwnd, VK.RETURN);
        }

        private static bool IsBlockingPhysicalKeyHeld(VK keyToSend)
        {
            if (CTOUtils.IsKeyPressed(keyToSend))
            {
                return true;
            }

            foreach (var vk in GameplayKeys)
            {
                if (vk == keyToSend)
                {
                    continue;
                }

                if (CTOUtils.IsKeyPressed(vk))
                {
                    return true;
                }
            }

            return false;
        }

        private static void SendGameKeyUp(IntPtr hwnd, VK key)
        {
            SendMessage(hwnd, WmKeyup, (int)key, 0);
        }

        private static void SendGameChar(IntPtr hwnd, char c)
        {
            SendMessage(hwnd, WmChar, c, 0);
        }

        private static void FocusGameWindow(IntPtr hwnd)
        {
            var foregroundWindow = GetForegroundWindow();
            var foregroundThread = GetWindowThreadProcessId(foregroundWindow, out _);
            var targetThread = GetWindowThreadProcessId(hwnd, out _);
            var currentThread = GetCurrentThreadId();
            AttachThreadInput(currentThread, targetThread, true);
            if (foregroundWindow != hwnd)
            {
                AttachThreadInput(foregroundThread, targetThread, true);
            }

            try
            {
                SetForegroundWindow(hwnd);
            }
            finally
            {
                AttachThreadInput(currentThread, targetThread, false);
                if (foregroundWindow != hwnd)
                {
                    AttachThreadInput(foregroundThread, targetThread, false);
                }
            }
        }

        private static bool SendInputKeyTap(ushort virtualKey)
        {
            var inputs = new Input[2];
            inputs[0].type = InputKeyboard;
            inputs[0].U.ki.wVk = virtualKey;
            inputs[1].type = InputKeyboard;
            inputs[1].U.ki.wVk = virtualKey;
            inputs[1].U.ki.dwFlags = KeyeventfKeyup;
            return SendInput(2, inputs, Marshal.SizeOf<Input>()) == 2;
        }

        private static bool SendInputCtrlChord(ushort virtualKey)
        {
            var inputs = new Input[4];
            inputs[0].type = InputKeyboard;
            inputs[0].U.ki.wVk = VkControl;
            inputs[1].type = InputKeyboard;
            inputs[1].U.ki.wVk = virtualKey;
            inputs[2].type = InputKeyboard;
            inputs[2].U.ki.wVk = virtualKey;
            inputs[2].U.ki.dwFlags = KeyeventfKeyup;
            inputs[3].type = InputKeyboard;
            inputs[3].U.ki.wVk = VkControl;
            inputs[3].U.ki.dwFlags = KeyeventfKeyup;
            return SendInput(4, inputs, Marshal.SizeOf<Input>()) == 4;
        }

        private static string? TryReadClipboardTextWithRetry(int attempts = 8)
        {
            for (var i = 0; i < attempts; i++)
            {
                var text = TryReadClipboardText();
                if (text != null)
                {
                    return text;
                }

                Thread.Sleep(3);
            }

            return null;
        }

        private static bool TrySetClipboardTextWithRetry(string text, int attempts = 8)
        {
            for (var i = 0; i < attempts; i++)
            {
                if (TrySetClipboardText(text))
                {
                    return true;
                }

                Thread.Sleep(3);
            }

            return false;
        }

        private static string? TryReadClipboardText()
        {
            if (!OpenClipboard(GetClipboardOwner()))
            {
                return null;
            }

            try
            {
                var handle = GetClipboardData(CfUnicodeText);
                if (handle == IntPtr.Zero)
                {
                    return string.Empty;
                }

                var pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero)
                {
                    return string.Empty;
                }

                try
                {
                    return Marshal.PtrToStringUni(pointer) ?? string.Empty;
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }

        private static bool TrySetClipboardText(string text)
        {
            if (!OpenClipboard(GetClipboardOwner()))
            {
                return false;
            }

            try
            {
                EmptyClipboard();
                var bytes = Encoding.Unicode.GetBytes(text + '\0');
                var global = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
                if (global == IntPtr.Zero)
                {
                    return false;
                }

                var target = GlobalLock(global);
                if (target == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    Marshal.Copy(bytes, 0, target, bytes.Length);
                }
                finally
                {
                    GlobalUnlock(global);
                }

                return SetClipboardData(CfUnicodeText, global) != IntPtr.Zero;
            }
            finally
            {
                CloseClipboard();
            }
        }

        private static IntPtr GetClipboardOwner()
        {
            return Core.Process.Address != IntPtr.Zero
                ? Core.Process.Information.MainWindowHandle
                : IntPtr.Zero;
        }

        /// <summary>
        ///     Kills the IPV4 TCP Connection for the process.
        /// </summary>
        /// <param name="processId">process Id whos tcp connection to kill.</param>
        /// <param name="source">optional label for the activity log.</param>
        public static void KillTCPConnectionForProcess(uint processId, string? source = null)
        {
            var label = string.IsNullOrWhiteSpace(source) ? "GameHelper" : source.Trim();
            MibTcprowOwnerPid[] table;
            var afInet = 2;
            var buffSize = 0;
            var ret = GetExtendedTcpTable(IntPtr.Zero, ref buffSize, true, afInet, TcpTableClass.TcpTableOwnerPidAll);
            var buffTable = Marshal.AllocHGlobal(buffSize);
            try
            {
                ret = GetExtendedTcpTable(buffTable, ref buffSize, true, afInet, TcpTableClass.TcpTableOwnerPidAll);
                if (ret != 0)
                {
                    return;
                }

                var tab = Marshal.PtrToStructure<MibTcptableOwnerPid>(buffTable);
                var rowPtr = (IntPtr)((long)buffTable + Marshal.SizeOf(tab.DwNumEntries));
                table = new MibTcprowOwnerPid[tab.DwNumEntries];
                for (var i = 0; i < tab.DwNumEntries; i++)
                {
                    var tcpRow = Marshal.PtrToStructure<MibTcprowOwnerPid>(rowPtr);
                    table[i] = tcpRow;
                    rowPtr = (IntPtr)((long)rowPtr + Marshal.SizeOf(tcpRow));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffTable);
            }

            // Kill Path Connection
            var pathConnection = table.FirstOrDefault(t => t.OwningPid == processId);
            if (!EqualityComparer<MibTcprowOwnerPid>.Default.Equals(pathConnection, default))
            {
                pathConnection.State = 12;
                var ptr = Marshal.AllocCoTaskMem(Marshal.SizeOf(pathConnection));
                Marshal.StructureToPtr(pathConnection, ptr, false);
                _ = SetTcpEntry(ptr);
                Marshal.FreeCoTaskMem(ptr);
                ActivityLog.Write("Network", $"{label}: game connection dropped (logout)");
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable,
            ref int dwOutBufLen,
            bool sort,
            int ipVersion,
            TcpTableClass tblClass,
            uint reserved = 0
        );

        [DllImport("iphlpapi.dll")] private static extern int SetTcpEntry(IntPtr pTcprow);

        private enum TcpTableClass
        {
            TcpTableBasicListener,
            TcpTableBasicConnections,
            TcpTableBasicAll,
            TcpTableOwnerPidListener,
            TcpTableOwnerPidConnections,
            TcpTableOwnerPidAll,
            TcpTableOwnerModuleListener,
            TcpTableOwnerModuleConnections,
            TcpTableOwnerModuleAll
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MibTcprowOwnerPid
        {
            public uint State;
            public readonly uint LocalAddr;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public readonly byte[] LocalPort;

            public readonly uint RemoteAddr;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public readonly byte[] RemotePort;

            public readonly uint OwningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct MibTcptableOwnerPid
        {
            public readonly uint DwNumEntries;
            private readonly MibTcprowOwnerPid table;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HardwareInput
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        // The native INPUT union is sized to its largest member (MOUSEINPUT). All three
        // variants MUST be declared so Marshal.SizeOf<Input>() equals the real sizeof(INPUT)
        // (40 bytes on x64). With only the keyboard variant it was 32 bytes, so every
        // SendInput call was rejected with ERROR_INVALID_PARAMETER (87) and silently injected
        // nothing — masking failures behind the SendMessage/WmChar fallbacks.
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MouseInput mi;

            [FieldOffset(0)]
            public KeyboardInput ki;

            [FieldOffset(0)]
            public HardwareInput hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint type;
            public InputUnion U;
        }
    }
}