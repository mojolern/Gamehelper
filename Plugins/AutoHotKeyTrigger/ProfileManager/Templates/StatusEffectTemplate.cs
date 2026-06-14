// <copyright file="StatusEffectTemplate.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Templates
{
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;
    using AutoHotKeyTrigger.ProfileManager.Enums;
    using GameHelper.Utils;
    using ImGuiNET;
    using System;
    using System.Collections.Generic;

    /// <summary>
    ///     ImGui widget that helps user modify the condition code in <see cref="DynamicCondition"/>.
    /// </summary>
    public static class StatusEffectTemplate
    {
        private static readonly List<string> SupportedOperatorTypes = new()
        {
            "has",
            "not has",
            ">",
            ">=",
            "<",
            "<="
        };

        private static string buffId = "grace_period";
        private static string selectedOperator = "has";
        private static StatusEffectCheckType checkType = StatusEffectCheckType.PercentTimeLeft;
        private static float threshold = 0;

        /// <summary>
        ///     Resets the template form when opening the add/edit dialog.
        /// </summary>
        public static void ResetForm()
        {
            buffId = "grace_period";
            selectedOperator = "has";
            checkType = StatusEffectCheckType.PercentTimeLeft;
            threshold = 0;
        }

        /// <summary>
        ///     Tries to populate the template fields from an existing condition expression.
        /// </summary>
        /// <param name="expression">existing dynamic condition source</param>
        /// <returns>true when at least the buff id was recognized</returns>
        public static bool TryLoadFromExpression(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return false;
            }

            expression = expression.Trim();
            const string hasPrefix = "PlayerBuffs.Has(\"";
            if (expression.StartsWith(hasPrefix, StringComparison.Ordinal) && expression.EndsWith("\")", StringComparison.Ordinal))
            {
                buffId = expression.Substring(hasPrefix.Length, expression.Length - hasPrefix.Length - 2);
                selectedOperator = "has";
                return true;
            }

            const string notHasPrefix = "!PlayerBuffs.Has(\"";
            if (expression.StartsWith(notHasPrefix, StringComparison.Ordinal) && expression.EndsWith("\")", StringComparison.Ordinal))
            {
                buffId = expression.Substring(notHasPrefix.Length, expression.Length - notHasPrefix.Length - 2);
                selectedOperator = "not has";
                return true;
            }

            const string indexerPrefix = "PlayerBuffs[\"";
            if (expression.StartsWith(indexerPrefix, StringComparison.Ordinal))
            {
                var closeQuote = expression.IndexOf("\"]", StringComparison.Ordinal);
                if (closeQuote < 0)
                {
                    return false;
                }

                buffId = expression.Substring(indexerPrefix.Length, closeQuote - indexerPrefix.Length);
                var remainder = expression[(closeQuote + 2)..].TrimStart();
                if (!remainder.StartsWith('.'))
                {
                    return false;
                }

                remainder = remainder[1..];
                var spaceIndex = remainder.IndexOf(' ');
                if (spaceIndex < 0)
                {
                    return false;
                }

                if (!Enum.TryParse(remainder[..spaceIndex], out checkType))
                {
                    return false;
                }

                remainder = remainder[(spaceIndex + 1)..].TrimStart();
                spaceIndex = remainder.IndexOf(' ');
                selectedOperator = spaceIndex < 0 ? remainder : remainder[..spaceIndex];
                var thresholdText = spaceIndex < 0 ? string.Empty : remainder[(spaceIndex + 1)..];
                if (!float.TryParse(thresholdText, out threshold))
                {
                    threshold = 0;
                }

                return true;
            }

            return false;
        }

        private static string NormalizeBuffId(string raw) => ExtractBuffId(raw);

        private static string ExtractBuffId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return raw;
            }

            raw = raw.Trim();
            const string hasPrefix = "PlayerBuffs.Has(\"";
            if (raw.StartsWith(hasPrefix, StringComparison.Ordinal) && raw.EndsWith("\")", StringComparison.Ordinal))
            {
                return raw.Substring(hasPrefix.Length, raw.Length - hasPrefix.Length - 2);
            }

            const string notHasPrefix = "!PlayerBuffs.Has(\"";
            if (raw.StartsWith(notHasPrefix, StringComparison.Ordinal) && raw.EndsWith("\")", StringComparison.Ordinal))
            {
                return raw.Substring(notHasPrefix.Length, raw.Length - notHasPrefix.Length - 2);
            }

            const string indexerPrefix = "PlayerBuffs[\"";
            if (raw.StartsWith(indexerPrefix, StringComparison.Ordinal))
            {
                var closeQuote = raw.IndexOf("\"]", StringComparison.Ordinal);
                if (closeQuote > indexerPrefix.Length)
                {
                    return raw.Substring(indexerPrefix.Length, closeQuote - indexerPrefix.Length);
                }
            }

            return raw;
        }

        /// <summary>
        ///     Display the ImGui widget for adding the condition in <see cref="DynamicCondition"/>.
        /// </summary>
        /// <returns>
        ///     condition in string format if user press Add button otherwise empty string.
        /// </returns>
        public static string Add()
        {
            ImGui.PushID("StatusEffectDuration");
            if (selectedOperator == "has" || selectedOperator == "not has")
            {
                ImGui.Text("Player");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 4.5f);
                ImGuiHelper.IEnumerableComboBox("##StatusEffectOperator", SupportedOperatorTypes, ref selectedOperator);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(Math.Min(TemplateUi.FieldWidth(0.55f), ImGui.GetFontSize() * 14f));
                ImGui.InputTextWithHint("##StatusEffectBuffId", "(de)buff id", ref buffId, 200);
                HelpBox();
            }
            else
            {
                ImGui.Text("Player has (de)buff");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(Math.Min(TemplateUi.FieldWidth(0.35f), ImGui.GetFontSize() * 10f));
                ImGui.InputTextWithHint("##StatusEffectBuffId2", "id", ref buffId, 200);
                HelpBox();
                ImGui.SameLine();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 4.5f);
                ImGuiHelper.IEnumerableComboBox("##StatusEffectOperator", SupportedOperatorTypes, ref selectedOperator);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5f);
                ImGui.InputFloat("##threshold", ref threshold);
                ImGui.SameLine();
                ImGuiHelper.EnumComboBox("##checkType", ref checkType);
                ImGuiHelper.ToolTip($"What to compare. {StatusEffectCheckType.PercentTimeLeft} ranges from " +
                    $"0 to 100, 0 being buff will expire imminently and 100 meaning " +
                    $"it was just applied");
            }

            ImGui.PopID();
            if (TemplateUi.AddButton("##StatusEffect"))
            {
                var id = NormalizeBuffId(buffId);
                return selectedOperator switch
                {
                    "has" => $"PlayerBuffs.Has(\"{id}\")",
                    "not has" => $"!PlayerBuffs.Has(\"{id}\")",
                    _ => $"PlayerBuffs[\"{id}\"].{checkType} {selectedOperator} {threshold}",
                };
            }
            else
            {
                return string.Empty;
            }
        }

        private static void HelpBox()
        {
            ImGuiHelper.ToolTip("Open Core -> DV -> States -> InGameStateObject -> " +
                "CurrentAreaInstance -> Player -> Components -> Buffs -> Status Effect to figure " +
                "out what value to put here. Make sure that (de)buff is active.");
        }
    }
}
