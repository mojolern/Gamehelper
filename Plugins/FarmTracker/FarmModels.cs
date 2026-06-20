namespace FarmTracker
{
    using System;
    using System.Collections.Generic;
    using Newtonsoft.Json;

    /// <summary>One map instance tracked by AreaHash (may span hideout trips).</summary>
    public sealed class MapRun
    {
        public string Name = string.Empty;
        public string Hash = string.Empty;
        public double BankedSeconds;
        public DateTime? RunStartUtc;
        public Dictionary<string, long> Gained = new(StringComparer.Ordinal);
        public int[] Kills = new int[4];

        [JsonIgnore]
        public bool IsCurrent;
    }

    public sealed class LootLine
    {
        public string Key = string.Empty;
        public string Label = string.Empty;
        public long Count;
        public double UnitDivine;
        public double TotalDivine;
        public bool Priced;
    }

    /// <summary>Loot line snapshotted at archive time (prices frozen).</summary>
    public sealed class FrozenLootLine
    {
        public string Label = string.Empty;
        public long Count;
        public double TotalDivine;
        public bool Priced;
    }

    public sealed class ArchivedMapRun
    {
        public string Name = string.Empty;
        public string Hash = string.Empty;
        public double ActiveSeconds;
        public double ProfitDivine;
        public int[] Kills = new int[4];
        public List<FrozenLootLine> Loot = new();
    }

    public sealed class SessionRecord
    {
        public DateTime StartUtc;
        public DateTime EndUtc;
        public double ChaosPerDivine;
        public double DivineToExalted;
        public int DisplayCurrency;
        public List<ArchivedMapRun> Maps = new();

        [JsonIgnore]
        public string FilePath = string.Empty;

        public double TotalDivine() => this.SumMaps(m => m.ProfitDivine);

        public double TotalActiveSeconds() => this.SumMaps(m => m.ActiveSeconds);

        public double ProfitPerHourDivine()
        {
            var hours = this.TotalActiveSeconds() / 3600.0;
            return hours > 0 ? this.TotalDivine() / hours : 0;
        }

        private double SumMaps(Func<ArchivedMapRun, double> pick)
        {
            double sum = 0;
            foreach (var m in this.Maps)
            {
                sum += pick(m);
            }

            return sum;
        }
    }

    public sealed class ArchivedSessionSummary
    {
        public string FileName = string.Empty;
        public DateTime StartUtc;
        public DateTime EndUtc;
        public int Maps;
        public double ProfitDivine;
        public double DurationSec;
        public double ProfitPerHourDivine;
    }
}
