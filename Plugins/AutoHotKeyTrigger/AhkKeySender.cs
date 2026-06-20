namespace AutoHotKeyTrigger
{
    using ClickableTransparentOverlay.Win32;
    using GameHelper.Utils;

    /// <summary>
    ///     AHK key sender. Normal keys use <see cref="MiscHelper.KeyUp"/> (full tap). ESC uses a
    ///     single key-down so the pause menu is not toggled open and closed in one action.
    /// </summary>
    internal static class AhkKeySender
    {
        internal static bool SendKey(VK key, string? source = null)
        {
            if (key == VK.ESCAPE)
            {
                return MiscHelper.TrySendEscapeKeyDown(source);
            }

            return MiscHelper.KeyUp(key, source);
        }
    }
}
