namespace ClientPatches
{
    using ClickableTransparentOverlay.Win32;
    using GameHelper.Plugin;

    public sealed class ClientPatchesSettings : IPSettings
    {
        public bool InfiniteZoomEnabled = false;
        public bool NoAtlasFogEnabled = false;
        public bool ApplyOnGameAttach = false;
        public bool ApplyFogOnGameAttach = false;
        public bool ShowStatusWindow = false;
        public bool ToggleHotkeyEnabled = true;
        public VK ToggleHotkey = VK.F3;
        public bool FogToggleHotkeyEnabled = true;
        public VK FogToggleHotkey = VK.F1;
    }
}
