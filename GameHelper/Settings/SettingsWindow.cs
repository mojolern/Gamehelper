// <copyright file="SettingsWindow.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using ClickableTransparentOverlay;
    using ClickableTransparentOverlay.Win32;
    using Coroutine;
    using CoroutineEvents;
    using ImGuiNET;
    using Plugin;
    using Utils;
    using GameOffsets.Objects.States.InGameState;
    using GameHelper.RemoteEnums.Entity;
    using GameHelper.RemoteEnums;
    using GameHelper.Localization;
    using GameHelper.Ui;

    /// <summary>
    ///     Creates the MainMenu on the UI.
    /// </summary>
    internal static class SettingsWindow
    {
        private static bool isOverlayRunningLocal = true;
        private static bool isSettingsWindowVisible = true;
        private static bool isGeneralWindowVisible;
        private static bool isPluginsWindowVisible;
        private static Vector2 mainWindowPos;
        private static Vector2 mainWindowSize;

        internal static Vector2 MainWindowPos => mainWindowPos;

        internal static Vector2 MainWindowSize => mainWindowSize;

        internal static bool IsSettingsWindowVisible => isSettingsWindowVisible;

        private const float GeneralDockWidth = 520f;
        private const float PluginsDockWidth = 460f;

        private static EntityFilterType efilterType = EntityFilterType.PATH;
        private static string filterText = string.Empty;
        private static Rarity erarity = Rarity.Normal;
        private static GameStats eStats = 0;
        private static int filterGroup = 0;

        private static string specialNpcPath = string.Empty;

        private static string specialMiscObjPath = string.Empty;

        private static string monterPathToIgnore = string.Empty;

#if DEBUG
        private static string pluginForHotReload = string.Empty;
        private static bool pluginLoaded = true;
        private static bool showImGuiDemo = false;
#endif

        /// <summary>
        ///     Initializes the Main Menu.
        /// </summary>
        internal static void InitializeCoroutines()
        {
            HideOnStartCheck();
            CoroutineHandler.Start(SaveCoroutine());
            Core.CoroutinesRegistrar.Add(CoroutineHandler.Start(
                RenderCoroutine(),
                "[Settings] Draw Core/Plugin settings",
                int.MaxValue));
        }

        private static void DrawManuBar()
        {
            if (!ImGui.BeginMenuBar())
            {
                return;
            }

            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
            ImGui.Text($"GameHelper {Core.GetVersion()}");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.TextDisabled("|");
            ImGui.SameLine();
            ImGui.TextDisabled($"{OverlayLocalization.L("Hide/show menu", "Menue ein/aus")}: {Core.GHSettings.MainMenuHotKey}");

#if DEBUG
            ImGui.SameLine(ImGui.GetWindowWidth() - 280f);
            ImGui.Checkbox("ImGui Demo", ref showImGuiDemo);
            if (showImGuiDemo)
            {
                ImGui.ShowDemoWindow(ref showImGuiDemo);
            }
#endif

            const string forkCredit = "Fork by MordWraith · basis Lafko / Gordin";
            var forkWidth = ImGui.CalcTextSize(forkCredit).X;
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, ImGui.GetContentRegionAvail().X - forkWidth));
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
            ImGui.Text(forkCredit);
            ImGui.PopStyleColor();

            ImGui.EndMenuBar();
        }

        private static void DrawHubToolbar()
        {
            if (ImGui.Button(OverlayLocalization.L("General", "Allgemein"), new Vector2(130, 28)))
            {
                isGeneralWindowVisible = !isGeneralWindowVisible;
            }

            ImGui.SameLine();
            if (ImGui.Button(OverlayLocalization.L("Plugins", "Plugins"), new Vector2(130, 28)))
            {
                isPluginsWindowVisible = !isPluginsWindowVisible;
            }

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
            ImGui.Text(OverlayLocalization.L("Docked left of this window.", "Links an dieses Fenster angedockt."));
            ImGui.PopStyleColor();

            var logLabel = OverlayLocalization.L("Log", "Log");
            var logButtonWidth = 130f;
            var logWasVisible = ActivityLogWindow.IsVisible;
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X - logButtonWidth);
            if (logWasVisible)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.AccentMuted);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiTheme.Accent);
            }

            if (ImGui.Button(logLabel, new Vector2(logButtonWidth, 28)))
            {
                ActivityLogWindow.ToggleVisible();
            }

            if (logWasVisible)
            {
                ImGui.PopStyleColor(2);
            }

            ImGui.Spacing();
        }

        private static void DrawDockedSideWindow(ref bool visible, string title, float width, float offsetFromMainRight, Action drawBody)
        {
            if (!visible)
            {
                return;
            }

            var pos = new Vector2(mainWindowPos.X - offsetFromMainRight, mainWindowPos.Y);
            var size = new Vector2(width, Math.Max(mainWindowSize.Y, 300f));

            ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
            ImGui.SetNextWindowSize(size, ImGuiCond.Always);

            if (!ImGui.Begin(title, ref visible, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse))
            {
                ImGui.End();
                return;
            }

            drawBody();
            ImGui.End();
        }

        private static float GetPluginsDockOffset()
        {
            return PluginsDockWidth;
        }

        private static float GetGeneralDockOffset()
        {
            var offset = GeneralDockWidth;
            if (isPluginsWindowVisible)
            {
                offset += PluginsDockWidth;
            }

            return offset;
        }

        private static void DrawPluginTabs()
        {
            ImGuiTheme.SectionHeader(
                OverlayLocalization.L("Plugin settings", "Plugin-Einstellungen"),
                OverlayLocalization.L(
                    "Configure active plugins here. Use the buttons above for global options.",
                    "Aktive Plugins hier konfigurieren. Buttons oben fuer globale Optionen."));

            var enabledPlugins = PManager.Plugins.Where(p => p.Metadata.Enable).ToList();
            if (enabledPlugins.Count == 0)
            {
                ImGui.TextDisabled(OverlayLocalization.L(
                    "No active plugins. Open Plugins to enable some.",
                    "Keine aktiven Plugins. Unter Plugins welche aktivieren."));
                return;
            }

            if (!ImGui.BeginTabBar("pluginSettingsBar", ImGuiTabBarFlags.AutoSelectNewTabs | ImGuiTabBarFlags.Reorderable))
            {
                return;
            }

            foreach (var container in enabledPlugins)
            {
                ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.16f, 0.20f, 0.30f, 1f));
                ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.28f, 0.38f, 0.55f, 1f));
                ImGui.PushStyleColor(ImGuiCol.TabSelected, ImGuiTheme.Accent);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.96f, 1f, 1f));

                if (ImGui.BeginTabItem($"{container.Name}##pluginCfg"))
                {
                    ImGuiTheme.BeginPanel($"PluginPanel_{container.Name}");
                    container.Plugin.DrawSettings();
                    ImGuiTheme.EndPanel();
                    ImGui.EndTabItem();
                }

                ImGui.PopStyleColor(4);
            }

            ImGui.EndTabBar();
        }

        private static void DrawGeneralWindow()
        {
            var title = $"{OverlayLocalization.L("General", "Allgemein")} | GameHelper###GameHelperGeneralPanel";
            DrawDockedSideWindow(
                ref isGeneralWindowVisible,
                title,
                GeneralDockWidth,
                GetGeneralDockOffset(),
                () =>
                {
                    ImGuiTheme.BeginPanel("GeneralContentPanel");
                    DrawCoreSettings();
                    ImGuiTheme.EndPanel();
                });
        }

        private static void DrawPluginsWindow()
        {
            var title = $"{OverlayLocalization.L("Plugins", "Plugins")} | GameHelper###GameHelperPluginsPanel";
            DrawDockedSideWindow(
                ref isPluginsWindowVisible,
                title,
                PluginsDockWidth,
                GetPluginsDockOffset(),
                () =>
                {
                    ImGuiTheme.BeginPanel("PluginsContentPanel");
                    DrawPluginsPanel();
                    ImGuiTheme.EndPanel();
                });
        }

        private static void DrawLanguageWidget()
        {
            ImGuiTheme.SectionHeader(
                OverlayLocalization.L("Language", "Sprache"),
                OverlayLocalization.L(
                    "Applies to the settings window and all plugins.",
                    "Gilt fuer das Einstellungsfenster und alle Plugins."));

            var lang = Core.GHSettings.OverlayLanguage;
            var preview = lang == OverlayLanguage.German
                ? OverlayLocalization.L("German", "Deutsch")
                : OverlayLocalization.L("English", "Englisch");
            ImGui.SetNextItemWidth(200f);
            if (ImGui.BeginCombo("##overlay_language", preview))
            {
                if (ImGui.Selectable(OverlayLocalization.L("English", "Englisch"), lang == OverlayLanguage.English) &&
                    lang != OverlayLanguage.English)
                {
                    Core.GHSettings.OverlayLanguage = OverlayLanguage.English;
                    PManager.RequestSaveAllSettings();
                }

                if (ImGui.Selectable(OverlayLocalization.L("German", "Deutsch"), lang == OverlayLanguage.German) &&
                    lang != OverlayLanguage.German)
                {
                    Core.GHSettings.OverlayLanguage = OverlayLanguage.German;
                    PManager.RequestSaveAllSettings();
                }

                ImGui.EndCombo();
            }
        }

        private static void DrawPluginsPanel()
        {
            ImGuiTheme.SectionHeader(
                OverlayLocalization.L("Plugin management", "Plugin-Verwaltung"),
                OverlayLocalization.L(
                    "Auto-start plugins load on GameHelper launch. Settings are saved immediately.",
                    "Autostart-Plugins werden beim Start geladen. Einstellungen werden sofort gespeichert."));

            var enabledCount = PManager.Plugins.Count(p => p.Metadata.Enable);
            ImGui.TextDisabled($"{OverlayLocalization.L("Active", "Aktiv")}: {enabledCount} / {PManager.Plugins.Count}");
            ImGui.SameLine();
            if (ImGui.SmallButton(OverlayLocalization.L("Enable all", "Alle an")))
            {
                SetAllPlugins(true);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton(OverlayLocalization.L("Disable all", "Alle aus")))
            {
                SetAllPlugins(false);
            }

            ImGui.Spacing();

            if (!ImGui.BeginTable("pluginTable", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY, new Vector2(0, 0)))
            {
                return;
            }

            ImGui.TableSetupColumn(OverlayLocalization.L("Plugin", "Plugin"), ImGuiTableColumnFlags.WidthStretch, 0.52f);
            ImGui.TableSetupColumn(OverlayLocalization.L("Author", "Ersteller"), ImGuiTableColumnFlags.WidthFixed, 96f);
            ImGui.TableSetupColumn(OverlayLocalization.L("Status", "Status"), ImGuiTableColumnFlags.WidthFixed, 64f);
            ImGui.TableSetupColumn(OverlayLocalization.L("Enable", "Aktivieren"), ImGuiTableColumnFlags.WidthFixed, 52f);
            ImGui.TableHeadersRow();

            foreach (var container in PManager.Plugins)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(container.Name);

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
                ImGui.Text(PluginCredits.GetOriginalAuthor(container.Name));
                ImGui.PopStyleColor();

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                if (container.Metadata.Enable)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.Success);
                    ImGui.Text(OverlayLocalization.L("Active", "Aktiv"));
                    ImGui.PopStyleColor();
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
                    ImGui.Text(OverlayLocalization.L("Off", "Aus"));
                    ImGui.PopStyleColor();
                }

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                var autoStart = container.Metadata.AutoStart;
                if (ImGui.Checkbox($"##enable_{container.Name}", ref autoStart))
                {
                    SetPluginAutoStart(container, autoStart);
                }
            }

            ImGui.EndTable();
        }

        private static void SetAllPlugins(bool enabled)
        {
            foreach (var container in PManager.Plugins)
            {
                if (container.Metadata.AutoStart != enabled)
                {
                    SetPluginAutoStart(container, enabled);
                }
            }
        }

        private static void SetPluginAutoStart(PluginContainer container, bool autoStart)
        {
            if (container.Metadata.AutoStart == autoStart)
            {
                return;
            }

            container.Metadata.AutoStart = autoStart;

            if (autoStart)
            {
                if (!container.Metadata.Enable)
                {
                    container.Metadata.Enable = true;
                    try
                    {
                        container.Plugin.OnEnable(Core.Process.Address != IntPtr.Zero);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Plugin '{container.Name}' konnte nicht gestartet werden: {ex.Message}");
                        container.Metadata.Enable = false;
                        container.Metadata.AutoStart = false;
                    }
                }
            }
            else if (container.Metadata.Enable)
            {
                container.Metadata.Enable = false;
                container.Plugin.SaveSettings();
                container.Plugin.OnDisable();
            }

            PManager.RequestSaveAllSettings();
        }

        /// <summary>
        ///     Draws the currently selected settings on ImGui.
        /// </summary>
        private static void DrawCoreSettings()
        {
            ImGui.PushItemWidth(-1);
            DrawLanguageWidget();

            ImGuiTheme.SectionHeader(
                OverlayLocalization.L("Status", "Status"),
                OverlayLocalization.L(
                    $"Settings are saved when you close the menu ({Core.GHSettings.MainMenuHotKey}) and when plugins change.",
                    $"Einstellungen werden beim Schliessen des Menues ({Core.GHSettings.MainMenuHotKey}) und bei Plugin-Aenderungen gespeichert."));

            ImGui.Text(OverlayLocalization.L("Game state", "Spielzustand"));
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.Accent);
            ImGui.Text($"{Core.States.GameCurrentState}");
            ImGui.PopStyleColor();
            InputTextTooltip(
                "##PartyLeaderName",
                ref Core.GHSettings.LeaderName,
                200,
                "Party leader name for party-related features.",
                "Name des Party-Leaders fuer Party-Funktionen.");

            ImGuiTheme.SectionHeader(
                OverlayLocalization.L("Controls & display", "Steuerung & Anzeige"));
            DrawInputConfigWidget();
            DrawNearbyWidget();
            DrawToolsConfig();

            ImGuiTheme.SectionHeader(
                OverlayLocalization.L("Filters & tracking", "Filter & Tracking"),
                OverlayLocalization.L(
                    "Advanced entity filters. Change zone or restart after edits.",
                    "Erweiterte Entity-Filter. Nach Aenderungen Zone wechseln oder neu starten."));
            DrawPoiWidget();
            DrawMonstersToIgnore();
            DrawNPCWidget();
            DrawMiscObjWidget();

            ImGuiTheme.SectionHeader(OverlayLocalization.L("Advanced", "Erweitert"));
            DrawMiscConfig();
            ChangeFontWidget();
            DrawReloadPluginWidget();
            ImGui.PopItemWidth();
        }

        private static void DrawNearbyWidget()
        {
            if (ImGui.CollapsingHeader(
                OverlayLocalization.L("Monster range", "Monster-Reichweite"),
                ImGuiTreeNodeFlags.DefaultOpen))
            {
                DragIntTooltip(
                    "##SmallMonsterRange",
                    ref Core.GHSettings.InnerCircle.Meaning,
                    1f,
                    0,
                    Core.GHSettings.OuterCircle.Meaning,
                    "Small monster range radius. Hover for details.",
                    "Kleine Monster-Reichweite. Fuer Details Maus darueber halten.");
                CheckboxLabeled(
                    OverlayLocalization.L("Visible##smallRange", "Sichtbar##smallRange"),
                    ref Core.GHSettings.InnerCircle.IsVisible,
                    "Show the small monster range circle on the overlay.",
                    "Kleinen Monster-Radius im Overlay anzeigen.");

                DragIntTooltip(
                    "##LargeMonsterRange",
                    ref Core.GHSettings.OuterCircle.Meaning,
                    1f,
                    Core.GHSettings.InnerCircle.Meaning,
                    AreaInstanceConstants.NETWORK_BUBBLE_RADIUS,
                    "Large monster range radius (network bubble limit). Hover for details.",
                    "Grosse Monster-Reichweite (Netzwerk-Grenze). Fuer Details Maus darueber halten.");
                CheckboxLabeled(
                    OverlayLocalization.L("Visible##largeRange", "Sichtbar##largeRange"),
                    ref Core.GHSettings.OuterCircle.IsVisible,
                    "Show the large monster range circle on the overlay.",
                    "Grossen Monster-Radius im Overlay anzeigen.");

                // ImGui.SameLine(0f, 30f);
                // ImGui.Checkbox($"Follow Mouse##{name}", ref value.FollowMouse);
            }
        }

        /// <summary>
        ///     Draws the ImGui widget for changing fonts.
        /// </summary>
        private static void ChangeFontWidget()
        {
            if (ImGui.CollapsingHeader("Change Fonts"))
            {
                ImGui.InputText("Pathname", ref Core.GHSettings.FontPathName, 300);
                ImGui.DragInt("Size", ref Core.GHSettings.FontSize, 0.1f, 13, 40);
                var languageChanged = ImGuiHelper.EnumComboBox("Language", ref Core.GHSettings.FontLanguage);
                var customLanguage = ImGui.InputText("Custom Glyph Ranges", ref Core.GHSettings.FontCustomGlyphRange, 100);
                ImGuiHelper.ToolTip("This is advance level feature. Do not modify this if you don't know what you are doing. " +
                    "Example usage:- If you have downloaded and pointed to the ArialUnicodeMS.ttf font, you can use " +
                    "0x0020, 0xFFFF, 0x00 text in this field to load all of the font texture in ImGui. Note the 0x00" +
                    " as the last item in the range.");
                if (languageChanged)
                {
                    Core.GHSettings.FontCustomGlyphRange = string.Empty;
                }

                if (customLanguage)
                {
                    Core.GHSettings.FontLanguage = FontGlyphRangeType.English;
                }

                if (ImGui.Button("Apply Changes"))
                {
                    if (MiscHelper.TryConvertStringToImGuiGlyphRanges(Core.GHSettings.FontCustomGlyphRange, out var glyphranges))
                    {
                        Core.Overlay.ReplaceFont(
                            Core.GHSettings.FontPathName,
                            Core.GHSettings.FontSize,
                            glyphranges);
                    }
                    else
                    {
                        Core.Overlay.ReplaceFont(
                            Core.GHSettings.FontPathName,
                            Core.GHSettings.FontSize,
                            Core.GHSettings.FontLanguage);
                    }
                }
            }
        }

        /// <summary>
        ///     Draws the ImGui widget for changing POI monsters.
        /// </summary>
        private static void DrawPoiWidget()
        {
            var isOpened = ImGui.CollapsingHeader("Special Monster Tracker (A.K.A Monster POI)");
            ImGuiHelper.ToolTip("In order to figure out the path/mod to add " +
                "please open DV -> States -> InGameState -> CurrentAreaInstance -> " +
                "Awake Entities -> click dump button against the entity you want to add. " +
                "This will create a new file in entity_dumps folder with all mod names and " +
                "path of that entity.");
            if (isOpened)
            {
                ImGui.TextWrapped("Please restart gamehelper or change area/zone if you make any changes over here.");
                for (var i = Core.GHSettings.PoiMonstersCategories2.Count - 1; i >= 0; i--)
                {
                    var (filtertype, filter, rarity, stat, group) = Core.GHSettings.PoiMonstersCategories2[i];
                    var isChanged = false;
                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 10);
                    if (ImGuiHelper.EnumComboBox($"Filter type     ##{i}MonsterPoiWidget", ref filtertype))
                    {
                        isChanged = true;
                    }

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 27);
                    if (ImGui.InputText($"Filter     ##{i}MonsterPoiWidget", ref filter, 200))
                    {
                        isChanged = true;
                    }

                    ImGuiHelper.ToolTip(filtertype == EntityFilterType.PATH ||
                        filtertype == EntityFilterType.PATHANDRARITY ||
                        filtertype == EntityFilterType.PATHANDSTAT ?
                        "Path is going to be checked from left to right (i.e. String.StartsWith), up till the filter length." :
                        "Mod name is fully checked, it need to be 100% match.");
                    ImGui.SameLine();
                    if (filtertype == EntityFilterType.PATHANDRARITY || filtertype == EntityFilterType.MODANDRARITY)
                    {
                        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                        if (ImGuiHelper.EnumComboBox($"Rarity     ##{i}MonsterPoiWidget", ref rarity))
                        {
                            isChanged = true;
                        }

                        ImGui.SameLine();
                    }

                    if (filtertype == EntityFilterType.PATHANDSTAT)
                    {
                        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                        if (ImGuiHelper.NonContinuousEnumComboBox($"Stat        ##{i}MonsterPoiWidget", ref stat))
                        {
                            isChanged = true;
                        }

                        ImGui.SameLine();
                    }

                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                    if (ImGui.InputInt($"Group Number##{i}MonsterPoiWidget", ref group))
                    {
                        if (group < 0)
                        {
                            group = 0;
                        }

                        isChanged = true;
                    }

                    if (isChanged)
                    {
                        Core.GHSettings.PoiMonstersCategories2[i] = new(filtertype, filter, rarity, stat, group);
                    }

                    ImGui.SameLine();
                    if (ImGui.Button($"delete##{i}MonsterPoiWidget"))
                    {
                        Core.GHSettings.PoiMonstersCategories2.RemoveAt(i);
                    }
                }

                ImGui.Separator();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 10);
                ImGuiHelper.EnumComboBox($"Filter type     ##addMonsterPoiWidget", ref efilterType);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 17);
                ImGui.InputText($"Filter     ##addMonsterPoiWidget", ref filterText, 200);
                ImGuiHelper.ToolTip(efilterType == EntityFilterType.PATH ||
                    efilterType == EntityFilterType.PATHANDRARITY ||
                    efilterType == EntityFilterType.PATHANDSTAT ?
                    "Path is going to be checked from left to right (i.e. String.StartsWith), up till the filter length." :
                    "Mod name is fully checked, it need to be 100% match.");
                ImGui.SameLine();
                if (efilterType == EntityFilterType.PATHANDRARITY || efilterType == EntityFilterType.MODANDRARITY)
                {
                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                    ImGuiHelper.EnumComboBox($"Rarity     ##addMonsterPoiWidget", ref erarity);
                    ImGui.SameLine();
                }

                if (efilterType == EntityFilterType.PATHANDSTAT)
                {
                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                    ImGuiHelper.NonContinuousEnumComboBox($"Stat        ##addMonsterPoiWidget", ref eStats);
                    ImGui.SameLine();
                }

                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                if (ImGui.InputInt($"Group Number##addMonsterPoiWidget", ref filterGroup) && filterGroup < 0)
                {
                    filterGroup = 0;
                }

                ImGui.SameLine();
                if(ImGui.Button("add##MonsterPoiWidget"))
                {
                    Core.GHSettings.PoiMonstersCategories2.Add(new(efilterType, filterText, erarity, eStats, filterGroup));
                    efilterType = EntityFilterType.PATH;
                    eStats = GameStats.is_capturable_monster;
                    filterText = string.Empty;
                    filterGroup = 0;
                }
            }
        }

        /// <summary>
        ///     Draws the ImGui widget for ignoring monsters.
        /// </summary>
        private static void DrawMonstersToIgnore()
        {
            var isOpened = ImGui.CollapsingHeader("Ignore Monsters");
            ImGuiHelper.ToolTip("In order to figure out the path, please open " +
                "DV -> States -> InGameState -> CurrentAreaInstance -> Awake Entities -> " +
                "Click Path -> see NPC path in the game world");
            if (isOpened)
            {
                ImGui.TextWrapped("Please restart gamehelper or change area/zone if you make any changes over here.");
                ImGui.InputText("Monster metadata path##ToRemove", ref monterPathToIgnore, 200);
                ImGuiHelper.ToolTip("Path is going to be checked from left to right (i.e. String.StartsWith), up till the filter length.");
                ImGui.SameLine();
                if (ImGui.Button("Add##monsterPathToRemove") && !string.IsNullOrEmpty(monterPathToIgnore))
                {
                    Core.GHSettings.MonstersPathsToIgnore.Add(monterPathToIgnore);
                    monterPathToIgnore = string.Empty;
                }

                for (var i = Core.GHSettings.MonstersPathsToIgnore.Count - 1; i >= 0; i--)
                {
                    ImGui.Text($"Path: {Core.GHSettings.MonstersPathsToIgnore[i]}");
                    ImGui.SameLine();
                    if (ImGui.Button($"Delete##{i}monsterPathToRemove"))
                    {
                        Core.GHSettings.MonstersPathsToIgnore.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        ///     Draws the ImGui widget for defining important NPCs.
        /// </summary>
        private static void DrawNPCWidget()
        {
            var isOpened = ImGui.CollapsingHeader("Special NPC Metadata Paths");
            ImGuiHelper.ToolTip("In order to figure out the path, please open " +
                "DV -> States -> InGameState -> CurrentAreaInstance -> Awake Entities -> " +
                "Click Path -> see NPC path in the game world");
            if (isOpened)
            {
                ImGui.TextWrapped("Please restart gamehelper or change area/zone if you make any changes over here.");
                ImGui.InputText("NPC Path##specialNPCPath", ref specialNpcPath, 200);
                ImGuiHelper.ToolTip("Path is going to be checked from left to right (i.e. String.StartsWith), up till the filter length.");
                ImGui.SameLine();
                if (ImGui.Button("Add##specialNPCPath") && !string.IsNullOrEmpty(specialNpcPath))
                {
                    Core.GHSettings.SpecialNPCPaths.Add(specialNpcPath);
                    specialNpcPath = string.Empty;
                }

                for (var i = Core.GHSettings.SpecialNPCPaths.Count - 1; i >= 0; i--)
                {
                    ImGui.Text($"Path: {Core.GHSettings.SpecialNPCPaths[i]}");
                    ImGui.SameLine();
                    if(ImGui.Button($"Delete##{i}specialNPCPath"))
                    {
                        Core.GHSettings.SpecialNPCPaths.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        ///     Draws the ImGui widget for defining important MiscellaneousObjects.
        /// </summary>
        private static void DrawMiscObjWidget()
        {
            var isOpened = ImGui.CollapsingHeader("Special Objects Metadata Paths");
            ImGuiHelper.ToolTip("In order to figure out the path, please open " +
                "DV -> States -> InGameState -> CurrentAreaInstance -> Awake Entities -> " +
                "Click Path -> see objects path in the game world");
            if (isOpened)
            {
                ImGui.TextWrapped("Please restart gamehelper or change area/zone if you make any changes over here.");
                ImGui.InputText("Object Path##MiscObjWidget", ref specialMiscObjPath, 200);
                ImGuiHelper.ToolTip("Path is going to be checked from left to right (i.e. String.StartsWith), up till the filter length.");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                if (ImGui.InputInt($"Group Number##MiscObjgroup", ref filterGroup) && filterGroup < 0)
                {
                    filterGroup = 0;
                }

                ImGui.SameLine();
                if (ImGui.Button("add##MiscObjadd"))
                {
                    Core.GHSettings.SpecialMiscObjPaths.Add(new(specialMiscObjPath, filterGroup));
                    specialMiscObjPath = string.Empty;
                    filterGroup = 0;
                }

                for (var i = Core.GHSettings.SpecialMiscObjPaths.Count - 1; i >= 0; i--)
                {
                    ImGui.Text($"Path: {Core.GHSettings.SpecialMiscObjPaths[i].path}, GroupId: {Core.GHSettings.SpecialMiscObjPaths[i].group}");
                    ImGui.SameLine();
                    if (ImGui.Button($"Delete##MiscObjDel{i}"))
                    {
                        Core.GHSettings.SpecialMiscObjPaths.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        ///     Draws the ImGui widget for changing keyboard related settings
        /// </summary>
        private static void DrawInputConfigWidget()
        {
            if (ImGui.CollapsingHeader(
                OverlayLocalization.L("Keys & input", "Tasten & Eingabe"),
                ImGuiTreeNodeFlags.DefaultOpen))
            {
                DragIntTooltip(
                    "##KeyPressTimeout",
                    ref Core.GHSettings.KeyPressTimeout,
                    0.2f,
                    60,
                    300,
                    "Key timeout (ms). When GameHelper sends a key press, the server needs time (about latency x 3). " +
                    "Set this to latency x 3 (e.g. 90 for 30 ms). Do not go below 60.",
                    "Tasten-Timeout (ms). Wenn GameHelper eine Taste sendet, braucht der Server Zeit (ca. Latenz x 3). " +
                    "Wert auf Latenz x 3 setzen (z. B. 90 bei 30 ms). Nicht unter 60.");

                ImGuiHelper.NonContinuousEnumComboBox("##MainMenuHotKey", ref Core.GHSettings.MainMenuHotKey);
                ImGuiHelper.ToolTip(OverlayLocalization.L(
                    "Hide/show settings menu — press this key to show or hide GameHelper (default: F11).",
                    "Einstellungsmenue ein/aus — mit dieser Taste GameHelper ein- oder ausblenden (Standard: F11)."));

                ImGuiHelper.NonContinuousEnumComboBox("##DisableRenderingKey", ref Core.GHSettings.DisableAllRenderingKey);
                ImGuiHelper.ToolTip(OverlayLocalization.L(
                    "Toggle overlay rendering — enable or disable all overlay drawing (default: F9).",
                    "Overlay-Darstellung ein/aus — gesamtes Overlay ein- oder ausschalten (Standard: F9)."));
            }
        }

        /// <summary>
        ///     Draws the imgui widget for enabling/disabling tools.
        /// </summary>
        private static void DrawToolsConfig()
        {
            if (ImGui.CollapsingHeader(
                OverlayLocalization.L("Developer tools", "Entwickler-Tools"),
                ImGuiTreeNodeFlags.DefaultOpen))
            {
                CheckboxLabeled(
                    OverlayLocalization.L("Performance stats", "Performance-Statistik"),
                    ref Core.GHSettings.ShowPerfStats,
                    "Show FPS and frame timing overlay.",
                    "FPS und Frame-Zeiten anzeigen.");
                if (Core.GHSettings.ShowPerfStats)
                {
                    CheckboxLabeled(
                        OverlayLocalization.L("Hide when game is in background", "Ausblenden wenn Spiel im Hintergrund"),
                        ref Core.GHSettings.HidePerfStatsWhenBg);
                    CheckboxLabeled(
                        OverlayLocalization.L("Show minimum stats", "Nur Mindest-Statistik"),
                        ref Core.GHSettings.MinimumPerfStats);
                }

                CheckboxLabeled(
                    OverlayLocalization.L("Game UI explorer (GE)", "Game-UI-Explorer (GE)"),
                    ref Core.GHSettings.ShowGameUiExplorer);
                CheckboxLabeled(
                    OverlayLocalization.L("Data visualization (DV)", "Daten-Visualisierung (DV)"),
                    ref Core.GHSettings.ShowDataVisualization);
                CheckboxLabeled(
                    OverlayLocalization.L("Performance profiler", "Performance-Profiler"),
                    ref Core.GHSettings.ShowPerfProfiler);
#if DEBUG
                ImGui.Checkbox("Krangled Passive Detector", ref Core.GHSettings.ShowKrangledPassiveDetector);
#endif
            }
        }

        /// <summary>
        ///     Draws the imgui widget for showing misc config
        /// </summary>
        private static void DrawMiscConfig()
        {
            if (ImGui.CollapsingHeader("Miscellaneous Config"))
            {
                if (ImGui.Checkbox("Fix Taskbar not showing", ref Core.GHSettings.FixTaskbarNotShowing))
                {
                    if (Core.States.GameCurrentState != GameStateTypes.GameNotLoaded)
                    {
                        CoroutineHandler.RaiseEvent(GameHelperEvents.OnMoved);
                    }
                }

                ImGui.Checkbox("Disable entity processing when in town or hideout",
                    ref Core.GHSettings.DisableEntityProcessingInTownOrHideout);
                ImGui.Checkbox("Hide overlay settings upon start", ref Core.GHSettings.HideSettingWindowOnStart);
                CheckboxLabeled(
                    OverlayLocalization.L(
                        "Hide overlay when game is in background",
                        "Overlay ausblenden wenn Spiel im Hintergrund"),
                    ref Core.GHSettings.HideOverlayWhenGameInBackground,
                    "Hide the entire GameHelper overlay while Path of Exile is not the active window.",
                    "Blendet das gesamte GameHelper-Overlay aus, solange Path of Exile nicht das aktive Fenster ist.");
                ImGui.Checkbox("Close GameHelper when Game Exit", ref Core.GHSettings.CloseWhenGameExit);
                if (ImGui.Checkbox("V-Sync", ref Core.Overlay.VSync))
                {
                    Core.GHSettings.Vsync = Core.Overlay.VSync;
                }

                ImGui.BeginDisabled(Core.Overlay.VSync);
                if (ImGui.InputInt("FPS Limiter (0 to disable)", ref Core.GHSettings.FPSLimit))
                {
                    Core.Overlay.FPSLimit = Core.GHSettings.FPSLimit;
                }

                ImGui.EndDisabled();

                ImGuiHelper.ToolTip("WARNING: There is no rate limiter in GameHelper, once V-Sync is off,\n" +
                    "it's your responsibility to use external rate limiter e.g. NVIDIA Control Panel\n" +
                    "-> Manage 3D Settings -> Set Max Framerate to what your monitor support.");
                ImGui.Checkbox("Process all renderable entities", ref Core.GHSettings.ProcessAllRenderableEntities);
                ImGuiHelper.ToolTip("WARNING: This will greatly reduce GH speed as well as increase crashes/glitches. Always keep it unchecked.");
                ImGui.Checkbox("Disable debug counters (do it on 6 man party + juiced maps only)", ref Core.GHSettings.DisableAllCounters);
                ImGui.Text("Entity MaxDegreeOfParallelism");
                ImGuiHelper.ToolTip("This limits the entity reading algorithm to a set number of CPUs." +
                    " Select -1 to disable this limit. Use Task Manager CPU usage stat + Misc Tools -> performance stats" +
                    " to figure out best FPS to CPU usage ratio.");
                ImGui.SameLine();
                if (ImGui.RadioButton("-1", Core.GHSettings.EntityReaderMaxDegreeOfParallelism == -1))
                {
                    Core.GHSettings.EntityReaderMaxDegreeOfParallelism = -1;
                }
                ImGui.SameLine();

                for (var i = 2; i < 128; i*=2)
                {
                    if (ImGui.RadioButton(i.ToString(), Core.GHSettings.EntityReaderMaxDegreeOfParallelism == i))
                    {
                        Core.GHSettings.EntityReaderMaxDegreeOfParallelism = i;
                    }

                    if (i*2 < 128)
                    {
                        ImGui.SameLine();
                    }
                }

                ImGui.Checkbox("Is Taiwan client", ref Core.GHSettings.IsTaiwanClient);

                ImGui.Separator();
                ImGui.Text("Entity Staleness Fixes");
                ImGuiHelper.ToolTip("These options help detect and fix stale entity data " +
                    "(e.g. NPCs that teleport but keep old position in memory).");

                ImGui.Checkbox("Enable NPC entity cleanup", ref Core.GHSettings.EnableNpcEntityCleanup);
                ImGuiHelper.ToolTip("Include NPC entities in the removal logic when they go invalid.\n" +
                    "Prevents stale NPC entities from lingering in the entity dictionary.");

                ImGui.Checkbox("Enable stale entity cleanup", ref Core.GHSettings.EnableStaleEntityCleanup);
                ImGuiHelper.ToolTip("Remove any entity that stays invalid for many consecutive frames,\n" +
                    "regardless of entity type. Catches NPCs and other entities that\n" +
                    "the default cleanup misses.");

                if (Core.GHSettings.EnableStaleEntityCleanup)
                {
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(80);
                    ImGui.InputInt("threshold (frames)", ref Core.GHSettings.StaleEntityFrameThreshold);
                    if (Core.GHSettings.StaleEntityFrameThreshold < 10)
                        Core.GHSettings.StaleEntityFrameThreshold = 10;
                }
            }
        }

        /// <summary>
        ///     Draws the imgui widget for reloading plugins
        /// </summary>
        private static void DrawReloadPluginWidget()
        {
#if DEBUG
            if (ImGui.CollapsingHeader("Reload Plugin"))
            {
                ImGuiHelper.IEnumerableComboBox<string>("Plugins", PManager.PluginNames, ref pluginForHotReload);
                ImGui.BeginDisabled(!pluginLoaded || string.IsNullOrEmpty(pluginForHotReload));
                if (ImGui.Button("Unload Plugin"))
                {
                    if (PManager.UnloadPlugin(pluginForHotReload))
                    {
                        pluginLoaded = false;
                    }
                }

                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.BeginDisabled(pluginLoaded || string.IsNullOrEmpty(pluginForHotReload));
                if (ImGui.Button("Load Plugin"))
                {
                    if (PManager.LoadPlugin(pluginForHotReload))
                    {
                        pluginLoaded = true;
                    }
                }

                ImGui.EndDisabled();
            }
#endif
        }

        /// <summary>
        ///     Draws the closing confirmation popup on ImGui.
        /// </summary>
        private static void DrawConfirmationPopup()
        {
            ImGui.SetNextWindowPos(new Vector2(Core.Overlay.Size.Width / 3f, Core.Overlay.Size.Height / 3f));
            if (ImGui.BeginPopup("GameHelperCloseConfirmation"))
            {
                ImGui.Text("Do you want to quit the GameHelper overlay?");
                ImGui.Separator();
                if (ImGui.Button("Yes", new Vector2(ImGui.GetContentRegionAvail().X / 2f, ImGui.GetTextLineHeight() * 2)))
                {
                    Core.GHSettings.IsOverlayRunning = false;
                    ImGui.CloseCurrentPopup();
                    isOverlayRunningLocal = true;
                }

                ImGui.SameLine();
                if (ImGui.Button("No", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight() * 2)))
                {
                    ImGui.CloseCurrentPopup();
                    isOverlayRunningLocal = true;
                }

                ImGui.EndPopup();
            }
        }

        /// <summary>
        ///     Hides the overlay on startup.
        /// </summary>
        private static void HideOnStartCheck()
        {
            if (Core.GHSettings.HideSettingWindowOnStart)
            {
                isSettingsWindowVisible = false;
                Core.IsSettingsMenuOpen = false;
            }
        }

        /// <summary>
        ///     Draws the Settings Window.
        /// </summary>
        /// <returns>co-routine IWait.</returns>
        private static IEnumerator<Wait> RenderCoroutine()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnRender);
                if (Utils.IsKeyPressedAndNotTimeout(Core.GHSettings.MainMenuHotKey))
                {
                    isSettingsWindowVisible = !isSettingsWindowVisible;
                    Core.IsSettingsMenuOpen = isSettingsWindowVisible;
                    ImGui.GetIO().WantCaptureMouse = true;
                    if (!isSettingsWindowVisible)
                    {
                        isGeneralWindowVisible = false;
                        isPluginsWindowVisible = false;
                        CoroutineHandler.RaiseEvent(GameHelperEvents.TimeToSaveAllSettings);
                    }
                }

                if (!isSettingsWindowVisible)
                {
                    Core.IsSettingsMenuOpen = false;
                    continue;
                }

                Core.IsSettingsMenuOpen = true;

                ImGui.SetNextWindowSizeConstraints(new Vector2(860, 620), Vector2.One * float.MaxValue);
                var isMainMenuExpanded = ImGui.Begin(
                    $"{OverlayLocalization.L("GameHelper Settings", "GameHelper Einstellungen")}  |  {Core.GetVersion()}###GameHelperMainSettings",
                    ref isOverlayRunningLocal,
                    ImGuiWindowFlags.MenuBar);

                if (!isOverlayRunningLocal)
                {
                    ImGui.OpenPopup("GameHelperCloseConfirmation");
                }

                DrawConfirmationPopup();
                if (!Core.GHSettings.IsOverlayRunning)
                {
                    CoroutineHandler.RaiseEvent(GameHelperEvents.TimeToSaveAllSettings);
                }

                if (!isMainMenuExpanded)
                {
                    isGeneralWindowVisible = false;
                    isPluginsWindowVisible = false;
                    ImGui.End();
                    continue;
                }

                DrawManuBar();
                DrawHubToolbar();
                DrawPluginTabs();
                mainWindowPos = ImGui.GetWindowPos();
                mainWindowSize = ImGui.GetWindowSize();
                ImGui.End();

                DrawPluginsWindow();
                DrawGeneralWindow();
            }
        }

        private static void DragIntTooltip(string id, ref int value, float speed, int min, int max, string english, string german)
        {
            ImGui.DragInt(id, ref value, speed, min, max);
            ImGuiHelper.ToolTip(OverlayLocalization.L(english, german));
        }

        private static void CheckboxLabeled(string label, ref bool value, string? english = null, string? german = null)
        {
            ImGui.Checkbox(label, ref value);
            if (english != null && german != null)
            {
                ImGuiHelper.ToolTip(OverlayLocalization.L(english, german));
            }
        }

        private static void InputTextTooltip(string id, ref string value, uint maxLength, string english, string german)
        {
            ImGui.InputText(id, ref value, maxLength);
            ImGuiHelper.ToolTip(OverlayLocalization.L(english, german));
        }

        /// <summary>
        ///     Saves the GameHelper settings to disk.
        /// </summary>
        /// <returns>co-routine IWait.</returns>
        private static IEnumerator<Wait> SaveCoroutine()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.TimeToSaveAllSettings);
                JsonHelper.SafeToFile(Core.GHSettings, State.CoreSettingFile);
            }
        }
    }
}
