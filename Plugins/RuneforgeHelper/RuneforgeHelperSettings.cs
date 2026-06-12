namespace RuneforgeHelper
{
    using System;
    using System.Numerics;
    using GameHelper.Plugin;

    public enum RewardColorMode
    {
        Off = 0,
        Relative = 1,
        Absolute = 2,
    }

    public sealed class RuneforgeHelperSettings : IPSettings
    {
        public const float DefaultPriceFontScale = 1.42f;
        public const float DefaultPriceOffsetX = 0f;
        public const float DefaultPriceOffsetY = 0f;
        public static readonly Vector4 DefaultPriceTextColor = new(0.67f, 0.04f, 0.04f, 1f);
        public static readonly Vector4 DefaultPriceBackgroundColor = new(0f, 0f, 0f, 0.85f);
        public const int DefaultDisplayCurrency = 1;
        public const RewardColorMode DefaultColorMode = RewardColorMode.Off;

        public string League = "Runes of Aldur";

        // 0 = poe.ninja, 1 = poe2scout
        public int PriceSource = 1;

        public int CacheTtlMinutes = 15;

        public DateTime LastSyncUtc = DateTime.MinValue;

        // 0 = Divine, 1 = Exalted
        public int DisplayCurrency = 1;

        // Matches PoE2 runeshape row font size relative to ImGui base font.
        public float PriceFontScale = DefaultPriceFontScale;

        // Fine-tune around the built-in anchor (left of reward text). Negative X = further left.
        public float PriceOffsetX = DefaultPriceOffsetX;

        public float PriceOffsetY = DefaultPriceOffsetY;

        public Vector4 PriceTextColor = DefaultPriceTextColor;

        public bool ShowPriceBackground = true;

        public Vector4 PriceBackgroundColor = DefaultPriceBackgroundColor;

        public RewardColorMode ColorMode = DefaultColorMode;

        public bool ShowWindow;

        public void ApplyDisplayDefaults()
        {
            this.DisplayCurrency = DefaultDisplayCurrency;
            this.PriceFontScale = DefaultPriceFontScale;
            this.PriceOffsetX = DefaultPriceOffsetX;
            this.PriceOffsetY = DefaultPriceOffsetY;
            this.PriceTextColor = DefaultPriceTextColor;
            this.ShowPriceBackground = true;
            this.PriceBackgroundColor = DefaultPriceBackgroundColor;
            this.ColorMode = DefaultColorMode;
        }
    }
}
