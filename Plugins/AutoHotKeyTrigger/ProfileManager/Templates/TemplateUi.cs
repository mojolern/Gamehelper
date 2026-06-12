namespace AutoHotKeyTrigger.ProfileManager.Templates
{
    using System;
    using System.Numerics;
    using ImGuiNET;

    /// <summary>
    ///     Shared layout helpers for condition template popups.
    /// </summary>
    internal static class TemplateUi
    {
        /// <summary>Fixed popup width so dialogs do not stretch across the whole overlay.</summary>
        internal const float PopupWidth = 420f;

        internal static void PrepareConditionPopup()
        {
            ImGui.SetNextWindowSize(new Vector2(PopupWidth, 0), ImGuiCond.Always);
            ImGui.SetNextWindowSizeConstraints(
                new Vector2(PopupWidth, 0),
                new Vector2(PopupWidth, 600));
        }

        internal static float ContentWidth()
        {
            var padding = ImGui.GetStyle().WindowPadding.X * 2f;
            var avail = ImGui.GetContentRegionAvail().X;
            if (avail > 1f)
            {
                return Math.Min(avail, PopupWidth - padding);
            }

            return PopupWidth - padding;
        }

        internal static float FieldWidth(float fraction = 1f)
        {
            return ContentWidth() * Math.Clamp(fraction, 0.1f, 1f);
        }

        internal static bool AddButton(string id = "##TemplateAdd")
        {
            return ImGui.Button($"Add{id}", new Vector2(Math.Max(80f, ImGui.GetFontSize() * 5f), 0));
        }
    }
}
