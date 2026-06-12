namespace MapKillCounter
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Numerics;
    using System.Runtime.InteropServices;
    using Coroutine;
    using GameHelper;
    using GameHelper.CoroutineEvents;
    using GameHelper.Localization;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.Utils;
    using GameHelper.RemoteEnums.Entity;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.FilesStructures;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using ImGuiNET;
    using Newtonsoft.Json;

    public sealed class MapKillCounterCore : PCore<MapKillCounterSettings>
{
    private readonly int[] killCounts = new int[4];
    private readonly int[] sessionKillCounts = new int[4];
    private readonly Dictionary<uint, MonsterTrack> trackedMonsters = new();
    private readonly Stopwatch mapTimer = new();
    private readonly Stopwatch sessionTimer = new();
    private bool timerRunning;
    private bool sessionTimerRunning;

    private string currentAreaName = string.Empty;
    private string sessionMapAreaName = string.Empty;
    private string sessionMapAreaHash = string.Empty;
    private bool inSanctuary;
    private bool areaChangePending;
    private ActiveCoroutine? onAreaChange;

    private string SettingsPath => Path.Join(this.DllDirectory, "config", "settings.txt");

    public override void OnEnable(bool isGameOpened)
    {
        if (File.Exists(this.SettingsPath))
        {
            try
            {
                var content = File.ReadAllText(this.SettingsPath);
                this.Settings = JsonConvert.DeserializeObject<MapKillCounterSettings>(content) ?? new MapKillCounterSettings();
            }
            catch
            {
                this.Settings = new MapKillCounterSettings();
            }
        }

        this.onAreaChange = CoroutineHandler.Start(this.OnAreaChange(), string.Empty, 0);
        this.ResetSessionTotals();
        this.sessionTimer.Restart();
        this.sessionTimerRunning = true;
        if (this.Settings.ResetOnlyOnNewMap)
        {
            this.areaChangePending = true;
        }
        else
        {
            this.ResetMapStats();
            this.mapTimer.Start();
            this.timerRunning = true;
        }
    }

