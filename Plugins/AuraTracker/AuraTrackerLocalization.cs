namespace AuraTracker;

using GameHelper.Localization;

internal static class AuraTrackerLocalization
{
    internal static string L(string english, string german) => OverlayLocalization.L(english, german);

    internal static readonly string[] RarityNamesEn = { "Normal", "Magic", "Rare", "Unique" };

    internal static readonly string[] RarityNamesDe = { "Normal", "Magisch", "Selten", "Einzigartig" };

    internal static string[] RarityNames => OverlayLocalization.IsGerman ? RarityNamesDe : RarityNamesEn;
}
