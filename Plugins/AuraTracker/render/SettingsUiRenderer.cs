using System;
using System.Numerics;
using GameHelper.RemoteEnums;
using ImGuiNET;
using static AuraTracker.AuraTrackerLocalization;

namespace AuraTracker.render;

internal sealed class SettingsUiRenderer
{
    private readonly string versionLabelEn;
    private readonly string versionLabelDe;

    public SettingsUiRenderer(string version)
    {
        versionLabelEn = $"AuraTracker v{version} by Skrip";
        versionLabelDe = $"AuraTracker v{version} von Skrip";
    }

    public void Draw(AuraTrackerSettings settings)
    {
        if (ImGui.CollapsingHeader(L("General", "Allgemein") + "###AuraTrackerGeneral"))
        {
            if (ImGui.BeginTable("at_general", 2))
            {
                ImGui.TableNextColumn();
                ImGui.Checkbox(L("Draw when game is backgrounded", "Bei Spiel im Hintergrund zeichnen"), ref settings.DrawWhenGameInBackground);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(180);
                ImGui.DragFloat(L("Screen Range (px)", "Bildschirmreichweite (px)"), ref settings.ScreenRangePx, 5f, 100f, 3000f);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(180);
                ImGui.DragInt(L("Max Enemies in List", "Max. Gegner in Liste"), ref settings.MaxEnemies, 1f, 1, 12);

                ImGui.TableNextColumn();
                var rarityNames = RarityNames;
                int curIdx = (int)settings.MinRarityToShow;
                if (ImGui.Combo(L("Min Rarity To Show", "Min. Seltenheit anzeigen"), ref curIdx, rarityNames, rarityNames.Length))
                {
                    settings.MinRarityToShow = (Rarity)Math.Clamp(curIdx, 0, 3);
                }

                ImGui.EndTable();
            }
        }

        if (ImGui.CollapsingHeader(L("List Layout", "Listen-Layout") + "###AuraTrackerLayout"))
        {
            if (ImGui.BeginTable("at_layout", 2))
            {
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(220);
                ImGui.DragFloat2(L("Left Anchor (x,y)", "Linke Position (x,y)"), ref settings.LeftAnchor, 1f, -4000, 4000);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(180);
                ImGui.DragFloat(L("Entry Spacing (px)", "Eintragsabstand (px)"), ref settings.EntrySpacing, 0.5f, 0f, 80f);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(180);
                ImGui.DragFloat(L("Bar→Buff Spacing (px)", "Balken→Buff Abstand (px)"), ref settings.BarToBuffSpacing, 0.5f, 0f, 40f);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(220);
                ImGui.DragFloat(L("Panel Width (px)", "Panelbreite (px)"), ref settings.PanelWidth, 1f, 120f, 1600f);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(220);
                ImGui.DragFloat(L("Max List Height (px, 0 = overlay)", "Max. Listenhoehe (px, 0 = Overlay)"), ref settings.MaxListHeight, 5f, 0f, 8000f);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(220);
                ImGui.DragFloat(L("Right Safe Margin (px)", "Rechter Sicherheitsabstand (px)"), ref settings.PanelRightSafeMargin, 0.5f, 0f, 120f);

                ImGui.EndTable();
            }
        }

        if (ImGui.CollapsingHeader(L("Bar & Buffs", "Balken & Buffs") + "###AuraTrackerBar"))
        {
            if (ImGui.BeginTable("at_bar", 2))
            {
                ImGui.TableNextColumn();
                ImGui.ColorEdit4(L("Bar Background", "Balken-Hintergrund"), ref settings.BarBg);

                ImGui.TableNextColumn();
                ImGui.ColorEdit4(L("HP Fill", "LP-Fuellung"), ref settings.BarHpFill);

                ImGui.TableNextColumn();
                ImGui.ColorEdit4(L("ES Fill", "ES-Fuellung"), ref settings.BarEsFill);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(180);
                ImGui.DragFloat2(L("Bar Size (w,h)", "Balkengroesse (b,h)"), ref settings.BarSize, 1f, 80, 600);

                ImGui.TableNextColumn();
                ImGui.Checkbox(L("HP Text Shows Percent (instead of absolute)", "LP-Text als Prozent (statt absolut)"), ref settings.ShowHpPercent);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(180);
                ImGui.DragFloat(L("Buff Padding (px)", "Buff-Abstand (px)"), ref settings.BuffPad, 0.5f, 0f, 16f);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(180);
                ImGui.DragInt(L("Max Buffs/Enemy", "Max. Buffs/Gegner"), ref settings.MaxBuffsPerEnemy, 1f, 1, 30);

                ImGui.TableNextColumn();
                ImGui.Checkbox(L("Show Buff Durations", "Buff-Dauer anzeigen"), ref settings.ShowDurations);

                ImGui.TableNextColumn();
                ImGui.SliderFloat(L("Buff BG Alpha", "Buff-Hintergrund Alpha"), ref settings.BuffBgAlpha, 0.0f, 1.0f);

                ImGui.TableNextColumn();
                ImGui.SliderFloat(L("Buff Text Scale", "Buff-Textgroesse"), ref settings.BuffTextScale, 0.5f, 2.0f);

                ImGui.TableNextColumn();
                ImGui.Checkbox(L("Show DPS Label", "DPS-Anzeige"), ref settings.ShowDps);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(180);
                ImGui.DragFloat(L("DPS Smoothing (s)", "DPS-Glaettung (s)"), ref settings.DpsSmoothingSeconds, 0.05f, 0.1f, 5f);

                ImGui.TableNextColumn();
                ImGui.ColorEdit4(L("DPS Text Color", "DPS-Textfarbe"), ref settings.DpsTextColor);

                ImGui.TableNextColumn();
                ImGui.Checkbox(L("Show Overall DPS Header", "Gesamt-DPS Kopfzeile"), ref settings.ShowOverallDps);

                ImGui.EndTable();
            }
        }

        if (ImGui.CollapsingHeader(L("Chip Color Overrides", "Chip-Farb-Ueberschreibungen") + "###AuraTrackerChips"))
        {
            ImGui.SetNextItemWidth(150);
            ImGui.DragInt(L("Chip Color Seed", "Chip-Farb-Seed"), ref settings.ChipColorSeed, 1, 0, 1000);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(L(
                    "Set the seed used for randomizing buff chip background colors.\nSame seed yields same color mapping each launch.",
                    "Seed fuer zufaellige Buff-Chip-Hintergrundfarben.\nGleicher Seed ergibt bei jedem Start dieselbe Farbzuordnung."));
            }

            ImGui.TextWrapped(L(
                "Add entries that match the chip's base text (without stacks or timer), e.g. \"Archnemesis\". The specified color overrides the random chip color. Alpha is ignored.",
                "Eintraege passend zum Chip-Basistext (ohne Stacks/Timer), z. B. \"Archnemesis\". Die Farbe ueberschreibt die Zufallsfarbe. Alpha wird ignoriert."));

            for (int i = 0; i < settings.ChipOverrides.Count; i++)
            {
                var item = settings.ChipOverrides[i];
                ImGui.PushID(i);

                if (ImGui.BeginTable("ovr_row", 3, ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn(L("Text", "Text"), ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn(L("Color", "Farbe"), ImGuiTableColumnFlags.WidthFixed, 180);
                    ImGui.TableSetupColumn(L("Del", "Entf."), ImGuiTableColumnFlags.WidthFixed, 60);

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    var match = item.Match ?? string.Empty;
                    if (ImGui.InputText("##txt", ref match, 256))
                    {
                        item.Match = match;
                    }

                    ImGui.TableNextColumn();
                    var rgb = new Vector3(item.Color.X, item.Color.Y, item.Color.Z);
                    if (ImGui.ColorEdit3("##col", ref rgb, ImGuiColorEditFlags.NoInputs))
                    {
                        item.Color = new Vector4(rgb.X, rgb.Y, rgb.Z, 1f);
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button(L("Remove", "Entfernen")))
                    {
                        settings.ChipOverrides.RemoveAt(i);
                        ImGui.EndTable();
                        ImGui.PopID();
                        i--;
                        continue;
                    }

                    ImGui.EndTable();
                }

                settings.ChipOverrides[i] = item;
                ImGui.PopID();
            }

            if (ImGui.Button(L("Add Override", "Ueberschreibung hinzufuegen")))
            {
                settings.ChipOverrides.Add(new AuraTrackerSettings.ChipColorOverride { Match = string.Empty, Color = new Vector4(1, 1, 1, 1) });
            }
        }

        if (ImGui.CollapsingHeader(L("Visuals", "Darstellung") + "###AuraTrackerVisuals"))
        {
            if (ImGui.BeginTable("at_fx", 2))
            {
                ImGui.TableNextColumn();
                ImGui.Checkbox(L("Panel Shadow", "Panel-Schatten"), ref settings.FancyPanelShadow);
                ImGui.TableNextColumn();
                ImGui.Checkbox(L("Rarity Stripe", "Seltenheits-Streifen"), ref settings.FancyRarityStripe);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(200);
                ImGui.DragFloat(L("Shadow Size", "Schatten-Groesse"), ref settings.PanelShadowSize, 0.5f, 0f, 40f);
                ImGui.TableNextColumn();
                ImGui.SliderFloat(L("Shadow Alpha", "Schatten-Alpha"), ref settings.PanelShadowAlpha, 0f, 1f);

                ImGui.TableNextColumn();
                ImGui.Checkbox(L("Bar Gloss", "Balken-Glanz"), ref settings.FancyBarGloss);
                ImGui.TableNextColumn();
                ImGui.Checkbox(L("Bar Inner Border", "Balken-Innenrand"), ref settings.FancyBarInnerBorder);

                ImGui.TableNextColumn();
                ImGui.Checkbox(L("ES Divider", "ES-Trennlinie"), ref settings.FancyEsDivider);
                ImGui.TableNextColumn();
                ImGui.SliderFloat(L("ES Divider Alpha", "ES-Trennlinie Alpha"), ref settings.EsDividerAlpha, 0f, 1f);

                ImGui.TableNextColumn();
                ImGui.SliderFloat(L("Bar Corner Radius", "Balken-Eckenradius"), ref settings.BarCornerRadius, 0f, 12f);
                ImGui.TableNextColumn();
                ImGui.SliderFloat(L("Bar Inner Border Alpha", "Balken-Innenrand Alpha"), ref settings.BarInnerBorderAlpha, 0f, 1f);

                ImGui.TableNextColumn();
                ImGui.Checkbox(L("Chip Gloss", "Chip-Glanz"), ref settings.FancyChipGloss);

                ImGui.TableNextColumn();
                ImGui.SliderFloat(L("Chip Corner Radius", "Chip-Eckenradius"), ref settings.ChipCornerRadius, 0f, 12f);
                ImGui.TableNextColumn();
                ImGui.SliderFloat(L("Chip Gloss Alpha", "Chip-Glanz Alpha"), ref settings.ChipGlossAlpha, 0f, 1f);

                ImGui.EndTable();
            }
        }

        if (ImGui.CollapsingHeader(L("List Background", "Listen-Hintergrund") + "###AuraTrackerBg"))
        {
            if (ImGui.BeginTable("at_bg", 2))
            {
                ImGui.TableNextColumn();
                ImGui.Checkbox(L("Show Panel Background", "Panel-Hintergrund anzeigen"), ref settings.ShowPanelBackground);
                ImGui.TableNextColumn();
                ImGui.ColorEdit4(L("Panel Background Color", "Panel-Hintergrundfarbe"), ref settings.PanelBg);

                ImGui.TableNextColumn();
                ImGui.ColorEdit4(L("Panel Border Color", "Panel-Rahmenfarbe"), ref settings.PanelBorder);
                ImGui.TableNextColumn();
                ImGui.DragFloat2(L("Panel Padding (x,y)", "Panel-Innenabstand (x,y)"), ref settings.PanelPadding, 0.5f, 0f, 40f);

                ImGui.TableNextColumn();
                ImGui.SliderFloat(L("Panel Corner Radius", "Panel-Eckenradius"), ref settings.PanelCornerRadius, 0f, 16f);
                ImGui.EndTable();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        var versionLabel = L(versionLabelEn, versionLabelDe);
        float txtW = ImGui.CalcTextSize(versionLabel).X;
        float availW = ImGui.GetContentRegionAvail().X;
        float padX = MathF.Max(0f, (availW - txtW) * 0.5f);
        float curX = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(curX + padX);
        ImGui.TextDisabled(versionLabel);
    }
}
