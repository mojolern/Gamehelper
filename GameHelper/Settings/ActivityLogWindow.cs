namespace GameHelper.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using Coroutine;
    using CoroutineEvents;
    using GameHelper.Localization;
    using GameHelper.Utils;
    using ImGuiNET;

    /// <summary>
    ///     Floating / docked activity log — stays visible when the main menu is hidden (F11).
    /// </summary>
    internal static class ActivityLogWindow
    {
        private const float LogDockWidth = 380f;

        private static bool isVisible;
        private static bool isDocked = true;
        private static bool transparentBackground;
        private static Vector2 floatingPos = new(120f, 120f);
        private static Vector2 floatingSize = new(LogDockWidth, 420f);
        private static Vector2 lastDockedPos;
        private static Vector2 lastDockedSize = new(LogDockWidth, 420f);
        private static int lastEntryCount;

        internal static bool IsVisible => isVisible;

        internal static void ToggleVisible() => isVisible = !isVisible;

        internal static void InitializeCoroutines()
        {
            CoroutineHandler.Start(RenderCoroutine(), "[ActivityLog] Draw activity log");
        }

        private static IEnumerator<Wait> RenderCoroutine()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnRender);
                if (!isVisible)
                {
                    continue;
                }

                DrawWindow();
            }
        }

        private const int TransparentStyleColorCount = 17;
        private const int TransparentStyleVarCount = 4;

        private static void PushTransparentWindowStyle()
        {
            var clear = Vector4.Zero;
            ImGui.PushStyleColor(ImGuiCol.WindowBg, clear);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, clear);
            ImGui.PushStyleColor(ImGuiCol.TitleBg, clear);
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, clear);
            ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, clear);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, clear);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, clear);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, clear);
            ImGui.PushStyleColor(ImGuiCol.Button, clear);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, clear);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, clear);
            ImGui.PushStyleColor(ImGuiCol.Border, clear);
            ImGui.PushStyleColor(ImGuiCol.Separator, clear);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, clear);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, clear);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, clear);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, clear);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 0f);
        }

        private static void PopTransparentWindowStyle()
        {
            ImGui.PopStyleVar(TransparentStyleVarCount);
            ImGui.PopStyleColor(TransparentStyleColorCount);
        }

        private static void DrawWindow()
        {
            var title = $"{OverlayLocalization.L("Activity log", "Aktivitaets-Log")}###GameHelperActivityLog";
            var windowFlags = ImGuiWindowFlags.None;

            if (isDocked && SettingsWindow.IsSettingsWindowVisible)
            {
                var pos = new Vector2(
                    SettingsWindow.MainWindowPos.X + SettingsWindow.MainWindowSize.X,
                    SettingsWindow.MainWindowPos.Y);
                var size = new Vector2(LogDockWidth, Math.Max(SettingsWindow.MainWindowSize.Y, 300f));
                lastDockedPos = pos;
                lastDockedSize = size;
                ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
                ImGui.SetNextWindowSize(size, ImGuiCond.Always);
                windowFlags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
            }
            else if (isDocked)
            {
                ImGui.SetNextWindowPos(lastDockedPos, ImGuiCond.Always);
                ImGui.SetNextWindowSize(lastDockedSize, ImGuiCond.Always);
                windowFlags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
            }
            else
            {
                ImGui.SetNextWindowPos(floatingPos, ImGuiCond.FirstUseEver);
                ImGui.SetNextWindowSize(floatingSize, ImGuiCond.FirstUseEver);
            }

            var useTransparentChrome = transparentBackground;
            if (useTransparentChrome)
            {
                ImGui.SetNextWindowBgAlpha(0f);
                PushTransparentWindowStyle();
            }

            var open = isVisible;
            try
            {
                if (ImGui.Begin(title, ref open, windowFlags))
                {
                    isVisible = open;
                    if (!isDocked)
                    {
                        floatingPos = ImGui.GetWindowPos();
                        floatingSize = ImGui.GetWindowSize();
                    }

                    if (ImGui.Button(OverlayLocalization.L("Clear", "Leeren")))
                    {
                        ActivityLog.Clear();
                        lastEntryCount = 0;
                    }

                    ImGui.SameLine();
                    if (isDocked)
                    {
                        if (ImGui.Button(OverlayLocalization.L("Undock", "Loesen")))
                        {
                            isDocked = false;
                            floatingPos = ImGui.GetWindowPos();
                            floatingSize = ImGui.GetWindowSize();
                        }
                    }
                    else if (ImGui.Button(OverlayLocalization.L("Dock right", "Rechts andocken")))
                    {
                        isDocked = true;
                    }

                    ImGui.SameLine();
                    ImGui.Checkbox(
                        OverlayLocalization.L("Transparent", "Transparent"),
                        ref transparentBackground);

                    if (!useTransparentChrome)
                    {
                        ImGui.Separator();
                    }

                    ImGui.BeginChild("activityLogScroll", Vector2.Zero, ImGuiChildFlags.None);
                    var lines = ActivityLog.Snapshot();
                    foreach (var line in lines)
                    {
                        ImGui.TextWrapped(line);
                    }

                    if (lines.Length > lastEntryCount)
                    {
                        ImGui.SetScrollHereY(1f);
                        lastEntryCount = lines.Length;
                    }

                    ImGui.EndChild();
                    ImGui.End();
                }
                else
                {
                    isVisible = open;
                }
            }
            finally
            {
                if (useTransparentChrome)
                {
                    PopTransparentWindowStyle();
                }
            }
        }

    }
}