    public override void OnDisable()
    {
        this.onAreaChange?.Cancel();
        this.onAreaChange = null;
        this.mapTimer.Stop();
        this.sessionTimer.Stop();
        this.sessionTimerRunning = false;
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
        ImGui.Checkbox(L("Show overlay window", "Overlay-Fenster anzeigen"), ref this.Settings.ShowOverlay);
        var overlayModeIndex = (int)this.Settings.OverlayMode;
        if (ImGui.Combo(
                L("Map overlay mode", "Map-Overlay-Modus"),
                ref overlayModeIndex,
                $"{L("Full (kills + time)", "Voll (Kills + Zeit)")}\0{L("Timer only (minimal)", "Nur Timer (minimal)")}\0"))
        {
            this.Settings.OverlayMode = (MapOverlayMode)overlayModeIndex;
        }

        ImGuiHelper.ToolTip(L(
            "Timer only: small clock in the corner, resets on each new map. Drag anywhere on the text to move.",
            "Nur Timer: kleine Uhr in der Ecke, setzt bei jeder neuen Map zurueck. Zum Verschieben irgendwo auf den Text ziehen."));
        ImGui.Checkbox(L("Show session overlay window", "Session-Overlay-Fenster anzeigen"), ref this.Settings.ShowSessionOverlay);
        ImGuiHelper.ToolTip(L(
            "Separate window with kills and play time for the whole GameHelper session. Timer never pauses in town, on ESC, or in background.",
            "Separates Fenster mit Kills und Spielzeit fuer die ganze GH-Session. Timer pausiert nicht in Stadt, bei ESC oder im Hintergrund."));
        ImGui.Checkbox(L("Pause timer in town / hideout", "Timer in Stadt / Hideout pausieren"), ref this.Settings.PauseTimerInTownOrHideout);
        ImGui.Checkbox(L("Hide overlay when game in background", "Overlay ausblenden wenn Spiel im Hintergrund"), ref this.Settings.HideOverlayWhenGameInBackground);
        ImGui.Checkbox(L("Pause timer when game in background", "Timer bei Spiel im Hintergrund pausieren"), ref this.Settings.PauseTimerWhenGameInBackground);
        ImGui.Checkbox(L("Pause timer when game is paused (ESC)", "Timer bei Spielpause pausieren (ESC)"), ref this.Settings.PauseTimerWhenGamePaused);
        ImGui.Checkbox(L("Count kills in town / hideout", "Kills in Stadt / Hideout zaehlen"), ref this.Settings.CountKillsInTownOrHideout);
        ImGui.Checkbox(
            L("Reset only on new map (keep stats in town / hideout)", "Nur bei neuer Map zuruecksetzen (Stats in Stadt / Hideout behalten)"),
            ref this.Settings.ResetOnlyOnNewMap);

        ImGui.Separator();
        var layoutIndex = (int)this.Settings.Layout;
        if (ImGui.Combo(L("Kill list layout", "Kill-Liste"), ref layoutIndex, "Vertical\0Horizontal\0"))
        {
            this.Settings.Layout = (KillListLayout)layoutIndex;
        }

        ImGui.DragFloat(L("Overlay font scale", "Overlay-Schriftgroesse"), ref this.Settings.OverlayFontScale, 0.02f, 0.7f, 1.5f, "%.2f");
        if (this.Settings.OverlaySize.X < 1f || this.Settings.OverlaySize.Y < 1f)
        {
            this.Settings.OverlaySize = this.GetDefaultOverlaySize();
        }

        if (this.Settings.SessionOverlaySize.X < 1f || this.Settings.SessionOverlaySize.Y < 1f)
        {
            this.Settings.SessionOverlaySize = this.GetDefaultOverlaySize();
        }

        ImGui.DragFloat2(L("Map window size (px)", "Map-Fenstergroesse (px)"), ref this.Settings.OverlaySize, 1f, 120f, 900f, "%.0f");
        if (ImGui.Button(L("Reset map window size", "Map-Fenstergroesse zuruecksetzen")))
        {
            this.Settings.OverlaySize = this.GetDefaultOverlaySize();
        }

        ImGui.DragFloat2(L("Session window size (px)", "Session-Fenstergroesse (px)"), ref this.Settings.SessionOverlaySize, 1f, 120f, 900f, "%.0f");
        if (ImGui.Button(L("Reset session window size", "Session-Fenstergroesse zuruecksetzen")))
        {
            this.Settings.SessionOverlaySize = this.GetDefaultOverlaySize();
        }

        ImGui.Separator();
        ImGui.ColorEdit4(L("Window background", "Fenster-Hintergrund"), ref this.Settings.BackgroundColor);
        ImGui.ColorEdit4(L("Text color", "Textfarbe"), ref this.Settings.TextColor);
        ImGui.ColorEdit4(L("Normal color", "Normal-Farbe"), ref this.Settings.NormalColor);
        ImGui.ColorEdit4(L("Magic color", "Magisch-Farbe"), ref this.Settings.MagicColor);
        ImGui.ColorEdit4(L("Rare color", "Selten-Farbe"), ref this.Settings.RareColor);
        ImGui.ColorEdit4(L("Unique color", "Einzigartig-Farbe"), ref this.Settings.UniqueColor);

        ImGui.Separator();
        if (ImGui.Button(L("Reset current map stats", "Aktuelle Map-Statistik zuruecksetzen")))
        {
            this.ResetMapStats(keepAreaName: true);
        }

        ImGui.SameLine();
        if (ImGui.Button(L("Reset session stats", "Session-Statistik zuruecksetzen")))
        {
            this.ResetSessionTotals();
            this.sessionTimer.Restart();
            this.sessionTimerRunning = true;
        }

        ImGui.TextDisabled(L(
            "With 'new map only', returning to hideout or town keeps your last map stats until you start the next map. Drag the title bar to move; set size in settings.",
            "Mit 'nur bei neuer Map' bleiben Stats nach Hideout/Stadt erhalten, bis du die naechste Map startest. Titelleiste zum Verschieben; Groesse in den Einstellungen."));
    }

    public override void DrawUI()
    {
        var gameState = Core.States.GameCurrentState;
        if (gameState is not (GameStateTypes.InGameState or GameStateTypes.EscapeState))
        {
            this.StopTimer();
            this.StopSessionTimer();
            return;
        }

        var isGamePaused = gameState == GameStateTypes.EscapeState;
        var inGame = Core.States.InGameStateObject;
        var area = inGame.CurrentAreaInstance;
        var areaDetails = inGame.CurrentWorldInstance.AreaDetails;
        var isTownOrHideout = areaDetails.IsTown || areaDetails.IsHideout;

        if (this.areaChangePending)
        {
            this.areaChangePending = false;
            this.HandleAreaTransition(areaDetails, area.AreaHash, isTownOrHideout);
        }

        this.UpdateAreaName(areaDetails.Name, isTownOrHideout);
        this.UpdateTimer(isTownOrHideout, isGamePaused);
        this.UpdateSessionTimer();

        if (!isGamePaused && (!isTownOrHideout || this.Settings.CountKillsInTownOrHideout))
        {
            this.ProcessKills(area);
        }

        if (this.Settings.ShowOverlay)
        {
            this.DrawMapOverlay(isTownOrHideout, isGamePaused);
        }

        if (this.Settings.ShowSessionOverlay)
        {
            this.DrawSessionOverlay();
        }
    }

