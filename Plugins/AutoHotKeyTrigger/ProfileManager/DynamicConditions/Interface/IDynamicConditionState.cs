// <copyright file="IDynamicConditionState.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface
{
    using System.Collections.Generic;
    using ClickableTransparentOverlay.Win32;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.Components;
    using GameOffsets.Objects.Components;

    /// <summary>
    ///     The structure that can be queried using DynamicCondition
    /// </summary>
    public interface IDynamicConditionState
    {
        IReadOnlyCollection<string> PlayerAilments { get; }

        int PlayerAnimation { get; }

        HashSet<string> PlayerSkillIsUseable { get; }

        HashSet<string> MinionCommandSkillIsUsable { get; }

        Dictionary<string, ActiveSkillDetails> ActiveSkills { get; }

        DeployedObjectCounter DeployedObjectsCount { get; }

        IBuffDictionary PlayerBuffs { get; }

        IFlasksInfo Flasks { get; }

        IVitalsInfo PlayerVitals { get; }

        int InnerCircleFriendlyMonsterCount { get; }

        int OuterCircleFriendlyMonsterCount { get; }

        int FriendlyMonsterCount { get; }

        int MonsterCount(MonsterRarity rarity);

        int MonsterCount(MonsterRarity rarity, MonsterNearbyZones zone);

        int UndamageableMonsterCount(MonsterRarity rarity);

        int UndamageableMonsterCount(MonsterRarity rarity, MonsterNearbyZones zone);

        int DamageableMonsterCount(MonsterRarity rarity);

        int DamageableMonsterCount(MonsterRarity rarity, MonsterNearbyZones zone);

        int MonsterCountInRange(MonsterRarity rarity, int maxDistance);

        int UndamageableMonsterCountInRange(MonsterRarity rarity, int maxDistance);

        int DamageableMonsterCountInRange(MonsterRarity rarity, int maxDistance);

        int CorpseCount(MonsterRarity rarity);

        int CorpseCount(MonsterRarity rarity, MonsterNearbyZones zone);

        int CorpseCountInRange(MonsterRarity rarity, int maxDistance);

        bool IsKeyPressedForAction(VK vk);

        bool PlayerFirstWeaponSetActive { get; }

        bool PlayerSecondWeaponSetActive { get; }

        bool PlayerIsShapeShifted { get; }
    }
}
