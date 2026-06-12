// <copyright file="VitalTemplate.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Templates
{
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;
    using AutoHotKeyTrigger.ProfileManager.Enums;
    using GameHelper.Utils;
    using ImGuiNET;
    using System.Collections.Generic;

    /// <summary>
    ///     ImGui widget that helps user modify the condition code in <see cref="DynamicCondition"/>.
    /// </summary>
    public static class VitalTemplate
    {
        private static readonly List<string> SupportedOperatorTypes = new()
        {
            ">",
            ">=",
            "<",
            "<="
        };

        private static VitalType vitalType = VitalType.HP_PERCENT;
        private static string selectedOperator = "<=";
        private static int threshold = 90;

        /// <summary>
        ///     Display the ImGui widget for adding the condition in <see cref="DynamicCondition"/>.
        /// </summary>
        /// <returns>
        ///     condition in string format if user press Add button otherwise empty string.
        /// </returns>
        public static string Add()
        {
            ImGui.Text("Player vital");
            ImGui.SetNextItemWidth(TemplateUi.FieldWidth());
            ImGuiHelper.EnumComboBox("##VitalSelector", ref vitalType);

            ImGui.Spacing();
            ImGui.Text("Operator");
            ImGui.SetNextItemWidth(TemplateUi.FieldWidth(0.4f));
            ImGuiHelper.IEnumerableComboBox("##VitalOperator", SupportedOperatorTypes, ref selectedOperator);

            ImGui.Text("Value");
            ImGui.SetNextItemWidth(TemplateUi.FieldWidth());
            ImGui.InputInt("##VitalThreshold", ref threshold);

            ImGui.Spacing();
            if (TemplateUi.AddButton("##Vital"))
            {
                return $"{ToExpressionPath(vitalType)} {selectedOperator} {threshold}";
            }

            return string.Empty;
        }

        private static string ToExpressionPath(VitalType vitalType) =>
            vitalType switch
            {
                VitalType.MANA_CURRENT => "PlayerVitals.Mana.Current",
                VitalType.MANA_PERCENT => "PlayerVitals.Mana.Percent",
                VitalType.MANA_RESERVED => "PlayerVitals.Mana.Reserved",
                VitalType.HP_CURRENT => "PlayerVitals.HP.Current",
                VitalType.HP_PERCENT => "PlayerVitals.HP.Percent",
                VitalType.HP_RESERVED => "PlayerVitals.HP.Reserved",
                VitalType.ES_CURRENT => "PlayerVitals.ES.Current",
                VitalType.ES_PERCENT => "PlayerVitals.ES.Percent",
                VitalType.ES_RESERVED => "PlayerVitals.ES.Reserved",
                _ => $"PlayerVitals.{vitalType}",
            };
    }
}
