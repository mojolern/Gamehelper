namespace FarmTracker
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameHelper.RemoteObjects.FilesStructures;
    using GameHelper.RemoteObjects.States.InGameStateObjects;

    public sealed partial class FarmTrackerCore
    {
        private DateTime nextLiveProfitUtc = DateTime.MinValue;
        private double liveMapProfitDivine;

        private void OnZoneHashEdge(AreaInstance area, WorldAreaDat details)
        {
            var inst = area.AreaHash;
            if (string.IsNullOrEmpty(inst) || string.IsNullOrEmpty(details.Name))
            {
                return;
            }

            if (inst != this.lastProcessedZoneHash)
            {
                this.lastProcessedZoneHash = inst;
                this.HandleZoneTransition(area, details, inst);
            }
        }

        private void UpdateMapAreaState(AreaInstance area, WorldAreaDat details, bool isTownOrHideout)
        {
            if (this.baselinePending && this.onMapArea && this.TrySnapshotInventory(out var snap))
            {
                this.inventoryBaseline = snap;
                this.baselinePending = false;
            }

            if (this.onMapArea && this.currentMap != null && DateTime.UtcNow >= this.nextLiveProfitUtc)
            {
                this.nextLiveProfitUtc = DateTime.UtcNow.AddMilliseconds(500);
                this.liveMapProfitDivine = this.ValueOfGained(this.CurrentGainedLive(), out _);
            }
        }

        private void HandleZoneTransition(AreaInstance area, WorldAreaDat details, string inst)
        {
            bool isMap = !details.IsHideout && !details.IsTown;
            this.onMapArea = isMap;
            var now = DateTime.UtcNow;

            if (isMap)
            {
                this.BankMapTime(now);

                if (this.currentMap != null && this.currentMap.Hash == inst)
                {
                    // same instance resume
                }
                else if (this.FindRun(inst) is { } existing)
                {
                    this.currentMap = existing;
                    existing.IsCurrent = true;
                }
                else
                {
                    this.currentMap = new MapRun
                    {
                        Name = details.Name,
                        Hash = inst,
                        IsCurrent = true,
                    };
                    this.mapRuns.Add(this.currentMap);
                    this.TrimMapHistory();
                }

                this.mapRunStartUtc = now;
                this.inventoryBaseline = null;
                this.baselinePending = true;
                this.trackedMonsters.Clear();
            }
            else if (this.currentMap != null)
            {
                this.BankMapTime(now);
                this.FoldInventoryIntoCurrentRun();
                this.currentMap.IsCurrent = false;
                this.currentMap = null;
                this.mapRunStartUtc = null;
                this.baselinePending = false;
                this.inventoryBaseline = null;
            }
        }

        private void BankMapTime(DateTime now)
        {
            if (this.currentMap == null || this.mapRunStartUtc == null)
            {
                return;
            }

            this.currentMap.BankedSeconds += (now - this.mapRunStartUtc.Value).TotalSeconds;
            this.mapRunStartUtc = null;
        }

        private void FoldInventoryIntoCurrentRun()
        {
            if (this.currentMap == null || this.inventoryBaseline == null)
            {
                return;
            }

            if (!this.TrySnapshotInventory(out var snap))
            {
                return;
            }

            foreach (var pair in snap)
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
        }

        private MapRun? FindRun(string hash)
        {
            for (int i = this.mapRuns.Count - 1; i >= 0; i--)
            {
                if (this.mapRuns[i].Hash.Equals(hash, StringComparison.Ordinal))
                {
                    return this.mapRuns[i];
                }
            }

            return null;
        }

        private void TrimMapHistory()
        {
            while (this.mapRuns.Count > this.Settings.MapHistorySize)
            {
                if (ReferenceEquals(this.mapRuns[0], this.currentMap))
                {
                    break;
                }

                this.mapRuns.RemoveAt(0);
            }
        }

        private TimeSpan CurrentLiveMapTime()
        {
            if (this.currentMap == null)
            {
                return TimeSpan.Zero;
            }

            var t = TimeSpan.FromSeconds(this.currentMap.BankedSeconds);
            if (this.mapRunStartUtc is { } start)
            {
                t += DateTime.UtcNow - start;
            }

            return t;
        }

        private Dictionary<string, long> CurrentGainedLive()
        {
            if (this.currentMap == null)
            {
                return new Dictionary<string, long>(StringComparer.Ordinal);
            }

            var combined = new Dictionary<string, long>(this.currentMap.Gained, StringComparer.Ordinal);
            if (this.mapRunStartUtc != null && this.inventoryBaseline != null &&
                this.TrySnapshotInventory(out var snap))
            {
                foreach (var pair in snap)
                {
                    this.inventoryBaseline.TryGetValue(pair.Key, out var before);
                    var delta = pair.Value - before;
                    if (delta == 0)
                    {
                        continue;
                    }

                    combined.TryGetValue(pair.Key, out var existing);
                    combined[pair.Key] = existing + delta;
                }
            }

            return combined;
        }

        private void SessionTotals(out TimeSpan totalActive, out double totalDivine)
        {
            totalActive = TimeSpan.Zero;
            totalDivine = 0;
            foreach (var run in this.mapRuns)
            {
                var active = TimeSpan.FromSeconds(run.BankedSeconds);
                if (ReferenceEquals(run, this.currentMap) && this.mapRunStartUtc is { } start)
                {
                    active += DateTime.UtcNow - start;
                }

                totalActive += active;

                if (ReferenceEquals(run, this.currentMap) && this.onMapArea)
                {
                    totalDivine += this.ValueOfGained(this.CurrentGainedLive(), out _);
                }
                else
                {
                    totalDivine += this.ValueOfGained(run.Gained, out _);
                }
            }
        }
    }
}
