namespace FarmTracker
{
    using System.Numerics;
    using GameHelper.Plugin;

    public enum FarmOverlayCurrency
    {
        Divine = 0,
        Exalted = 1,
        Chaos = 2,
    }

    public enum FarmOverlayMode
    {
        /// <summary>Map line on maps, session line in town/hideout.</summary>
        Auto = 0,

        MapOnly = 1,
        SessionOnly = 2,
        Hidden = 3,
    }

    public enum FarmOverlayAnchor
    {
        ExperienceBar = 0,
        Custom = 1,
    }

    public sealed class FarmTrackerSettings : IPSettings
    {
        public FarmOverlayMode OverlayMode = FarmOverlayMode.Auto;
        public FarmOverlayAnchor OverlayAnchor = FarmOverlayAnchor.ExperienceBar;
        public bool BarOnRight = true;
        public float BarBottomOffset = 5f;
        public float BarOpacity = 0.55f;
        public float OverlayFontScale = 1f;
        public Vector2 CustomOverlayPosition = new(40f, 120f);

        public bool OverlayShowKills = true;
        public bool OverlayShowProfitPerHour = true;
        public bool ShowCurrencyIcons = true;

        public FarmOverlayCurrency DisplayCurrency = FarmOverlayCurrency.Divine;
        public int PriceSource = FarmPriceFetcher.SourcePoe2Scout;
        public string League = "Runes of Aldur";
        public int PriceRefreshMinutes = 5;
        public bool UseMetaArtForPricing = true;

        public bool PauseTimerInTownOrHideout = true;
        public bool PauseTimerWhenGameInBackground;
        public bool HideOverlayWhenGameInBackground = true;
        public bool PauseTimerWhenGamePaused = true;
        public bool CountKillsInTownOrHideout;

        public int MapHistorySize = 50;
        public int MaxSessions = 30;
        public bool ShowUnpricedItems = true;

        public Vector4 ProfitColor = new(0.35f, 1f, 0.5f, 1f);
        public Vector4 LossColor = new(1f, 0.45f, 0.35f, 1f);
        public Vector4 TextColor = new(1f, 1f, 1f, 1f);
        public Vector4 UnpricedColor = new(1f, 0.85f, 0.2f, 1f);
    }
}
