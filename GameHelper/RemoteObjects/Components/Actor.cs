// <copyright file="Actor.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameHelper.Utils;
    using GameOffsets.Objects.Components;
    using GameOffsets.Objects.FilesStructures;
    using ImGuiNET;
    using RemoteEnums;

    /// <summary>
    ///     The <see cref="Actor" /> component in the entity.
    /// </summary>
    public class Actor : ComponentBase
    {
        private Dictionary<uint, ActiveSkillCooldown> ActiveSkillCooldowns { get; } = new();

        public Actor(IntPtr address)
            : base(address) { }

        public Animation Animation { get; private set; }

        public Dictionary<string, ActiveSkillDetails> ActiveSkills { get; } = new();

        public HashSet<string> IsSkillUsable { get; } = new();

        public Dictionary<string, bool> MinionCommandSkills { get; } = new();

        public HashSet<string> UsableMinionCommandSkills { get; } = new();

        public DeployedObjectCounter DeployedEntities { get; } = new();

        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"AnimationId: 0x{(int)this.Animation:X}, Animation: {this.Animation}");

            if (ImGui.TreeNode("Cooldowns"))
            {
                var cooldownSkillNames = new Dictionary<uint, string>();
                foreach (var (skillName, details) in this.ActiveSkills)
                {
                    cooldownSkillNames[details.UnknownIdAndEquipmentInfo] = skillName;
                }

                foreach (var (skillId, skillDetails) in this.ActiveSkillCooldowns)
                {
                    var name = cooldownSkillNames.TryGetValue(skillId, out var sn) ? sn : "?";
                    if (ImGui.TreeNode($"{name} ({skillId:X})"))
                    {
                        ImGui.Text($"Active Skill Id: {skillDetails.ActiveSkillsDatId}");
                        ImGuiHelper.IntPtrToImGui(
                            $"Cooldowns Vector (Length {skillDetails.TotalActiveCooldowns()})",
                            skillDetails.CooldownsList.First);
                        ImGui.Text($"Max Uses: {skillDetails.MaxUses}");
                        ImGui.Text($"Total Cooldown Time (ms): {skillDetails.TotalCooldownTimeInMs}");
                        ImGui.TreePop();
                    }
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Active Skills"))
            {
                foreach (var (skillname, skilldetails) in this.ActiveSkills)
                {
                    if (ImGui.TreeNode($"{skillname}"))
                    {
                        ImGui.Text($"Use Stage: {skilldetails.UseStage}");
                        ImGui.Text($"Cast Type: {skilldetails.CastType}");
                        ImGui.Text($"Skill UnknownIdAndEquipmentInfo: {skilldetails.UnknownIdAndEquipmentInfo:X}");
                        MiscHelper.ActiveSkillGemDataParser(
                            skilldetails.UnknownIdAndEquipmentInfo,
                            out var iue,
                            out var iu,
                            out var si,
                            out var li,
                            out var inv,
                            out var uid);
                        ImGui.Text($"Can skill be on player item: {iue}");
                        ImGui.Text($"Not sure what this does (something related to vaal skill): {iu}");
                        ImGui.Text($"Skill Gem link Number: {li}");
                        ImGui.Text($"Skill Gem socket Number: {si}");
                        ImGui.Text($"Skill Gem Inventory Slot: {(InventoryName)inv}");
                        ImGui.Text($"Skill Gem Name Hash: {uid:X}");
                        ImGuiHelper.IntPtrToImGui("Granted Effects Ptr", skilldetails.GrantedEffectsPerLevelDatRow);
                        ImGuiHelper.IntPtrToImGui($"Active Skills Ptr", skilldetails.ActiveSkillsDatPtr);
                        ImGuiHelper.IntPtrToImGui("Granted Effect Stat Sets Per Level Ptr", skilldetails.GrantedEffectStatSetsPerLevelDatRow);
                        ImGui.Text($"Total Uses: {skilldetails.TotalUses}");
                        ImGui.Text($"Cooldown Time (ms): {skilldetails.TotalCooldownTimeInMs}");
                        ImGui.TreePop();
                    }
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Can use skills"))
            {
                foreach (var skill in this.IsSkillUsable)
                {
                    ImGui.Text($"Skill {skill} can be used.");
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Deployed Objects"))
            {
                ImGui.Text("Please throw mines, totem, minons, traps, etc to populate the data over here.");
                foreach (var (type, count) in this.DeployedEntities)
                {
                    ImGui.Text($"{DeployedObjectCounter.CategoryName(type)} ({type}): {count}");
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Minion command skills"))
            {
                if (this.MinionCommandSkills.Count == 0)
                {
                    ImGui.TextWrapped("Empty — summon minions with command skills while in-game.");
                }

                foreach (var (name, usable) in this.MinionCommandSkills.OrderBy(static pair => pair.Key))
                {
                    ImGui.Text($"{name}: {(usable ? "usable" : "on cooldown")}");
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Minion Cooldowns"))
            {
                ImGui.TextWrapped("Command skills grouped by minion. Each line: skill name, cooldown " +
                    "key, datId, active uses, and whether it is currently unusable (active == uses).");
                var cdReader = Core.Process.Handle;
                var cdAreaState = Core.States.InGameStateObject.CurrentAreaInstance;
                foreach (var (_, entity) in cdAreaState.AwakeEntities)
                {
                    if (!entity.IsValid || !entity.TryGetComponent<Actor>(out var minionActor))
                    {
                        continue;
                    }

                    if (minionActor.Address == this.Address || minionActor.ActiveSkillCooldowns.Count == 0)
                    {
                        continue;
                    }

                    if (ImGui.TreeNode($"{entity.Path}##cd{entity.Id}"))
                    {
                        var keyToName = new Dictionary<uint, string>();
                        var minionData = cdReader.ReadMemory<ActorOffset>(minionActor.Address);
                        var minionSkills = cdReader.ReadStdVector<ActiveSkillStructure>(minionData.ActiveSkillsPtr);
                        for (var i = 0; i < minionSkills.Length; i++)
                        {
                            if (minionSkills[i].ActiveSkillPtr == IntPtr.Zero)
                            {
                                continue;
                            }

                            var sd = cdReader.ReadMemory<ActiveSkillDetails>(minionSkills[i].ActiveSkillPtr);
                            if (sd.GrantedEffectsPerLevelDatRow == IntPtr.Zero)
                            {
                                continue;
                            }

                            var (skillName, _) = ((string, IntPtr))Core.GgpkObjectCache.AddOrGetExisting(
                                sd.GrantedEffectsPerLevelDatRow,
                                key => (cdReader.ReadUnicodeString(cdReader.ReadMemory<IntPtr>(key)), key));
                            keyToName[sd.UnknownIdAndEquipmentInfo] = skillName;
                        }

                        foreach (var (cdKey, cd) in minionActor.ActiveSkillCooldowns)
                        {
                            var name = keyToName.TryGetValue(cdKey, out var n) ? n : "?";
                            ImGui.Text(
                                $"{name}: key=0x{cdKey:X} datId={cd.ActiveSkillsDatId} " +
                                $"active={cd.TotalActiveCooldowns()} cannotUse={cd.CannotBeUsed()}");
                        }

                        ImGui.TreePop();
                    }
                }

                ImGui.TreePop();
            }
        }

        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<ActorOffset>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.Animation = (Animation)data.AnimationId;
            this.IsSkillUsable.Clear();
            this.ActiveSkills.Clear();
            this.ActiveSkillCooldowns.Clear();

            var cooldowns = reader.ReadStdVector<ActiveSkillCooldown>(data.CooldownsPtr);
            for (var i = 0; i < cooldowns.Length; i++)
            {
                this.ActiveSkillCooldowns[cooldowns[i].UnknownIdAndEquipmentInf0] = cooldowns[i];
            }

            var activeSkills = reader.ReadStdVector<ActiveSkillStructure>(data.ActiveSkillsPtr);
            for (var i = 0; i < activeSkills.Length; i++)
            {
                var skillDetails = reader.ReadMemory<ActiveSkillDetails>(activeSkills[i].ActiveSkillPtr);
                if (skillDetails.GrantedEffectsPerLevelDatRow == IntPtr.Zero ||
                    (skillDetails.UnknownIdAndEquipmentInfo >> 0x10) < 0x8000)
                {
                    continue;
                }

                (var name, skillDetails.ActiveSkillsDatPtr) = ((string, IntPtr))Core.GgpkObjectCache.
                    AddOrGetExisting(skillDetails.GrantedEffectsPerLevelDatRow, (key) =>
                    {
                        return (reader.ReadUnicodeString(reader.ReadMemory<IntPtr>(key)), key);
                    });

                var cannotbeused = false;
                if (this.ActiveSkillCooldowns.TryGetValue(skillDetails.UnknownIdAndEquipmentInfo, out var cooldownInfo))
                {
                    cannotbeused |= cooldownInfo.CannotBeUsed();
                }

                this.ActiveSkills[name] = skillDetails;
                if (!cannotbeused)
                {
                    this.IsSkillUsable.Add(name);
                }
            }

            this.DeployedEntities.Clear();
            var deployedEntities = reader.ReadStdVector<DeployedEntityStructure>(data.DeployedEntityArray);
            for (var i = 0; i < deployedEntities.Length; i++)
            {
                this.DeployedEntities.Increment(deployedEntities[i].DeployedObjectType);
            }

            this.UpdateMinionCommandSkills(deployedEntities);
        }

        private void UpdateMinionCommandSkills(DeployedEntityStructure[] deployedEntities)
        {
            this.MinionCommandSkills.Clear();
            this.UsableMinionCommandSkills.Clear();

            var area = Core.States.InGameStateObject.CurrentAreaInstance;
            if (deployedEntities.Length == 0 ||
                this.OwnerEntityAddress == IntPtr.Zero ||
                this.OwnerEntityAddress != area.Player.Address)
            {
                return;
            }

            var deployedIds = new HashSet<uint>();
            for (var i = 0; i < deployedEntities.Length; i++)
            {
                deployedIds.Add((uint)deployedEntities[i].EntityId);
            }

            foreach (var (_, entity) in area.AwakeEntities)
            {
                if (!entity.IsValid ||
                    !deployedIds.Contains(entity.Id) ||
                    !entity.TryGetComponent<Actor>(out var minionActor))
                {
                    continue;
                }

                minionActor.CollectCommandSkills(this.MinionCommandSkills);
            }

            foreach (var (name, usable) in this.MinionCommandSkills)
            {
                if (usable)
                {
                    this.UsableMinionCommandSkills.Add(name);
                }
            }
        }

        private void CollectCommandSkills(Dictionary<string, bool> aggregate)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<ActorOffset>(this.Address);
            var skills = reader.ReadStdVector<ActiveSkillStructure>(data.ActiveSkillsPtr);
            for (var i = 0; i < skills.Length; i++)
            {
                if (skills[i].ActiveSkillPtr == IntPtr.Zero)
                {
                    continue;
                }

                var skillDetails = reader.ReadMemory<ActiveSkillDetails>(skills[i].ActiveSkillPtr);

                if ((skillDetails.UnknownIdAndEquipmentInfo & 0xFFFF0000u) != 0x40000000u ||
                    skillDetails.GrantedEffectsPerLevelDatRow == IntPtr.Zero)
                {
                    continue;
                }

                var (name, _) = ((string, IntPtr))Core.GgpkObjectCache.AddOrGetExisting(
                    skillDetails.GrantedEffectsPerLevelDatRow,
                    key => (reader.ReadUnicodeString(reader.ReadMemory<IntPtr>(key)), key));
                var usable = !(this.ActiveSkillCooldowns.TryGetValue(
                    skillDetails.UnknownIdAndEquipmentInfo, out var cooldown) && cooldown.CannotBeUsed());

                aggregate[name] = aggregate.TryGetValue(name, out var existing) ? existing || usable : usable;
            }
        }
    }
}
