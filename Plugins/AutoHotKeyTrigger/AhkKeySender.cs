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
    ///     AHK key sender. Non-escape keys use <see cref="MiscHelper.KeyUp"/> (full tap, shared
    ///     rate limit with AutoPot and other plugins). Escape uses WM_KEYUP only — a full tap
    ///     toggles the PoE pause menu twice.
    /// </summary>
    internal static class AhkKeySender
    {
        private const int WmKeyup = 0x101;

        private static readonly Random Rand = new();
        private static readonly Stopwatch DelayBetweenKeys = Stopwatch.StartNew();
        private static Task? sendingMessage;

        internal static bool SendKey(VK key, string? source = null)
        {
            if (key != VK.ESCAPE)
            {
                return MiscHelper.KeyUp(key, source);
            }

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

            if (!Core.Process.Foreground)
            {
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
