namespace FarmTracker
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using Coroutine;
    using GameHelper;
    using GameHelper.CoroutineEvents;
    using GameHelper.Localization;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteEnums.Entity;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.FilesStructures;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using ImGuiNET;
    using Newtonsoft.Json;

    public sealed partial class FarmTrackerCore : PCore<FarmTrackerSettings>
    {
        private readonly Dictionary<uint, MonsterTrack> trackedMonsters = new();
        private readonly List<MapRun> mapRuns = new();
        private readonly FarmExperienceBarAnchor expBarAnchor = new();
        private readonly Stopwatch sessionTimer = new();

        private MapRun? currentMap;
        private Dictionary<string, int>? inventoryBaseline;
        private bool baselinePending;
        private bool onMapArea;
        private string lastProcessedZoneHash = string.Empty;
        private DateTime sessionStartUtc = DateTime.UtcNow;
        private DateTime? mapRunStartUtc;
        private bool wasGamePaused;
        private bool mapTimerPausedByEsc;
        private int lootScanCooldown;
        private bool areaChangePending;
        private ActiveCoroutine? onAreaChange;

        private string SettingsPath => Path.Join(this.DllDirectory, "config", "settings.txt");
        private string ArchiveDir => Path.Join(this.DllDirectory, "sessions");

        public override void OnEnable(bool isGameOpened)
        {
            if (File.Exists(this.SettingsPath))
            {
                try
                {
                    this.Settings = JsonConvert.DeserializeObject<FarmTrackerSettings>(File.ReadAllText(this.SettingsPath))
                                    ?? new FarmTrackerSettings();
                }
                catch
                {
                    this.Settings = new FarmTrackerSettings();
                }
            }

            FarmPriceFetcher.Configure(this.Settings.PriceSource, this.Settings.League, this.Settings.PriceRefreshMinutes);
            FarmPriceFetcher.Initialize(this.DllDirectory);
            FarmLeagueProvider.EnsureLoaded();
            FarmCustomPrices.ReloadIfNeeded(this.DllDirectory);
            FarmMetaArt.Load(this.DllDirectory);
            FarmOverlayIcons.Load(this.DllDirectory);

            this.onAreaChange = CoroutineHandler.Start(this.OnAreaChange(), string.Empty, 0);
            this.ResetSession(archive: false);
            this.areaChangePending = true;
        }

        public override void OnDisable()
        {
            this.onAreaChange?.Cancel();
            this.onAreaChange = null;
            this.expBarAnchor.Reset();
            FarmOverlayIcons.Unload(this.DllDirectory);
            this.sessionTimer.Stop();
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

        public override void DrawUI()
        {
            FarmPriceFetcher.RefreshIfNeeded();
            FarmCustomPrices.ReloadIfNeeded(this.DllDirectory);

            this.DrawSessionHistoryWindows();

            var gameState = Core.States.GameCurrentState;
            if (gameState is not (GameStateTypes.InGameState or GameStateTypes.EscapeState))
            {
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
                this.OnZoneHashEdge(area, areaDetails);
            }

            this.UpdateMapAreaState(area, areaDetails, isTownOrHideout);
            this.UpdateMapTimerEscPause(isGamePaused);

            if (!isGamePaused && (!isTownOrHideout || this.Settings.CountKillsInTownOrHideout))
            {
                this.ProcessKills(area);
            }

            if (!isGamePaused && this.onMapArea)
            {
                this.ProcessInventoryDelta();
            }

            if (this.ShouldDrawOverlay(isTownOrHideout))
            {
                this.DrawSlimOverlay(isTownOrHideout, isGamePaused);
            }
        }

        private void ProcessKills(AreaInstance area)
        {
            foreach (var entity in area.AwakeEntities.Values)
            {
                if (!IsCountableMonster(entity))
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
                var isAlive = IsAliveMonster(entity);
                var isDead = !isAlive || entity.EntityState == EntityStates.Useless;

                if (!this.trackedMonsters.TryGetValue(id, out var track))
                {
                    this.trackedMonsters[id] = new MonsterTrack { Rarity = rarity, WasAlive = isAlive, Counted = isDead };
                    continue;
                }

                if (!track.Counted && track.WasAlive && isDead)
                {
                    if (this.currentMap != null)
                    {
                        this.currentMap.Kills[(int)rarity]++;
                    }

                    track.Counted = true;
                }
                else if (isAlive)
                {
                    track.WasAlive = true;
                }

                track.Rarity = rarity;
                this.trackedMonsters[id] = track;
            }
        }

        private void ProcessInventoryDelta()
        {
            if (--this.lootScanCooldown > 0)
            {
                return;
            }

            this.lootScanCooldown = 4;
            if (this.baselinePending)
            {
                if (this.TrySnapshotInventory(out var snap))
                {
                    this.inventoryBaseline = snap;
                    this.baselinePending = false;
                }

                return;
            }

            if (this.inventoryBaseline == null || this.currentMap == null)
            {
                return;
            }

            if (!this.TrySnapshotInventory(out var current))
            {
                return;
            }

            foreach (var pair in current)
            {
                this.inventoryBaseline.TryGetValue(pair.Key, out var before);
                var delta = pair.Value - before;
                if (delta <= 0)
                {
                    continue;
                }

                this.currentMap.Gained.TryGetValue(pair.Key, out var existing);
                this.currentMap.Gained[pair.Key] = existing + delta;
            }

            this.inventoryBaseline = current;
        }

        private bool TrySnapshotInventory(out Dictionary<string, int> snap)
        {
            snap = InventoryScanner.SnapshotCounts();
            return snap.Count > 0 || this.onMapArea;
        }

        private double ValueOfGained(Dictionary<string, long> gained, out List<LootLine> lines)
        {
            lines = new List<LootLine>();
            double total = 0;
            foreach (var kv in gained)
            {
                if (kv.Value == 0)
                {
                    continue;
                }

                var label = InventoryScanner.ReadableName(kv.Key);
                var priced = this.TryPriceItem(kv.Key, label, out var unitDivine);
                var lineTotal = priced ? unitDivine * kv.Value : 0;
                if (priced)
                {
                    total += lineTotal;
                }

                lines.Add(new LootLine
                {
                    Key = kv.Key,
                    Label = label,
                    Count = kv.Value,
                    UnitDivine = unitDivine,
                    TotalDivine = lineTotal,
                    Priced = priced,
                });
            }

            lines.Sort((a, b) => b.TotalDivine.CompareTo(a.TotalDivine));
            return total;
        }

        private bool TryPriceItem(string itemKey, string displayName, out double unitDivine)
        {
            unitDivine = 0;
            if (FarmCustomPrices.TryGetDivine(displayName, out unitDivine))
            {
                return true;
            }

            if (FarmCurrencyCatalog.TryGetBuiltinDivineValue(itemKey, out unitDivine) && unitDivine > 0)
            {
                return true;
            }

            if (FarmCurrencyCatalog.TryResolveItemName(itemKey, out var catalogName))
            {
                displayName = catalogName;
            }

            if (this.Settings.UseMetaArtForPricing &&
                FarmPriceFetcher.TryGetDivineByItemKey(itemKey, true, out unitDivine))
            {
                return true;
            }

            var price = FarmPriceFetcher.GetPrice(displayName, null, itemKey, null, displayName);
            if (price == null)
            {
                return false;
            }

            unitDivine = FarmPriceFetcher.GetDivineValue(price);
            return unitDivine > 0;
        }

        private List<LootLine> BuildSessionLootLines()
        {
            var merged = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var run in this.mapRuns)
            {
                foreach (var kv in run.Gained)
                {
                    merged.TryGetValue(kv.Key, out var v);
                    merged[kv.Key] = v + kv.Value;
                }
            }

            this.ValueOfGained(merged, out var lines);
            return lines;
        }

        private void ResetSession(bool archive)
        {
            if (archive)
            {
                this.ArchiveCurrentSession();
            }

            this.mapRuns.Clear();
            this.currentMap = null;
            this.mapRunStartUtc = null;
            this.inventoryBaseline = null;
            this.baselinePending = false;
            this.wasGamePaused = false;
            this.mapTimerPausedByEsc = false;
            this.lastProcessedZoneHash = string.Empty;
            this.onMapArea = false;
            this.trackedMonsters.Clear();
            this.sessionStartUtc = DateTime.UtcNow;
            this.sessionTimer.Restart();
        }

        private void ArchiveCurrentSession()
        {
            this.FoldInventoryIntoCurrentRun();
            this.BankMapTime(DateTime.UtcNow);
            if (this.mapRuns.Count == 0)
            {
                return;
            }

            try
            {
                var rec = this.BuildSessionRecord(this.sessionStartUtc, DateTime.UtcNow);
                FarmSessionHistory.SaveSession(this.ArchiveDir, rec);
                FarmSessionHistory.TrimSessions(this.ArchiveDir, this.Settings.MaxSessions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FarmTracker] archive failed: {ex.Message}");
            }
        }

        private SessionRecord BuildSessionRecord(DateTime startUtc, DateTime endUtc)
        {
            var chaosPerDiv = FarmPriceFetcher.GetChaosPerDivine();
            var rec = new SessionRecord
            {
                StartUtc = startUtc,
                EndUtc = endUtc,
                ChaosPerDivine = chaosPerDiv,
                DivineToExalted = FarmPriceFetcher.DivineToExaltedRate,
                DisplayCurrency = (int)this.Settings.DisplayCurrency,
            };

            foreach (var run in this.mapRuns)
            {
                var profit = this.ValueOfGained(run.Gained, out var lines);
                rec.Maps.Add(new ArchivedMapRun
                {
                    Name = run.Name,
                    Hash = run.Hash,
                    ActiveSeconds = run.BankedSeconds,
                    ProfitDivine = profit,
                    Kills = (int[])run.Kills.Clone(),
                    Loot = lines
                        .Where(l => this.Settings.ShowUnpricedItems || l.Priced)
                        .Select(l => new FrozenLootLine
                        {
                            Label = l.Label,
                            Count = l.Count,
                            TotalDivine = l.TotalDivine,
                            Priced = l.Priced,
                        })
                        .ToList(),
                });
            }

            return rec;
        }

        private string FormatCurrency(double divine)
        {
            switch (this.Settings.DisplayCurrency)
            {
                case FarmOverlayCurrency.Exalted:
                {
                    var rate = FarmPriceFetcher.DivineToExaltedRate;
                    return rate > 0 ? $"{divine * rate:F1} ex" : $"{divine:F2} div";
                }

                case FarmOverlayCurrency.Chaos:
                {
                    var chaosPerDiv = FarmPriceFetcher.GetChaosPerDivine();
                    return chaosPerDiv > 0 ? $"{divine * chaosPerDiv:F0} c" : $"{divine:F2} div";
                }

                default:
                    return $"{divine:F2} div";
            }
        }

        private static string FormatElapsed(TimeSpan elapsed) =>
            elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
                : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

        private static string L(string english, string german) => OverlayLocalization.L(english, german);

        private static bool IsCountableMonster(Entity entity) =>
            entity.IsValid
            && entity.EntityType == EntityTypes.Monster
            && entity.EntityState is not (EntityStates.MonsterFriendly or EntityStates.PinnacleBossHidden);

        private static bool IsAliveMonster(Entity entity) =>
            entity.TryGetComponent<Life>(out var life, true) && life.IsAlive;

        private static bool IsGameOrOverlayForeground() =>
            Core.Process.Foreground || Process.GetCurrentProcess().MainWindowHandle == GetForegroundWindow();

        private IEnumerator<Wait> OnAreaChange()
        {
            while (true)
            {
                yield return new Wait(RemoteEvents.AreaChanged);
                this.areaChangePending = true;
            }
        }

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
