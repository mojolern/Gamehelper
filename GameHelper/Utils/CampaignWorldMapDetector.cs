// <copyright file="CampaignWorldMapDetector.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Utils
{
    using GameHelper.RemoteObjects.States.InGameStateObjects;

    /// <summary>
    ///     Detects the campaign WORLD / checkpoint / chapter overview map (not the in-area Tab overlay).
    ///     Raw flags on WorldMapPanel stay set in memory — never use flag-only checks in-zone.
    /// </summary>
    internal static class CampaignWorldMapDetector
    {
        public static bool IsOpen(ImportantUiElements gameUi)
        {
            if (gameUi.WorldMapPanel.Address == System.IntPtr.Zero)
            {
                return false;
            }

            if (gameUi.IsCampaignChapterTabEngaged)
            {
                return true;
            }

            if (gameUi.LargeMap.IsVisible && !gameUi.IsCampaignChapterTabEngaged)
            {
                return false;
            }

            if (gameUi.IsInAreaTabLargeMapOpen)
            {
                return false;
            }

            if (gameUi.IsCornerMinimapLive)
            {
                return false;
            }

            if (gameUi.WorldMapPanel.IsVisible)
            {
                return true;
            }

            return UiElementTreeVisibility.HasVisibleDescendant(gameUi.WorldMapPanel.Address, maxDepth: 6);
        }
    }
}
