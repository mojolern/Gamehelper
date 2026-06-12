namespace PlayerBuffBar
{
    using System.Collections.Generic;
    using System.Numerics;
    using GameHelper.Plugin;

    public enum BuffBarDisplayMode
    {
        Icons,
        Text,
        IconsAndText,
    }

    public sealed class PlayerBuffBarSettings : IPSettings
    {
        public int SettingsVersion = 3;

        public bool ShowOverlay = true;

        public BuffBarDisplayMode DisplayMode = BuffBarDisplayMode.Icons;

        public bool HideInTownOrHideout = true;

        public bool HideWhenGameInBackground = true;

        public bool ShowInactiveWatchlist = true;

        public bool ShowCharges = true;

        public bool ShowRage = true;

        public bool HideEmptyResources = true;

        public bool ShowDurations = true;

        public bool ShowStacks = true;

        public bool AutoDownloadWikiIcons = true;

        public float FontScale = 1f;

        public float MaxBarWidth = 260f;

        public float InactiveIconAlpha = 0.35f;

        public Vector4 ActiveColor = new(0.2f, 0.85f, 0.35f, 0.92f);

        public Vector4 InactiveColor = new(0.45f, 0.45f, 0.45f, 0.55f);

        public Vector4 ChargeTextColor = new(1f, 0.92f, 0.35f, 1f);

        public Vector4 RageTextColor = new(1f, 0.45f, 0.2f, 1f);

        // Resource row (power / frenzy / endurance charges + rage stat)
        public bool ResourceAnchorToHealthBar = true;

        public bool ResourceShowPositionDummy;

        public float ResourceIconSize = 28f;

        public float ResourceIconSpacing = 4f;

        public Vector2 ResourceScreenOffset = new(0f, 8f);

        public Vector2 ResourceFixedPosition = new(40f, 480f);

        // Buff watchlist row
        public bool BuffAnchorToHealthBar = true;

        public bool BuffShowPositionDummy;

        public float BuffIconSize = 28f;

        public float BuffIconSpacing = 4f;

        public Vector2 BuffScreenOffset = new(0f, 36f);

        public Vector2 BuffFixedPosition = new(40f, 520f);

        // Legacy fields (migrated into resource/buff settings on load)
        public bool AnchorToHealthBar = true;

        public float IconSize = 28f;

        public float IconSpacing = 4f;

        public Vector2 ScreenOffset = new(0f, 36f);

        public Vector2 FixedPosition = new(40f, 520f);

        public List<string> Watchlist = new()
        {
            "blood_rage",
            "fortify",
            "adrenaline",
            "berserk",
            "molten",
            "granite",
            "grace",
            "haste",
            "herald",
        };
    }
}
