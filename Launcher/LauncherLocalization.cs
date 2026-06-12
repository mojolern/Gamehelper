namespace Launcher
{
    internal enum LauncherLanguage
    {
        English,
        German,
    }

    internal static class LauncherLocalization
    {
        private static LauncherLanguage language = LauncherLanguage.English;

        internal static LauncherLanguage Language
        {
            get => language;
            set => language = value;
        }

        internal static void ToggleLanguage()
        {
            language = language == LauncherLanguage.English
                ? LauncherLanguage.German
                : LauncherLanguage.English;
        }

        internal static string LanguageToggleLabel =>
            language == LauncherLanguage.English ? "Deutsch" : "English";

        internal static string L(string english, string german) =>
            language == LauncherLanguage.German ? german : english;
    }
}
