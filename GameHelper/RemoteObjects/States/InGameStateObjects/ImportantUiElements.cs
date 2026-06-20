// <copyright file="ImportantUiElements.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.States.InGameStateObjects
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Coroutine;
    using CoroutineEvents;
    using GameHelper.Cache;
    using GameHelper.Utils;
    using GameOffsets.Objects.States.InGameState;
    using GameOffsets.Objects.UiElement;
    using ImGuiNET;
    using RemoteEnums;
    using UiElement;

    /// <summary>
    ///     This is actually UiRoot main child which contains
    ///     all the UiElements (100+). Normally it's at index 1 of UiRoot.
    ///     This class is created because traversing childrens of
    ///     UiRoot is a slow process that requires lots of memory reads.
    ///     Drawback:
    ///     1: Every league/patch the offsets needs to be updated.
    ///     Offsets used over here are very unstable.
    ///     2: More UiElements we are tracking = More memory read
    ///     every X seconds just to update IsVisible :(.
    /// </summary>
    public class ImportantUiElements : RemoteObjectBase
    {
        private static readonly int[] WorldMapPanelChildPath = { 22, 0 };
        private static readonly int[] Act1PanelChildPath = { 22, 0, 0 };
        private static readonly int[] Act2PanelChildPath = { 22, 0, 1 };
        private static readonly int[] Act3PanelChildPath = { 22, 0, 2 };
        private static readonly int[] Act4PanelChildPath = { 22, 0, 3 };
        private static readonly int[] InterludePanelChildPath = { 22, 0, 5 };

        private readonly UiElementParents rootCache;
        private readonly UiElementParents passiveSkillTreeCache;

        /// <summary>
        ///     Passive skill tree node Parent UI element.
        ///     UiRoot -> MainChild -> index 28 -> 1
        /// </summary>
        private UiElementBase passiveskilltreenodes;
        private UiElementBase sekhemasTrialMapPanel;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ImportantUiElements" /> class.
        /// </summary>
        /// <param name="address">
        ///     UiRoot 1st child address (starting from 0)
        ///     or <see cref="IntPtr.Zero" /> in case UiRoot has no child.
        /// </param>
        internal ImportantUiElements(IntPtr address)
            : base(address)
        {
            this.rootCache = new(null, GameStateTypes.InGameState, GameStateTypes.EscapeState, "Root");
            this.passiveSkillTreeCache = new(this.rootCache, GameStateTypes.InGameState, GameStateTypes.EscapeState, "PassiveSkillTree");

            this.passiveskilltreenodes = new(IntPtr.Zero, this.rootCache);
            this.sekhemasTrialMapPanel = new(IntPtr.Zero, this.rootCache);
            this.LargeMap = new(IntPtr.Zero, this.rootCache);
            this.MiniMap = new(IntPtr.Zero, this.rootCache);
            this.WorldMapPanel = new(IntPtr.Zero, this.rootCache);
            this.Act1 = new(IntPtr.Zero, this.rootCache);
            this.Act2 = new(IntPtr.Zero, this.rootCache);
            this.Act3 = new(IntPtr.Zero, this.rootCache);
            this.Act4 = new(IntPtr.Zero, this.rootCache);
            this.Interlude = new(IntPtr.Zero, this.rootCache);
            this.LeftPanel = new(IntPtr.Zero, this.rootCache);
            this.RightPanel = new(IntPtr.Zero, this.rootCache);
            this.ChatParent = new(IntPtr.Zero, this.rootCache);

            this.SkillTreeNodesUiElements = new();
            Core.CoroutinesRegistrar.Add(CoroutineHandler.Start(
                this.OnPerFrame(), "[InGameState] Update ImportantUiElements", priority: int.MaxValue - 3));
        }

        /// <summary>
        ///     Gets the LargeMap UiElement.
        ///     UiRoot -> MainChild -> 3rd index -> 1nd index.
        /// </summary>
        public LargeMapUiElement LargeMap { get; }

        /// <summary>
        ///     Gets the MiniMap UiElement.
        ///     UiRoot -> MainChild -> 3rd index -> 2nd index.
        /// </summary>
        public MapUiElement MiniMap { get; }

        /// <summary>
        ///     Gets the checkpoint / world-travel map panel UiElement.
        ///     It is only <see cref="UiElementBase.IsVisible" /> while that screen is open
        ///     (opened by interacting with a checkpoint). The in-area LargeMap stays visible
        ///     underneath it, so consumers gate on this to tell the two apart.
        ///     UiRoot manager -> 0x988.
        /// </summary>
        public UiElementBase WorldMapPanel { get; }

        internal UiElementBase Act1 { get; }

        internal UiElementBase Act2 { get; }

        internal UiElementBase Act3 { get; }

        internal UiElementBase Act4 { get; }

        internal UiElementBase Interlude { get; }

        /// <summary>
        ///     Gets the currently-open left-side panel UiElement (character, skills, etc.).
        ///     It is only <see cref="UiElementBase.IsVisible" /> while such a panel is open;
        ///     the backing pointer is null when no left panel is open.
        ///     UiRoot manager -> 0x6D8.
        /// </summary>
        public UiElementBase LeftPanel { get; }

        /// <summary>
        ///     Gets the currently-open right-side panel UiElement (inventory, vendor/shop, stash, etc.).
        ///     It is only <see cref="UiElementBase.IsVisible" /> while such a panel is open;
        ///     the backing pointer is null when no right panel is open.
        ///     UiRoot manager -> 0x6E0.
        /// </summary>
        public UiElementBase RightPanel { get; }

        /// <summary>
        ///     Gets a value indicating whether any large blocking panel is currently open
        ///     (a left/right side panel, the passive skill tree, or the world-travel map).
        ///     Useful for overlays that should hide world-space drawing while the player is in a menu.
        /// </summary>
        public bool IsAnyLargePanelOpen =>
            this.LeftPanel.IsVisible ||
            this.RightPanel.IsVisible ||
            this.SekhemasTrialMapPanel.IsVisible ||
            this.IsPassiveSkillTreeOpen;

        /// <summary>
        ///     In-area overlay map (Tab). Radar/Wraedar draw here.
        /// </summary>
        public bool IsInAreaTabLargeMapOpen =>
            this.LargeMap.IsVisible && !this.WorldMapPanel.IsVisible;

        /// <summary>
        ///     Tab overlay without an active world-travel panel (used to keep bars visible).
        /// </summary>
        public bool IsTabLargeMapOverlayActive => this.IsInAreaTabLargeMapOpen;

        /// <summary>
        ///     Player is in a zone with minimap or Tab large map — not a fullscreen travel menu.
        /// </summary>
        public bool IsInZoneMapUiActive =>
            this.IsInAreaTabLargeMapOpen ||
            this.LargeMap.IsVisible ||
            this.MiniMap.IsVisible ||
            UiElementVisibility.IsFlagVisible(this.MiniMap.Address);

        /// <summary>
        ///     Campaign WORLD / checkpoint travel map — not the Tab overlay.
        /// </summary>
        public bool IsCampaignWorldMapOpen => CampaignWorldMapDetector.IsOpen(this);

        /// <summary>
        ///     Any Act / Interlude tab under the checkpoint map is engaged (chapter overview).
        /// </summary>
        internal bool IsCampaignChapterTabEngaged =>
            this.Act1.IsVisible ||
            this.Act2.IsVisible ||
            this.Act3.IsVisible ||
            this.Act4.IsVisible ||
            this.Interlude.IsVisible;

        /// <summary>
        ///     In-zone Tab large map (minimap flag is off while this is up).
        /// </summary>
        internal bool IsInZoneTabLargeMapOverlay =>
            this.LargeMap.IsVisible && !this.IsCampaignChapterTabEngaged;

        /// <summary>
        ///     Gets a value indicating whether the PoE2 endgame atlas panel is open.
        /// </summary>
        public bool IsEndgameAtlasPanelOpen
        {
            get
            {
                if (this.Address == IntPtr.Zero)
                {
                    return false;
                }

                if (EndgameAtlasPanelDetector.HasStrongOpenSignal(this.Address))
                {
                    return true;
                }

                // Tab hides the minimap — never use the loose fingerprint walk in-zone.
                if (this.IsInZoneTabLargeMapOverlay || this.IsCornerMinimapLive)
                {
                    return false;
                }

                return EndgameAtlasPanelDetector.IsOpen(this.Address);
            }
        }

        /// <summary>
        ///     Hide floating world-space overlays on fullscreen map menus (not the Tab overlay).
        /// </summary>
        public bool ShouldHideWorldSpaceBars
        {
            get
            {
                if (this.IsEndgameAtlasPanelOpen)
                {
                    return true;
                }

                if (this.IsCampaignWorldMapOpen)
                {
                    return true;
                }

                if (this.SekhemasTrialMapPanel.IsVisible)
                {
                    return true;
                }

                if (this.IsPassiveSkillTreeOpen)
                {
                    return true;
                }

                if (this.LargeMap.IsVisible && !this.IsCampaignChapterTabEngaged)
                {
                    return false;
                }

                if (this.IsInAreaTabLargeMapOpen)
                {
                    return false;
                }

                return false;
            }
        }

        internal bool IsCornerMinimapLive =>
            this.MiniMap.IsVisible || UiElementVisibility.IsFlagVisible(this.MiniMap.Address);

        /// <summary>
        ///     Gets the Sekhemas Trial Map panel (endgame atlas UI).
        /// </summary>
        public UiElementBase SekhemasTrialMapPanel => this.sekhemasTrialMapPanel;

        /// <summary>
        ///     Gets a value indicating whether the passive skill tree is currently open.
        ///     Gated on the visibility of the tree's node container (UiRoot manager -> 0x730 -> child 2),
        ///     not on <see cref="SkillTreeNodesUiElements" />: in v0.5.x the per-node SkillInfo pointer
        ///     reads null so that list never populates, but the container's visibility bit is reliable.
        /// </summary>
        public bool IsPassiveSkillTreeOpen => this.passiveskilltreenodes.IsVisible;

        /// <summary>
        ///     Gets the Chat UiElement parent.
        ///     UiRoot -> MainChild -> this index keeps moving around
        /// </summary>
        public ChatParentUiElement ChatParent { get; }

        /// <summary>
        ///     Gets the skill tree nodes UI Elements.
        ///     UiRoot -> MainChild -> index 28 -> 2 -> all childrens;
        /// </summary>
        public List<SkillTreeNodeUiElement> SkillTreeNodesUiElements { get; }

        internal override void ToImGui()
        {
            this.displayParentsCache();
            base.ToImGui();
            ImGui.Text($"In-Area Tab Large Map: {this.IsInAreaTabLargeMapOpen}");
            ImGui.Text($"In-Zone Tab Overlay: {this.IsInZoneTabLargeMapOverlay}");
            ImGui.Text($"In-Zone Map UI: {this.IsInZoneMapUiActive}");
            ImGui.Text($"LargeMap Visible: {this.LargeMap.IsVisible}");
            ImGui.Text($"Hide World-Space Bars: {this.ShouldHideWorldSpaceBars}");
            ImGui.Text($"MiniMap Visible: {this.MiniMap.IsVisible}");
            ImGui.Text($"WorldMapPanel Visible: {this.WorldMapPanel.IsVisible}");
            ImGui.Text($"Campaign World Map Open: {this.IsCampaignWorldMapOpen}");
            ImGui.Text($"Endgame Atlas Panel Open: {this.IsEndgameAtlasPanelOpen}");
            ImGui.Text($"Passive Skill Tree Panel Visible: {this.passiveskilltreenodes.IsVisible}");
            ImGui.Text($"Total Skill Tree Nodes: {this.SkillTreeNodesUiElements.Count}");
            if (ImGui.TreeNode("Skill Tree Nodes"))
            {
                for (var i = 0; i < this.SkillTreeNodesUiElements.Count; i++)
                {
                    var skillId = this.SkillTreeNodesUiElements[i].SkillGraphId;
                    ImGuiHelper.DisplayTextAndCopyOnClick($"index: {i}, skillId: {skillId}", $"{skillId}");
                    ImGui.GetForegroundDrawList().AddText(this.SkillTreeNodesUiElements[i].Position, 0xFF0000FF, $"{i}");
                }

                ImGui.TreePop();
            }
        }

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            this.passiveskilltreenodes.Address = IntPtr.Zero;
            this.sekhemasTrialMapPanel.Address = IntPtr.Zero;
            this.MiniMap.Address = IntPtr.Zero;
            this.LargeMap.Address = IntPtr.Zero;
            this.WorldMapPanel.Address = IntPtr.Zero;
            this.Act1.Address = IntPtr.Zero;
            this.Act2.Address = IntPtr.Zero;
            this.Act3.Address = IntPtr.Zero;
            this.Act4.Address = IntPtr.Zero;
            this.Interlude.Address = IntPtr.Zero;
            this.LeftPanel.Address = IntPtr.Zero;
            this.RightPanel.Address = IntPtr.Zero;
            this.ChatParent.Address = IntPtr.Zero;
            this.SkillTreeNodesUiElements.Clear();
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            this.UpdateParentsCache();
            var reader = Core.Process.Handle;
            var data1 = reader.ReadMemory<ImportantUiElementsOffsets>(Core.GHSettings.IsTaiwanClient ? this.Address - 0x08 : this.Address);
            if (Core.GHSettings.EnableControllerMode)
            {
                var data2 = reader.ReadMemory<MapParentStruct>(data1.ControllerModeMapParentPtr);
                this.LargeMap.Address = data2.LargeMapPtr;
                this.MiniMap.Address = data2.MiniMapPtr;
                this.UpdateWorldMapPanelAddresses();
                this.LeftPanel.Address = IntPtr.Zero;
                this.RightPanel.Address = IntPtr.Zero;
                this.ChatParent.Address = IntPtr.Zero;
                this.passiveskilltreenodes.Address = IntPtr.Zero;
                this.sekhemasTrialMapPanel.Address = IntPtr.Zero;
            }
            else
            {
                var data2 = reader.ReadMemory<MapParentStruct>(data1.MapParentPtr);
                var data3 = reader.ReadMemory<UiElementBaseOffset>(data1.PassiveSkillTreePanel);
                var data4 = reader.ReadMemory<IntPtr>(data3.ChildrensPtr.First + (PassiveSkillTreeStruct.ChildNumber));
                // This won't throw an exception (i.e. this address is not a UIElement) because (lucky us)
                // game UiElement garbage collection is not instant. if this ever changes, put try catch on it.
                this.LargeMap.Address = data2.LargeMapPtr;
                this.MiniMap.Address = data2.MiniMapPtr;
                this.UpdateWorldMapPanelAddresses(data1.WorldMapPanelPtr);
                this.LeftPanel.Address = data1.LeftPanelPtr;
                this.RightPanel.Address = data1.RightPanelPtr;
                this.ChatParent.Address = data1.ChatParentPtr;
                this.passiveskilltreenodes.Address = data4;
                this.updatePassiveSkillTreeData();
                this.UpdateSekhemasTrialMapPanel();
            }
        }

        private void UpdateWorldMapPanelAddresses(IntPtr managerWorldMapPanelPtr = default)
        {
            var fromChildPath = ResolveChildAddress(this.Address, WorldMapPanelChildPath);
            this.WorldMapPanel.Address = fromChildPath != IntPtr.Zero
                ? fromChildPath
                : ValidUiElementOrZero(managerWorldMapPanelPtr);
            this.Act1.Address = ResolveChildAddress(this.Address, Act1PanelChildPath);
            this.Act2.Address = ResolveChildAddress(this.Address, Act2PanelChildPath);
            this.Act3.Address = ResolveChildAddress(this.Address, Act3PanelChildPath);
            this.Act4.Address = ResolveChildAddress(this.Address, Act4PanelChildPath);
            this.Interlude.Address = ResolveChildAddress(this.Address, InterludePanelChildPath);
        }

        private static IntPtr ResolveChildAddress(IntPtr rootAddress, int[] childPath)
        {
            if (rootAddress == IntPtr.Zero || childPath == null || childPath.Length == 0)
            {
                return IntPtr.Zero;
            }

            var reader = Core.Process.Handle;
            var currentAddress = rootAddress;
            foreach (var childIndex in childPath)
            {
                if (childIndex < 0)
                {
                    return IntPtr.Zero;
                }

                var data = reader.ReadMemory<UiElementBaseOffset>(currentAddress);
                var childCount = data.ChildrensPtr.TotalElements(IntPtr.Size);
                if (data.ChildrensPtr.First == IntPtr.Zero || childIndex >= childCount)
                {
                    return IntPtr.Zero;
                }

                currentAddress = reader.ReadMemory<IntPtr>(data.ChildrensPtr.First + (childIndex * IntPtr.Size));
                if (currentAddress == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }
            }

            return ValidUiElementOrZero(currentAddress);
        }

        private static IntPtr ValidUiElementOrZero(IntPtr address)
        {
            if (address == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var data = Core.Process.Handle.ReadMemory<UiElementBaseOffset>(address);
            return data.Self != IntPtr.Zero && data.Self != address ? IntPtr.Zero : address;
        }

        private void UpdateSekhemasTrialMapPanel()
        {
            try
            {
                var reader = Core.Process.Handle;
                var mgrOff = reader.ReadMemory<UiElementBaseOffset>(this.Address);
                var parentAddr = reader.ReadMemory<IntPtr>(mgrOff.ChildrensPtr.First + (84 * IntPtr.Size));
                if (parentAddr == IntPtr.Zero)
                {
                    this.sekhemasTrialMapPanel.Address = IntPtr.Zero;
                    return;
                }

                var parentOff = reader.ReadMemory<UiElementBaseOffset>(parentAddr);
                this.sekhemasTrialMapPanel.Address = reader.ReadMemory<IntPtr>(parentOff.ChildrensPtr.First);
            }
            catch
            {
                this.sekhemasTrialMapPanel.Address = IntPtr.Zero;
            }
        }

        private void updatePassiveSkillTreeData()
        {
            if (this.passiveskilltreenodes.IsVisible)
            {
                this.AddOrUpdateSkillNodes();
            }
            else
            {
                this.ClearSkillNodes();
            }
        }

        private void ClearSkillNodes()
        {
            this.SkillTreeNodesUiElements.Clear();
            this.passiveSkillTreeCache.Clear();
        }

        private void AddOrUpdateSkillNodes()
        {
            if (this.SkillTreeNodesUiElements.Count == 0)
            {
                var currentChild = new UiElementBase(IntPtr.Zero, this.passiveSkillTreeCache);
                for (var i = 3; i < this.passiveskilltreenodes.TotalChildrens; i++)
                {
                    currentChild.Address = this.passiveskilltreenodes[i]!.Address;
                    if (!currentChild.IsVisible)
                    {
                        break;
                    }

                    this.AddSkillTreeNodeUiElementRecursive(currentChild);
                }
            }
            else
            {
                Parallel.For(0, this.SkillTreeNodesUiElements.Count, (i) =>
                {
                    this.SkillTreeNodesUiElements[i].Address = this.SkillTreeNodesUiElements[i].Address;
                });
            }
        }

        private void AddSkillTreeNodeUiElementRecursive(UiElementBase uie)
        {
            if (uie.TotalChildrens > 0)
            {
                for (var i = 0; i < uie.TotalChildrens; i++)
                {
                    this.AddSkillTreeNodeUiElementRecursive(uie[i]!);
                }
            }
            else
            {
                var skillNode = new SkillTreeNodeUiElement(uie.Address, this.passiveSkillTreeCache);
                if (skillNode.SkillGraphId != 0)
                {
                    this.SkillTreeNodesUiElements.Add(skillNode);
                }
            }
        }

        private void displayParentsCache()
        {
            this.rootCache.ToImGui();
            this.passiveSkillTreeCache.ToImGui();
        }

        private void UpdateParentsCache()
        {
            this.rootCache.UpdateAllParentsParallel();
            this.passiveSkillTreeCache.UpdateAllParentsParallel();
        }

        private IEnumerator<Wait> OnPerFrame()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.PerFrameDataUpdate);
                try
                {
                    if (this.Address != IntPtr.Zero &&
                        Core.States.GameCurrentState is GameStateTypes.InGameState or GameStateTypes.EscapeState)
                    {
                        // sending false because "true" use-case is handled
                        // by UpdateData function when address actually gets changed.
                        this.UpdateData(false);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ImportantUiElements.OnPerFrame] {ex}");
                }
            }
        }
    }
}