// <copyright file="AutoHotKeyTriggerCore.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Numerics;
    using ClickableTransparentOverlay.Win32;
    using Coroutine;
    using GameHelper;
    using GameHelper.CoroutineEvents;
    using GameHelper.Localization;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.Utils;
    using ImGuiNET;
    using Newtonsoft.Json;
    using AutoHotKeyTrigger.ProfileManager;
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;

    /// <summary>
    ///     <see cref="AutoHotKeyTrigger" /> plugin.
    /// </summary>
    public sealed class AutoHotKeyTriggerCore : PCore<AutoHotKeyTriggerSettings>
    {
        /// <summary>Built-in profile shell; user edits persist in settings.txt.</summary>
        public const string LeagueStartDefaultProfileName = "LeagueStartDefaultProfile";

        private readonly string warningMsg = "The current condition you have put for AutoQuit is yielding true.\n" +
            "This mean you will automatically logout as soon as you leave town/hideout.\n" +
            "Please update your AutoQuit condition and/or disable it and/or fix your exile state.";

        private readonly List<(string name, Profile value)> clonesToAdd = new();
        private readonly Vector4 impTextColor = new(255, 255, 0, 255);
        private readonly Vector2 size = new(624, 380);
        private readonly List<string> keyPressInfo = new();
        private bool keyPressInfoAdded = false;
        private bool isDebugWindowHovered = false;
        private ActiveCoroutine? onAreaChange;
        private string debugMessage = string.Empty;
        private string newProfileName = string.Empty;
        private bool stopShowingAutoQuitWarning = false;

        private string SettingPathname => Path.Join(this.DllDirectory, "config", "settings.txt");
        private bool ShouldExecuteAutoQuit =>
            this.Settings.EnableAutoQuit &&
            this.Settings.AutoQuitCondition.Evaluate();

        /// <inheritdoc />
        public override void DrawSettings()
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
            ImGui.TextColored(this.impTextColor, L(
                "Do not trust Settings.txt files for Auto Hokey Trigger from sources you have not personally verified. They may contain malicious content that can compromise your computer. Using profiles with incorrectly configured rules may also lead to you being kicked from the server, or your account being banned as a result of preforming to many actions repeatedly.",
                "Vertraue Settings.txt-Dateien fuer Auto Hotkey Trigger nur aus verifizierten Quellen. Sie koennen schaedlichen Inhalt enthalten. Falsch konfigurierte Profile koennen zu Server-Kicks oder Banns fuehren."));
            ImGui.NewLine();
            ImGui.TextColored(this.impTextColor, L(
                "Again, all profiles/rules created to use a specified flask(s) should have at a minimum the FLASK_EFFECT and an appropriate number of FLASK_CHARGES defined as part of the use condition of a given profile rule. Failing to to include these two conditions as part of a rule will likely result in Auto Hotkey Trigger spamming the flask(s), resulting in a possible kick or ban from the game servers because of sending to many actions to the server. You have been warrned, use common sense when creating profiles/rulse with this tool.",
                "Flask-Regeln brauchen mindestens FLASK_EFFECT und FLASK_CHARGES. Ohne diese Bedingungen kann das Plugin Flasks spammen und zu Kick/Bann fuehren."));
            ImGui.PopTextWrapPos();
            if (ImGui.CollapsingHeader(L("Common Config", "Allgemeine Einstellungen")))
            {
                ImGui.Checkbox(L("Debug Mode", "Debug-Modus"), ref this.Settings.DebugMode);
                ImGui.SameLine();
                ImGui.Checkbox(L("Trigger rules or execute Autoquit in Hideout", "Regeln/Auto-Quit im Hideout ausfuehren"), ref this.Settings.ShouldRunInHideout);
                ImGuiHelper.ToolTip(L(
                    "The debug mode may prove to be a helpful tool in troubleshooting Auto HotKey Trigger profile rules that are not preforming as expected. It is highly suggested to create and test all new profiles/rules with the debug mode turned on.",
                    "Debug-Modus hilft beim Testen neuer Profile/Regeln. Neue Regeln immer zuerst mit Debug-Modus testen."));
                ImGuiHelper.NonContinuousEnumComboBox(L("Dump Player Status Effects", "Spieler-Status-Effekte speichern"),
                    ref this.Settings.DumpStatusEffectOnMe);
                ImGuiHelper.ToolTip(L(
                    "This hotkey will dump the current active player's buff(s), debuff(s) into a text file in the GameHelper -> Plugins -> AutoHotKeyTrigger folder.",
                    "Speichert aktuelle Buffs/Debuffs des Spielers in eine Textdatei im AutoHotKeyTrigger-Plugin-Ordner."));
                ImGuiHelper.IEnumerableComboBox(L("Profile", "Profil"), this.Settings.Profiles.Keys, ref this.Settings.CurrentProfile);
                if (string.IsNullOrEmpty(this.Settings.CurrentProfile) && this.Settings.Profiles.Count > 0)
                {
                    ImGui.TextColored(this.impTextColor, L(
                        "No active profile selected — rules will not run until you pick one above.",
                        "Kein aktives Profil gewaehlt — Regeln laufen erst, wenn oben ein Profil ausgewaehlt ist."));
                }

                if (ImGui.Button(L("Reset factory flask rules (League Start profile)", "Werk Flask-Regeln zuruecksetzen (Liga-Start-Profil)")))
                {
                    this.ResetLeagueStartDefaultProfileRules();
                }
            }

            if (ImGui.CollapsingHeader(L("Add New Profile", "Neues Profil")))
            {
                ImGui.InputText(L("Name", "Name"), ref this.newProfileName, 100);
                ImGui.SameLine();
                if (ImGui.Button(L("Add", "Hinzufuegen")))
                {
                    if (!string.IsNullOrEmpty(this.newProfileName))
                    {
                        this.Settings.Profiles.Add(this.newProfileName, new Profile());
                        this.newProfileName = string.Empty;
                    }
                }
            }

            // separate update to allow settings to draw correctly,
            // does not really hurt performance and only called
            // when the settings window is open
            DynamicCondition.UpdateState();
            if (ImGui.CollapsingHeader(L("Existing Profiles", "Vorhandene Profile")))
            {
                foreach (var (key, profile) in this.Settings.Profiles)
                {
                    var isOpened = ImGui.TreeNode($"{key} (?)");
                    ImGuiHelper.ToolTip(L(
                        "Rules (tabs) can be moved via drag and drop. They can be cloned by right click.",
                        "Regeln (Tabs) per Drag & Drop verschieben. Rechtsklick zum Klonen."));
                    if (isOpened)
                    {
                        ImGui.SameLine();
                        if (key == LeagueStartDefaultProfileName)
                        {
                            ImGui.TextDisabled(L("(built-in)", "(fest eingebaut)"));
                        }
                        else if (ImGui.SmallButton(L("Delete Profile", "Profil loeschen")))
                        {
                            this.Settings.Profiles.Remove(key);
                            if (this.Settings.CurrentProfile == key)
                            {
                                this.Settings.CurrentProfile = string.Empty;
                            }
                        }

                        ImGui.SameLine();
                        if (ImGui.SmallButton(L("Clone Profile", "Profil klonen")))
                        {
                            this.clonesToAdd.Add(($"{key}1", new(profile)));

                        }

                        profile.DrawSettings(key, this.Settings.Profiles);
                        ImGui.TreePop();
                    }
                }

                this.clonesToAdd.RemoveAll(k => this.Settings.Profiles.TryAdd(k.name, k.value) || true); // remove even if add fails.
            }

            if (ImGui.CollapsingHeader(L("Auto Quit", "Auto-Quit")))
            {
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 6);
                ImGui.Checkbox(L("Enable AutoQuit", "Auto-Quit aktivieren"), ref this.Settings.EnableAutoQuit);
                this.Settings.AutoQuitCondition.Display(true);
                ImGui.Separator();
                ImGui.Checkbox(L("Enable AutoQuit Manual Hotkey", "Manuellen Auto-Quit-Hotkey aktivieren"), ref this.Settings.EnableAutoQuitKey);
                ImGui.Text(L("Hotkey to manually quit game connection: ", "Hotkey zum manuellen Trennen der Spielverbindung: "));
                ImGui.SameLine();
                ImGuiHelper.NonContinuousEnumComboBox("##Manual Quit HotKey", ref this.Settings.AutoQuitKey);
                ImGui.PopItemWidth();
            }
        }

        /// <inheritdoc />
        public override void DrawUI()
        {
            if (this.Settings.DebugMode)
            {
                ImGui.SetNextWindowSize(size, ImGuiCond.FirstUseEver);
                if (ImGui.Begin($"AHK Debug Window", ref this.Settings.DebugMode,
                    this.isDebugWindowHovered ? ImGuiWindowFlags.MenuBar : ImGuiWindowFlags.None))
                {
                    this.isDebugWindowHovered =  ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
                    if (ImGui.BeginMenuBar())
                    {
                        if (ImGui.Button("Clear History"))
                        {
                            this.keyPressInfo.Clear();
                        }

                        ImGui.EndMenuBar();
                    }

                    for (var i = 0; i < this.keyPressInfo.Count; i++)
                    {
                        ImGui.Text($"{i}-{this.keyPressInfo[i]}");
                    }

                    if (this.keyPressInfoAdded)
                    {
                        ImGui.SetScrollHereY();
                        this.keyPressInfoAdded = false;
                    }

                    if (!string.IsNullOrEmpty(this.debugMessage))
                    {
                        ImGui.Separator();
                        ImGui.TextWrapped($"Issues: {this.debugMessage}");
                    }
                }

                ImGui.End();
            }

            this.AutoQuitWarningUi();
            if (!this.ShouldExecutePlugin())
            {
                return;
            }

            DynamicCondition.UpdateState();
            if (this.ShouldExecuteAutoQuit ||
                (this.Settings.EnableAutoQuitKey &&
                Utils.IsKeyPressedAndNotTimeout(this.Settings.AutoQuitKey, 200)))
            {
                MiscHelper.KillTCPConnectionForProcess(Core.Process.Pid, "AHK/AutoQuit");
            }

            if (Utils.IsKeyPressedAndNotTimeout(this.Settings.DumpStatusEffectOnMe, 200))
            {
                if (Core.States.InGameStateObject.CurrentAreaInstance.Player.TryGetComponent<Buffs>(out var buff))
                {
                    var data = "===========================================" + Environment.NewLine;
                    foreach (var statusEffect in buff.StatusEffects)
                    {
                        data += $"{statusEffect.Key} {statusEffect.Value}\n";
                    }

                    if (!string.IsNullOrEmpty(data))
                    {
                        File.AppendAllText(Path.Join(this.DllDirectory, "player_status_effect.txt"), data + Environment.NewLine);
                    }
                }
            }

            if (Core.GHSettings.EnableControllerMode)
            {
                // this is actually disabled in <see cref="MiscHelper.KeyUp"/> function.
                // follow is done just to provide debug msg to end users.
                this.debugMessage = "Controller mode enabled. this plugin doesn't support controllers";
                return;
            }

            if (string.IsNullOrEmpty(this.Settings.CurrentProfile))
            {
                this.debugMessage = "No Profile Selected.";
                return;
            }

            if (!this.Settings.Profiles.ContainsKey(this.Settings.CurrentProfile))
            {
                this.debugMessage = $"{this.Settings.CurrentProfile} not found.";
                return;
            }

            if (Core.States.InGameStateObject.GameUi.ChatParent.IsChatActive)
            {
                this.debugMessage = "Chat window is active, so can not drink flasks or trigger skills.";
                return;
            }

            foreach (var rule in this.Settings.Profiles[this.Settings.CurrentProfile].Rules)
            {
                rule.Execute(this.DebugLog);
            }
        }

        private void DebugLog(string logText)
        {
            if (this.Settings.DebugMode)
            {
                this.keyPressInfo.Add($"{DateTime.Now.TimeOfDay}: {logText}");
            }

            this.keyPressInfoAdded = true;
        }

        /// <inheritdoc />
        public override void OnDisable()
        {
            this.onAreaChange?.Cancel();
            this.onAreaChange = null;
        }

        /// <inheritdoc />
        public override void OnEnable(bool isGameOpened)
        {
            var jsonData2 = File.ReadAllText(this.DllDirectory + @"/StatusEffectGroup.json");
            JsonDataHelper.StatusEffectGroups = JsonConvert.DeserializeObject<
                Dictionary<string, List<string>>>(jsonData2)
                ?? new Dictionary<string, List<string>>();

            if (File.Exists(this.SettingPathname))
            {
                var content = File.ReadAllText(this.SettingPathname);
                this.Settings = JsonConvert.DeserializeObject<AutoHotKeyTriggerSettings>(
                    content,
                    AutoHotKeyTriggerJson.Settings) ?? new AutoHotKeyTriggerSettings();
                this.MigrateSettingsAfterLoad(content);
            }
            else
            {
                this.CreateDefaultProfile();
            }

            this.EnsureDefaultProfile();

            this.onAreaChange = CoroutineHandler.Start(this.EnableAutoQuitWarningUiOnAreaChange());
        }

        /// <inheritdoc />
        public override void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.SettingPathname) ?? string.Empty);
            var settingsData = JsonConvert.SerializeObject(this.Settings,
                Formatting.Indented,
                AutoHotKeyTriggerJson.Settings);
            File.WriteAllText(this.SettingPathname, settingsData);
        }

        private bool ShouldExecutePlugin()
        {
            var cgs = Core.States.GameCurrentState;
            if (cgs != GameStateTypes.InGameState)
            {
                this.debugMessage = $"Current game state isn't InGameState, it's {cgs}.";
                return false;
            }

            if (!Core.Process.Foreground)
            {
                this.debugMessage = "Game is minimized.";
                return false;
            }

            var areaDetails = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails;
            if (areaDetails.IsTown)
            {
                this.debugMessage = "Player is in town.";
                return false;
            }

            if (!this.Settings.ShouldRunInHideout && areaDetails.IsHideout)
            {
                this.debugMessage = "Player is in hideout & hideout execution is turned off.";
                return false;
            }

            if (Core.States.InGameStateObject.CurrentAreaInstance.Player.TryGetComponent<Life>(out var lifeComp))
            {
                if (lifeComp.Health.Current <= 0)
                {
                    this.debugMessage = "Player is dead.";
                    return false;
                }
            }
            else
            {
                this.debugMessage = "Can not find player Life component.";
                return false;
            }

            if (Core.States.InGameStateObject.CurrentAreaInstance.Player.TryGetComponent<Buffs>(out var buffComp))
            {
                if (buffComp.StatusEffects.ContainsKey("grace_period"))
                {
                    this.debugMessage = "Player has Grace Period.";
                    return false;
                }
            }
            else
            {
                this.debugMessage = "Can not find player PlayerBuffs component.";
                return false;
            }

            if (!Core.States.InGameStateObject.CurrentAreaInstance.Player.TryGetComponent<Actor>(out var _))
            {
                this.debugMessage = "Can not find player Actor component.";
                return false;
            }

            this.debugMessage = string.Empty;
            return true;
        }

        /// <summary>
        ///     Creates a default profile that is only valid for flasks on newly created character.
        /// </summary>
        private void CreateDefaultProfile()
        {
            this.Settings.Profiles[LeagueStartDefaultProfileName] = BuildLeagueStartDefaultProfile();
            this.Settings.CurrentProfile = LeagueStartDefaultProfileName;
            this.Settings.Profiles.TryAdd("ProfileMidGame", new());
            this.Settings.Profiles.TryAdd("ProfileEndGame", new());
        }

        private void EnsureDefaultProfile()
        {
            if (!this.Settings.Profiles.ContainsKey(LeagueStartDefaultProfileName))
            {
                this.Settings.Profiles[LeagueStartDefaultProfileName] = BuildLeagueStartDefaultProfile();
            }

            if (string.IsNullOrEmpty(this.Settings.CurrentProfile) ||
                !this.Settings.Profiles.ContainsKey(this.Settings.CurrentProfile))
            {
                this.Settings.CurrentProfile = LeagueStartDefaultProfileName;
            }
        }

        private void ResetLeagueStartDefaultProfileRules()
        {
            this.Settings.Profiles[LeagueStartDefaultProfileName] = BuildLeagueStartDefaultProfile();
            this.Settings.CurrentProfile = LeagueStartDefaultProfileName;
        }

        private static Profile BuildLeagueStartDefaultProfile()
        {
            var profile = new Profile();
            foreach (var rule in Rule.CreateDefaultRules())
            {
                profile.Rules.Add(rule);
            }

            return profile;
        }

        private void MigrateSettingsAfterLoad(string settingsJson)
        {
            if (!settingsJson.Contains("UseLegacyKeyInput", StringComparison.Ordinal))
            {
                this.Settings.UseLegacyKeyInput = true;
            }

            if (this.Settings.SettingsMigrationVersion < 4)
            {
                this.Settings.SettingsMigrationVersion = 4;
                this.SaveSettings();
            }
        }

        private void AutoQuitWarningUi()
        {

            if (!this.stopShowingAutoQuitWarning &&
                (Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails.IsTown ||
                Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails.IsHideout) &&
                this.ShouldExecuteAutoQuit)
            {
                ImGui.OpenPopup("AutoQuitWarningUi");
            }

            if (ImGui.BeginPopup("AutoQuitWarningUi"))
            {
                ImGui.Text(this.warningMsg);
                if (ImGui.Button("I understand", new Vector2(ImGui.CalcTextSize(this.warningMsg).X, 50f)))
                {
                    this.stopShowingAutoQuitWarning = true;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }

        private IEnumerator<Wait> EnableAutoQuitWarningUiOnAreaChange()
        {
            while (true)
            {
                yield return new Wait(RemoteEvents.AreaChanged);
                this.stopShowingAutoQuitWarning = false;
            }
        }

        private static string L(string english, string german) => OverlayLocalization.L(english, german);
    }
}