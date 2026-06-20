namespace FarmTracker
{
    using System;
    using System.Numerics;
    using GameHelper;
    using ImGuiNET;

    public sealed partial class FarmTrackerCore
    {
        private const float SegmentGap = 14f;
        private const float IconPad = 4f;

        private bool ShouldDrawOverlay(bool isGamePaused, bool isTownOrHideout)
        {
            if (this.Settings.OverlayMode == FarmOverlayMode.Hidden)
            {
                return false;
            }

            if (!IsGameOrOverlayForeground())
            {
                if (this.Settings.HideOverlayWhenGameInBackground)
                {
                    return false;
                }
            }

            if (this.Settings.PauseTimerWhenGamePaused && isGamePaused)
            {
                return false;
            }

            if (this.Settings.OverlayAnchor == FarmOverlayAnchor.ExperienceBar &&
                !this.expBarAnchor.TryGetRect(out _, out _))
            {
                return false;
            }

            return !InGamePanelsBlockOverlay();
        }

        private static bool InGamePanelsBlockOverlay()
        {
            var inGame = Core.States.InGameStateObject;
            return inGame.GameUi.SkillTreeNodesUiElements.Count > 0;
        }

        private void DrawSlimOverlay(bool isTownOrHideout)
        {
            var mode = this.Settings.OverlayMode;
            var showMap = mode == FarmOverlayMode.MapOnly ||
                          (mode == FarmOverlayMode.Auto && this.onMapArea && !isTownOrHideout);
            var showSession = mode == FarmOverlayMode.SessionOnly ||
                              (mode == FarmOverlayMode.Auto && (!this.onMapArea || isTownOrHideout));

            if (showMap && this.onMapArea && !isTownOrHideout)
            {
                this.DrawMapStrip();
            }
            else if (showSession)
            {
                this.DrawSessionStrip();
            }
        }

        private void DrawMapStrip()
        {
            if (!this.TryGetCenteredStripAnchor(out var anchorX, out var anchorY))
            {
                return;
            }

            ImGui.SetNextWindowPos(new Vector2(anchorX, anchorY), ImGuiCond.Always, new Vector2(0.5f, 1f));
            ImGui.SetNextWindowBgAlpha(Math.Clamp(this.Settings.BarOpacity, 0f, 1f));
            const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs |
                ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoMove;

            if (!ImGui.Begin("##farmtracker_map_strip", flags))
            {
                ImGui.End();
                return;
            }

            var scale = Math.Clamp(this.Settings.OverlayFontScale, 0.75f, 1.4f);
            ImGui.SetWindowFontScale(scale);

            var profit = this.ValueOfGained(this.CurrentGainedLive(), out _);
            this.liveMapProfitDivine = profit;

            ImGui.TextUnformatted(this.currentMap?.Name ?? L("Unknown", "Unbekannt"));

            ImGui.SameLine(0f, SegmentGap);
            if (this.DrawInlineIcon("Time"))
            {
                ImGui.SameLine(0f, IconPad);
            }

            ImGui.TextUnformatted(FormatElapsed(this.CurrentLiveMapTime()));

            ImGui.SameLine(0f, SegmentGap);
            this.DrawOverlayProfit(profit, showAltCurrency: false);

            if (this.Settings.OverlayShowKills && this.currentMap != null)
            {
                var k = this.currentMap.Kills;
                ImGui.SameLine(0f, SegmentGap);
                this.DrawKillStat(k[0], "NormalMob");
                ImGui.SameLine(0f, SegmentGap);
                this.DrawKillStat(k[1], "MagicMob");
                ImGui.SameLine(0f, SegmentGap);
                this.DrawKillStat(k[2], "RareMob");
                ImGui.SameLine(0f, SegmentGap);
                this.DrawKillStat(k[3], "UniqueMob");
            }

            ImGui.SetWindowFontScale(1f);
            ImGui.End();
        }

        private void DrawSessionStrip()
        {
            if (!this.TryGetCenteredStripAnchor(out var anchorX, out var anchorY))
            {
                return;
            }

            ImGui.SetNextWindowPos(new Vector2(anchorX, anchorY), ImGuiCond.Always, new Vector2(0.5f, 1f));
            ImGui.SetNextWindowBgAlpha(Math.Clamp(this.Settings.BarOpacity, 0f, 1f));
            const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoNav |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoMove;

            if (!ImGui.Begin("##farmtracker_session_strip", flags))
            {
                ImGui.End();
                return;
            }

            var scale = Math.Clamp(this.Settings.OverlayFontScale, 0.75f, 1.4f);
            ImGui.SetWindowFontScale(scale);

            this.SessionTotals(out var totalActive, out var totalDivine);
            int maps = this.mapRuns.Count;
            double perHour = totalActive.TotalHours > 0.01 ? totalDivine / totalActive.TotalHours : 0;

            ImGui.TextDisabled(L("Session", "Session"));
            ImGui.SameLine(0f, IconPad);
            ImGui.TextUnformatted(FormatElapsed(DateTime.UtcNow - this.sessionStartUtc));

            ImGui.SameLine(0f, SegmentGap);
            ImGui.TextUnformatted($"{maps} {L("maps", "maps")}");

            ImGui.SameLine(0f, SegmentGap);
            this.DrawOverlayProfit(totalDivine, showAltCurrency: false);

            if (this.Settings.OverlayShowProfitPerHour)
            {
                ImGui.SameLine(0f, SegmentGap);
                this.DrawOverlayProfit(perHour, suffix: "/h", showAltCurrency: false);
            }

            ImGui.SetWindowFontScale(1f);
            ImGui.End();
        }

        private void DrawOverlayProfit(double divine, string suffix = "", bool showAltCurrency = true)
        {
            var col = divine >= 0 ? this.Settings.ProfitColor : this.Settings.LossColor;

            switch (this.Settings.DisplayCurrency)
            {
                case FarmOverlayCurrency.Exalted:
                    this.DrawProfitAmount(col, divine * Math.Max(FarmPriceFetcher.DivineToExaltedRate, 0), "Exalt", suffix);
                    if (showAltCurrency && FarmPriceFetcher.DivineToExaltedRate > 0)
                    {
                        this.DrawAltProfitInParens(col, divine, "Divine");
                    }

                    break;

                case FarmOverlayCurrency.Chaos:
                    var cpd = FarmPriceFetcher.GetChaosPerDivine();
                    this.DrawProfitAmount(col, cpd > 0 ? divine * cpd : 0, "Chaos", suffix);
                    if (showAltCurrency && cpd > 0)
                    {
                        this.DrawAltProfitInParens(col, divine, "Divine");
                    }

                    break;

                default:
                    this.DrawProfitAmount(col, divine, "Divine", suffix);
                    if (showAltCurrency && FarmPriceFetcher.DivineToExaltedRate > 0)
                    {
                        this.DrawAltProfitInParens(col, divine * FarmPriceFetcher.DivineToExaltedRate, "Exalt");
                    }

                    break;
            }
        }

        private void DrawProfitAmount(Vector4 col, double amount, string iconKey, string suffix)
        {
            ImGui.TextColored(col, $"{amount:+0.0;-0.0;0}{suffix}");
            ImGui.SameLine(0f, IconPad);
            if (this.Settings.ShowCurrencyIcons)
            {
                var h = ImGui.GetTextLineHeight();
                if (!FarmOverlayIcons.TryDraw(iconKey, h))
                {
                    ImGui.TextDisabled(iconKey[..1]);
                }
            }
        }

        private void DrawAltProfitInParens(Vector4 col, double amount, string iconKey)
        {
            ImGui.SameLine(0f, SegmentGap);
            ImGui.TextColored(col, $"({amount:+0.0;-0.0;0}");
            ImGui.SameLine(0f, IconPad);
            if (this.Settings.ShowCurrencyIcons)
            {
                var h = ImGui.GetTextLineHeight();
                if (FarmOverlayIcons.TryDraw(iconKey, h))
                {
                    ImGui.SameLine(0f, 1f);
                }
            }

            ImGui.TextColored(col, ")");
        }

        private void DrawKillStat(int count, string mobIconKey)
        {
            ImGui.TextUnformatted(count.ToString());
            if (!this.Settings.ShowCurrencyIcons)
            {
                return;
            }

            ImGui.SameLine(0f, 3f);
            var h = ImGui.GetTextLineHeight();
            if (!FarmOverlayIcons.TryDraw(mobIconKey, h))
            {
                ImGui.TextDisabled("·");
            }
        }

        private bool DrawInlineIcon(string key)
        {
            if (!this.Settings.ShowCurrencyIcons)
            {
                return false;
            }

            var h = ImGui.GetTextLineHeight();
            return FarmOverlayIcons.TryDraw(key, h);
        }

        private bool TryGetCenteredStripAnchor(out float anchorX, out float anchorY)
        {
            if (this.Settings.OverlayAnchor == FarmOverlayAnchor.Custom)
            {
                anchorX = this.Settings.CustomOverlayPosition.X;
                anchorY = this.Settings.CustomOverlayPosition.Y;
                return true;
            }

            if (this.expBarAnchor.TryGetRect(out var xpPos, out var xpSize))
            {
                anchorY = xpPos.Y - this.Settings.BarBottomOffset;
                anchorX = xpPos.X + (xpSize.X * 0.5f);
                return true;
            }

            var vp = ImGui.GetMainViewport();
            anchorY = vp.Pos.Y + vp.Size.Y - this.Settings.BarBottomOffset;
            anchorX = vp.Pos.X + (vp.Size.X * 0.5f);
            return true;
        }
    }
}
