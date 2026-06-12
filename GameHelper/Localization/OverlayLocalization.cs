namespace GameHelper.Localization
{
    /// <summary>
    ///     Zentrale Uebersetzung fuer Overlay und Einstellungen.
    /// </summary>
    public static class OverlayLocalization
    {
        public static bool IsGerman => Core.GHSettings.OverlayLanguage == OverlayLanguage.German;

        public static string L(string english, string german) => IsGerman ? german : english;
    }
}
