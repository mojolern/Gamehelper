namespace RuneforgeHelper
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    public enum PriceSyncStatus { Idle, Syncing, Ready, Error }

    // Fetches PoE2 prices (in Divine) from poe.ninja or poe2scout, keyed by normalized item name.
    public sealed class PriceCache
    {
        public const int SourcePoeNinja = 0;
        public const int SourcePoe2Scout = 1;

        // Bump when cache shape or art-index build changes so stale files are discarded.
        private const int CacheFormatVersion = 5;

        private static readonly string[] NinjaOverviewTypes =
            { "Currency", "UncutGems", "Runes", "Idols", "Verisium", "Expedition" };

        private static readonly string[] NinjaStashTypes =
        {
            "UniqueArmours", "UniqueAccessories", "UniqueCharms", "UniqueWeapons",
        };

        private static readonly string[] ScoutCurrencyCategories =
        {
            "currency", "ritual", "runes", "idol", "essences", "fragments", "abyss", "breach",
            "delirium", "expedition", "incursion", "ultimatum", "vaal", "vaultkeys", "verisium",
            "uncutgems", "lineagesupportgems",
        };

        private static readonly HttpClient http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("RuneforgeHelper/1.1 (gamehelper2-fork plugin)");
            return c;
        }

        private readonly object gate = new();
        private Dictionary<string, double> pricesDivine = new(StringComparer.Ordinal);
        private Dictionary<string, double> pricesByArtDivine = new(StringComparer.Ordinal);
        private Dictionary<string, string> namesByArt = new(StringComparer.Ordinal);

        public PriceSyncStatus Status { get; private set; } = PriceSyncStatus.Idle;
        public DateTime LastSyncUtc { get; private set; } = DateTime.MinValue;
        public string LastError { get; private set; } = string.Empty;
        public double DivineToExaltedRate { get; private set; }
        public int ActivePriceSource { get; private set; } = SourcePoeNinja;

        public int PriceCount
        {
            get { lock (this.gate) return this.pricesDivine.Count; }
        }

        public bool TryGetDivinePrice(string itemName, out double divinePrice)
        {
            divinePrice = 0;
            if (string.IsNullOrEmpty(itemName)) return false;
            var key = Normalize(itemName);
            if (key.Length == 0) return false;
            lock (this.gate)
            {
                return this.pricesDivine.TryGetValue(key, out divinePrice);
            }
        }

        public bool TryGetExaltedPrice(string itemName, out double exaltedPrice)
        {
            exaltedPrice = 0;
            if (!this.TryGetDivinePrice(itemName, out var divine) || divine <= 0) return false;
            var rate = this.DivineToExaltedRate;
            if (rate <= 0) return false;
            exaltedPrice = divine * rate;
            return exaltedPrice > 0;
        }

        public bool TryGetDivinePriceByArtId(string artId, out double divinePrice)
        {
            divinePrice = 0;
            if (string.IsNullOrEmpty(artId)) return false;
            lock (this.gate)
            {
                foreach (var variant in ArtKeyVariants(artId))
                {
                    var key = Normalize(variant);
                    if (key.Length > 0 && this.pricesByArtDivine.TryGetValue(key, out divinePrice))
                        return true;
                }
            }

            return false;
        }

        public bool TryGetNameByArtId(string artId, out string name)
        {
            name = string.Empty;
            if (string.IsNullOrEmpty(artId)) return false;
            lock (this.gate)
            {
                foreach (var variant in ArtKeyVariants(artId))
                {
                    var key = Normalize(variant);
                    if (key.Length > 0 && this.namesByArt.TryGetValue(key, out name!))
                        return true;
                }
            }

            return false;
        }

        public bool TryLoadFromDisk(string filePath, string league, int priceSource, int ttlMinutes)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                var content = File.ReadAllText(filePath);
                var dto = JsonConvert.DeserializeObject<PriceCacheDto>(content);
                if (dto == null || dto.PricesDivine == null || dto.FormatVersion != CacheFormatVersion)
                {
                    TryDeleteCacheFile(filePath);
                    return false;
                }
                if (dto.PriceSource != priceSource) return false;
                if (!string.Equals(dto.League, league?.Trim(), StringComparison.OrdinalIgnoreCase)) return false;

                lock (this.gate)
                {
                    this.pricesDivine = new Dictionary<string, double>(dto.PricesDivine, StringComparer.Ordinal);
                    this.pricesByArtDivine = dto.ArtPricesDivine != null
                        ? new Dictionary<string, double>(dto.ArtPricesDivine, StringComparer.Ordinal)
                        : new Dictionary<string, double>(StringComparer.Ordinal);
                    this.namesByArt = dto.ArtNames != null
                        ? new Dictionary<string, string>(dto.ArtNames, StringComparer.Ordinal)
                        : new Dictionary<string, string>(StringComparer.Ordinal);
                    this.DivineToExaltedRate = dto.DivineToExaltedRate;
                    this.LastSyncUtc = dto.LastSyncUtc;
                    this.ActivePriceSource = dto.PriceSource;
                    this.Status = PriceSyncStatus.Ready;
                }

                var age = DateTime.UtcNow - this.LastSyncUtc;
                return age <= TimeSpan.FromMinutes(Math.Max(1, ttlMinutes));
            }
            catch (Exception ex)
            {
                TryDeleteCacheFile(filePath);
                lock (this.gate) this.LastError = $"load failed: {ex.Message}";
                return false;
            }
        }

        public void StartRefresh(string league, int priceSource, string filePath)
        {
            lock (this.gate)
            {
                if (this.Status == PriceSyncStatus.Syncing) return;
                this.Status = PriceSyncStatus.Syncing;
            }
            _ = Task.Run(() => this.RefreshAsync(league, priceSource, filePath));
        }

        private async Task RefreshAsync(string league, int priceSource, string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(league))
                    throw new InvalidOperationException("League name is empty.");

                var aggregated = new Dictionary<string, double>(StringComparer.Ordinal);
                var artPrices = new Dictionary<string, double>(StringComparer.Ordinal);
                var artNames = new Dictionary<string, string>(StringComparer.Ordinal);
                double divToEx = 0;
                string partialError = string.Empty;

                if (priceSource == SourcePoe2Scout)
                {
                    (aggregated, divToEx, partialError) = await this.FetchFromScoutAsync(league.Trim()).ConfigureAwait(false);
                    var (runePrices, runeArt, runeNames, ninjaDivToEx, ninjaError) = await this.FetchNinjaOverviewAsync(
                        league.Trim(), "Runes").ConfigureAwait(false);
                    this.MergePrices(aggregated, runePrices);
                    this.MergePrices(artPrices, runeArt);
                    this.MergeArtNames(artNames, runeNames);
                    if (divToEx <= 0 && ninjaDivToEx > 0)
                        divToEx = ninjaDivToEx;
                    if (!string.IsNullOrEmpty(ninjaError))
                        partialError = string.IsNullOrEmpty(partialError)
                            ? $"ninja runes: {ninjaError}"
                            : $"{partialError}; ninja runes: {ninjaError}";

                    var (_, ninjaArt, ninjaArtNames, _, ninjaArtError) = await this.FetchFromNinjaAsync(league.Trim()).ConfigureAwait(false);
                    this.MergePrices(artPrices, ninjaArt);
                    this.MergeArtNames(artNames, ninjaArtNames);
                    if (!string.IsNullOrEmpty(ninjaArtError))
                        partialError = string.IsNullOrEmpty(partialError)
                            ? $"ninja art-keys: {ninjaArtError}"
                            : $"{partialError}; ninja art-keys: {ninjaArtError}";
                }
                else
                {
                    (aggregated, artPrices, artNames, divToEx, partialError) = await this.FetchFromNinjaAsync(league.Trim()).ConfigureAwait(false);
                }

                if (aggregated.Count == 0)
                    throw new InvalidOperationException(string.IsNullOrEmpty(partialError)
                        ? "no prices fetched"
                        : partialError);

                if (divToEx > 0)
                {
                    aggregated[Normalize("Exalted Orb")] = 1.0 / divToEx;
                    aggregated[Normalize("Divine Orb")] = 1.0;
                }

                lock (this.gate)
                {
                    this.pricesDivine = aggregated;
                    this.pricesByArtDivine = artPrices;
                    this.namesByArt = artNames;
                    this.DivineToExaltedRate = divToEx;
                    this.LastSyncUtc = DateTime.UtcNow;
                    this.ActivePriceSource = priceSource;
                    this.Status = PriceSyncStatus.Ready;
                    this.LastError = partialError;
                }

                var dto = new PriceCacheDto
                {
                    FormatVersion = CacheFormatVersion,
                    PriceSource = priceSource,
                    League = league.Trim(),
                    LastSyncUtc = this.LastSyncUtc,
                    DivineToExaltedRate = divToEx,
                    PricesDivine = aggregated,
                    ArtPricesDivine = artPrices,
                    ArtNames = artNames,
                };
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllText(filePath, JsonConvert.SerializeObject(dto, Formatting.Indented));
            }
            catch (Exception ex)
            {
                lock (this.gate)
                {
                    this.Status = PriceSyncStatus.Error;
                    this.LastError = ex.Message;
                }
            }
        }

        private async Task<(Dictionary<string, double> Prices, Dictionary<string, double> ArtPrices, Dictionary<string, string> ArtNames, double DivToEx, string PartialError)> FetchFromNinjaAsync(string league)
        {
            var aggregated = new Dictionary<string, double>(StringComparer.Ordinal);
            var artPrices = new Dictionary<string, double>(StringComparer.Ordinal);
            var artNames = new Dictionary<string, string>(StringComparer.Ordinal);
            double divToEx = 0;
            var failedTypes = new List<string>();

            foreach (var type in NinjaOverviewTypes)
            {
                var (prices, art, names, localRate, error) = await this.FetchNinjaOverviewAsync(league, type).ConfigureAwait(false);
                this.MergePrices(aggregated, prices);
                this.MergePrices(artPrices, art);
                this.MergeArtNames(artNames, names);
                if (localRate > 0)
                    divToEx = localRate;
                if (!string.IsNullOrEmpty(error))
                    failedTypes.Add(error);
            }

            var stashError = await this.FetchNinjaStashOverviewsAsync(league, aggregated, artPrices, artNames, divToEx).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(stashError))
                failedTypes.Add(stashError);

            var partial = failedTypes.Count == 0
                ? string.Empty
                : $"partial — skipped {string.Join(", ", failedTypes)}";
            return (aggregated, artPrices, artNames, divToEx, partial);
        }

        private async Task<(Dictionary<string, double> Prices, Dictionary<string, double> ArtPrices, Dictionary<string, string> ArtNames, double DivToEx, string Error)> FetchNinjaOverviewAsync(
            string league,
            string type)
        {
            var prices = new Dictionary<string, double>(StringComparer.Ordinal);
            var artPrices = new Dictionary<string, double>(StringComparer.Ordinal);
            var artNames = new Dictionary<string, string>(StringComparer.Ordinal);
            double divToEx = 0;
            try
            {
                var leagueParam = Uri.EscapeDataString(league).Replace("%20", "+");
                var url = $"https://poe.ninja/poe2/api/economy/exchange/current/overview?league={leagueParam}&type={type}";
                using var resp = await http.GetAsync(url).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                var parsed = JObject.Parse(json);
                var localRate = parsed["core"]?["rates"]?["exalted"]?.Value<double>() ?? 0;
                if (localRate > 0)
                    divToEx = localRate;

                var nameById = new Dictionary<string, string>(StringComparer.Ordinal);
                var artById = new Dictionary<string, string>(StringComparer.Ordinal);
                var levelKeyById = new Dictionary<string, string>(StringComparer.Ordinal);
                if (parsed["items"] is JArray itemsArr)
                {
                    foreach (var it in itemsArr)
                    {
                        var id = it["id"]?.Value<string>();
                        if (string.IsNullOrEmpty(id)) continue;
                        var name = it["name"]?.Value<string>();
                        if (!string.IsNullOrEmpty(name)) nameById[id!] = name!;

                        var art = ExtractArtId(it["image"]?.Value<string>());
                        if (string.IsNullOrEmpty(art)) continue;

                        var grade = DetectCurrencyGrade(id!);
                        var artKey = grade == 1 || EndsWithDigit(art!) ? art! : art! + grade.ToString();
                        artById[id!] = artKey;
                        if (!string.IsNullOrEmpty(name))
                        {
                            var nk = Normalize(artKey);
                            if (nk.Length > 0) artNames[nk] = name!;

                            var level = TrailingLevel(id!);
                            if (level >= 0)
                            {
                                var levelKey = art! + level.ToString();
                                levelKeyById[id!] = levelKey;
                                var lnk = Normalize(levelKey);
                                if (lnk.Length > 0) artNames[lnk] = name!;
                            }
                        }
                    }
                }

                if (parsed["lines"] is JArray lines)
                {
                    foreach (var ln in lines)
                    {
                        var id = ln["id"]?.Value<string>();
                        var primary = ln["primaryValue"]?.Value<double?>() ?? 0;
                        if (string.IsNullOrEmpty(id) || primary <= 0)
                            continue;

                        if (nameById.TryGetValue(id!, out var name))
                        {
                            this.AddDivinePrice(prices, name, primary);
                            this.AddDivinePrice(prices, id, primary);
                        }

                        if (artById.TryGetValue(id!, out var artKey))
                            this.AddDivinePrice(artPrices, artKey, primary);
                        if (levelKeyById.TryGetValue(id!, out var levelKey))
                            this.AddDivinePrice(artPrices, levelKey, primary);
                    }
                }
            }
            catch (Exception ex)
            {
                return (prices, artPrices, artNames, divToEx, $"{type}: {ex.Message}");
            }

            return (prices, artPrices, artNames, divToEx, string.Empty);
        }

        private async Task<string> FetchNinjaStashOverviewsAsync(
            string league,
            Dictionary<string, double> aggregated,
            Dictionary<string, double> artPrices,
            Dictionary<string, string> artNames,
            double divToEx)
        {
            var leagueParam = Uri.EscapeDataString(league).Replace("%20", "+");
            var failedTypes = new List<string>();

            foreach (var type in NinjaStashTypes)
            {
                var url = $"https://poe.ninja/poe2/api/economy/stash/current/item/overview?league={leagueParam}&type={type}";
                var error = await this.FetchNinjaStashApi(url, aggregated, artPrices, artNames, divToEx).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(error))
                    failedTypes.Add(error);
            }

            return failedTypes.Count == 0 ? string.Empty : $"stash: {string.Join(", ", failedTypes)}";
        }

        private async Task<string> FetchNinjaStashApi(
            string url,
            Dictionary<string, double> aggregated,
            Dictionary<string, double> artPrices,
            Dictionary<string, string> artNames,
            double divToEx)
        {
            try
            {
                using var resp = await http.GetAsync(url).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var data = JObject.Parse(json);

                var primaryCurrency = data["core"]?["primary"]?.Value<string>() ?? "exalted";
                if (data["lines"] is not JArray lines)
                    return string.Empty;

                foreach (var line in lines)
                {
                    var name = line["name"]?.Value<string>();
                    var baseType = line["baseType"]?.Value<string>() ?? string.Empty;
                    var primary = line["primaryValue"]?.Value<double?>() ?? 0;
                    if (string.IsNullOrEmpty(name) || primary <= 0)
                        continue;

                    var divine = this.PrimaryValueToDivine(primary, primaryCurrency, divToEx);
                    if (divine <= 0)
                        continue;

                    var cacheKey = BuildStashCacheKey(name, baseType);
                    this.AddDivinePrice(aggregated, cacheKey, divine);
                    this.AddDivinePrice(aggregated, name, divine);

                    var icon = line["icon"]?.Value<string>() ?? line["image"]?.Value<string>();
                    var artBasename = ExtractArtId(icon);
                    if (!string.IsNullOrEmpty(artBasename))
                    {
                        this.AddDivinePrice(artPrices, artBasename, divine);
                        this.IndexArtName(artNames, artBasename, cacheKey);
                        this.IndexArtName(artNames, artBasename, name);
                    }
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }

            return string.Empty;
        }

        private double PrimaryValueToDivine(double value, string primaryCurrency, double divToEx)
        {
            if (primaryCurrency.Equals("divine", StringComparison.OrdinalIgnoreCase))
                return value;

            if (divToEx > 0)
                return value / divToEx;

            return 0;
        }

        private static string BuildStashCacheKey(string name, string baseType)
        {
            if (baseType.Contains("Runeforged", StringComparison.OrdinalIgnoreCase))
                return $"{name} Runeforged";

            if (baseType.Contains("Runemastered", StringComparison.OrdinalIgnoreCase))
                return $"{name} Runemastered";

            return name;
        }

        private void IndexArtName(Dictionary<string, string> artNames, string artBasename, string displayName)
        {
            if (string.IsNullOrWhiteSpace(artBasename) || string.IsNullOrWhiteSpace(displayName))
                return;

            var key = Normalize(artBasename);
            if (key.Length > 0)
                artNames[key] = displayName.Trim();
        }

        private static IEnumerable<string> ArtKeyVariants(string artBasename)
        {
            if (string.IsNullOrWhiteSpace(artBasename))
                yield break;

            yield return artBasename;
            if (artBasename.StartsWith("The", StringComparison.OrdinalIgnoreCase) && artBasename.Length > 3)
                yield return artBasename[3..];
            else
                yield return "The" + artBasename;
        }

        private static void TryDeleteCacheFile(string filePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch
            {
            }
        }

        private void MergePrices(Dictionary<string, double> target, Dictionary<string, double> source)
        {
            foreach (var kv in source)
            {
                if (!target.ContainsKey(kv.Key) || target[kv.Key] < kv.Value)
                    target[kv.Key] = kv.Value;
            }
        }

        private void MergeArtNames(Dictionary<string, string> target, Dictionary<string, string> source)
        {
            foreach (var kv in source)
                target[kv.Key] = kv.Value;
        }

        private async Task<(Dictionary<string, double> Prices, double DivToEx, string PartialError)> FetchFromScoutAsync(string league)
        {
            var aggregated = new Dictionary<string, double>(StringComparer.Ordinal);
            double chaosPerDivine = 0;
            double chaosPerExalted = 0;
            var failedTypes = new List<string>();
            var leagueEscaped = Uri.EscapeDataString(league);

            try
            {
                var json = await http.GetStringAsync("https://poe2scout.com/api/poe2/Leagues").ConfigureAwait(false);
                var leagues = JObject.Parse(json)["value"] as JArray ?? JObject.Parse(json)["Value"] as JArray;
                if (leagues != null)
                {
                    foreach (var entry in leagues)
                    {
                        if (!string.Equals(entry["Value"]?.ToString(), league, StringComparison.OrdinalIgnoreCase))
                            continue;

                        chaosPerDivine = entry["ChaosDivinePrice"]?.Value<double?>() ?? 0;
                        var divEx = entry["DivinePrice"]?.Value<double?>() ?? 0;
                        if (divEx > 0 && chaosPerDivine > 0)
                            chaosPerExalted = chaosPerDivine / divEx;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                failedTypes.Add($"leagues: {ex.Message}");
            }

            try
            {
                var url = $"https://poe2scout.com/api/poe2/Leagues/{leagueEscaped}/Currencies/ByCategory?Category=currency&ReferenceCurrency=chaos&PerPage=250&Page=1";
                var json = await http.GetStringAsync(url).ConfigureAwait(false);
                if (JObject.Parse(json)["Items"] is JArray items)
                {
                    foreach (var item in items)
                    {
                        var text = item["Text"]?.ToString();
                        var price = item["CurrentPrice"]?.Value<double?>() ?? 0;
                        if (string.IsNullOrEmpty(text) || price <= 0) continue;

                        if (text.Contains("Divine Orb", StringComparison.OrdinalIgnoreCase))
                            chaosPerDivine = price;
                        if (text.Contains("Exalted Orb", StringComparison.OrdinalIgnoreCase))
                            chaosPerExalted = price;
                    }
                }
            }
            catch (Exception ex)
            {
                failedTypes.Add($"currency-rates: {ex.Message}");
            }

            foreach (var category in ScoutCurrencyCategories)
            {
                try
                {
                    await this.FetchScoutCategoryAsync(leagueEscaped, category, chaosPerDivine, aggregated).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    failedTypes.Add($"{category}: {ex.Message}");
                }
            }

            double divToEx = chaosPerDivine > 0 && chaosPerExalted > 0
                ? chaosPerDivine / chaosPerExalted
                : 0;

            var partial = failedTypes.Count == 0
                ? string.Empty
                : $"partial — skipped {string.Join(", ", failedTypes)}";
            return (aggregated, divToEx, partial);
        }

        private async Task FetchScoutCategoryAsync(
            string leagueEscaped,
            string category,
            double chaosPerDivine,
            Dictionary<string, double> aggregated)
        {
            var page = 1;
            var pages = 1;
            while (page <= pages)
            {
                var url = $"https://poe2scout.com/api/poe2/Leagues/{leagueEscaped}/Currencies/ByCategory?Category={category}&ReferenceCurrency=chaos&PerPage=250&Page={page}";
                var json = await http.GetStringAsync(url).ConfigureAwait(false);
                var data = JObject.Parse(json);
                pages = data["Pages"]?.Value<int?>() ?? 1;
                if (data["Items"] is not JArray items) break;

                foreach (var item in items)
                {
                    var chaos = item["CurrentPrice"]?.Value<double?>() ?? 0;
                    if (chaos <= 0 || chaosPerDivine <= 0) continue;
                    var divine = chaos / chaosPerDivine;
                    this.AddDivinePrice(aggregated, item["Text"]?.ToString(), divine);
                    this.AddDivinePrice(aggregated, item["ApiId"]?.ToString(), divine);
                    this.AddDivinePrice(aggregated, item["ItemMetadata"]?["name"]?.ToString(), divine);
                    this.AddDivinePrice(aggregated, item["ItemMetadata"]?["base_type"]?.ToString(), divine);
                }

                page++;
            }
        }

        private void AddDivinePrice(Dictionary<string, double> aggregated, string? name, double divine)
        {
            if (string.IsNullOrWhiteSpace(name) || divine <= 0) return;
            var key = Normalize(name);
            if (key.Length == 0) return;
            if (!aggregated.ContainsKey(key) || aggregated[key] < divine)
                aggregated[key] = divine;
        }

        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c >= 'A' && c <= 'Z') sb.Append((char)(c + 32));
                else if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool EndsWithDigit(string s) => s.Length > 0 && char.IsDigit(s[^1]);

        private static int TrailingLevel(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            int i = id.Length;
            while (i > 0 && char.IsDigit(id[i - 1])) i--;
            if (i == id.Length || i == 0 || id[i - 1] != '-') return -1;
            return int.TryParse(id.AsSpan(i), out var n) ? n : -1;
        }

        public static int DetectCurrencyGrade(string ninjaId)
        {
            if (string.IsNullOrEmpty(ninjaId)) return 1;
            if (ninjaId.StartsWith("perfect-", StringComparison.Ordinal)) return 3;
            if (ninjaId.StartsWith("greater-", StringComparison.Ordinal)) return 2;
            return 1;
        }

        public static string ExtractArtId(string? imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl)) return string.Empty;
            var s = imageUrl;
            int q = s.IndexOf('?');
            if (q >= 0) s = s.Substring(0, q);
            int slash = s.LastIndexOf('/');
            if (slash >= 0) s = s.Substring(slash + 1);
            int dot = s.LastIndexOf('.');
            if (dot > 0) s = s.Substring(0, dot);
            return s;
        }

        private sealed class PriceCacheDto
        {
            public int FormatVersion { get; set; } = CacheFormatVersion;
            public int PriceSource { get; set; }
            public string League { get; set; } = string.Empty;
            public DateTime LastSyncUtc { get; set; }
            public double DivineToExaltedRate { get; set; }
            public Dictionary<string, double> PricesDivine { get; set; } = new();
            public Dictionary<string, double>? ArtPricesDivine { get; set; }
            public Dictionary<string, string>? ArtNames { get; set; }
        }
    }
}
