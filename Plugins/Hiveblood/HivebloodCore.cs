namespace Hiveblood
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Numerics;
    using System.Runtime.InteropServices;
    using GameHelper;
    using GameHelper.Localization;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.UiElement;
    using GameHelper.Utils;
    using ImGuiNET;
    using Newtonsoft.Json;

    public sealed class HivebloodCore : PCore<HivebloodSettings>
    {
        private const int GainDedupSeconds = 4;

        private readonly HivebloodUiScanner scanner = new();
        private readonly Dictionary<string, DateTime> recentGainKeys = new(StringComparer.Ordinal);
        private readonly Stopwatch gainSaveDebounce = Stopwatch.StartNew();
        private bool pendingTrackerSave;

        private string lastStatus = string.Empty;

        private string SettingsPath => Path.Join(this.DllDirectory, "config", "settings.txt");

        public override void OnEnable(bool isGameOpened)
        {
            if (File.Exists(this.SettingsPath))
            {
                try
                {
                    var content = File.ReadAllText(this.SettingsPath);
                    this.Settings = JsonConvert.DeserializeObject<HivebloodSettings>(content) ?? new HivebloodSettings();
                }
                catch
                {
                    this.Settings = new HivebloodSettings();
                }
            }
        }

        public override void OnDisable()
        {
            this.FlushPendingTrackerSave(force: true);
            this.scanner.ResetProcess();
            this.SaveSettings();
        }

        public override void SaveSettings()
        {
            var dir = Path.GetDirectoryName(this.SettingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(this.SettingsPath, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
        }

        public override void DrawSettings()
        {
            ImGui.TextWrapped(L(
                "Tracks Hiveblood for Keepers of the Flame: syncs from the Genesis Tree UI and adds +N popups from Breach runs. Visit the Genesis Tree once to calibrate.",
                "Verfolgt Hiveblood (Keepers of the Flame): Sync am Genesis Tree und +N-Popups aus Breaches. Einmal am Genesis Tree kalibrieren."));

            ImGui.Separator();
            ImGui.Checkbox(L("Only when inventory is open", "Nur bei offenem Inventar"), ref this.Settings.ShowOnlyWithInventory);
            ImGui.Checkbox(L("Also show when inventory is closed", "Auch bei geschlossenem Inventar"), ref this.Settings.ShowAlways);
            ImGui.SliderFloat(L("Font scale", "Schriftgroesse"), ref this.Settings.OverlayFontScale, 0.75f, 1.6f, "%.2f");

            var anchorIndex = (int)this.Settings.OverlayAnchor;
            if (ImGui.Combo(
                    L("Position anchor", "Positions-Anker"),
                    ref anchorIndex,
                    $"{L("Inventory top-left", "Inventar oben links")}\0" +
                    $"{L("Inventory bottom (near gold)", "Inventar unten (nahe Gold)")}\0" +
                    $"{L("Custom screen position", "Eigene Bildschirmposition")}\0"))
            {
                this.Settings.OverlayAnchor = (HivebloodOverlayAnchor)anchorIndex;
            }

            ImGui.DragFloat2(
                L("Offset from anchor (px)", "Offset vom Anker (px)"),
                ref this.Settings.OverlayOffset,
                1f,
                -400f,
                400f,
                "%.0f");
            ImGuiHelper.ToolTip(L(
                "Bottom anchor: left edge of the inventory panel, just above the gold line. Drag X/Y to fine-tune.",
                "Unten-Anker: linke Kante des Inventars, knapp ueber der Gold-Zeile. X/Y zum Feintuning."));

            if (this.Settings.OverlayAnchor == HivebloodOverlayAnchor.CustomScreen)
            {
                ImGui.DragFloat2(
                    L("Screen position (px)", "Bildschirmposition (px)"),
                    ref this.Settings.OverlayScreenPosition,
                    1f,
                    0f,
                    4000f,
                    "%.0f");
            }
            ImGui.ColorEdit4(L("Text color", "Textfarbe"), ref this.Settings.TextColor);
            ImGui.Checkbox(L("Warn near cap (100,000)", "Warnung nahe Cap (100.000)"), ref this.Settings.WarnNearCap);
            ImGui.SliderInt(L("Warn from amount", "Warnung ab Betrag"), ref this.Settings.WarnThreshold, 80_000, 100_000);
            ImGui.Checkbox(L("Show gains since last tree sync", "Gewinn seit letztem Tree-Sync"), ref this.Settings.ShowSessionGains);
            ImGui.Checkbox(L("Debug status line", "Debug-Statuszeile"), ref this.Settings.DebugStatusLine);

            ImGui.Separator();
            this.DrawStatusBlock();

            if (ImGui.Button(L("Reset tracker", "Tracker zuruecksetzen")))
            {
                this.Settings.EstimatedAmount = 0;
                this.Settings.HasSyncedOnce = false;
                this.Settings.LastTreeSyncUtc = DateTime.MinValue;
                this.Settings.SessionGainSinceSync = 0;
                this.recentGainKeys.Clear();
                this.QueueTrackerSave(immediate: true);
            }

            ImGui.SameLine();
            if (ImGui.Button(L("Save now", "Jetzt speichern")))
            {
                this.FlushPendingTrackerSave(force: true);
                this.SaveSettings();
            }
        }

        public override void DrawUI()
        {
            if (Core.States.GameCurrentState != GameStateTypes.InGameState)
            {
                return;
            }

            if (!Core.Process.Foreground &&
                Process.GetCurrentProcess().MainWindowHandle != GetForegroundWindow())
            {
                return;
            }

            var gameUi = Core.States.InGameStateObject.GameUi;
            if (gameUi.Address == IntPtr.Zero)
            {
                return;
            }

            var inventoryOpen = gameUi.RightPanel.IsVisible;
            this.UpdateTracker(gameUi.Address);
            this.FlushPendingTrackerSave();

            if (this.Settings.ShowOnlyWithInventory && !inventoryOpen && !this.Settings.ShowAlways)
            {
                return;
            }

            if (!this.ShouldDrawOverlay(inventoryOpen))
            {
                return;
            }

            this.DrawOverlay(gameUi.RightPanel, inventoryOpen);
        }

        private void UpdateTracker(IntPtr gameUiRoot)
        {
            if (!this.scanner.TryScan(gameUiRoot, out var treeTotal, out var gains))
            {
                return;
            }

            var changed = false;
            var treeSyncedThisFrame = false;
            if (treeTotal.HasValue)
            {
                var total = Math.Clamp(treeTotal.Value, 0, HivebloodSettings.HivebloodCap);
                if (this.Settings.EstimatedAmount != total || !this.Settings.HasSyncedOnce)
                {
                    this.Settings.EstimatedAmount = total;
                    this.Settings.HasSyncedOnce = true;
                    this.Settings.LastTreeSyncUtc = DateTime.UtcNow;
                    this.Settings.SessionGainSinceSync = 0;
                    this.recentGainKeys.Clear();
                    changed = true;
                    treeSyncedThisFrame = true;
                    this.lastStatus = L($"Synced from Genesis Tree: {total:N0}", $"Genesis Tree Sync: {total:N0}");
                }
            }

            foreach (var gain in gains)
            {
                var key = $"+{gain}";
                var now = DateTime.UtcNow;
                if (this.recentGainKeys.TryGetValue(key, out var seen) &&
                    (now - seen).TotalSeconds < GainDedupSeconds)
                {
                    continue;
                }

                this.recentGainKeys[key] = now;
                this.Settings.EstimatedAmount = Math.Clamp(
                    this.Settings.EstimatedAmount + gain,
                    0,
                    HivebloodSettings.HivebloodCap);
                this.Settings.SessionGainSinceSync += gain;
                if (!this.Settings.HasSyncedOnce)
                {
                    this.Settings.HasSyncedOnce = true;
                }

                changed = true;
                this.lastStatus = L($"+{gain:N0} Hiveblood", $"+{gain:N0} Hiveblood");
            }

            if (changed)
            {
                this.QueueTrackerSave(immediate: treeSyncedThisFrame);
            }
        }

        private void QueueTrackerSave(bool immediate)
        {
            this.pendingTrackerSave = true;
            if (immediate)
            {
                this.FlushPendingTrackerSave(force: true);
            }
        }

        private void FlushPendingTrackerSave(bool force = false)
        {
            if (!this.pendingTrackerSave)
            {
                return;
            }

            if (!force && this.gainSaveDebounce.Elapsed.TotalSeconds < 1.5)
            {
                return;
            }

            this.SaveSettings();
            this.pendingTrackerSave = false;
            this.gainSaveDebounce.Restart();
        }

        private bool ShouldDrawOverlay(bool inventoryOpen)
        {
            if (!this.Settings.HasSyncedOnce && this.Settings.EstimatedAmount <= 0)
            {
                return this.Settings.DebugStatusLine;
            }

            if (this.Settings.ShowOnlyWithInventory && !inventoryOpen && !this.Settings.ShowAlways)
            {
                return false;
            }

            return true;
        }

        private void DrawOverlay(UiElementBase rightPanel, bool inventoryOpen)
        {
            var fg = ImGui.GetForegroundDrawList();
            var font = ImGui.GetFont();
            var scale = Math.Clamp(this.Settings.OverlayFontScale, 0.75f, 1.6f);
            var fontSize = ImGui.GetFontSize() * scale;

            string line1;
            string? line2 = null;
            if (!this.Settings.HasSyncedOnce)
            {
                line1 = L("Hiveblood: visit Genesis Tree", "Hiveblood: Genesis Tree besuchen");
            }
            else
            {
                line1 = $"Hiveblood: {this.Settings.EstimatedAmount:N0}";
                if (this.Settings.ShowSessionGains && this.Settings.SessionGainSinceSync > 0)
                {
                    line2 = L(
                        $"(+{this.Settings.SessionGainSinceSync:N0} since tree)",
                        $"(+{this.Settings.SessionGainSinceSync:N0} seit Tree)");
                }
            }

            var pos = this.ResolveOverlayPosition(rightPanel, inventoryOpen);

            var color = this.Settings.TextColor;
            if (this.Settings.WarnNearCap &&
                this.Settings.EstimatedAmount >= this.Settings.WarnThreshold)
            {
                color = new Vector4(1f, 0.45f, 0.35f, 1f);
            }

            var textColor = ImGui.ColorConvertFloat4ToU32(color);
            var shadow = ImGui.ColorConvertFloat4ToU32(this.Settings.ShadowColor);
            this.DrawShadowedText(fg, font, fontSize, pos, shadow, textColor, line1);
            if (!string.IsNullOrEmpty(line2))
            {
                var size1 = ImGui.CalcTextSize(line1);
                var pos2 = pos + new Vector2(0f, size1.Y * (scale / Math.Max(ImGui.GetFontSize(), 1f)) + 2f);
                this.DrawShadowedText(fg, font, fontSize * 0.9f, pos2, shadow, textColor, line2);
            }

            if (this.Settings.DebugStatusLine && !string.IsNullOrEmpty(this.lastStatus))
            {
                var debugPos = pos + new Vector2(0f, 36f);
                this.DrawShadowedText(
                    fg,
                    font,
                    fontSize * 0.8f,
                    debugPos,
                    shadow,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.7f, 1f)),
                    this.lastStatus);
            }
        }

        private void DrawShadowedText(
            ImDrawListPtr draw,
            ImFontPtr font,
            float fontSize,
            Vector2 pos,
            uint shadowColor,
            uint textColor,
            string text)
        {
            draw.AddText(font, fontSize, pos + new Vector2(1f, 1f), shadowColor, text);
            draw.AddText(font, fontSize, pos, textColor, text);
        }

        private void DrawStatusBlock()
        {
            ImGui.TextDisabled(L("Tracker", "Tracker"));
            ImGui.BulletText(this.Settings.HasSyncedOnce
                ? L($"Estimate: {this.Settings.EstimatedAmount:N0}", $"Schaetzung: {this.Settings.EstimatedAmount:N0}")
                : L("Not calibrated yet", "Noch nicht kalibriert"));
            if (this.Settings.LastTreeSyncUtc != DateTime.MinValue)
            {
                ImGui.BulletText(L(
                    $"Last tree sync: {this.Settings.LastTreeSyncUtc.ToLocalTime():g}",
                    $"Letzter Tree-Sync: {this.Settings.LastTreeSyncUtc.ToLocalTime():g}"));
            }

            if (this.Settings.SessionGainSinceSync > 0)
            {
                ImGui.BulletText(L(
                    $"Gains since sync: +{this.Settings.SessionGainSinceSync:N0}",
                    $"Gewinn seit Sync: +{this.Settings.SessionGainSinceSync:N0}"));
            }

            if (!string.IsNullOrEmpty(this.lastStatus))
            {
                ImGui.TextDisabled(this.lastStatus);
            }
        }

        private Vector2 ResolveOverlayPosition(UiElementBase rightPanel, bool inventoryOpen)
        {
            if (this.Settings.OverlayAnchor == HivebloodOverlayAnchor.CustomScreen || !inventoryOpen)
            {
                return this.Settings.OverlayScreenPosition + this.Settings.OverlayOffset;
            }

            var panelPos = rightPanel.Position;
            var panelSize = rightPanel.Size;
            var basePos = this.Settings.OverlayAnchor switch
            {
                HivebloodOverlayAnchor.InventoryBottomNearGold => panelPos + new Vector2(6f, panelSize.Y - 32f),
                _ => panelPos,
            };

            return basePos + this.Settings.OverlayOffset;
        }

        private static string L(string english, string german) => OverlayLocalization.L(english, german);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}