    private void UpdateAreaName(string name, bool isTownOrHideout)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (this.Settings.ResetOnlyOnNewMap && isTownOrHideout && !string.IsNullOrWhiteSpace(this.sessionMapAreaName))
        {
            this.currentAreaName = this.sessionMapAreaName;
            return;
        }

        this.currentAreaName = name;
    }

    private void UpdateTimer(bool isTownOrHideout, bool isGamePaused)
    {
        if (!this.ShouldTimerRun(isTownOrHideout, isGamePaused))
        {
            this.StopTimer();
            return;
        }

        if (!this.timerRunning)
        {
            this.mapTimer.Start();
            this.timerRunning = true;
        }
    }

    private bool ShouldTimerRun(bool isTownOrHideout, bool isGamePaused)
    {
        if (this.Settings.PauseTimerWhenGamePaused && isGamePaused)
        {
            return false;
        }

        if (this.Settings.PauseTimerInTownOrHideout && isTownOrHideout)
        {
            return false;
        }

        if (this.Settings.PauseTimerWhenGameInBackground && !IsGameOrOverlayForeground())
        {
            return false;
        }

        return true;
    }

    private void StopTimer()
    {
        if (!this.timerRunning)
        {
            return;
        }

        this.mapTimer.Stop();
        this.timerRunning = false;
    }

    private void UpdateSessionTimer()
    {
        if (!this.sessionTimerRunning)
        {
            this.sessionTimer.Start();
            this.sessionTimerRunning = true;
        }
    }

    private void StopSessionTimer()
    {
        if (!this.sessionTimerRunning)
        {
            return;
        }

        this.sessionTimer.Stop();
        this.sessionTimerRunning = false;
    }

    private bool IsTimerPaused(bool isTownOrHideout, bool isGamePaused) =>
        !this.ShouldTimerRun(isTownOrHideout, isGamePaused);

    private void ProcessKills(AreaInstance area)
    {
        foreach (var entity in area.AwakeEntities.Values)
        {
            if (!this.IsCountableMonster(entity))
            {
                continue;
            }

            if (!entity.TryGetComponent<ObjectMagicProperties>(out var omp, true))
            {
                continue;
            }

            var rarity = omp.Rarity;
            if ((int)rarity < (int)Rarity.Normal || (int)rarity > (int)Rarity.Unique)
            {
                continue;
            }

            var id = entity.Id;
            var isAlive = this.IsAliveMonster(entity);
            var isDead = !isAlive || entity.EntityState == EntityStates.Useless;

            if (!this.trackedMonsters.TryGetValue(id, out var track))
            {
                this.trackedMonsters[id] = new MonsterTrack
                {
                    Rarity = rarity,
                    WasAlive = isAlive,
                    Counted = isDead,
                };
                continue;
            }

            if (!track.Counted && track.WasAlive && isDead)
            {
                this.killCounts[(int)rarity]++;
                this.sessionKillCounts[(int)rarity]++;
                track.Counted = true;
            }
            else if (isAlive)
            {
                track.WasAlive = true;
            }

            track.Rarity = rarity;
            this.trackedMonsters[id] = track;
        }

        if (this.trackedMonsters.Count > 2500)
        {
            this.PruneTrackedMonsters(area);
        }
    }

    private void PruneTrackedMonsters(AreaInstance area)
    {
        var aliveIds = new HashSet<uint>();
        foreach (var entity in area.AwakeEntities.Values)
        {
            aliveIds.Add(entity.Id);
        }

        var stale = new List<uint>();
        foreach (var id in this.trackedMonsters.Keys)
        {
            if (!aliveIds.Contains(id))
            {
                stale.Add(id);
            }
        }

        foreach (var id in stale)
        {
            this.trackedMonsters.Remove(id);
        }
    }

    private void DrawMapOverlay(bool isTownOrHideout, bool isGamePaused)
    {
        if (this.Settings.OverlayMode == MapOverlayMode.TimerOnly)
        {
            this.DrawTimerOnlyOverlay(isTownOrHideout, isGamePaused);
            return;
        }

        var areaLabel = string.IsNullOrWhiteSpace(this.currentAreaName)
            ? L("Unknown area", "Unbekannte Area")
            : this.currentAreaName;
        var subtitle = isTownOrHideout && this.Settings.ResetOnlyOnNewMap && !string.IsNullOrWhiteSpace(this.sessionMapAreaName)
            ? L("In town / hideout", "In Stadt / Hideout")
            : null;

        this.DrawStatsOverlay(
            windowId: "###MapKillCounterOverlay",
            title: L("Map kills", "Map-Kills"),
            ref this.Settings.OverlayPosition,
            this.Settings.OverlaySize,
            this.killCounts,
            this.mapTimer.Elapsed,
            areaLabel,
            subtitle,
            this.IsTimerPaused(isTownOrHideout, isGamePaused));
    }

    private void DrawSessionOverlay()
    {
        this.DrawStatsOverlay(
            windowId: "###MapKillCounterSessionOverlay",
            title: L("Session kills", "Session-Kills"),
            ref this.Settings.SessionOverlayPosition,
            this.Settings.SessionOverlaySize,
            this.sessionKillCounts,
            this.sessionTimer.Elapsed,
            L("Whole session", "Ganze Session"),
            null,
            showPaused: false);
    }

    private void DrawTimerOnlyOverlay(bool isTownOrHideout, bool isGamePaused)
    {
        if (!IsGameOrOverlayForeground() && this.Settings.HideOverlayWhenGameInBackground)
        {
            return;
        }

        if (inGamePanelsBlockOverlay())
        {
            return;
        }

        var isPaused = this.IsTimerPaused(isTownOrHideout, isGamePaused);
        var timeText = this.FormatElapsed(this.mapTimer.Elapsed);
        if (isPaused)
        {
            timeText += " *";
        }

        if (this.Settings.OverlayPosition == new Vector2(40f, 120f))
        {
            var display = ImGui.GetIO().DisplaySize;
            ImGui.SetNextWindowPos(new Vector2(display.X - 72f, 12f), ImGuiCond.FirstUseEver);
        }
        else
        {
            ImGui.SetNextWindowPos(this.Settings.OverlayPosition, ImGuiCond.FirstUseEver);
        }

        ImGui.SetNextWindowBgAlpha(this.Settings.BackgroundColor.W);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, this.Settings.BackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Text, this.Settings.TextColor);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6f, 3f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);

        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize |
                    ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

        if (!ImGui.Begin("###MapKillCounterTimerOnly", flags))
        {
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
            ImGui.End();
            return;
        }

        var fontScale = Math.Clamp(this.Settings.OverlayFontScale, 0.7f, 1.5f);
        ImGui.SetWindowFontScale(fontScale);
        this.Settings.OverlayPosition = ImGui.GetWindowPos();
        ImGui.TextUnformatted(timeText);
        ImGui.SetWindowFontScale(1f);
        ImGui.End();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private void DrawStatsOverlay(
        string windowId,
        string title,
        ref Vector2 windowPosition,
        Vector2 configuredSize,
        int[] killCounts,
        TimeSpan elapsed,
        string areaLabel,
        string? subtitle,
        bool showPaused)
    {
        if (!IsGameOrOverlayForeground() && this.Settings.HideOverlayWhenGameInBackground)
        {
            return;
        }

        if (inGamePanelsBlockOverlay())
        {
            return;
        }

        var overlaySize = configuredSize;
        if (overlaySize.X < 1f || overlaySize.Y < 1f)
        {
            overlaySize = this.GetDefaultOverlaySize();
        }

        ImGui.SetNextWindowPos(windowPosition, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(overlaySize, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(this.Settings.BackgroundColor.W);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, this.Settings.BackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Text, this.Settings.TextColor);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 8f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(4f, 4f));

        if (!ImGui.Begin($"{title}{windowId}",
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(2);
            ImGui.End();
            return;
        }

        var fontScale = Math.Clamp(this.Settings.OverlayFontScale, 0.7f, 1.5f);
        ImGui.SetWindowFontScale(fontScale);
        windowPosition = ImGui.GetWindowPos();

        ImGui.TextDisabled(areaLabel);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            ImGui.TextDisabled(subtitle);
        }

        ImGui.Text($"{L("Time", "Zeit")}: {this.FormatElapsed(elapsed)}");
        if (showPaused)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(L("(paused)", "(pausiert)"));
        }

        ImGui.Separator();
        this.DrawKillList(killCounts);
        var total = killCounts[0] + killCounts[1] + killCounts[2] + killCounts[3];
        ImGui.Separator();
        ImGui.Text($"{L("Total", "Gesamt")}: {total}");

        ImGui.SetWindowFontScale(1f);
        ImGui.End();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    private void DrawKillList(int[] killCounts)
    {
        if (this.Settings.Layout == KillListLayout.Horizontal)
        {
            this.DrawKillLine(killCounts, "N", Rarity.Normal, this.Settings.NormalColor, sameLine: false);
            this.DrawKillLine(killCounts, "M", Rarity.Magic, this.Settings.MagicColor, sameLine: true);
            this.DrawKillLine(killCounts, "R", Rarity.Rare, this.Settings.RareColor, sameLine: true);
            this.DrawKillLine(killCounts, "U", Rarity.Unique, this.Settings.UniqueColor, sameLine: true);
            return;
        }

        this.DrawKillLine(killCounts, "N", Rarity.Normal, this.Settings.NormalColor, sameLine: false);
        this.DrawKillLine(killCounts, "M", Rarity.Magic, this.Settings.MagicColor, sameLine: false);
        this.DrawKillLine(killCounts, "R", Rarity.Rare, this.Settings.RareColor, sameLine: false);
        this.DrawKillLine(killCounts, "U", Rarity.Unique, this.Settings.UniqueColor, sameLine: false);
    }

    private void DrawKillLine(int[] killCounts, string label, Rarity rarity, Vector4 color, bool sameLine)
    {
        if (sameLine)
        {
            ImGui.SameLine(0f, 14f);
        }

        ImGui.TextColored(color, $"{label}: {killCounts[(int)rarity]}");
    }

    private Vector2 GetDefaultOverlaySize() =>
        this.Settings.Layout == KillListLayout.Horizontal
            ? new Vector2(500f, 118f)
            : new Vector2(175f, 158f);

    private string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }

        return $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }

    private void ResetMapStats(bool keepAreaName = false)
    {
        Array.Clear(this.killCounts);
        this.trackedMonsters.Clear();
        this.mapTimer.Reset();
        if (!keepAreaName)
        {
            this.currentAreaName = string.Empty;
            this.sessionMapAreaName = string.Empty;
            this.sessionMapAreaHash = string.Empty;
        }
    }

    private void ResetSessionTotals()
    {
        Array.Clear(this.sessionKillCounts);
    }

    private void HandleAreaTransition(WorldAreaDat areaDetails, string areaHash, bool isTownOrHideout)
    {
        if (!this.Settings.ResetOnlyOnNewMap)
        {
            this.inSanctuary = isTownOrHideout;
            this.ResetMapStats();
            if (!string.IsNullOrWhiteSpace(areaDetails.Name))
            {
                this.currentAreaName = areaDetails.Name;
            }

            this.mapTimer.Restart();
            this.timerRunning = true;
            return;
        }

        if (isTownOrHideout)
        {
            this.inSanctuary = true;
            return;
        }

        var isNewMap = this.inSanctuary
            || string.IsNullOrEmpty(this.sessionMapAreaHash)
            || !string.Equals(this.sessionMapAreaHash, areaHash, StringComparison.OrdinalIgnoreCase);

        this.inSanctuary = false;

        if (!isNewMap)
        {
            return;
        }

        this.sessionMapAreaHash = areaHash;
        this.sessionMapAreaName = areaDetails.Name;
        this.ResetMapStats(keepAreaName: true);
        this.currentAreaName = this.sessionMapAreaName;
        this.mapTimer.Restart();
        this.timerRunning = true;
    }

    private IEnumerator<Wait> OnAreaChange()
    {
        while (true)
        {
            yield return new Wait(RemoteEvents.AreaChanged);
            this.areaChangePending = true;
        }
    }

    private bool IsCountableMonster(Entity entity)
    {
        if (!entity.IsValid)
        {
            return false;
        }

        if (entity.EntityType != EntityTypes.Monster)
        {
            return false;
        }

        if (entity.EntityState is EntityStates.MonsterFriendly or EntityStates.PinnacleBossHidden)
        {
            return false;
        }

        return true;
    }

    private bool IsAliveMonster(Entity entity) =>
        entity.TryGetComponent<Life>(out var life, true) && life.IsAlive;

    private static bool inGamePanelsBlockOverlay()
    {
        var inGame = Core.States.InGameStateObject;
        return inGame.GameUi.SkillTreeNodesUiElements.Count > 0;
    }

    /// <summary>PoE or GameHelper overlay/settings focused — not e.g. Discord.</summary>
    private static bool IsGameOrOverlayForeground() =>
        Core.Process.Foreground ||
        Process.GetCurrentProcess().MainWindowHandle == GetForegroundWindow();

    private static string L(string english, string german) => OverlayLocalization.L(english, german);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private struct MonsterTrack
    {
        public Rarity Rarity;
        public bool WasAlive;
        public bool Counted;
    }
    }
}
