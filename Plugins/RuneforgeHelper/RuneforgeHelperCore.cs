namespace RuneforgeHelper
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Numerics;
    using System.Runtime.InteropServices;
    using System.Text;
    using GameHelper;
    using GameHelper.Localization;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameOffsets.Objects.UiElement;
    using ImGuiNET;
    using Newtonsoft.Json;

    public sealed class RuneforgeHelperCore : PCore<RuneforgeHelperSettings>
    {
        // Fixed UI path through PoE2 0.5.x's Runeshape Combinations panel:
        //   GameUi → window-container → ? → ? → ? → recipes-container
        // Child indices wiggle across game restarts, but each UiElement's Flags field encodes
        // its "role" (panel/list/row/etc.) and those bits stay stable — so we match by Flags
        // fingerprint instead of by index. The IsVisible bit (bit 0x0B / mask 0x800) is masked
        // out before comparison because it toggles when the player opens/closes the panel.
        //
        // PoE2's UI tree has many sibling UiElements sharing the same fp at each level, so a
        // greedy "pick the first/visible match" walk can step into the wrong subtree and
        // silently dead-end. WalkFp instead BACKTRACKS: at each step it tries every matching
        // sibling (visible candidates first), recurses, and keeps whichever branch reaches a
        // valid recipes-container at the bottom (see IsRecipesContainer). Mirrors the Atlas
        // plugin's resolver.
        //
        // GateStep (the window-container) is the panel-open gate: its IsVisible bit flips with
        // the panel, so that hop only accepts a visible match — when the panel is closed the
        // whole walk fails and we draw nothing.
        //
        // The recipes-container has ~320 child rows; only a handful are visible at a time (rest
        // are scrolled off / templated). Each visible row's kid[0] holds an inline std::wstring
        // "<count>x <name>" at +0x390.
        private static readonly uint[] PanelFlagFingerprints =
        {
            0x00462EF1, // window-container (its IsVisible bit toggles with the panel)
            0x00502EF3,
            0x00502EF7,
            0x00542EF1,
            0x00502EF1, // recipes-container
        };
        private const int GateStep = 0;
        private const int ViewportStep = 2;
        private const int ScrollOffsetFieldOffset = 0x120;
        private const float PriceOutsideGap = 8f;

        private const int RecipeStride = 0xBA;
        private const int RecipeRewardTableOffset = 0x34;
        private const int TableRowsVectorOffset = 0x28;
        private const int DatPathOffset = 0x08;
        private const int BaseItemTypeStride = 0x168;
        private const int BaseItemTypeIdOffset = 0x00;
        private const int BaseItemTypeNameOffset = 0x20;
        private const int BaseItemTypeArtOffset = 0x7C;
        private const int ArtSubPathOffset = 0x08;

        private const int NameWStringOffset = 0x390;
        private const int UiElementChildrenOffset = 0x10;
        private const int UiElementFlagsOffset = 0x180;
        private const int IsVisibleBit = 0x0B;
        private const uint IsVisibleMask = 1u << IsVisibleBit; // = 0x800

        private IntPtr processHandle = IntPtr.Zero;
        private int handlePid;

        private readonly List<Recipe> recipes = new();
        private readonly PriceCache priceCache = new();
        private readonly CurrencyIconLoader currencyIcons = new();
        private Dictionary<string, (string MetaId, string DdsArt)> nameToArtId = new(StringComparer.Ordinal);
        private bool nameToArtLookupFinished;
        private IntPtr cachedPanel = IntPtr.Zero;
        private IntPtr resolvedViewport = IntPtr.Zero;
        private Vector2 viewportScrollOffset;
        private DateTime nextPanelScanUtc = DateTime.MinValue;
        private DateTime nextPriceLayoutRefreshUtc = DateTime.MinValue;
        private DateTime nextAutoRefreshCheckUtc = DateTime.MinValue;
        private const int PriceLayoutRefreshMs = 100;
        private readonly List<PriceLabelDraw> cachedPriceLabels = new();
        private readonly List<PriceLabelDraw> scratchPriceLabels = new();
        private readonly List<(Recipe Recipe, double DisplayValue, string ValueText, uint TextColor)> pricedRowsScratch = new();
        private readonly Dictionary<long, (Vector2 Pos, Vector2 Size, int StaleFrames)> lastGoodGeom = new();
        private readonly Dictionary<long, Vector2> smoothLabelPositions = new();
        private const int MaxStaleGeomFrames = 15;
        private bool iconsReloadPending;

        private readonly Dictionary<long, UiElementBaseOffset> frameBaseCache = new();
        private static readonly int UiBaseSize = Marshal.SizeOf<UiElementBaseOffset>();
        private readonly byte[] uiBaseBuf = new byte[Marshal.SizeOf<UiElementBaseOffset>()];

        private const uint ColorGreen = 0xFF55FF55u;
        private const uint ColorYellow = 0xFF55FFFFu;
        private const uint ColorRed = 0xFF4040FFu;

        private static readonly string PriceColorComboEn = "Off\0Relative (vs. median)\0Absolute (Ex thresholds)\0";
        private static readonly string PriceColorComboDe = "Aus\0Relativ (vs. Median)\0Absolut (Ex-Schwellen)\0";

        private bool panelResolvedLastFrame;
        private int visibleRecipesLastFrame;
        private string lastDebugRecipe = string.Empty;
        private string lastDebugPriceLookup = string.Empty;

        private string SettingPathname => Path.Join(this.DllDirectory, "config", "settings.txt");
        private string PriceCachePathname => Path.Join(this.DllDirectory, "config", "prices.json");

        public override void OnEnable(bool isGameOpened)
        {
            if (File.Exists(this.SettingPathname))
            {
                var content = File.ReadAllText(this.SettingPathname);
                this.Settings = JsonConvert.DeserializeObject<RuneforgeHelperSettings>(content)
                                ?? new RuneforgeHelperSettings();
            }

            var fresh = this.priceCache.TryLoadFromDisk(
                this.PriceCachePathname,
                this.Settings.League,
                this.Settings.PriceSource,
                this.Settings.CacheTtlMinutes);
            if (!fresh)
                this.priceCache.StartRefresh(this.Settings.League, this.Settings.PriceSource, this.PriceCachePathname);

            this.currencyIcons.Initialize(this.DllDirectory);
            this.iconsReloadPending = true;
        }

        public override void OnDisable()
        {
            this.ResetHandle();
            this.nameToArtId = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            this.nameToArtLookupFinished = false;
            this.cachedPanel = IntPtr.Zero;
        }

        public override void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.SettingPathname)!);
            this.Settings.LastSyncUtc = this.priceCache.LastSyncUtc;
            File.WriteAllText(this.SettingPathname, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
        }

        public override void DrawSettings()
        {
            if (ImGui.BeginTabBar("##RuneforgeHelperTabs"))
            {
                if (ImGui.BeginTabItem(this.L("General", "Allgemein")))
                {
                    ImGui.Text(this.L("Display", "Anzeige"));
                    if (ImGui.RadioButton(this.L("Divine (D)", "Divine (D)"), this.Settings.DisplayCurrency == 0))
                        this.Settings.DisplayCurrency = 0;
                    ImGui.SameLine();
                    if (ImGui.RadioButton(this.L("Exalted (Ex)", "Exalted (Ex)"), this.Settings.DisplayCurrency == 1))
                        this.Settings.DisplayCurrency = 1;

                    ImGui.SliderFloat(this.L("Font Scale", "Schriftgroesse"), ref this.Settings.PriceFontScale, 0.5f, 2.5f);
                    ImGui.SliderFloat(this.L("Offset X", "Versatz X"), ref this.Settings.PriceOffsetX, -400f, 400f, "%.0f px");
                    ImGui.SliderFloat(this.L("Offset Y", "Versatz Y"), ref this.Settings.PriceOffsetY, -100f, 100f, "%.0f px");
                    ImGui.ColorEdit4(this.L("Text Color", "Textfarbe"), ref this.Settings.PriceTextColor);

                    ImGui.Checkbox(this.L("Price background", "Preis-Hintergrund"), ref this.Settings.ShowPriceBackground);
                    if (this.Settings.ShowPriceBackground)
                    {
                        ImGui.ColorEdit4(this.L("Background color", "Hintergrundfarbe"), ref this.Settings.PriceBackgroundColor);
                    }

                    if (ImGui.Button(this.L("Reset display defaults", "Anzeige auf Standard")))
                        this.Settings.ApplyDisplayDefaults();
                    ImGui.SameLine();
                    ImGui.TextDisabled(this.L(
                        $"X={RuneforgeHelperSettings.DefaultPriceOffsetX:0}, Y={RuneforgeHelperSettings.DefaultPriceOffsetY:0}, scale={RuneforgeHelperSettings.DefaultPriceFontScale:0.##}",
                        $"X={RuneforgeHelperSettings.DefaultPriceOffsetX:0}, Y={RuneforgeHelperSettings.DefaultPriceOffsetY:0}, scale={RuneforgeHelperSettings.DefaultPriceFontScale:0.##}"));

                    int colorMode = (int)this.Settings.ColorMode;
                    var colorCombo = OverlayLocalization.IsGerman ? PriceColorComboDe : PriceColorComboEn;
                    if (ImGui.Combo(this.L("Price color", "Preisfarbe"), ref colorMode, colorCombo))
                        this.Settings.ColorMode = (RewardColorMode)colorMode;

                    ImGui.Checkbox(this.L("Show debug list window", "Debug-Listenfenster"), ref this.Settings.ShowWindow);
                    ImGui.TextWrapped(this.L(
                        "Fine-tune position while the Runeshape panel is open. Prices sit in a small box just to the right of each row (outside the panel edge). Negative X moves them left, positive X further right. Prices are total value for the listed quantity (e.g. 2x = double).",
                        "Position anpassen, waehrend das Runeshape-Panel offen ist. Preise erscheinen in einer kleinen Box rechts neben jeder Zeile (aussen am Panel). Negatives X = weiter links, positives X = weiter rechts. Preise sind der Gesamtwert fuer die angezeigte Menge (z. B. 2x = doppelt)."));
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(this.L("Data Source", "Datenquelle")))
                {
                    ImGui.InputText(this.L("League", "Liga"), ref this.Settings.League, 64);
                    ImGui.SliderInt(this.L("Refresh interval (min)", "Aktualisierungsintervall (Min.)"), ref this.Settings.CacheTtlMinutes, 5, 60);

                    ImGui.Spacing();
                    this.DrawPriceSourceSelector();

                    ImGui.Spacing();
                    var status = this.priceCache.Status;
                    var lastSync = this.priceCache.LastSyncUtc;
                    string statusText = status switch
                    {
                        PriceSyncStatus.Syncing => this.L("syncing…", "lade…"),
                        PriceSyncStatus.Ready => lastSync == DateTime.MinValue
                            ? this.L("ready (no data yet)", "bereit (noch keine Daten)")
                            : this.L($"updated {FormatRelative(lastSync)} ago", $"aktualisiert vor {FormatRelative(lastSync)}"),
                        PriceSyncStatus.Error => $"{this.L("error", "Fehler")}: {this.priceCache.LastError}",
                        _ => this.L("idle", "inaktiv"),
                    };

                    ImGui.Text($"{this.L("Status", "Status")}: {statusText}");
                    ImGui.Text($"{this.L("Items cached", "Items im Cache")}: {this.priceCache.PriceCount}");
                    if (this.priceCache.DivineToExaltedRate > 0)
                    {
                        ImGui.Text($"1 Divine = {this.priceCache.DivineToExaltedRate:F2} Exalted");
                    }

                    if (this.Settings.PriceSource == PriceCache.SourcePoe2Scout)
                    {
                        ImGui.TextWrapped(this.L(
                            "Basic rune prices (Storm, Desert, Iron, …) are merged from poe.ninja because poe2scout does not list them.",
                            "Basis-Runenpreise (Storm, Desert, Iron, …) werden von poe.ninja ergaenzt, da poe2scout sie nicht fuehrt."));
                    }

                    ImGui.BeginDisabled(status == PriceSyncStatus.Syncing);
                    if (ImGui.Button(this.L("Refresh Prices Now", "Preise jetzt aktualisieren")))
                        this.priceCache.StartRefresh(this.Settings.League, this.Settings.PriceSource, this.PriceCachePathname);
                    ImGui.EndDisabled();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(this.L("Advanced", "Erweitert")))
                {
                    ImGui.Text($"{this.L("Panel resolved", "Panel erkannt")}: {(this.panelResolvedLastFrame ? this.L("yes", "ja") : this.L("no", "nein"))}");
                    ImGui.Text($"{this.L("Visible recipes", "Sichtbare Rezepte")}: {this.visibleRecipesLastFrame}");
                    if (!string.IsNullOrEmpty(this.lastDebugRecipe))
                    {
                        ImGui.Text($"{this.L("Last recipe", "Letztes Rezept")}: {this.lastDebugRecipe}");
                        ImGui.Text($"{this.L("Price lookup", "Preisabgleich")}: {this.lastDebugPriceLookup}");
                    }

                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            ImGui.Separator();
            ImGui.TextWrapped(this.L(
                "RuneforgeHelper draws stack-total prices (count × unit price) next to each visible reward in the in-game Runeshape Combinations panel.",
                "RuneforgeHelper zeigt Gesamtpreise (Anzahl × Stueckpreis) neben jeder sichtbaren Belohnung im Runeshape-Combinations-Panel."));
        }

        private string L(string english, string german) => OverlayLocalization.L(english, german);

        private void DrawPriceSourceSelector()
        {
            ImGui.Text(this.L("Price source:", "Preisquelle:"));
            var previousSource = this.Settings.PriceSource;

            if (ImGui.RadioButton("poe.ninja", this.Settings.PriceSource == PriceCache.SourcePoeNinja))
                this.Settings.PriceSource = PriceCache.SourcePoeNinja;
            ImGui.SameLine();
            if (ImGui.RadioButton("poe2scout", this.Settings.PriceSource == PriceCache.SourcePoe2Scout))
                this.Settings.PriceSource = PriceCache.SourcePoe2Scout;

            if (previousSource != this.Settings.PriceSource)
            {
                this.priceCache.StartRefresh(this.Settings.League, this.Settings.PriceSource, this.PriceCachePathname);
            }
        }

        public override void DrawUI()
        {
            if (Core.States.GameCurrentState != GameStateTypes.InGameState)
            {
                this.recipes.Clear();
                this.cachedPanel = IntPtr.Zero;
                this.resolvedViewport = IntPtr.Zero;
                this.lastGoodGeom.Clear();
                this.smoothLabelPositions.Clear();
                return;
            }

            this.MaybeAutoRefreshPrices();

            if (!Core.Process.Foreground &&
                System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle != GetForegroundWindow())
            {
                this.recipes.Clear();
                this.cachedPanel = IntPtr.Zero;
                this.resolvedViewport = IntPtr.Zero;
                this.lastGoodGeom.Clear();
                this.smoothLabelPositions.Clear();
                return;
            }

            var iconFile = this.Settings.DisplayCurrency == 1 ? "exalted.png" : "divine.png";
            if (this.iconsReloadPending || !this.currencyIcons.TryGet(iconFile, out _, out _, out _))
            {
                this.currencyIcons.Reload();
                this.iconsReloadPending = false;
            }

            if (!this.EnsureProcess()) return;

            var panel = this.GetPanelCached();
            this.panelResolvedLastFrame = panel != IntPtr.Zero;
            if (panel == IntPtr.Zero)
            {
                this.recipes.Clear();
                this.visibleRecipesLastFrame = 0;
                this.cachedPriceLabels.Clear();
                this.lastGoodGeom.Clear();
                this.smoothLabelPositions.Clear();
                return;
            }

            this.BuildNameToArtIfNeeded(panel);

            var layoutNow = DateTime.UtcNow;
            if (layoutNow >= this.nextPriceLayoutRefreshUtc)
            {
                this.nextPriceLayoutRefreshUtc = layoutNow.AddMilliseconds(PriceLayoutRefreshMs);
                this.RefreshPriceLabelCache(panel);
            }

            this.DrawCachedPriceLabels();

            if (this.Settings.ShowWindow)
            {
                this.ReadVisibleRecipes(panel);
                this.visibleRecipesLastFrame = this.recipes.Count;
                this.DrawDebugWindow();
            }
        }

        // ── Panel resolution ──────────────────────────────────────────────

        private IntPtr GetPanelCached()
        {
            var now = DateTime.UtcNow;
            var intervalMs = Core.IsSettingsMenuOpen ? 300 : 120;
            if (now >= this.nextPanelScanUtc || this.cachedPanel == IntPtr.Zero)
            {
                this.nextPanelScanUtc = now.AddMilliseconds(intervalMs);
                this.cachedPanel = this.ResolvePanel();
            }

            return this.cachedPanel;
        }

        private IntPtr ResolvePanel()
        {
            var gameUi = Core.States.InGameStateObject.GameUi.Address;
            this.resolvedViewport = IntPtr.Zero;
            if (gameUi == IntPtr.Zero) return IntPtr.Zero;
            return this.WalkFp(gameUi, PanelFlagFingerprints, GateStep, 0);
        }

        private IntPtr WalkFp(IntPtr parentAddr, uint[] fps, int gateStep, int step)
        {
            if (step == fps.Length)
                return this.IsRecipesContainer(parentAddr) ? parentAddr : IntPtr.Zero;

            if (!this.TryReadStdVector(parentAddr + UiElementChildrenOffset, out var first, out var last))
                return IntPtr.Zero;
            long n = ((long)last - (long)first) / 8;
            if (n <= 0 || n > 4000) return IntPtr.Zero;

            uint target = fps[step] & ~IsVisibleMask;

            for (int pass = 0; pass < 2; pass++)
            {
                bool wantVisible = pass == 0;
                for (int i = 0; i < n; i++)
                {
                    var childAddr = this.ReadPtr(first + (nint)(i * 8));
                    if (childAddr == IntPtr.Zero) continue;
                    if (!this.TryReadFlags(childAddr, out var flags)) continue;
                    if ((flags & ~IsVisibleMask) != target) continue;

                    bool visible = (flags & IsVisibleMask) != 0;
                    if (visible != wantVisible) continue;
                    if (step == gateStep && !visible) continue;

                    var deeper = this.WalkFp(childAddr, fps, gateStep, step + 1);
                    if (deeper != IntPtr.Zero)
                    {
                        if (step == ViewportStep)
                            this.resolvedViewport = childAddr;
                        return deeper;
                    }
                }
            }
            return IntPtr.Zero;
        }

        private bool IsRecipesContainer(IntPtr addr)
        {
            if (!this.TryReadStdVector(addr + UiElementChildrenOffset, out var first, out var last)) return false;
            long n = ((long)last - (long)first) / 8;
            if (n <= 0 || n > 4000) return false;

            for (int i = 0; i < n; i++)
            {
                var row = this.ReadPtr(first + (nint)(i * 8));
                if (row == IntPtr.Zero) continue;
                var label = this.GetChild(row, 0);
                if (label == IntPtr.Zero) continue;
                if (!string.IsNullOrEmpty(this.ReadStdWString(label + NameWStringOffset)))
                    return true;
            }
            return false;
        }

        private IntPtr GetChild(IntPtr addr, int index)
        {
            if (addr == IntPtr.Zero) return IntPtr.Zero;
            if (!this.TryReadStdVector(addr + UiElementChildrenOffset, out var first, out var last)) return IntPtr.Zero;
            long n = ((long)last - (long)first) / 8;
            if (index < 0 || index >= n) return IntPtr.Zero;
            return this.ReadPtr(first + (nint)(index * 8));
        }

        // ── Reading rows ──────────────────────────────────────────────────

        private void ReadVisibleRecipes(IntPtr panel)
        {
            this.recipes.Clear();
            if (!this.TryReadStdVector(panel + UiElementChildrenOffset, out var first, out var last)) return;
            long n = ((long)last - (long)first) / 8;
            if (n <= 0 || n > 4000) return;

            for (int i = 0; i < n; i++)
            {
                var row = this.ReadPtr(first + (nint)(i * 8));
                if (row == IntPtr.Zero) continue;
                if (!this.IsUiElementVisible(row)) continue;

                var label = this.GetChild(row, 0);
                if (label == IntPtr.Zero) continue;

                var raw = this.ReadStdWString(label + NameWStringOffset);
                if (string.IsNullOrEmpty(raw)) continue;

                ParseNameAndCount(raw, out var count, out var name);
                this.nameToArtId.TryGetValue(name.Trim(), out var keys);
                this.recipes.Add(new Recipe(count, name, row, label, keys.MetaId ?? string.Empty, keys.DdsArt ?? string.Empty));
            }
        }

        // ── Price refresh polling ─────────────────────────────────────────

        private void MaybeAutoRefreshPrices()
        {
            var now = DateTime.UtcNow;
            if (now < this.nextAutoRefreshCheckUtc) return;
            this.nextAutoRefreshCheckUtc = now.AddMinutes(1);

            if (this.priceCache.Status == PriceSyncStatus.Syncing) return;
            var ttl = TimeSpan.FromMinutes(Math.Max(1, this.Settings.CacheTtlMinutes));
            if (this.priceCache.LastSyncUtc != DateTime.MinValue && now - this.priceCache.LastSyncUtc < ttl) return;

            this.priceCache.StartRefresh(this.Settings.League, this.Settings.PriceSource, this.PriceCachePathname);
        }

        // ── Drawing ───────────────────────────────────────────────────────

        private void DrawCachedPriceLabels()
        {
            if (this.cachedPriceLabels.Count == 0)
            {
                return;
            }

            var fgDraw = ImGui.GetForegroundDrawList();
            var font = ImGui.GetFont();
            const uint shadowColor = 0xCC000000;
            this.DrawPriceLabels(fgDraw, font, shadowColor, this.cachedPriceLabels);
        }

        private void RefreshPriceLabelCache(IntPtr panel)
        {
            this.frameBaseCache.Clear();
            this.ReadVisibleRecipes(panel);
            this.visibleRecipesLastFrame = this.recipes.Count;

            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize() * this.Settings.PriceFontScale;
            var defaultTextColor = ImGui.ColorConvertFloat4ToU32(this.Settings.PriceTextColor);
            var showExalted = this.Settings.DisplayCurrency == 1;
            var iconFile = showExalted ? "exalted.png" : "divine.png";
            var divToEx = this.priceCache.DivineToExaltedRate;

            var iconWidth = fontSize;
            if (this.currencyIcons.TryGet(iconFile, out _, out var iconW, out var iconH) && iconH > 0)
                iconWidth = fontSize * iconW / (float)iconH;

            // Prices/median first — independent of per-frame geometry success (RunecraftHelper v0.0.5).
            var totalsEx = new List<double>();
            this.pricedRowsScratch.Clear();
            foreach (var recipe in this.recipes)
            {
                if (!this.TryGetRecipeUnitDivine(in recipe, out var unitDivine) || unitDivine <= 0)
                {
                    continue;
                }

                var count = Math.Max(1, recipe.Count);
                var totalDivine = unitDivine * count;
                double displayValue;
                string valueText;
                if (showExalted)
                {
                    if (divToEx <= 0)
                    {
                        continue;
                    }

                    displayValue = totalDivine * divToEx;
                    valueText = FormatExalted(displayValue);
                }
                else
                {
                    displayValue = totalDivine;
                    valueText = FormatDivine(displayValue);
                }

                if (this.Settings.ColorMode == RewardColorMode.Relative)
                {
                    totalsEx.Add(displayValue);
                }

                this.pricedRowsScratch.Add((recipe, displayValue, valueText, 0));
            }

            if (this.pricedRowsScratch.Count == 0)
            {
                return;
            }

            var medianEx = totalsEx.Count > 0 ? Median(totalsEx) : 0;
            for (var i = 0; i < this.pricedRowsScratch.Count; i++)
            {
                var priced = this.pricedRowsScratch[i];
                var textColor = this.Settings.ColorMode == RewardColorMode.Off
                    ? defaultTextColor
                    : this.PickColor(priced.DisplayValue, medianEx, showExalted);
                this.pricedRowsScratch[i] = (priced.Recipe, priced.DisplayValue, priced.ValueText, textColor);
            }

            this.viewportScrollOffset = this.ReadScrollOffset(this.resolvedViewport);

            Vector2 vpPos = default;
            Vector2 vpSize = default;
            var haveClip = this.resolvedViewport != IntPtr.Zero &&
                           this.TryResolveRowGeometry(this.resolvedViewport, out vpPos, out vpSize);
            var clipTop = haveClip ? vpPos.Y : 0f;
            var clipBottom = haveClip ? vpPos.Y + vpSize.Y : 0f;

            var scratch = this.scratchPriceLabels;
            scratch.Clear();
            foreach (var (recipe, displayValue, valueText, textColor) in this.pricedRowsScratch)
            {
                if (!this.TryResolveRowGeometry(recipe.RowAddr, out var rowPos, out var rowSize))
                {
                    continue;
                }

                var centreY = rowPos.Y + rowSize.Y * 0.5f;
                if (haveClip && (centreY < clipTop || centreY > clipBottom))
                {
                    continue;
                }

                this.lastDebugRecipe = $"{recipe.Count}x {recipe.Name}";
                this.lastDebugPriceLookup = $"{displayValue:F4}";

                var textWidth = font.CalcTextSizeA(fontSize, float.MaxValue, 0f, valueText).X;
                const float iconGap = 3f;
                var contentWidth = textWidth + iconGap + iconWidth;
                var textPos = new Vector2(
                    rowPos.X + rowSize.X + PriceOutsideGap + this.Settings.PriceOffsetX,
                    rowPos.Y + (rowSize.Y - fontSize) * 0.5f + this.Settings.PriceOffsetY);

                var rowKey = (long)recipe.RowAddr;
                if (this.smoothLabelPositions.TryGetValue(rowKey, out var prevPos))
                {
                    textPos = Vector2.Lerp(prevPos, textPos, 0.45f);
                }

                this.smoothLabelPositions[rowKey] = textPos;

                var label = new PriceLabelDraw
                {
                    RowKey = rowKey,
                    Pos = textPos,
                    IconFile = iconFile,
                    IconWidth = iconWidth,
                    IconHeight = fontSize,
                    TextWidth = textWidth,
                    ValueText = valueText,
                    TextColor = textColor,
                    FontSize = fontSize,
                };

                if (this.Settings.ShowPriceBackground)
                {
                    const float padH = 6f;
                    const float padV = 3f;
                    label.ShowBackground = true;
                    label.BgMin = new Vector2(textPos.X - padH, textPos.Y - padV);
                    label.BgMax = new Vector2(textPos.X + contentWidth + padH, textPos.Y + fontSize + padV);
                    label.BgColor = ImGui.ColorConvertFloat4ToU32(this.Settings.PriceBackgroundColor);
                    label.BgRounding = 3f;
                }

                scratch.Add(label);
            }

            if (scratch.Count > 0)
            {
                this.cachedPriceLabels.Clear();
                this.cachedPriceLabels.AddRange(scratch);

                var staleKeys = new List<long>();
                foreach (var key in this.smoothLabelPositions.Keys)
                {
                    var keep = false;
                    foreach (var label in scratch)
                    {
                        if (label.RowKey == key)
                        {
                            keep = true;
                            break;
                        }
                    }

                    if (!keep)
                    {
                        staleKeys.Add(key);
                    }
                }

                foreach (var key in staleKeys)
                {
                    this.smoothLabelPositions.Remove(key);
                }
            }
        }

        private void DrawPriceLabels(ImDrawListPtr fgDraw, ImFontPtr font, uint shadowColor, List<PriceLabelDraw> priceLabels)
        {
            foreach (var label in priceLabels)
            {
                if (label.ShowBackground)
                {
                    fgDraw.AddRectFilled(label.BgMin, label.BgMax, label.BgColor, label.BgRounding);
                }

                fgDraw.AddText(font, label.FontSize, label.Pos + new Vector2(1f, 1f), shadowColor, label.ValueText);
                fgDraw.AddText(font, label.FontSize, label.Pos, label.TextColor, label.ValueText);

                if (this.currencyIcons.TryGet(label.IconFile, out var texPtr, out _, out _))
                {
                    var iconPos = label.Pos + new Vector2(label.TextWidth + 3f, 0f);
                    var iconMax = iconPos + new Vector2(label.IconWidth, label.IconHeight);
                    fgDraw.AddImage(texPtr, iconPos, iconMax);
                }
            }
        }

        private void DrawDebugWindow()
        {
            ImGui.SetNextWindowSize(new Vector2(460, 340), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin($"Runeshape Rewards ({this.recipes.Count})###RuneforgeHelperDebug"))
            {
                ImGui.End();
                return;
            }

            if (ImGui.BeginTable("recipes", 5,
                    ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit |
                    ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 30f);
                ImGui.TableSetupColumn("metaId", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("dds art", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn(this.L("price", "preis"), ImGuiTableColumnFlags.WidthFixed, 72f);
                ImGui.TableSetupColumn(this.L("name (EN)", "name (EN)"), ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();

                var missing = new Vector4(1f, 0.45f, 0.45f, 1f);
                var showExalted = this.Settings.DisplayCurrency == 1;
                var divToEx = this.priceCache.DivineToExaltedRate;

                foreach (var r in this.recipes)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextDisabled($"{r.Count}x");

                    ImGui.TableSetColumnIndex(1);
                    if (string.IsNullOrEmpty(r.MetaId)) ImGui.TextColored(missing, "(none)");
                    else ImGui.TextUnformatted(r.MetaId);

                    ImGui.TableSetColumnIndex(2);
                    if (string.IsNullOrEmpty(r.DdsArt)) ImGui.TextColored(missing, "(none)");
                    else ImGui.TextUnformatted(r.DdsArt);

                    ImGui.TableSetColumnIndex(3);
                    if (this.TryGetRecipeUnitDivine(in r, out var unit))
                    {
                        var total = unit * Math.Max(1, r.Count);
                        if (showExalted && divToEx > 0)
                            ImGui.TextUnformatted(FormatExalted(total * divToEx));
                        else
                            ImGui.TextUnformatted(FormatDivine(total));
                    }
                    else
                        ImGui.TextDisabled("—");

                    ImGui.TableSetColumnIndex(4);
                    if (this.TryGetRecipeEnglishName(in r, out var en))
                        ImGui.TextUnformatted(en);
                    else
                        ImGui.TextDisabled("—");
                }

                ImGui.EndTable();
            }

            ImGui.End();
        }

        // ── UI position (mirrors GameHelper UiElementBase screen geometry) ─

        private static (float W, float H) ScaleValue(byte index, float multiplier)
        {
            var io = ImGui.GetIO();
            float v1 = io.DisplaySize.X / (float)UiElementBaseFuncs.BaseResolution.X;
            float v2 = io.DisplaySize.Y / (float)UiElementBaseFuncs.BaseResolution.Y;
            float w = multiplier, h = multiplier;
            switch (index)
            {
                case 1: w *= v1; h *= v1; break;
                case 2: w *= v2; h *= v2; break;
                case 3: w *= v1; h *= v2; break;
            }
            return (w, h);
        }

        private Vector2 ScaledSize(in UiElementBaseOffset el)
        {
            var (w, h) = ScaleValue(el.ScaleIndex, el.LocalScaleMultiplier);
            return new Vector2(el.UnscaledSize.X * w, el.UnscaledSize.Y * h);
        }

        private bool TryScreenPosition(in UiElementBaseOffset el, out Vector2 screen)
        {
            if (!this.TryGetUnscaledPosition(in el, 0, out var p))
            {
                screen = default;
                return false;
            }

            var (w, h) = ScaleValue(el.ScaleIndex, el.LocalScaleMultiplier);
            screen = new Vector2(p.X * w, p.Y * h);
            return true;
        }

        private bool TryGetUnscaledPosition(in UiElementBaseOffset el, int depth, out Vector2 pos)
        {
            var local = new Vector2(el.RelativePosition.X, el.RelativePosition.Y);
            if (el.ParentPtr == IntPtr.Zero || depth >= 64)
            {
                pos = local;
                return true;
            }

            if (!this.TryReadBaseCached(el.ParentPtr, out var parent))
            {
                pos = local;
                return false;
            }

            if (!this.TryGetUnscaledPosition(in parent, depth + 1, out var parentPos))
            {
                pos = local;
                return false;
            }

            if (UiElementBaseFuncs.ShouldModifyPos(el.Flags))
            {
                parentPos += new Vector2(el.PositionModifier.X, el.PositionModifier.Y);
            }

            if (el.ParentPtr == this.resolvedViewport)
                parentPos += this.viewportScrollOffset;

            if (parent.ScaleIndex == el.ScaleIndex &&
                parent.LocalScaleMultiplier == el.LocalScaleMultiplier)
            {
                pos = parentPos + local;
                return true;
            }

            var (psw, psh) = ScaleValue(parent.ScaleIndex, parent.LocalScaleMultiplier);
            var (msw, msh) = ScaleValue(el.ScaleIndex, el.LocalScaleMultiplier);
            pos = new Vector2(
                parentPos.X * psw / msw + local.X,
                parentPos.Y * psh / msh + local.Y);
            return true;
        }

        private Vector2 ReadScrollOffset(IntPtr addr)
        {
            if (addr == IntPtr.Zero) return Vector2.Zero;
            var buf = new byte[8];
            if (!ReadProcessMemory(this.processHandle, addr + ScrollOffsetFieldOffset, buf, (uint)buf.Length, out _))
                return Vector2.Zero;
            return new Vector2(BitConverter.ToSingle(buf, 0), BitConverter.ToSingle(buf, 4));
        }

        private bool TryResolveRowGeometry(IntPtr addr, out Vector2 pos, out Vector2 size)
        {
            pos = default;
            size = default;
            if (addr == IntPtr.Zero)
            {
                return false;
            }

            long key = (long)addr;
            if (this.TryReadUiBase(addr, out var el))
            {
                if ((el.Flags & IsVisibleMask) == 0)
                {
                    if (this.lastGoodGeom.TryGetValue(key, out var hidden) && hidden.StaleFrames < MaxStaleGeomFrames)
                    {
                        pos = hidden.Pos;
                        size = hidden.Size;
                        this.lastGoodGeom[key] = (hidden.Pos, hidden.Size, hidden.StaleFrames + 1);
                        return true;
                    }

                    this.lastGoodGeom.Remove(key);
                    return false;
                }

                var s = this.ScaledSize(in el);
                if (s.X > 1f && s.Y > 1f &&
                    this.TryScreenPosition(in el, out var p) && !float.IsNaN(p.X) && !float.IsNaN(p.Y))
                {
                    pos = p;
                    size = s;
                    this.lastGoodGeom[key] = (p, s, 0);
                    return true;
                }
            }

            if (this.lastGoodGeom.TryGetValue(key, out var lg) && lg.StaleFrames < MaxStaleGeomFrames)
            {
                pos = lg.Pos;
                size = lg.Size;
                this.lastGoodGeom[key] = (lg.Pos, lg.Size, lg.StaleFrames + 1);
                return true;
            }

            this.lastGoodGeom.Remove(key);
            return false;
        }

        private bool TryReadBaseCached(IntPtr addr, out UiElementBaseOffset ui)
        {
            if (this.frameBaseCache.TryGetValue((long)addr, out ui)) return true;
            if (!this.TryReadUiBase(addr, out ui)) return false;
            this.frameBaseCache[(long)addr] = ui;
            return true;
        }

        private bool TryReadUiBase(IntPtr addr, out UiElementBaseOffset ui)
        {
            ui = default;
            ulong u = (ulong)addr;
            if (u < 0x10000 || u > 0x7FFFFFFFFFFF) return false;
            if (!ReadProcessMemory(this.processHandle, addr, this.uiBaseBuf, (uint)UiBaseSize, out var got)
                || got < UiBaseSize)
                return false;
            ui = MemoryMarshal.Read<UiElementBaseOffset>(this.uiBaseBuf);
            return true;
        }

        // ── Reward art-id dictionary (localized name → metaId / dds art) ──

        private void BuildNameToArtIfNeeded(IntPtr panel)
        {
            if (this.nameToArtId.Count > 0 || this.nameToArtLookupFinished) return;
            this.nameToArtLookupFinished = true;

            var handle = this.FindRecipeTableHandle(panel);
            if (handle == IntPtr.Zero) return;

            var recVec = this.ReadPtr(handle + TableRowsVectorOffset);
            var recBegin = this.ReadPtr(recVec);
            var recEnd = this.ReadPtr(recVec + 8);
            if (recBegin == IntPtr.Zero || (long)recEnd <= (long)recBegin) return;
            long recCount = ((long)recEnd - (long)recBegin) / RecipeStride;
            if (recCount <= 0 || recCount > 5000) return;

            IntPtr bitTable = IntPtr.Zero;
            for (long k = 0; k < recCount && bitTable == IntPtr.Zero; k++)
                bitTable = this.ReadPtr(recBegin + (nint)(k * RecipeStride) + RecipeRewardTableOffset);
            if (bitTable == IntPtr.Zero) return;

            var bitVec = this.ReadPtr(bitTable + TableRowsVectorOffset);
            var bitBegin = this.ReadPtr(bitVec);
            var bitEnd = this.ReadPtr(bitVec + 8);
            if (bitBegin == IntPtr.Zero || (long)bitEnd <= (long)bitBegin) return;
            long bitCount = ((long)bitEnd - (long)bitBegin) / BaseItemTypeStride;
            if (bitCount <= 0 || bitCount > 200000) return;

            var dict = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            for (long j = 0; j < bitCount; j++)
            {
                var row = bitBegin + (nint)(j * BaseItemTypeStride);
                var localizedName = this.ReadUtf16Z(this.ReadPtr(row + BaseItemTypeNameOffset), 64);
                if (localizedName.Length < 2) continue;

                var metaId = LastMetaSegment(this.ReadUtf16Z(this.ReadPtr(row + BaseItemTypeIdOffset), 128));
                var artSub = this.ReadPtr(row + BaseItemTypeArtOffset);
                var ddsArt = artSub == IntPtr.Zero
                    ? string.Empty
                    : ArtIdFromDdsPath(this.ReadUtf16Z(this.ReadPtr(artSub + ArtSubPathOffset), 128));
                if (metaId.Length == 0 && ddsArt.Length == 0) continue;
                dict[localizedName.Trim()] = (metaId, ddsArt);
            }

            if (dict.Count > 0) this.nameToArtId = dict;
        }

        private IntPtr FindRecipeTableHandle(IntPtr panel)
        {
            var seen = new HashSet<long> { (long)panel };
            var queue = new Queue<(IntPtr addr, int depth)>();
            queue.Enqueue((panel, 0));
            int visited = 0;
            while (queue.Count > 0 && visited < 40000)
            {
                var (addr, depth) = queue.Dequeue();
                visited++;

                if (IsExeAddr(this.ReadPtr(addr)))
                {
                    var pathPtr = this.ReadPtr(addr + DatPathOffset);
                    if (pathPtr != IntPtr.Zero)
                    {
                        var s = this.ReadUtf16Z(pathPtr, 80);
                        if (s.Contains("Expedition2Recipes", StringComparison.Ordinal))
                            return addr;
                    }
                }

                if (depth >= 7) continue;
                var buf = new byte[0x180];
                if (!ReadProcessMemory(this.processHandle, addr, buf, (uint)buf.Length, out var got)) continue;
                for (int o = 0; o + 8 <= got; o += 8)
                {
                    long v = BitConverter.ToInt64(buf, o);
                    if ((ulong)v < 0x10000 || (ulong)v > 0x7FFFFFFFFFFF) continue;
                    if (seen.Add(v)) queue.Enqueue(((IntPtr)v, depth + 1));
                }
            }
            return IntPtr.Zero;
        }

        private static bool IsExeAddr(IntPtr p) => (ulong)p >= 0x7FF000000000ul && (ulong)p < 0x800000000000ul;

        private string ReadUtf16Z(IntPtr ptr, int maxChars)
        {
            if (ptr == IntPtr.Zero) return string.Empty;
            ulong u = (ulong)ptr;
            if (u < 0x10000 || u > 0x7FFFFFFFFFFF) return string.Empty;
            var buf = new byte[maxChars * 2];
            if (!ReadProcessMemory(this.processHandle, ptr, buf, (uint)buf.Length, out var read)) return string.Empty;
            int n = read / 2;
            var sb = new StringBuilder(n);
            for (int i = 0; i < n; i++)
            {
                char c = (char)BitConverter.ToUInt16(buf, i * 2);
                if (c == '\0') break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private bool TryGetRecipeUnitDivine(in Recipe r, out double unitDivine)
        {
            if (IsUncutGem(r.MetaId))
            {
                int gemLevel = UncutGemLevel(r.MetaId);
                if (gemLevel >= 0 && !string.IsNullOrEmpty(r.DdsArt) &&
                    this.priceCache.TryGetDivinePriceByArtId(r.DdsArt + gemLevel.ToString(), out unitDivine) && unitDivine > 0)
                    return true;
                unitDivine = 0;
                return false;
            }

            if (!string.IsNullOrEmpty(r.MetaId) && this.priceCache.TryGetDivinePriceByArtId(r.MetaId, out unitDivine) && unitDivine > 0)
                return true;

            int level = LevelFromMetaId(r.MetaId);
            if (level >= 0)
            {
                if (!string.IsNullOrEmpty(r.DdsArt) &&
                    this.priceCache.TryGetDivinePriceByArtId(r.DdsArt + level.ToString(), out unitDivine) && unitDivine > 0)
                    return true;
            }
            else if (!string.IsNullOrEmpty(r.DdsArt) && this.priceCache.TryGetDivinePriceByArtId(r.DdsArt, out unitDivine) && unitDivine > 0)
            {
                return true;
            }

            if (this.priceCache.TryGetDivinePrice(r.Name, out unitDivine) && unitDivine > 0)
                return true;
            unitDivine = 0;
            return false;
        }

        private bool TryGetRecipeEnglishName(in Recipe r, out string name)
        {
            if (IsUncutGem(r.MetaId))
            {
                int gemLevel = UncutGemLevel(r.MetaId);
                if (gemLevel >= 0 && !string.IsNullOrEmpty(r.DdsArt) &&
                    this.priceCache.TryGetNameByArtId(r.DdsArt + gemLevel.ToString(), out name) && !string.IsNullOrEmpty(name))
                    return true;
                name = string.Empty;
                return false;
            }

            if (!string.IsNullOrEmpty(r.MetaId) && this.priceCache.TryGetNameByArtId(r.MetaId, out name) && !string.IsNullOrEmpty(name))
                return true;

            int level = LevelFromMetaId(r.MetaId);
            string? artKey = string.IsNullOrEmpty(r.DdsArt)
                ? null
                : (level >= 0 ? r.DdsArt + level.ToString() : r.DdsArt);
            if (artKey != null && this.priceCache.TryGetNameByArtId(artKey, out name) && !string.IsNullOrEmpty(name))
                return true;

            name = string.Empty;
            return false;
        }

        private uint PickColor(double displayValue, double median, bool valueIsExalted)
        {
            switch (this.Settings.ColorMode)
            {
                case RewardColorMode.Absolute:
                    var ex = valueIsExalted ? displayValue : displayValue * Math.Max(this.priceCache.DivineToExaltedRate, 0);
                    if (ex >= 5.0) return ColorGreen;
                    if (ex < 0.5) return ColorRed;
                    return ColorYellow;
                case RewardColorMode.Relative:
                    if (median <= 0) return ImGui.ColorConvertFloat4ToU32(this.Settings.PriceTextColor);
                    var ratio = displayValue / median;
                    if (ratio >= 1.3) return ColorGreen;
                    if (ratio <= 0.7) return ColorRed;
                    return ColorYellow;
                default:
                    return ImGui.ColorConvertFloat4ToU32(this.Settings.PriceTextColor);
            }
        }

        private static double Median(List<double> values)
        {
            var arr = values.ToArray();
            Array.Sort(arr);
            int n = arr.Length;
            return n % 2 == 1 ? arr[n / 2] : (arr[n / 2 - 1] + arr[n / 2]) * 0.5;
        }

        // ── Parsing / formatting ─────────────────────────────────────────

        private static void ParseNameAndCount(string raw, out int count, out string name)
        {
            count = 1;
            name = raw?.Trim() ?? string.Empty;
            if (name.Length == 0) return;

            int i = 0;
            while (i < name.Length && char.IsDigit(name[i])) i++;
            if (i > 0 && i < name.Length && (name[i] == 'x' || name[i] == 'X'))
            {
                if (int.TryParse(name.AsSpan(0, i), out var c) && c > 0)
                {
                    count = c;
                    name = name[(i + 1)..].TrimStart();
                    return;
                }
            }

            if (name[^1] == ')')
            {
                int open = name.LastIndexOf('(');
                if (open > 0)
                {
                    var inner = name.Substring(open + 1, name.Length - open - 2).Trim();
                    if (int.TryParse(inner, out var c) && c > 0)
                    {
                        count = c;
                        name = name[..open].TrimEnd();
                    }
                }
            }
        }

        private static string LastMetaSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            int slash = path.LastIndexOf('/');
            return slash >= 0 ? path[(slash + 1)..] : path;
        }

        private static string ArtIdFromDdsPath(string path)
        {
            var seg = LastMetaSegment(path);
            int dot = seg.LastIndexOf('.');
            return dot > 0 ? seg[..dot] : seg;
        }

        private static int LevelFromMetaId(string metaId)
        {
            if (string.IsNullOrEmpty(metaId)) return -1;
            int i = metaId.Length;
            while (i > 0 && char.IsDigit(metaId[i - 1])) i--;
            if (i == metaId.Length) return -1;
            const string marker = "Level";
            if (i < marker.Length || !metaId.AsSpan(i - marker.Length, marker.Length).SequenceEqual(marker))
                return -1;
            return int.TryParse(metaId.AsSpan(i), out var n) ? n : -1;
        }

        private static bool IsUncutGem(string metaId) =>
            !string.IsNullOrEmpty(metaId) &&
            (metaId.StartsWith("SkillGemUncut", StringComparison.Ordinal)
             || metaId.StartsWith("SupportGemUncut", StringComparison.Ordinal)
             || metaId.StartsWith("ReservationGemUncut", StringComparison.Ordinal));

        private static int UncutGemLevel(string metaId)
        {
            if (string.IsNullOrEmpty(metaId)) return -1;
            int i = metaId.Length;
            while (i > 0 && char.IsDigit(metaId[i - 1])) i--;
            if (i == metaId.Length) return -1;
            return int.TryParse(metaId.AsSpan(i), out var n) ? n : -1;
        }

        private static string FormatDivine(double value) =>
            value.ToString("0.000", CultureInfo.InvariantCulture);

        private static string FormatExalted(double value)
        {
            if (value >= 100) return value.ToString("0", CultureInfo.InvariantCulture);
            if (value >= 1) return value.ToString("0.#", CultureInfo.InvariantCulture);
            if (value >= 0.1) return value.ToString("0.##", CultureInfo.InvariantCulture);
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatRelative(DateTime utc)
        {
            var span = DateTime.UtcNow - utc;
            if (span.TotalSeconds < 60) return $"{(int)span.TotalSeconds}s";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h";
            return $"{(int)span.TotalDays}d";
        }

        // ── Memory primitives ────────────────────────────────────────────

        private bool EnsureProcess()
        {
            int pid = (int)Core.Process.Pid;
            if (pid == 0)
            {
                if (this.handlePid != 0) this.ResetHandle();
                return false;
            }

            if (pid == this.handlePid && this.processHandle != IntPtr.Zero) return true;

            this.ResetHandle();
            this.processHandle = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, pid);
            if (this.processHandle == IntPtr.Zero) return false;
            this.handlePid = pid;
            return true;
        }

        private void ResetHandle()
        {
            if (this.processHandle != IntPtr.Zero)
            {
                CloseHandle(this.processHandle);
                this.processHandle = IntPtr.Zero;
            }

            this.handlePid = 0;
            this.nameToArtId = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            this.nameToArtLookupFinished = false;
            this.cachedPanel = IntPtr.Zero;
            this.resolvedViewport = IntPtr.Zero;
            this.lastGoodGeom.Clear();
            this.smoothLabelPositions.Clear();
            this.cachedPriceLabels.Clear();
        }

        private bool IsUiElementVisible(IntPtr addr)
        {
            return this.TryReadFlags(addr, out var flags) && (flags & IsVisibleMask) != 0;
        }

        private bool TryReadFlags(IntPtr addr, out uint flags)
        {
            flags = 0;
            if (addr == IntPtr.Zero) return false;
            var buf = new byte[4];
            if (!ReadProcessMemory(this.processHandle, addr + UiElementFlagsOffset, buf, (uint)buf.Length, out _)) return false;
            flags = BitConverter.ToUInt32(buf, 0);
            return true;
        }

        private IntPtr ReadPtr(IntPtr addr)
        {
            if (addr == IntPtr.Zero) return IntPtr.Zero;
            var buf = new byte[8];
            if (!ReadProcessMemory(this.processHandle, addr, buf, (uint)buf.Length, out _)) return IntPtr.Zero;
            return (IntPtr)BitConverter.ToInt64(buf, 0);
        }

        private bool TryReadStdVector(IntPtr addr, out IntPtr first, out IntPtr last)
        {
            first = IntPtr.Zero;
            last = IntPtr.Zero;
            var buf = new byte[16];
            if (!ReadProcessMemory(this.processHandle, addr, buf, (uint)buf.Length, out _)) return false;
            first = (IntPtr)BitConverter.ToInt64(buf, 0);
            last = (IntPtr)BitConverter.ToInt64(buf, 8);
            if (first == IntPtr.Zero) return false;
            ulong f = (ulong)(long)first;
            if (f < 0x10000 || f > 0x7FFFFFFFFFFFul) return false;
            if ((long)last < (long)first) return false;
            return true;
        }

        private string ReadStdWString(IntPtr addr)
        {
            var buf = new byte[0x20];
            if (!ReadProcessMemory(this.processHandle, addr, buf, (uint)buf.Length, out _)) return string.Empty;

            int len = BitConverter.ToInt32(buf, 0x10);
            if (len <= 0 || len > 256) return string.Empty;
            int cap = BitConverter.ToInt32(buf, 0x18);
            if (cap < len) return string.Empty;

            if (cap < 8)
            {
                int byteLen = Math.Min(len * 2, 16);
                return Encoding.Unicode.GetString(buf, 0, byteLen);
            }

            long ptr = BitConverter.ToInt64(buf, 0);
            if (ptr < 0x10000 || ptr > 0x7FFFFFFFFFFF) return string.Empty;
            var outBuf = new byte[len * 2];
            if (!ReadProcessMemory(this.processHandle, (IntPtr)ptr, outBuf, (uint)outBuf.Length, out _)) return string.Empty;
            return Encoding.Unicode.GetString(outBuf);
        }

        // ── P/Invoke ─────────────────────────────────────────────────────

        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint dwSize, out int lpNumberOfBytesRead);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private readonly record struct Recipe(int Count, string Name, IntPtr RowAddr, IntPtr LabelAddr, string MetaId, string DdsArt);

        private struct PriceLabelDraw
        {
            public long RowKey;
            public Vector2 Pos;
            public string IconFile;
            public float IconWidth;
            public float IconHeight;
            public string ValueText;
            public uint TextColor;
            public float FontSize;
            public float TextWidth;
            public bool ShowBackground;
            public Vector2 BgMin;
            public Vector2 BgMax;
            public uint BgColor;
            public float BgRounding;
        }
    }
}
