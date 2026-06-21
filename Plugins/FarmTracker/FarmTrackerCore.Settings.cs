namespace FarmTracker
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using GameHelper.Utils;
    using ImGuiNET;

    public sealed partial class FarmTrackerCore
    {
        private int archivedSessionsRefreshCooldown;
        private List<ArchivedSessionSummary> archivedSessions = new();
        private int expandedMapIndex = -1;
        private bool showSessionHistory;
        private SessionRecord? detailSession;
        private ArchivedMapRun? detailMap;

        public override void DrawSettings()
        {
            FarmLeagueProvider.EnsureLoaded();
            FarmCustomPrices.ReloadIfNeeded(this.DllDirectory);

            if (ImGui.BeginTabBar("##FarmTrackerTabs"))
            {
                if (ImGui.BeginTabItem(L("Overlay", "Overlay")))
                {
                    this.DrawOverlaySettingsTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(L("Session", "Session")))
                {
                    this.DrawCurrentSessionTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(L("Archive", "Archiv")))
                {
                    this.DrawArchiveTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(L("Loot", "Loot")))
                {
                    this.DrawSessionLootTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(L("Prices", "Preise")))
                {
                    this.DrawPricesTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(L("Custom prices", "Eigene Preise")))
                {
                    ImGui.TextWrapped(L(
                        "Edit custom_prices.txt in the plugin folder (ItemName=divine value, one per line).",
                        "Bearbeite custom_prices.txt im Plugin-Ordner (ItemName=Divine-Wert, eine Zeile pro Item)."));
                    ImGui.TextDisabled(Path.Join(this.DllDirectory, "custom_prices.txt"));
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        private void DrawOverlaySettingsTab()
        {
            var mode = (int)this.Settings.OverlayMode;
            if (ImGui.Combo(L("Overlay mode", "Overlay-Modus"), ref mode,
                    $"{L("Auto (map / session)", "Auto (Map / Session)")}\0" +
                    $"{L("Map only", "Nur Map")}\0" +
                    $"{L("Session only", "Nur Session")}\0" +
                    $"{L("Hidden", "Aus")}\0"))
            {
                this.Settings.OverlayMode = (FarmOverlayMode)mode;
            }

            var anchor = (int)this.Settings.OverlayAnchor;
            if (ImGui.Combo(L("Anchor", "Anker"), ref anchor,
                    $"{L("Experience bar", "Erfahrungsleiste")}\0{L("Custom position", "Eigene Position")}\0"))
            {
                this.Settings.OverlayAnchor = (FarmOverlayAnchor)anchor;
            }

            if (this.Settings.OverlayAnchor == FarmOverlayAnchor.Custom)
            {
                ImGui.DragFloat2(L("Position (px)", "Position (px)"), ref this.Settings.CustomOverlayPosition, 1f);
            }
            else
            {
                ImGui.SliderFloat(L("Offset above XP bar (px)", "Abstand ueber XP-Leiste (px)"), ref this.Settings.BarBottomOffset, 0f, 80f, "%.0f");
                ImGui.TextDisabled(L(
                    "Map and session bars are centered above the experience bar.",
                    "Map- und Session-Leiste sind zentriert ueber der Erfahrungsleiste."));
            }

            ImGui.SliderFloat(L("Bar opacity", "Leisten-Deckkraft"), ref this.Settings.BarOpacity, 0.2f, 1f, "%.2f");
            ImGui.SliderFloat(L("Font scale", "Schriftgroesse"), ref this.Settings.OverlayFontScale, 0.75f, 1.4f, "%.2f");
            ImGui.Checkbox(L("Show kills on map strip", "Kills auf Map-Leiste"), ref this.Settings.OverlayShowKills);
            ImGui.Checkbox(L("Show profit/h (div/h)", "Profit/h (div/h) anzeigen"), ref this.Settings.OverlayShowProfitPerHour);
            ImGui.Checkbox(L("Overlay icons", "Overlay-Icons"), ref this.Settings.ShowCurrencyIcons);
            ImGui.Checkbox(L("Hide when game in background", "Bei Hintergrund ausblenden"), ref this.Settings.HideOverlayWhenGameInBackground);
            ImGui.Checkbox(L("Pause map timer when game paused (ESC)", "Map-Timer bei ESC pausieren"),
                ref this.Settings.PauseTimerWhenGamePaused);
            ImGuiHelper.ToolTip(L(
                "Stops the map timer and div/h denominator while the escape menu is open.",
                "Stoppt Map-Timer und div/h-Nenner solange das ESC-Menue offen ist."));
            ImGui.Checkbox(L("Count kills in town / hideout", "Kills in Stadt / Hideout"), ref this.Settings.CountKillsInTownOrHideout);

            if (!FarmMetaArt.HasBridge)
            {
                ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), L(
                    "Tip: place metaArt.json beside the plugin for language-independent ninja pricing.",
                    "Tipp: metaArt.json neben das Plugin legen fuer sprachunabhaengige Ninja-Preise."));
            }

            if (!Directory.Exists(Path.Join(this.DllDirectory, "icons")) ||
                !File.Exists(Path.Join(this.DllDirectory, "icons", "Divine.png")))
            {
                ImGui.TextDisabled(L(
                    "Overlay icons (map, time, kills, currency) download on first run.",
                    "Overlay-Icons (Map, Zeit, Kills, Waehrung) werden beim ersten Start geladen."));
            }
        }

        private void DrawCurrentSessionTab()
        {
            this.SessionTotals(out var totalActive, out var totalDivine);
            var perHour = totalActive.TotalHours > 0.01 ? totalDivine / totalActive.TotalHours : 0;

            ImGui.Text($"{L("Session time", "Session-Zeit")}: {FormatElapsed(DateTime.UtcNow - this.sessionStartUtc)}");
            ImGui.Text($"{L("Maps", "Maps")}: {this.mapRuns.Count}");
            ImGui.Text($"{L("Total profit", "Gesamt-Profit")}: {this.FormatCurrency(totalDivine)}");
            if (perHour > 0)
            {
                ImGui.Text($"{L("Profit/h", "Profit/h")}: {this.FormatCurrency(perHour)}");
            }

            if (ImGui.Button(L("New session", "Neue Session")))
            {
                this.ResetSession(archive: true);
            }

            ImGui.SameLine();
            if (ImGui.Button(L("Refresh lists", "Listen aktualisieren")))
            {
                this.archivedSessionsRefreshCooldown = 0;
            }

            ImGui.Separator();
            ImGui.Text(L("Maps this session", "Maps dieser Session"));

            if (this.mapRuns.Count == 0)
            {
                ImGui.TextDisabled(L("No maps yet.", "Noch keine Maps."));
                ImGui.TextWrapped(L(
                    "Enter a map to start tracking. Loot is counted per map run (inventory diff).",
                    "Betritt eine Map zum Tracken. Loot wird pro Map-Lauf gezaehlt (Inventar-Diff)."));
                return;
            }

            if (ImGui.BeginTable("##farm_maps", 5,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                    new Vector2(0, 220)))
            {
                ImGui.TableSetupColumn(L("Map", "Map"));
                ImGui.TableSetupColumn(L("Time", "Zeit"), ImGuiTableColumnFlags.WidthFixed, 64);
                ImGui.TableSetupColumn(L("Profit", "Profit"), ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("N·M·R·U", ImGuiTableColumnFlags.WidthFixed, 72);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 56);
                ImGui.TableHeadersRow();

                for (int i = this.mapRuns.Count - 1; i >= 0; i--)
                {
                    var run = this.mapRuns[i];
                    var isLive = ReferenceEquals(run, this.currentMap) && this.onMapArea;
                    var active = TimeSpan.FromSeconds(run.BankedSeconds);
                    if (isLive && this.mapRunStartUtc is { } start)
                    {
                        active += DateTime.UtcNow - start;
                    }

                    var profit = isLive
                        ? this.ValueOfGained(this.CurrentGainedLive(), out _)
                        : this.ValueOfGained(run.Gained, out _);

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var label = isLive ? $"{run.Name} *" : run.Name;
                    ImGui.TextUnformatted(label);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(FormatElapsed(active));
                    ImGui.TableNextColumn();
                    ImGui.TextColored(profit >= 0 ? this.Settings.ProfitColor : this.Settings.LossColor,
                        this.FormatCurrency(profit));
                    ImGui.TableNextColumn();
                    ImGui.TextDisabled($"{run.Kills[0]}·{run.Kills[1]}·{run.Kills[2]}·{run.Kills[3]}");
                    ImGui.TableNextColumn();
                    if (ImGui.SmallButton($"{L("Loot", "Loot")}##m{i}"))
                    {
                        this.expandedMapIndex = this.expandedMapIndex == i ? -1 : i;
                    }
                }

                ImGui.EndTable();
            }

            if (this.expandedMapIndex >= 0 && this.expandedMapIndex < this.mapRuns.Count)
            {
                var run = this.mapRuns[this.expandedMapIndex];
                var gained = ReferenceEquals(run, this.currentMap) && this.onMapArea
                    ? this.CurrentGainedLive()
                    : run.Gained;
                this.ValueOfGained(gained, out var lines);
                this.DrawLootTable(lines, 160f);
            }

            ImGui.SliderInt(L("Map history size", "Map-Historie Groesse"), ref this.Settings.MapHistorySize, 10, 200);
        }

        private void DrawSessionLootTab()
        {
            var lines = this.BuildSessionLootLines();
            this.DrawLootTable(lines, 280f);
            ImGui.Checkbox(L("Show unpriced items", "Unbewertetes Loot anzeigen"), ref this.Settings.ShowUnpricedItems);
        }

        private void DrawLootTable(List<LootLine> lines, float height)
        {
            var filtered = lines
                .Where(l => this.Settings.ShowUnpricedItems || l.Priced)
                .ToList();

            if (filtered.Count == 0)
            {
                ImGui.TextDisabled(L("No loot recorded.", "Kein Loot erfasst."));
                return;
            }

            if (ImGui.BeginTable("##farm_loot", 3,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                    new Vector2(0, height)))
            {
                ImGui.TableSetupColumn(L("Item", "Item"));
                ImGui.TableSetupColumn("x", ImGuiTableColumnFlags.WidthFixed, 36);
                ImGui.TableSetupColumn(L("Value", "Wert"), ImGuiTableColumnFlags.WidthFixed, 88);
                ImGui.TableHeadersRow();
                foreach (var row in filtered)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var color = row.Priced ? this.Settings.TextColor : this.Settings.UnpricedColor;
                    ImGui.TextColored(color, row.Label);
                    ImGui.TableNextColumn();
                    ImGui.TextColored(color, row.Count.ToString());
                    ImGui.TableNextColumn();
                    ImGui.TextColored(row.Priced ? this.Settings.ProfitColor : this.Settings.UnpricedColor,
                        row.Priced ? this.FormatCurrency(row.TotalDivine) : "—");
                }

                ImGui.EndTable();
            }
        }

        private void DrawArchiveTab()
        {
            if (--this.archivedSessionsRefreshCooldown <= 0)
            {
                this.archivedSessionsRefreshCooldown = 120;
                this.archivedSessions = FarmSessionHistory.LoadSummaries(this.ArchiveDir);
            }

            ImGui.SliderInt(L("Sessions to keep", "Sessions behalten"), ref this.Settings.MaxSessions, 5, 200);
            if (ImGui.Button(L("Open session history", "Session-Historie oeffnen")))
            {
                this.archivedSessions = FarmSessionHistory.LoadSummaries(this.ArchiveDir);
                this.showSessionHistory = true;
            }

            ImGui.Separator();
            if (this.archivedSessions.Count == 0)
            {
                ImGui.TextDisabled(L("No archived sessions.", "Keine archivierten Sessions."));
                return;
            }

            if (ImGui.BeginTable("##farm_archive_preview", 5,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                    new Vector2(0, 200)))
            {
                ImGui.TableSetupColumn(L("Date", "Datum"));
                ImGui.TableSetupColumn(L("Maps", "Maps"), ImGuiTableColumnFlags.WidthFixed, 48);
                ImGui.TableSetupColumn(L("Profit", "Profit"), ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn(L("Profit/h", "Profit/h"), ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableHeadersRow();

                for (int i = 0; i < this.archivedSessions.Count; i++)
                {
                    var s = this.archivedSessions[i];
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(s.StartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(s.Maps.ToString());
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(this.FormatCurrency(s.ProfitDivine));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(this.FormatCurrency(s.ProfitPerHourDivine));
                    ImGui.TableNextColumn();
                    if (ImGui.SmallButton($"{L("Details", "Details")}##a{i}"))
                    {
                        this.detailSession = FarmSessionHistory.LoadSession(this.ArchiveDir, s.FileName);
                        this.detailMap = null;
                        this.showSessionHistory = true;
                    }

                    ImGui.SameLine();
                    if (ImGui.SmallButton($"{L("Delete", "Loeschen")}##d{i}"))
                    {
                        var full = FarmSessionHistory.LoadSession(this.ArchiveDir, s.FileName);
                        if (full != null)
                        {
                            FarmSessionHistory.DeleteSession(full);
                            this.archivedSessionsRefreshCooldown = 0;
                        }
                    }
                }

                ImGui.EndTable();
            }
        }

        private void DrawPricesTab()
        {
            var source = this.Settings.PriceSource;
            if (ImGui.RadioButton("poe2scout", source == FarmPriceFetcher.SourcePoe2Scout))
            {
                this.Settings.PriceSource = FarmPriceFetcher.SourcePoe2Scout;
                FarmPriceFetcher.Configure(this.Settings.PriceSource, this.Settings.League, this.Settings.PriceRefreshMinutes);
                FarmPriceFetcher.ForceRefresh(this.DllDirectory, ignoreCooldown: true);
            }

            ImGui.SameLine();
            if (ImGui.RadioButton("poe.ninja", source == FarmPriceFetcher.SourcePoeNinja))
            {
                this.Settings.PriceSource = FarmPriceFetcher.SourcePoeNinja;
                FarmPriceFetcher.Configure(this.Settings.PriceSource, this.Settings.League, this.Settings.PriceRefreshMinutes);
                FarmPriceFetcher.ForceRefresh(this.DllDirectory, ignoreCooldown: true);
            }

            ImGui.Checkbox(L("Use metaArt.json for item keys", "metaArt.json fuer Item-Keys"), ref this.Settings.UseMetaArtForPricing);
            ImGuiHelper.ToolTip(L(
                "Maps metadata ids to poe.ninja art ids — works on non-English clients when metaArt.json is present.",
                "Mappt Metadata-IDs auf Ninja-Art-IDs — funktioniert bei nicht-englischem Client mit metaArt.json."));

            var leagues = FarmLeagueProvider.Leagues;
            if (leagues.Count > 0)
            {
                var idx = Math.Max(0, leagues.ToList().FindIndex(l => l.Equals(this.Settings.League, StringComparison.OrdinalIgnoreCase)));
                if (ImGui.Combo(L("League", "League"), ref idx, string.Join('\0', leagues) + "\0"))
                {
                    this.Settings.League = leagues[idx];
                    FarmPriceFetcher.Configure(this.Settings.PriceSource, this.Settings.League, this.Settings.PriceRefreshMinutes);
                    FarmPriceFetcher.ForceRefresh(this.DllDirectory, ignoreCooldown: true);
                }
            }

            ImGui.SliderInt(L("Refresh (min)", "Aktualisierung (Min.)"), ref this.Settings.PriceRefreshMinutes, 1, 60);
            var currency = (int)this.Settings.DisplayCurrency;
            if (ImGui.Combo(L("Display currency", "Anzeige-Waehrung"), ref currency, "Divine\0Exalted\0Chaos\0"))
            {
                this.Settings.DisplayCurrency = (FarmOverlayCurrency)currency;
            }

            ImGui.Text($"{L("Market prices", "Marktpreise")}: {FarmPriceFetcher.LoadedItemCount}");
            if (ImGui.Button(L("Refresh prices now", "Preise jetzt aktualisieren")))
            {
                FarmPriceFetcher.ForceRefresh(this.DllDirectory, ignoreCooldown: true);
            }
        }

        private void DrawSessionHistoryWindows()
        {
            if (this.showSessionHistory)
            {
                this.DrawSessionHistoryWindow();
            }

            if (this.detailSession != null)
            {
                this.DrawSessionDetailWindow();
            }

            if (this.detailMap != null)
            {
                this.DrawArchivedMapLootWindow();
            }
        }

        private void DrawSessionHistoryWindow()
        {
            ImGui.SetNextWindowSize(new Vector2(720, 360), ImGuiCond.FirstUseEver);
            var open = true;
            if (ImGui.Begin(L("Session history", "Session-Historie"), ref open))
            {
                if (ImGui.Button(L("Refresh", "Aktualisieren")))
                {
                    this.archivedSessions = FarmSessionHistory.LoadSummaries(this.ArchiveDir);
                }

                if (ImGui.BeginTable("##hist_sessions", 6,
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupColumn(L("Date", "Datum"));
                    ImGui.TableSetupColumn(L("Length", "Dauer"), ImGuiTableColumnFlags.WidthFixed, 70);
                    ImGui.TableSetupColumn(L("Maps", "Maps"), ImGuiTableColumnFlags.WidthFixed, 48);
                    ImGui.TableSetupColumn(L("Profit", "Profit"), ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn(L("Profit/h", "Profit/h"), ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 140);
                    ImGui.TableHeadersRow();

                    foreach (var s in this.archivedSessions)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(s.StartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(FormatElapsed(TimeSpan.FromSeconds(s.DurationSec)));
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(s.Maps.ToString());
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(this.FormatCurrency(s.ProfitDivine));
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(this.FormatCurrency(s.ProfitPerHourDivine));
                        ImGui.TableNextColumn();
                        if (ImGui.SmallButton($"{L("Details", "Details")}##{s.FileName}"))
                        {
                            this.detailSession = FarmSessionHistory.LoadSession(this.ArchiveDir, s.FileName);
                        }
                    }

                    ImGui.EndTable();
                }
            }

            ImGui.End();
            if (!open)
            {
                this.showSessionHistory = false;
            }
        }

        private void DrawSessionDetailWindow()
        {
            var rec = this.detailSession!;
            ImGui.SetNextWindowSize(new Vector2(640, 400), ImGuiCond.FirstUseEver);
            var open = true;
            if (ImGui.Begin($"{L("Session", "Session")} {rec.StartUtc.ToLocalTime():yyyy-MM-dd HH:mm}###farm_detail", ref open))
            {
                ImGui.Text($"{L("Duration", "Dauer")}: {FormatElapsed(rec.EndUtc - rec.StartUtc)}");
                ImGui.Text($"{L("Maps", "Maps")}: {rec.Maps.Count}");
                ImGui.Text($"{L("Total", "Gesamt")}: {this.FormatCurrency(rec.TotalDivine())}");
                ImGui.Text($"{L("Profit/h", "Profit/h")}: {this.FormatCurrency(rec.ProfitPerHourDivine())}");
                ImGui.Separator();

                if (ImGui.BeginTable("##detail_maps", 4,
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupColumn(L("Map", "Map"));
                    ImGui.TableSetupColumn(L("Time", "Zeit"), ImGuiTableColumnFlags.WidthFixed, 64);
                    ImGui.TableSetupColumn(L("Profit", "Profit"), ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 56);
                    ImGui.TableHeadersRow();
                    for (int i = 0; i < rec.Maps.Count; i++)
                    {
                        var m = rec.Maps[i];
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(m.Name);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(FormatElapsed(TimeSpan.FromSeconds(m.ActiveSeconds)));
                        ImGui.TableNextColumn();
                        ImGui.TextColored(m.ProfitDivine >= 0 ? this.Settings.ProfitColor : this.Settings.LossColor,
                            this.FormatCurrency(m.ProfitDivine));
                        ImGui.TableNextColumn();
                        if (ImGui.SmallButton($"{L("Loot", "Loot")}##dm{i}"))
                        {
                            this.detailMap = m;
                        }
                    }

                    ImGui.EndTable();
                }
            }

            ImGui.End();
            if (!open)
            {
                this.detailSession = null;
                this.detailMap = null;
            }
        }

        private void DrawArchivedMapLootWindow()
        {
            var m = this.detailMap!;
            ImGui.SetNextWindowSize(new Vector2(420, 320), ImGuiCond.FirstUseEver);
            var open = true;
            if (ImGui.Begin($"{L("Loot", "Loot")} — {m.Name}###farm_map_loot", ref open))
            {
                ImGui.TextColored(this.Settings.ProfitColor, this.FormatCurrency(m.ProfitDivine));
                ImGui.Separator();
                if (ImGui.BeginTable("##arch_loot", 3,
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupColumn(L("Item", "Item"));
                    ImGui.TableSetupColumn("x", ImGuiTableColumnFlags.WidthFixed, 40);
                    ImGui.TableSetupColumn(L("Value", "Wert"), ImGuiTableColumnFlags.WidthFixed, 88);
                    ImGui.TableHeadersRow();
                    foreach (var line in m.Loot)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(line.Label);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(line.Count.ToString());
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(line.Priced ? this.FormatCurrency(line.TotalDivine) : "—");
                    }

                    ImGui.EndTable();
                }
            }

            ImGui.End();
            if (!open)
            {
                this.detailMap = null;
            }
        }
    }
}
