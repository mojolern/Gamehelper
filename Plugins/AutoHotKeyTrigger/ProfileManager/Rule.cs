// <copyright file="Rule.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Numerics;
    using GameHelper.Localization;
    using GameHelper.Utils;
    using ImGuiNET;
    using Newtonsoft.Json;
    using AutoHotKeyTrigger.ProfileManager.Enums;
    using AutoHotKeyTrigger.ProfileManager.Component;
    using ClickableTransparentOverlay.Win32;
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;
    using AutoHotKeyTrigger.ProfileManager.Templates;

    /// <summary>
    ///     Abstraction for the rule condition list
    /// </summary>
    public class Rule
    {
        private int conditionToModify = -1;
        private int conditionIndexToSwap = -1;
        private bool expand;
        private ConditionType newConditionType = ConditionType.AILMENT;
        private readonly Stopwatch cooldownStopwatch = Stopwatch.StartNew();
        private bool armed = true;

        [JsonProperty("Conditions", NullValueHandling = NullValueHandling.Ignore)]
        private readonly List<DynamicCondition> conditions = new();

        [JsonProperty] private float delayBetweenRuns = 0;

        /// <summary>
        ///     Enable/Disable the rule.
        /// </summary>
        public bool Enabled;

        /// <summary>
        ///     User friendly name given to a rule.
        /// </summary>
        public string Name;

        /// <summary>
        ///     Rule key to press on success.
        /// </summary>
        public VK Key;

        /// <summary>
        ///     When enabled, the rule fires at most once until all conditions become false again.
        /// </summary>
        [JsonProperty]
        public bool FireOnceUntilConditionClears;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Rule" /> class.
        /// </summary>
        /// <param name="name"></param>
        [JsonConstructor]
        public Rule(string name)
        {
            this.Name = name;
        }

        /// <summary>
        ///     Initializes a new instace of the <see cref="Rule"/> class by cloning existing one
        /// </summary>
        /// <param name="other"></param>
        public Rule(Rule other)
        {
            this.delayBetweenRuns = other.delayBetweenRuns;
            this.Enabled = false;
            this.Name = $"{other.Name}1";
            this.Key = other.Key;
            this.FireOnceUntilConditionClears = other.FireOnceUntilConditionClears;
            this.conditions = new();
            foreach (var condition in other.conditions)
            {
                this.conditions.Add(new(condition));
            }
        }

        /// <summary>
        ///     Creates default rules that are only valid for flasks on the newly created character.
        /// </summary>
        /// <returns>List of rules that are valid for newly created player.</returns>
        public static Rule[] CreateDefaultRules()
        {
            var rules = new Rule[2];
            rules[0] = new("LifeFlask");
            rules[0].Enabled = true;
            rules[0].Key = VK.KEY_1;
            rules[0].conditions.Add(new DynamicCondition($"PlayerVitals.HP.Percent <= 80 && Flasks.Flask1.IsUsable && !Flasks.Flask1.Active"));

            rules[1] = new($"ManaFlask");
            rules[1].Enabled = true;
            rules[1].Key = VK.KEY_2;
            rules[1].conditions.Add(new DynamicCondition($"PlayerVitals.Mana.Percent <= 30 && Flasks.Flask2.IsUsable && !Flasks.Flask2.Active"));

            return rules;
        }

        /// <summary>
        ///     Clears the list of conditions
        /// </summary>
        public void Clear()
        {
            this.conditions.Clear();
        }

        /// <summary>
        ///     Displays the rule settings
        /// </summary>
        public void DrawSettings()
        {
            ImGui.Checkbox(L("Enable", "Aktivieren"), ref this.Enabled);
            ImGui.InputText(L("Name", "Name"), ref this.Name, 100);
            var tmpKey = this.Key;
            if (ImGuiHelper.NonContinuousEnumComboBox(L("Key", "Taste"), ref tmpKey))
            {
                this.Key = tmpKey;
            }

            ImGui.Checkbox(L(
                "Fire once until condition clears",
                "Einmal feuern bis Bedingung wieder false"), ref this.FireOnceUntilConditionClears);
            ImGuiHelper.ToolTip(L(
                "Useful for ESC/pause: fires once when the condition becomes true, then waits until it is false again (e.g. ES recovers) before re-arming.",
                "Sinnvoll fuer ESC/Pause: feuert einmal wenn die Bedingung true wird, dann erst wieder wenn sie false ist (z. B. ES erholt)."));

            if (this.Key == VK.ESCAPE && !this.FireOnceUntilConditionClears && this.delayBetweenRuns < 1f)
            {
                ImGui.TextColored(new Vector4(1f, 0.55f, 0.2f, 1f), L(
                    "ESC with a short cooldown may spam the pause menu. Enable fire-once or raise cooldown.",
                    "ESC mit kurzem Cooldown kann das Pause-Menue spammen. Einmal-feueren aktivieren oder Cooldown erhoehen."));
            }

            this.DrawCooldownWidget();
            this.DrawAddNewCondition();
            this.DrawExistingConditions();
        }

        /// <summary>
        ///     Checks the rule conditions and presses its key if conditions are satisfied
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="useLegacyKeyInput">Use pre-1.3 AHK key send when true.</param>
        public void Execute(Action<string> logger, bool useLegacyKeyInput)
        {
            if (!this.Enabled)
            {
                return;
            }

            if (!this.AreConditionsMet())
            {
                this.armed = true;
                return;
            }

            if (this.FireOnceUntilConditionClears && !this.armed)
            {
                return;
            }

            if (!this.IsCooldownReady())
            {
                return;
            }

            var source = $"AHK/{this.Name}";
            var sent = useLegacyKeyInput
                ? AhkKeySender.SendKey(this.Key, source)
                : MiscHelper.KeyUp(this.Key, source);

            if (!sent)
            {
                return;
            }

            logger($"{this.Key} is pressed.");
            this.cooldownStopwatch.Restart();
            if (this.FireOnceUntilConditionClears)
            {
                this.armed = false;
            }
        }

        /// <summary>
        ///     Adds a new condition
        /// </summary>
        /// <param name="conditionType"></param>
        private void Add(ConditionType conditionType)
        {
            if (conditionType == ConditionType.DYNAMIC)
            {
                var condition = DynamicCondition.Add();
                if (condition != null)
                {
                    this.conditions.Add(condition);
                }
            }
            else
            {
                var sourceString = TemplateHelper.EnumToTemplate(conditionType);
                if (!string.IsNullOrEmpty(sourceString))
                {
                    this.conditions.Add(new(sourceString));
                }
            }
        }

        private void ModifyExistingCondition(ConditionType conditionType, int index)
        {
            if (conditionType == ConditionType.DYNAMIC)
            {
                var condition = DynamicCondition.Add();
                if (condition != null)
                {
                    this.conditions[index] = condition;
                }
            }
            else
            {
                var sourceString = TemplateHelper.EnumToTemplate(conditionType);
                if (!string.IsNullOrEmpty(sourceString))
                {
                    this.conditions[index] = new(sourceString);
                }
            }
        }

        /// <summary>
        ///     Removes a condition at a specific index.
        /// </summary>
        /// <param name="index">index of the condition to remove.</param>
        private void RemoveAt(int index)
        {
            this.conditions.RemoveAt(index);
        }

        /// <summary>
        ///     Swap two conditions.
        /// </summary>
        /// <param name="i">index of the condition to swap.</param>
        /// <param name="j">index of the condition to swap.</param>
        private void Swap(int i, int j)
        {
            (this.conditions[i], this.conditions[j]) = (this.conditions[j], this.conditions[i]);
        }

        private bool AreConditionsMet() =>
            this.conditions.Count > 0 && this.conditions.TrueForAll(x => x.Evaluate());

        private bool IsCooldownReady() =>
            this.cooldownStopwatch.Elapsed.TotalSeconds > this.delayBetweenRuns;

        private void DrawCooldownWidget()
        {
            ImGui.DragFloat(L("Cooldown time (seconds)", "Abklingzeit (Sekunden)") + "##DelayTimerConditionDelay", ref this.delayBetweenRuns, 0.1f, 0.0f, 30.0f);
            if (this.delayBetweenRuns > 0)
            {
                var cooldownTimeFraction = this.delayBetweenRuns <= 0f ? 1f :
                    MathF.Min((float)this.cooldownStopwatch.Elapsed.TotalSeconds, this.delayBetweenRuns) / this.delayBetweenRuns;
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiHelper.Color(200, 0, 200, 255));
                ImGui.ProgressBar(
                    (float)cooldownTimeFraction,
                    Vector2.Zero,
                    cooldownTimeFraction < 1f
                        ? $"{L("Cooling", "Abklingen")} {(cooldownTimeFraction * 100f):0}%"
                        : L("Ready", "Bereit"));
                ImGui.PopStyleColor();
            }
        }

        private void DrawExistingConditions()
        {
            var isOpened = ImGui.TreeNodeEx(L("Existing Conditions (?)", "Vorhandene Bedingungen (?)"), ImGuiTreeNodeFlags.DefaultOpen);
            ImGuiHelper.ToolTip(L(
                "All of the conditions needs to be true. Conditions can be moved up and down via drag and drop when not expanded.",
                "Alle Bedingungen muessen erfuellt sein. Per Drag & Drop verschieben, wenn nicht aufgeklappt."));
            if (isOpened)
            {
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 6);
                for (var i = 0; i < this.conditions.Count; i++)
                {
                    ImGui.PushID($"ConditionNo{i}");
                    if (i != 0)
                    {
                        ImGui.Separator();
                    }

                    ImGui.PushStyleColor(ImGuiCol.Button, 0);
                    if (ImGui.ArrowButton("###ExpandHideButton", (expand) ? ImGuiDir.Down : ImGuiDir.Right))
                    {
                        expand = !expand;
                    }

                    ImGui.PopStyleColor();
                    ImGui.SameLine();
                    if (expand && ImGui.SmallButton(L("Delete", "Loeschen")))
                    {
                        this.RemoveAt(i);
                        ImGui.PopID();
                        break;
                    }

                    ImGui.SameLine();
                    if (expand && ImGui.SmallButton(L("Add Component", "Komponente hinzufuegen")))
                    {
                        this.conditions[i].Add(new Wait(0));
                    }

                    ImGui.SameLine();
                    if (expand && ImGui.SmallButton(L("Edit Via Template", "Per Vorlage bearbeiten")))
                    {
                        this.conditionToModify = i;
                        TemplateUi.PrepareConditionPopup();
                        ImGui.OpenPopup("ModifyExistingConditionPopUp");
                    }

                    TemplateUi.PrepareConditionPopup();
                    if (ImGui.BeginPopup("ModifyExistingConditionPopUp"))
                    {
                        ImGui.Text(L("NOTE: Click outside this popup to close it.", "HINWEIS: Ausserhalb klicken zum Schliessen."));
                        ImGui.Text(L("NOTE: This Overwrites the whole condition.", "HINWEIS: Ueberschreibt die gesamte Bedingung."));
                        ImGui.SetNextItemWidth(TemplateUi.FieldWidth());
                        ImGuiHelper.EnumComboBox(L("Condition Type", "Bedingungstyp"), ref this.newConditionType);
                        ImGui.Separator();
                        this.ModifyExistingCondition(this.newConditionType, this.conditionToModify);
                        ImGui.EndPopup();
                    }

                    ImGui.BeginGroup();
                    this.conditions[i].Display(expand);
                    ImGui.EndGroup();
                    if (!expand)
                    {
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGuiHelper.Color(255, 255, 0, 255));
                        }

                        if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullID))
                        {
                            this.conditionIndexToSwap = i;
                            ImGui.SetDragDropPayload("ConditionIndex", IntPtr.Zero, 0);
                            ImGui.EndDragDropSource();
                        }

                        if (ImGui.BeginDragDropTarget())
                        {
                            ImGui.AcceptDragDropPayload("ConditionIndex");
                            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                            {
                                this.Swap(this.conditionIndexToSwap, i);
                            }

                            ImGui.EndDragDropTarget();
                        }

                        ImGui.SameLine();
                        var evaluationResult = this.conditions[i].Evaluate();
                        ImGui.TextColored(
                            evaluationResult ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0, 0, 1),
                            evaluationResult ? "(true)" : "(false)");
                    }

                    ImGui.PopID();
                }

                ImGui.PopItemWidth();
                ImGui.TreePop();
            }
        }

        private void DrawAddNewCondition()
        {
            if (ImGui.Button(L("Add New Condition", "Neue Bedingung")))
            {
                TemplateUi.PrepareConditionPopup();
                ImGui.OpenPopup("AddNewConditionPopUp");
            }

            ImGui.SameLine();
            if (ImGui.Button(L("Clear All Conditions", "Alle Bedingungen loeschen")))
            {
                this.Clear();
            }

            ImGui.SameLine();
            var isClicked = ImGui.Button(L("Merge All conditions", "Alle Bedingungen zusammenfuehren"));
            ImGuiHelper.ToolTip(L(
                "This merges all the conditions into one so you can easily copy paste it into multiple rules. Conditions with component can not be merged.",
                "Fuegt Bedingungen zusammen zum Kopieren in mehrere Regeln. Bedingungen mit Komponenten werden nicht zusammengefuehrt."));
            if (isClicked)
            {
                var newConditions = new List<DynamicCondition>();
                foreach (var condition in this.conditions)
                {
                    if (newConditions.Count == 0)
                    {
                        newConditions.Add(condition);
                        continue;
                    }

                    if (!newConditions.Last().Merge(condition))
                    {
                        newConditions.Add(condition);
                    }
                }

                this.conditions.Clear();
                this.conditions.AddRange(newConditions);
            }

            TemplateUi.PrepareConditionPopup();
            if (ImGui.BeginPopup("AddNewConditionPopUp"))
            {
                ImGui.Text(L("NOTE: Click outside this popup to close it.", "HINWEIS: Ausserhalb klicken zum Schliessen."));
                ImGui.SetNextItemWidth(TemplateUi.FieldWidth());
                ImGuiHelper.EnumComboBox(L("Condition Type", "Bedingungstyp"), ref this.newConditionType);
                ImGui.Separator();
                this.Add(this.newConditionType);
                ImGui.EndPopup();
            }
        }

        private static string L(string english, string german) => OverlayLocalization.L(english, german);
    }
}
