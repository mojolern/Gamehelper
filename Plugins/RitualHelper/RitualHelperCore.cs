// <copyright file="RitualHelperCore.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace RitualHelper
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Numerics;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using GameHelper;
    using GameHelper.Localization;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using GameOffsets.Objects.States.InGameState;
    using GameOffsets.Objects.UiElement;
    using GameOffsets.Natives;
    using ImGuiNET;
    using Newtonsoft.Json;

    public sealed class RitualHelperCore : PCore<RitualHelperSettings>
    {
        private readonly Dictionary<string, List<string>> debugInventories = new();
        private readonly HashSet<string> alertedItemsThisSession = new(StringComparer.OrdinalIgnoreCase);
        private readonly CurrencyIconLoader currencyIcons = new();

        private MethodInfo readUiOffsetMethod;
        private MethodInfo readStdVectorMethod;
        private MethodInfo readIntPtrMethod;
        private object handleObj;
        private bool wasRitualWindowOpen;
        private string lastClipboardText = string.Empty;
        private bool clipboardChangedThisFrame;
        private Dictionary<string, string> customNamesCache;
        private int selectedLeagueIndex = -1;
        private bool iconsReloadPending;
        private int? pendingSoundPlayback;
        private readonly Dictionary<string, (string Name, string BaseType, List<string> Mods)> itemClipboardHints =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (string Name, string BaseType, List<string> Mods)> clipboardHintsByDisplayName =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<PriceLabelDraw> cachedPriceLabels = new();
        private readonly Dictionary<string, double> sessionStablePriceChaos = new(StringComparer.OrdinalIgnoreCase);
        private object? uiParentsObj;
        private DateTime nextPriceRecomputeUtc = DateTime.MinValue;

        private string SettingPathname => Path.Join(this.DllDirectory, "config", "settings.txt");

        public override void DrawSettings()
        {
            try
            {
            LeagueProvider.EnsureLoaded();

            if (ImGui.BeginTabBar("##RitualHelperTabs"))
            {
                if (ImGui.BeginTabItem(this.L("General", "Allgemein")))
                {
                    ImGui.Checkbox(this.L("Show Prices", "Preise anzeigen"), ref this.Settings.ShowOverlay);

                    ImGui.Separator();
                    ImGui.Text(this.L("Display", "Anzeige"));
                    if (ImGui.RadioButton(this.L("Chaos (c)", "Chaos (c)"), this.Settings.DisplayCurrency == 2))
                        this.Settings.DisplayCurrency = 2;
                    ImGui.SameLine();
                    if (ImGui.RadioButton(this.L("Divine (D)", "Divine (D)"), this.Settings.DisplayCurrency == 0))
                        this.Settings.DisplayCurrency = 0;
                    ImGui.SameLine();
                    if (ImGui.RadioButton(this.L("Exalted (Ex)", "Exalted (Ex)"), this.Settings.DisplayCurrency == 1))
                        this.Settings.DisplayCurrency = 1;

                    ImGui.SliderFloat(this.L("Font Scale", "Schriftgroesse"), ref this.Settings.PriceFontScale, 0.1f, 2.0f);
                    ImGui.SliderFloat(this.L("Offset X", "Versatz X"), ref this.Settings.PriceOffsetX, -50f, 50f);
                    ImGui.SliderFloat(this.L("Offset Y", "Versatz Y"), ref this.Settings.PriceOffsetY, -50f, 50f);
                    ImGui.ColorEdit4(this.L("Text Color", "Textfarbe"), ref this.Settings.PriceTextColor);
                    ImGui.TextWrapped(this.L(
                        "Prices are shown as value + currency orb icon (no background box).",
                        "Preise werden als Wert + Waehrungs-Icon angezeigt (ohne Hintergrundbox)."));

                    ImGui.Separator();
                    ImGui.Text(this.L("Alert Sound", "Alarmton"));
                    ImGui.Checkbox(this.L("Enable alert", "Alarm aktivieren"), ref this.Settings.PlayValueAlert);
                    ImGui.SliderFloat(this.L("Alert from (Divine)", "Alarm ab (Divine)"), ref this.Settings.AlertMinDivine, 0.1f, 50f, "%.3f");

                    ImGui.Text(this.L("Sound", "Ton"));
                    if (ImGui.RadioButton(this.L("Asterisk", "Asterisk"), this.Settings.AlertSound == 0))
                        this.Settings.AlertSound = 0;
                    ImGui.SameLine();
                    if (ImGui.RadioButton(this.L("Exclamation", "Ausrufezeichen"), this.Settings.AlertSound == 1))
                        this.Settings.AlertSound = 1;
                    ImGui.SameLine();
                    if (ImGui.RadioButton(this.L("Hand", "Hand"), this.Settings.AlertSound == 2))
                        this.Settings.AlertSound = 2;
                    if (ImGui.RadioButton(this.L("Question", "Frage"), this.Settings.AlertSound == 3))
                        this.Settings.AlertSound = 3;
                    ImGui.SameLine();
                    if (ImGui.RadioButton(this.L("Beep", "Piepton"), this.Settings.AlertSound == 4))
                        this.Settings.AlertSound = 4;

                    if (ImGui.Button(this.L("Test Sound", "Ton testen")))
                        this.pendingSoundPlayback = this.Settings.AlertSound;

                    ImGui.Separator();
                    ImGui.TextWrapped(this.L(
                        "POE2 should be set to English for item name matching. " +
                        "For uniques, copy the item once (Ctrl+C) while hovering — prices then stay stable for that slot. " +
                        "Prices come from poe2scout + poe.ninja; items missing there show no label (trade tools may differ).",
                        "POE2 sollte auf Englisch eingestellt sein. " +
                        "Bei Uniques einmal mit Maus darueber und Ctrl+C kopieren — der Preis bleibt dann fuer den Slot stabil. " +
                        "Preise von poe2scout + poe.ninja; fehlende Items ohne Label (Trade-Tools koennen abweichen)."));
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(this.L("Data Source", "Datenquelle")))
                {
                    this.DrawPriceSourceSelector();
                    this.DrawLeagueSelector();

                    ImGui.SliderInt(this.L("Refresh interval (min)", "Aktualisierungsintervall (Min.)"), ref this.Settings.RefreshIntervalMin, 1, 120);
                    if (ImGui.Button(this.L("Refresh Prices Now", "Preise jetzt aktualisieren")))
                    {
                        PoeNinjaPriceFetcher.Configure(this.Settings.PriceSource, this.Settings.League, this.Settings.RefreshIntervalMin);
                        PoeNinjaPriceFetcher.ForceRefresh(this.DllDirectory);
                    }

                    ImGui.SameLine();
                    if (PoeNinjaPriceFetcher.IsFetching || LeagueProvider.IsLoading)
                    {
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.2f, 1f), this.L("Loading...", "Lade..."));
                    }
                    else if (PoeNinjaPriceFetcher.LastFetchUtc > DateTime.MinValue)
                    {
                        var mins = Math.Max(0, (int)(DateTime.UtcNow - PoeNinjaPriceFetcher.LastFetchUtc).TotalMinutes);
                        ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1f),
                            this.L($"{PoeNinjaPriceFetcher.LoadedItemCount} items | {mins} min ago",
                                   $"{PoeNinjaPriceFetcher.LoadedItemCount} Items | vor {mins} Min."));
                    }

                    ImGui.TextWrapped(this.L(
                        "Prices are estimates from poe2scout / poe.ninja (not live trade). Uniques use the higher of scout and ninja. Refresh after league economy shifts.",
                        "Preise sind Schaetzungen von poe2scout / poe.ninja (kein Live-Trade). Uniques nutzen den hoeheren Wert aus Scout und Ninja. Nach Economy-Aenderungen aktualisieren."));

                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(this.L("Advanced", "Erweitert")))
                {
                    ImGui.Checkbox(this.L("Debug Mode (Show All Inventories)", "Debug-Modus (alle Inventare anzeigen)"), ref this.Settings.DebugMode);
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            ImGui.Separator();
            ImGui.TextWrapped(this.L(
                "This plugin reads items from the Ritual shop (Favours window) and displays prices on each item.",
                "Dieses Plugin liest Items aus dem Ritual-Shop (Favours-Fenster) und zeigt Preise an jedem Item an."));
            }
            finally
            {
                this.FlushPendingSound();
            }
        }

        private void DrawLeagueSelector()
        {
            var leagues = LeagueProvider.Leagues;
            if (leagues.Count == 0)
            {
                ImGui.InputText(this.L("League", "Liga"), ref this.Settings.League, 64);
                return;
            }

            if (this.selectedLeagueIndex < 0 || this.selectedLeagueIndex >= leagues.Count ||
                !string.Equals(leagues[this.selectedLeagueIndex], this.Settings.League, StringComparison.OrdinalIgnoreCase))
            {
                this.selectedLeagueIndex = 0;
                for (var i = 0; i < leagues.Count; i++)
                {
                    if (string.Equals(leagues[i], this.Settings.League, StringComparison.OrdinalIgnoreCase))
                    {
                        this.selectedLeagueIndex = i;
                        break;
                    }
                }
            }

            ImGui.SetNextItemWidth(260f);
            if (ImGui.BeginCombo(this.L("League", "Liga"), leagues[this.selectedLeagueIndex]))
            {
                for (var i = 0; i < leagues.Count; i++)
                {
                    if (ImGui.Selectable(leagues[i], i == this.selectedLeagueIndex))
                    {
                        this.selectedLeagueIndex = i;
                        this.Settings.League = leagues[i];
                    }
                }

                ImGui.EndCombo();
            }
        }

        private string L(string english, string german) => OverlayLocalization.L(english, german);

        private void DrawPriceSourceSelector()
        {
            ImGui.Text(this.L("Price source:", "Preisquelle:"));
            var previousSource = this.Settings.PriceSource;

            if (ImGui.RadioButton("poe.ninja", this.Settings.PriceSource == PoeNinjaPriceFetcher.SourcePoeNinja))
            {
                this.Settings.PriceSource = PoeNinjaPriceFetcher.SourcePoeNinja;
            }

            ImGui.SameLine();
            if (ImGui.RadioButton("poe2scout", this.Settings.PriceSource == PoeNinjaPriceFetcher.SourcePoe2Scout))
            {
                this.Settings.PriceSource = PoeNinjaPriceFetcher.SourcePoe2Scout;
            }

            if (this.Settings.PriceSource != previousSource)
            {
                PoeNinjaPriceFetcher.Configure(
                    this.Settings.PriceSource,
                    this.Settings.League,
                    this.Settings.RefreshIntervalMin);
                PoeNinjaPriceFetcher.ForceRefresh(this.DllDirectory, ignoreCooldown: true);
            }
        }

        private void NormalizePriceSourceSetting()
        {
            if (this.Settings.PriceSource == PoeNinjaPriceFetcher.SourcePoeNinja ||
                this.Settings.PriceSource == PoeNinjaPriceFetcher.SourcePoe2Scout)
            {
                return;
            }

            this.Settings.PriceSource = PoeNinjaPriceFetcher.SourcePoe2Scout;
        }

        public override void DrawUI()
        {
            try
            {
            if (!this.Settings.ShowOverlay && !this.Settings.DebugMode) return;
            if (Core.States.GameCurrentState != GameStateTypes.InGameState) return;

            PoeNinjaPriceFetcher.Configure(this.Settings.PriceSource, this.Settings.League, this.Settings.RefreshIntervalMin);
            PoeNinjaPriceFetcher.RefreshIfNeeded();

            if (this.iconsReloadPending || !this.currencyIcons.TryGet("divine.png", out _, out _, out _))
            {
                this.currencyIcons.Reload();
                this.iconsReloadPending = false;
            }

            try
            {
                var currentClipboard = string.Empty;
                try { currentClipboard = ImGui.GetClipboardText() ?? string.Empty; } catch { }

                if (currentClipboard.StartsWith("Item Class:", StringComparison.Ordinal) &&
                    !string.Equals(currentClipboard, this.lastClipboardText, StringComparison.Ordinal))
                {
                    this.lastClipboardText = currentClipboard;
                    this.clipboardChangedThisFrame = true;
                }

                if (this.handleObj == null)
                {
                    PoeNinjaPriceFetcher.Initialize(this.DllDirectory);
                    this.currencyIcons.Initialize(this.DllDirectory);
                    this.iconsReloadPending = true;

                    var handleProp = typeof(GameProcess).GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic);
                    this.handleObj = handleProp?.GetValue(Core.Process);
                    if (this.handleObj == null) return;

                    var methods = this.handleObj.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    var readMemMethod = System.Linq.Enumerable.First(methods, m => m.Name == "ReadMemory" && m.IsGenericMethod && m.GetParameters().Length == 1);
                    var readVectorMethod = System.Linq.Enumerable.First(methods, m => m.Name == "ReadStdVector" && m.IsGenericMethod);
                    this.readUiOffsetMethod = readMemMethod.MakeGenericMethod(typeof(UiElementBaseOffset));
                    this.readStdVectorMethod = readVectorMethod.MakeGenericMethod(typeof(IntPtr));
                    this.readIntPtrMethod = readMemMethod.MakeGenericMethod(typeof(IntPtr));
                }

                var gameUiAddr = Core.States.InGameStateObject.GameUi.Address;
                var rootOffsetObj = this.readUiOffsetMethod.Invoke(this.handleObj, new object[] { gameUiAddr });
                var mainChildren = (IntPtr[])this.readStdVectorMethod.Invoke(this.handleObj, new object[] { ((UiElementBaseOffset)rootOffsetObj).ChildrensPtr });

                if (mainChildren.Length > 76)
                {
                    var child76Offset = (UiElementBaseOffset)this.readUiOffsetMethod.Invoke(this.handleObj, new object[] { mainChildren[76] });
                    var child76Children = (IntPtr[])this.readStdVectorMethod.Invoke(this.handleObj, new object[] { child76Offset.ChildrensPtr });

                    if (child76Children.Length > 13)
                    {
                        var ritualWindowOffset = (UiElementBaseOffset)this.readUiOffsetMethod.Invoke(this.handleObj, new object[] { child76Children[13] });
                        var ritualWindowOpen = UiElementBaseFuncs.IsVisibleChecker(ritualWindowOffset.Flags);

                        if (!ritualWindowOpen && this.wasRitualWindowOpen)
                        {
                            this.alertedItemsThisSession.Clear();
                            this.itemClipboardHints.Clear();
                            this.clipboardHintsByDisplayName.Clear();
                            this.sessionStablePriceChaos.Clear();
                            this.cachedPriceLabels.Clear();
                            this.nextPriceRecomputeUtc = DateTime.MinValue;
                        }

                        this.wasRitualWindowOpen = ritualWindowOpen;

                        if (ritualWindowOpen && this.Settings.ShowOverlay)
                            this.DrawRitualPrices(ritualWindowOffset, currentClipboard);
                    }
                }
            }
            catch { }

            if (this.Settings.DebugMode)
            {
                this.UpdateDebugInventories();
                this.DrawDebugOverlay();
            }
            }
            finally
            {
                this.FlushPendingSound();
            }
        }

        private void DrawRitualPrices(UiElementBaseOffset ritualWindowOffset, string currentClipboard)
        {
            var fgDraw = ImGui.GetForegroundDrawList();
            var font = ImGui.GetFont();
            const uint shadowColor = 0xCC000000;

            var now = DateTime.UtcNow;
            var recomputeIntervalMs = Core.IsSettingsMenuOpen ? 200 : 120;
            if (now < this.nextPriceRecomputeUtc && this.cachedPriceLabels.Count > 0)
            {
                this.DrawPriceLabels(fgDraw, font, shadowColor, this.cachedPriceLabels);
                return;
            }

            this.nextPriceRecomputeUtc = now.AddMilliseconds(recomputeIntervalMs);

            var itemUiElements = (IntPtr[])this.readStdVectorMethod.Invoke(this.handleObj, new object[] { ritualWindowOffset.ChildrensPtr });
            var mousePos = ImGui.GetMousePos();
            var priceLabels = this.cachedPriceLabels;
            priceLabels.Clear();

            this.uiParentsObj ??= PluginUiElementReflection.CreateParents();
            var parentsObj = this.uiParentsObj;

            for (var i = 0; i < itemUiElements.Length; i++)
            {
                var itemUiOffset = (UiElementBaseOffset)this.readUiOffsetMethod.Invoke(this.handleObj, new object[] { itemUiElements[i] });
                if (!UiElementBaseFuncs.IsVisibleChecker(itemUiOffset.Flags)) continue;

                object? uiElementObj = parentsObj == null
                    ? null
                    : PluginUiElementReflection.CreateUiElement(itemUiElements[i], parentsObj);

                var ptr = (IntPtr)this.readIntPtrMethod.Invoke(this.handleObj, new object[] { itemUiElements[i] + 0x4F8 });
                var itemName = $"Item {i}";
                var internalNameOnly = string.Empty;
                var fullItemPath = string.Empty;
                var scoutText = string.Empty;
                List<string> memoryMods = null;

                if (ptr != IntPtr.Zero)
                {
                    var itemInstance = ItemModHelper.ReadFreshItem(ptr);
                    if (itemInstance != null && !string.IsNullOrEmpty(itemInstance.Path))
                    {
                        fullItemPath = itemInstance.Path;
                        var parts = itemInstance.Path.Split('/');
                        if (parts.Length > 0)
                        {
                            internalNameOnly = parts[^1];
                            itemName = this.GetPrettyName(internalNameOnly, out _);
                        }

                        memoryMods = ItemModHelper.GetModLines(itemInstance);
                        if (memoryMods.Count > 0)
                        {
                            var baseTypeFromPath = this.InferBaseTypeFromMetadataPath(fullItemPath);
                            scoutText = this.BuildScoutText(itemName, baseTypeFromPath);
                        }
                    }
                }

                if (uiElementObj == null) continue;

                var screenPos = (Vector2)PluginUiElementReflection.UiElementPositionProperty!.GetValue(uiElementObj)!;
                var size = (Vector2)PluginUiElementReflection.UiElementSizeProperty!.GetValue(uiElementObj)!;
                var mouseIsHovering = mousePos.X >= screenPos.X && mousePos.X <= screenPos.X + size.X &&
                                      mousePos.Y >= screenPos.Y && mousePos.Y <= screenPos.Y + size.Y;

                var useClipboardForItem = mouseIsHovering &&
                    currentClipboard.StartsWith("Item Class:", StringComparison.Ordinal) &&
                    this.ShouldUseClipboardForItem(
                        currentClipboard,
                        internalNameOnly,
                        fullItemPath,
                        itemName,
                        memoryMods);

                if (useClipboardForItem && !string.IsNullOrEmpty(internalNameOnly))
                {
                    this.ApplyClipboardHintToItem(currentClipboard, internalNameOnly, ref itemName, ref scoutText);
                }
                else if (this.TryGetCachedItemHint(internalNameOnly, out var cachedHint))
                {
                    itemName = cachedHint.Name;
                    scoutText = this.BuildScoutText(cachedHint.Name, cachedHint.BaseType);
                }

                List<string> clipboardMods = null;
                if (useClipboardForItem)
                {
                    clipboardMods = ParseClipboardMods(currentClipboard);
                }
                else if (this.TryGetCachedItemHint(internalNameOnly, out var hintForMods))
                {
                    clipboardMods = hintForMods.Mods;
                }
                var mods = ItemModHelper.MergeModLines(memoryMods, clipboardMods);

                itemName = this.ResolveItemDisplayName(itemName, internalNameOnly);
                var priceInfo = PoeNinjaPriceFetcher.GetPrice(itemName, mods, internalNameOnly, fullItemPath, scoutText);
                if (priceInfo == null) continue;

                var priceChaos = this.StabilizeSessionPrice(
                    internalNameOnly,
                    priceInfo.PriceChaos,
                    useClipboardForItem);

                var (displayValue, displayCurrency) = PoeNinjaPriceFetcher.GetDisplayPrice(
                    new PoeNinjaPrice { PriceChaos = priceChaos },
                    this.Settings.DisplayCurrency);
                var divineValue = priceChaos / Math.Max(PoeNinjaPriceFetcher.GetChaosPerDivine(), 1.0);
                if (this.Settings.PlayValueAlert && divineValue >= this.Settings.AlertMinDivine)
                {
                    var alertKey = string.IsNullOrEmpty(internalNameOnly) ? itemName : internalNameOnly;
                    if (this.alertedItemsThisSession.Add(alertKey))
                        this.pendingSoundPlayback = this.Settings.AlertSound;
                }

                var fontSize = ImGui.GetFontSize() * this.Settings.PriceFontScale;
                var valueText = displayCurrency switch
                {
                    "divine" => displayValue.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                    "chaos" => displayValue.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                    _ => displayValue.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                };
                var textWidth = ImGui.CalcTextSize(valueText).X * this.Settings.PriceFontScale;
                var iconFile = displayCurrency switch
                {
                    "divine" => "divine.png",
                    "chaos" => "chaos.png",
                    _ => "exalted.png",
                };
                var iconWidth = fontSize;
                if (this.currencyIcons.TryGet(iconFile, out _, out var iconW, out var iconH) && iconH > 0)
                    iconWidth = fontSize * iconW / (float)iconH;

                var pos = new Vector2(
                    screenPos.X + this.Settings.PriceOffsetX,
                    screenPos.Y + size.Y - fontSize + this.Settings.PriceOffsetY);

                priceLabels.Add(new PriceLabelDraw
                {
                    Pos = pos,
                    IconFile = iconFile,
                    IconWidth = iconWidth,
                    IconHeight = fontSize,
                    TextWidth = textWidth,
                    ValueText = valueText,
                    TextColor = ImGui.ColorConvertFloat4ToU32(this.Settings.PriceTextColor),
                    FontSize = fontSize,
                });
            }

            this.DrawPriceLabels(fgDraw, font, shadowColor, priceLabels);
            this.clipboardChangedThisFrame = false;
        }

        private void DrawPriceLabels(ImDrawListPtr fgDraw, ImFontPtr font, uint shadowColor, List<PriceLabelDraw> priceLabels)
        {
            foreach (var label in priceLabels)
            {
                var textPos = label.Pos;
                fgDraw.AddText(font, label.FontSize, textPos + new Vector2(1f, 1f), shadowColor, label.ValueText);
                fgDraw.AddText(font, label.FontSize, textPos, label.TextColor, label.ValueText);

                if (this.currencyIcons.TryGet(label.IconFile, out var texPtr, out _, out _))
                {
                    var iconPos = label.Pos + new Vector2(label.TextWidth + 3f, 0f);
                    var iconMax = iconPos + new Vector2(label.IconWidth, label.IconHeight);
                    fgDraw.AddImage(texPtr, iconPos, iconMax);
                }
            }
        }

        private void FlushPendingSound()
        {
            if (!this.pendingSoundPlayback.HasValue) return;

            var soundType = this.pendingSoundPlayback.Value;
            this.pendingSoundPlayback = null;
            AlertSoundPlayer.Play(soundType);
        }

        private string ParseClipboardForItemName(string text)
        {
            if (string.IsNullOrEmpty(text) || !text.StartsWith("Item Class:", StringComparison.Ordinal)) return null;
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 2 ? lines[2].Trim() : null;
        }

        private string ParseClipboardBaseType(string text)
        {
            if (string.IsNullOrEmpty(text) || !text.StartsWith("Item Class:", StringComparison.Ordinal)) return null;
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length <= 3) return null;

            var baseType = lines[3].Trim();
            if (baseType.StartsWith("--------", StringComparison.Ordinal)) return null;
            return baseType;
        }

        private string BuildScoutText(string itemName, string baseType)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return string.Empty;
            if (string.IsNullOrWhiteSpace(baseType)) return itemName.Trim();
            return $"{itemName.Trim()} {baseType.Trim()}";
        }

        private bool ShouldUseClipboardForItem(
            string clipboard,
            string internalNameOnly,
            string fullItemPath,
            string memoryDisplayName,
            IReadOnlyList<string> memoryMods)
        {
            if (this.ClipboardBelongsToItem(clipboard, internalNameOnly, fullItemPath, memoryDisplayName, memoryMods))
            {
                return true;
            }

            // Fresh copy (e.g. Ctrl+C / overlay hotkey) while hovering this slot — bind once to this item.
            return this.clipboardChangedThisFrame &&
                   (memoryMods == null || memoryMods.Count == 0);
        }

        private bool ClipboardBelongsToItem(
            string clipboard,
            string internalNameOnly,
            string fullItemPath,
            string memoryDisplayName,
            IReadOnlyList<string> memoryMods)
        {
            if (string.IsNullOrWhiteSpace(clipboard) ||
                !clipboard.StartsWith("Item Class:", StringComparison.Ordinal))
            {
                return false;
            }

            var parsedName = this.ParseClipboardForItemName(clipboard);
            if (string.IsNullOrWhiteSpace(parsedName))
            {
                return false;
            }

            var parsedKey = NormalizeItemLookupKey(parsedName);
            foreach (var candidate in this.BuildDisplayNameCandidates(memoryDisplayName, internalNameOnly))
            {
                if (string.Equals(candidate, parsedName, StringComparison.OrdinalIgnoreCase) ||
                    NormalizeItemLookupKey(candidate) == parsedKey)
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(fullItemPath))
            {
                foreach (var segment in fullItemPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (NormalizeItemLookupKey(segment) == parsedKey)
                    {
                        return true;
                    }

                    if (PoeNinjaPriceFetcher.TryResolveDisplayName(segment, out var mapped) &&
                        NormalizeItemLookupKey(mapped) == parsedKey)
                    {
                        return true;
                    }
                }
            }

            var clipboardMods = ParseClipboardMods(clipboard);
            if (memoryMods != null && memoryMods.Count > 0 && clipboardMods.Count > 0)
            {
                return ScoreModOverlap(memoryMods, clipboardMods) >= 2;
            }

            return false;
        }

        private static int ScoreModOverlap(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            var score = 0;
            foreach (var leftMod in left)
            {
                if (string.IsNullOrWhiteSpace(leftMod))
                {
                    continue;
                }

                var leftNorm = NormalizeModForMatch(leftMod);
                if (leftNorm.Length < 4)
                {
                    continue;
                }

                foreach (var rightMod in right)
                {
                    if (string.IsNullOrWhiteSpace(rightMod))
                    {
                        continue;
                    }

                    var rightNorm = NormalizeModForMatch(rightMod);
                    if (rightNorm == leftNorm ||
                        rightNorm.Contains(leftNorm, StringComparison.Ordinal) ||
                        leftNorm.Contains(rightNorm, StringComparison.Ordinal))
                    {
                        score++;
                        break;
                    }
                }
            }

            return score;
        }

        private static string NormalizeModForMatch(string mod) =>
            Regex.Replace(mod.ToLowerInvariant(), @"\s+", " ").Trim();

        private double StabilizeSessionPrice(string internalNameOnly, double priceChaos, bool useClipboardForItem)
        {
            if (string.IsNullOrWhiteSpace(internalNameOnly) || priceChaos <= 0)
            {
                return priceChaos;
            }

            if (useClipboardForItem)
            {
                this.sessionStablePriceChaos[internalNameOnly] = priceChaos;
                return priceChaos;
            }

            if (this.sessionStablePriceChaos.TryGetValue(internalNameOnly, out var stable))
            {
                if (priceChaos < stable)
                {
                    return stable;
                }

                this.sessionStablePriceChaos[internalNameOnly] = priceChaos;
                return priceChaos;
            }

            if (this.TryGetCachedItemHint(internalNameOnly, out _))
            {
                this.sessionStablePriceChaos[internalNameOnly] = priceChaos;
            }

            return priceChaos;
        }

        private void RegisterClipboardHint(string clipboard)
        {
            var parsedName = this.ParseClipboardForItemName(clipboard);
            if (string.IsNullOrWhiteSpace(parsedName))
            {
                return;
            }

            var parsedBaseType = this.ParseClipboardBaseType(clipboard) ?? string.Empty;
            var parsedMods = ParseClipboardMods(clipboard);
            var hint = (parsedName, parsedBaseType, parsedMods);
            this.clipboardHintsByDisplayName[parsedName] = hint;
            this.clipboardHintsByDisplayName[NormalizeItemLookupKey(parsedName)] = hint;

            var scoutText = this.BuildScoutText(parsedName, parsedBaseType);
            if (!string.IsNullOrWhiteSpace(scoutText))
            {
                this.clipboardHintsByDisplayName[scoutText] = hint;
                this.clipboardHintsByDisplayName[NormalizeItemLookupKey(scoutText)] = hint;
            }
        }

        private void ApplyClipboardHintToItem(
            string clipboard,
            string internalNameOnly,
            ref string itemName,
            ref string scoutText)
        {
            var parsedName = this.ParseClipboardForItemName(clipboard);
            var parsedBaseType = this.ParseClipboardBaseType(clipboard);
            var parsedMods = ParseClipboardMods(clipboard);
            if (string.IsNullOrWhiteSpace(parsedName))
            {
                return;
            }

            this.UpdateCustomName(internalNameOnly, parsedName);
            var hint = (parsedName, parsedBaseType ?? string.Empty, parsedMods);
            this.itemClipboardHints[internalNameOnly] = hint;
            this.RegisterClipboardHint(clipboard);
            itemName = parsedName;
            scoutText = this.BuildScoutText(parsedName, parsedBaseType);
        }

        private bool TryGetCachedItemHint(
            string internalNameOnly,
            out (string Name, string BaseType, List<string> Mods) hint)
        {
            if (!string.IsNullOrEmpty(internalNameOnly) &&
                this.itemClipboardHints.TryGetValue(internalNameOnly, out hint))
            {
                return true;
            }

            hint = default;
            return false;
        }

        private IEnumerable<string> BuildDisplayNameCandidates(string currentDisplayName, string internalNameOnly)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void track(string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    seen.Add(value.Trim());
                }
            }

            track(currentDisplayName);
            if (!string.IsNullOrWhiteSpace(internalNameOnly))
            {
                track(this.GetPrettyName(internalNameOnly, out _));
            }

            if (PoeNinjaPriceFetcher.TryResolveDisplayName(internalNameOnly, out var mapped))
            {
                track(mapped);
            }

            foreach (var value in seen)
            {
                yield return value;
                var key = NormalizeItemLookupKey(value);
                if (!string.IsNullOrEmpty(key))
                {
                    yield return key;
                }
            }
        }

        private static string NormalizeItemLookupKey(string value) =>
            Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);

        private string InferBaseTypeFromMetadataPath(string metadataPath)
        {
            if (string.IsNullOrWhiteSpace(metadataPath))
            {
                return string.Empty;
            }

            var parts = metadataPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return string.Empty;
            }

            var parent = parts[^2];
            var spaced = Regex.Replace(parent, "([a-z])([A-Z])", "$1 $2");
            spaced = Regex.Replace(spaced, "([A-Z]+)([A-Z][a-z])", "$1 $2");
            return spaced.Trim();
        }

        private static List<string> ParseClipboardMods(string text)
        {
            var mods = new List<string>();
            if (string.IsNullOrEmpty(text) || !text.StartsWith("Item Class:", StringComparison.Ordinal)) return mods;

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var inModBlock = false;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("--------", StringComparison.Ordinal))
                {
                    inModBlock = !inModBlock;
                    continue;
                }

                if (!inModBlock) continue;
                if (line.StartsWith("Requirements:", StringComparison.OrdinalIgnoreCase)) break;
                if (line.StartsWith("Item Level:", StringComparison.OrdinalIgnoreCase)) break;
                if (line.StartsWith("Quality:", StringComparison.OrdinalIgnoreCase)) break;
                if (line.StartsWith("Sockets:", StringComparison.OrdinalIgnoreCase)) break;
                if (line.StartsWith("Corrupted", StringComparison.OrdinalIgnoreCase)) continue;
                mods.Add(line);
            }

            return mods;
        }

        private void UpdateCustomName(string internalName, string newName)
        {
            this.EnsureNameCache();
            TrySplitRuneforgeSuffix(internalName, out var baseInternalName, out _);
            if (!this.customNamesCache.TryGetValue(baseInternalName, out var existing) ||
                !string.Equals(existing, newName, StringComparison.Ordinal))
            {
                this.customNamesCache[baseInternalName] = newName;
                this.SaveNameCache();
            }
        }

        private void EnsureNameCache()
        {
            if (this.customNamesCache != null) return;

            var dictionaryPath = Path.Combine(this.DllDirectory, "item_names.json");
            if (File.Exists(dictionaryPath))
            {
                try
                {
                    this.customNamesCache = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(dictionaryPath))
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    return;
                }
                catch { }
            }

            this.customNamesCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private void SaveNameCache()
        {
            try
            {
                var dictionaryPath = Path.Combine(this.DllDirectory, "item_names.json");
                File.WriteAllText(dictionaryPath, JsonConvert.SerializeObject(this.customNamesCache, Formatting.Indented));
            }
            catch { }
        }

        private static bool TrySplitRuneforgeSuffix(string internalName, out string baseInternalName, out string suffix)
        {
            baseInternalName = internalName;
            suffix = string.Empty;

            if (internalName.EndsWith("Runeforged", StringComparison.OrdinalIgnoreCase))
            {
                baseInternalName = internalName[..^"Runeforged".Length];
                suffix = "Runeforged";
                return true;
            }

            if (internalName.EndsWith("Runemastered", StringComparison.OrdinalIgnoreCase))
            {
                baseInternalName = internalName[..^"Runemastered".Length];
                suffix = "Runemastered";
                return true;
            }

            if (internalName.EndsWith("reforged", StringComparison.OrdinalIgnoreCase))
            {
                baseInternalName = internalName[..^"reforged".Length];
                suffix = "Runeforged";
                return true;
            }

            return false;
        }

        private string GetPrettyName(string internalName, out bool isMapped)
        {
            isMapped = false;
            this.EnsureNameCache();
            TrySplitRuneforgeSuffix(internalName, out var baseInternalName, out var suffix);

            if (this.customNamesCache.TryGetValue(baseInternalName, out var pretty))
            {
                isMapped = true;
                return string.IsNullOrEmpty(suffix) ? pretty : $"{pretty} {suffix}";
            }

            if (PoeNinjaPriceFetcher.TryResolveDisplayName(internalName, out var scoutName))
            {
                isMapped = true;
                return scoutName;
            }

            var clean = System.Text.RegularExpressions.Regex.Replace(internalName, "([A-Z])", " $1").Trim();
            clean = clean.Replace("Four ", string.Empty, StringComparison.Ordinal);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\d+", string.Empty).Trim();
            if (PoeNinjaPriceFetcher.IsGenericLookupName(clean))
                return internalName;

            return clean;
        }

        private string ResolveItemDisplayName(string currentName, string internalNameOnly)
        {
            if (!string.IsNullOrWhiteSpace(currentName) &&
                !currentName.StartsWith("Item ", StringComparison.Ordinal) &&
                !PoeNinjaPriceFetcher.IsGenericLookupName(currentName))
            {
                return currentName;
            }

            if (!string.IsNullOrEmpty(internalNameOnly))
            {
                var mapped = this.GetPrettyName(internalNameOnly, out _);
                if (!string.IsNullOrWhiteSpace(mapped))
                    return mapped;
            }

            return currentName;
        }

        public override void OnDisable()
        {
            this.wasRitualWindowOpen = false;
            this.handleObj = null;
            this.uiParentsObj = null;
            this.cachedPriceLabels.Clear();
            this.alertedItemsThisSession.Clear();
        }

        public override void OnEnable(bool isGameOpened)
        {
            if (File.Exists(this.SettingPathname))
            {
                try
                {
                    var content = File.ReadAllText(this.SettingPathname);
                    this.Settings = JsonConvert.DeserializeObject<RitualHelperSettings>(content) ?? new RitualHelperSettings();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RitualHelper] Failed to load settings: {ex.Message}");
                    this.Settings = new RitualHelperSettings();
                }
            }

            this.NormalizePriceSourceSetting();
            LeagueProvider.EnsureLoaded();
            PoeNinjaPriceFetcher.Configure(this.Settings.PriceSource, this.Settings.League, this.Settings.RefreshIntervalMin);
            PoeNinjaPriceFetcher.Initialize(this.DllDirectory);
            this.currencyIcons.Initialize(this.DllDirectory);
            this.iconsReloadPending = true;
        }

        public override void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(this.SettingPathname) ?? string.Empty);
                File.WriteAllText(this.SettingPathname, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RitualHelper] Failed to save settings: {ex.Message}");
            }
        }

        private void UpdateDebugInventories()
        {
            try
            {
                var serverData = Core.States.InGameStateObject.CurrentAreaInstance.ServerDataObject;
                if (serverData == null || serverData.Address == IntPtr.Zero) return;

                var propInfo = typeof(ServerData).GetProperty("PlayerInventories", BindingFlags.Instance | BindingFlags.NonPublic);
                if (propInfo == null) return;

                var playerInventories = propInfo.GetValue(serverData) as Dictionary<InventoryName, IntPtr>;
                if (playerInventories == null) return;

                var handleProp = typeof(GameProcess).GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic);
                if (handleProp == null) return;
                var handleObj = handleProp.GetValue(Core.Process);
                if (handleObj == null) return;
                var handleType = handleObj.GetType();

                var readInvMethod = handleType.GetMethod("ReadMemory", BindingFlags.Instance | BindingFlags.NonPublic)?.MakeGenericMethod(typeof(InventoryStruct));
                var readVectorMethod = handleType.GetMethod("ReadStdVector", BindingFlags.Instance | BindingFlags.NonPublic)?.MakeGenericMethod(typeof(IntPtr));
                var readInvItemMethod = handleType.GetMethod("ReadMemory", BindingFlags.Instance | BindingFlags.NonPublic)?.MakeGenericMethod(typeof(InventoryItemStruct));
                if (readInvMethod == null || readVectorMethod == null || readInvItemMethod == null) return;

                this.debugInventories.Clear();
                foreach (var invKvp in playerInventories)
                {
                    if (invKvp.Value == IntPtr.Zero) continue;
                    var dbgInvObj = readInvMethod.Invoke(handleObj, new object[] { invKvp.Value });
                    if (dbgInvObj == null) continue;
                    var dbgInv = (InventoryStruct)dbgInvObj;
                    var dbgPtrArrayObj = readVectorMethod.Invoke(handleObj, new object[] { dbgInv.ItemList });
                    if (dbgPtrArrayObj is not IntPtr[] dbgArray || dbgArray.Length == 0) continue;

                    var itemNames = new List<string>();
                    foreach (var dbgInvItemPtr in dbgArray)
                    {
                        if (dbgInvItemPtr == IntPtr.Zero) continue;
                        var dbgInvItemObj = readInvItemMethod.Invoke(handleObj, new object[] { dbgInvItemPtr });
                        if (dbgInvItemObj == null) continue;
                        var dbgInvItem = (InventoryItemStruct)dbgInvItemObj;
                        if (dbgInvItem.Item == IntPtr.Zero) continue;
                        var itemInstance = Activator.CreateInstance(typeof(Item), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { dbgInvItem.Item }, null) as Item;
                        if (itemInstance != null && !string.IsNullOrEmpty(itemInstance.Path))
                            itemNames.Add(GetReadableName(itemInstance.Path));
                    }

                    if (itemNames.Count > 0)
                        this.debugInventories[$"{invKvp.Key} ({(int)invKvp.Key})"] = itemNames;
                }
            }
            catch { }
        }

        private void DrawDebugOverlay()
        {
            ImGui.SetNextWindowSize(new Vector2(400, 400), ImGuiCond.FirstUseEver);
            var flags = ImGuiWindowFlags.NoFocusOnAppearing;
            if (ImGui.Begin($"{this.L("Ritual Debug Inventories", "Ritual Debug Inventare")}##RitualHelperDebug", ref this.Settings.DebugMode, flags))
            {
                ImGui.TextWrapped(this.L(
                    "Showing all active ServerData PlayerInventories with >0 items.",
                    "Zeigt alle aktiven ServerData PlayerInventories mit >0 Items."));

                if (ImGui.Button(this.L("Dump to inventory_dump.txt", "In inventory_dump.txt speichern")))
                {
                    try
                    {
                        var dumpPath = Path.Join(this.DllDirectory, "inventory_dump.txt");
                        var lines = new List<string>();
                        foreach (var kvp in this.debugInventories)
                        {
                            lines.Add($"[{kvp.Key}] - {kvp.Value.Count} {this.L("items", "Items")}:");
                            foreach (var itemName in kvp.Value)
                                lines.Add($"    - {itemName}");
                            lines.Add(string.Empty);
                        }

                        File.WriteAllLines(dumpPath, lines);
                    }
                    catch { }
                }

                ImGui.Separator();
                foreach (var kvp in this.debugInventories)
                {
                    if (ImGui.TreeNode($"{kvp.Key} ({kvp.Value.Count} {this.L("items", "Items")})"))
                    {
                        foreach (var itemName in kvp.Value)
                            ImGui.Text($"  - {itemName}");
                        ImGui.TreePop();
                    }
                }
            }

            ImGui.End();
        }

        private static string GetReadableName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "Unknown";
            var lastSlash = path.LastIndexOf('/');
            var name = lastSlash >= 0 && lastSlash < path.Length - 1 ? path[(lastSlash + 1)..] : path;
            var result = new System.Text.StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) result.Append(' ');
                result.Append(name[i]);
            }

            return result.ToString();
        }

        private struct PriceLabelDraw
        {
            public Vector2 Pos;
            public string IconFile;
            public float IconWidth;
            public float IconHeight;
            public string ValueText;
            public uint TextColor;
            public float FontSize;
            public float TextWidth;
        }
    }
}
