// <copyright file="IsMinionCommandUseableTemplate.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Templates
{
    using System.Linq;
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;
    using GameHelper;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.Utils;
    using ImGuiNET;

    /// <summary>
    ///     ImGui widget that helps the user add a "minion command useable" condition to a
    ///     <see cref="DynamicCondition"/>. Minion command skills carry their cooldown on the
    ///     summoned minion, so they never appear in the normal player skill list.
    /// </summary>
    public static class IsMinionCommandUseableTemplate
    {
        private static string commandName = string.Empty;

        /// <summary>
        ///     Display the ImGui widget for adding the condition in <see cref="DynamicCondition"/>.
        /// </summary>
        /// <returns>
        ///     condition in string format if user press Add button otherwise empty string.
        /// </returns>
        public static string Add()
        {
            if (!Core.States.InGameStateObject.CurrentAreaInstance.Player.TryGetComponent<Actor>(out var actor))
            {
                ImGui.TextWrapped(
                    "Summon a minion with a command skill while in-game, then reopen this dialog. " +
                    "The list is read live from your minions (not from your skill bar).");
                commandName = string.Empty;
                ImGui.BeginDisabled();
                ImGui.Button("Add##MinionCommandUsable");
                ImGui.EndDisabled();
                return string.Empty;
            }

            var options = actor.MinionCommandSkills.Keys.OrderBy(static name => name).ToList();
            ImGui.Text("Minion command");
            ImGui.SameLine();
            if (options.Count > 0)
            {
                ImGuiHelper.IEnumerableComboBox("###MinionCommandName", options, ref commandName);
            }
            else
            {
                ImGui.TextDisabled("(empty)");
                ImGui.TextWrapped(
                    "No command skills detected yet. Summon a minion that has a player command " +
                    "(e.g. Attack, Retreat), keep GameHelper attached, then try again.");
            }

            ImGui.Text("Or type name");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(180f);
            ImGui.InputText("###MinionCommandManual", ref commandName, 128);

            ImGui.SameLine();
            var canAdd = !string.IsNullOrWhiteSpace(commandName);
            if (!canAdd)
            {
                ImGui.BeginDisabled();
            }

            var added = ImGui.Button("Add##MinionCommandUsable");
            if (!canAdd)
            {
                ImGui.EndDisabled();
            }

            return added && canAdd
                ? $"MinionCommandSkillIsUsable.Contains(\"{commandName.Trim()}\")"
                : string.Empty;
        }
    }
}
