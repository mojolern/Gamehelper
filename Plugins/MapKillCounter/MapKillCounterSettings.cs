namespace MapKillCounter
{
    using System.Numerics;
    using GameHelper.Plugin;

    public enum KillListLayout
    {
        Vertical,
        Horizontal,
    }

    public enum MapOverlayMode
    {
        Full,
        TimerOnly,
    }

    public sealed class MapKillCounterSettings : IPSettings
{
    public bool ShowOverlay = true;

    public MapOverlayMode OverlayMode = MapOverlayMode.Full;

    public bool ShowSessionOverlay;

    public bool PauseTimerInTownOrHideout = true;

    public bool PauseTimerWhenGameInBackground;

    /// <summary>Hide the overlay while PoE is not the foreground window.</summary>
    public bool HideOverlayWhenGameInBackground = true;

    /// <summary>Pause while the in-game escape menu is open (ESC).</summary>
    public bool PauseTimerWhenGamePaused = true;

    public bool CountKillsInTownOrHideout;

    /// <summary>When true, stats survive town/hideout visits and reset only on a new map instance.</summary>
    public bool ResetOnlyOnNewMap = true;

    public Vector2 OverlayPosition = new(40f, 120f);

    public Vector2 SessionOverlayPosition = new(40f, 300f);

    /// <summary>Fixed overlay size in pixels. (0,0) picks a default for the current layout.</summary>
    public Vector2 OverlaySize = Vector2.Zero;

    public Vector2 SessionOverlaySize = Vector2.Zero;

    public KillListLayout Layout = KillListLayout.Vertical;

    /// <summary>Per-window font scale so other plugins (e.g. Atlas) cannot resize this overlay.</summary>
    public float OverlayFontScale = 1f;

    public Vector4 BackgroundColor = new(0f, 0f, 0f, 0.72f);

    public Vector4 TextColor = new(1f, 1f, 1f, 1f);

    public Vector4 NormalColor = new(0.92f, 0.92f, 0.92f, 1f);

    public Vector4 MagicColor = new(0.35f, 0.55f, 1f, 1f);

    public Vector4 RareColor = new(1f, 1f, 0.2f, 1f);

    public Vector4 UniqueColor = new(1f, 0.55f, 0.1f, 1f);
    }
}
