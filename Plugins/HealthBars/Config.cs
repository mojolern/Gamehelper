namespace HealthBars
{
    using System.Numerics;
    using GameHelper.Localization;
    using GameHelper.Utils;
    using ImGuiNET;
    using Newtonsoft.Json;

    /// <summary>
    ///     Saves config of each type of healthbar.
    /// </summary>
    public class Config
    {
        /// <summary>
        ///     Enables the healthbar
        /// </summary>
        public bool Enable;

        /// <summary>
        ///     Change texture if player can cull strike this healthbar.
        /// </summary>
        public bool ShowCullStrike;

        /// <summary>
        ///     Show the absolute Health + Es as text aboved the healthbar
        /// </summary>
        public bool ShowText;

        /// <summary>
        ///     Gets the color to apply on healthbar background.
        /// </summary>
        public Vector4 BackgroundColor;

        /// <summary>
        ///     Gets the color to apply on healthbar.
        /// </summary>
        public Vector4 HealthbarColor;

        /// <summary>
        ///     Gets the color to apply on ES bar.
        /// </summary>
        public Vector4 ESColor;

        /// <summary>
        ///     Gets the color of the next.
        /// </summary>
        public Vector4 TextColor;

        /// <summary>
        ///     Healthbar size multiplier
        /// </summary>
        public Vector2 Scale;

        /// <summary>
        ///     Healthbar position shift.
        /// </summary>
        public Vector2 Shift;

        /// <summary>
        ///     Gets the half of the scale value.
        /// </summary>
        public Vector2 HalfOfScale;

        /// <summary>
        ///     Total number of Graduations on this healthbar
        /// </summary>
        public int Graduations;

        /// <summary>
        ///     Stores the start location of any given Graduation.
        /// </summary>
        public float GraduationsLocationStart;

        /// <summary>
        ///     Stores the end location of any given Graduation.
        /// </summary>
        public Vector2 GraduationsLocationEnd;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Config" /> class.
        /// </summary>
        /// <param name="healthbarcolor">color of the healthbar</param>
        /// <param name="graduations">number of graduations on the healthbar</param>
        /// <param name="showText">show the absolute Health + Es as text aboved the healthbar</param>
        /// <param name="sizeY">healthbar default size Y axis.</param>
        public Config(Vector4 healthbarcolor, int graduations, bool showText, float sizeY)
        {
            this.Enable = true;
            this.ShowCullStrike = true;
            this.ShowText = showText;
            this.BackgroundColor = new(Vector3.Zero, 1f);
            this.HealthbarColor = healthbarcolor;
            this.ESColor = new(0f, 1f, 1f, 1f);
            this.TextColor = new(0f, 1f, 1f, 1f);
            this.Scale = new(128f, sizeY);
            this.HalfOfScale = this.Scale / 2;
            this.Shift = new(0f, 11f);
            this.Graduations = graduations;
            this.GraduationsLocationStart = 0f;
            this.GraduationsLocationEnd = Vector2.Zero;
            this.UpdateGrauationsLocationData();
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Config" /> class.
        /// </summary>
        /// <param name="healthbarcolor">color of the healthbar</param>
        /// <param name="sizeY">healthbar default size Y axis.</param>
        public Config(Vector4 healthbarcolor, float sizeY) :
            this(healthbarcolor, 0, false, sizeY) { }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Config" /> class.
        /// </summary>
        /// <param name="healthbarcolor">color of the healthbar</param>
        /// <param name="graduations">number of graduations on the healthbar</param>
        public Config(Vector4 healthbarcolor, int graduations) :
            this(healthbarcolor, graduations, false, 8f) { }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Config" /> class.
        /// </summary>
        /// <param name="healthbarcolor">color of the healthbar</param>
        public Config(Vector4 healthbarcolor) :
            this(healthbarcolor, 0) { }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Config" /> class.
        /// </summary>
        [JsonConstructor]
        public Config() :
            this(Vector4.One) { }

        /// <summary>
        ///     Display the Config on imgui.
        /// </summary>
        public void Draw()
        {
            ImGui.Text(L(
                "NOTE: For going above/below the limit, or for manual editing, press CTRL + Left Mouse Button click.",
                "HINWEIS: Fuer Werte ausserhalb des Limits oder manuelle Eingabe: STRG + Linksklick."));
            if (ImGui.BeginTable("config_table", 2))
            {
                ImGui.TableNextColumn();
                ImGui.Checkbox(L("Enable Healthbar", "Lebensbalken aktivieren"), ref this.Enable);
                ImGui.TableNextColumn();
                ImGui.Checkbox(L("Visualize Culling Strike Range", "Cull-Strike-Bereich anzeigen"), ref this.ShowCullStrike);
                ImGui.TableNextColumn();
                ImGui.Checkbox(L("Show health+ES (absolute) as text", "Leben+ES (absolut) als Text"), ref this.ShowText);
                ImGui.TableNextColumn();
                ImGui.ColorEdit4(L("Text Color", "Textfarbe"), ref this.TextColor);
                ImGui.TableNextColumn();
                if (ImGuiHelper.Vector2SliderInt(L("Scale (x, y)", "Skalierung (x, y)"), ImGui.GetColumnWidth(), ref this.Scale, 160, 500, 16, 128, ImGuiSliderFlags.Logarithmic))
                {
                    this.UpdateGrauationsLocationData();
                }

                ImGuiHelper.ToolTip(L(
                    "By default texture is of height 16. If increasing the Y axis ruins the texture, modify the texture height via your fav texture editor. This doesn't apply to x axis.",
                    "Standard-Texturhoehe ist 16. Bei Y-Verzerrung Textur im Editor anpassen. Gilt nicht fuer die X-Achse."));
                ImGui.TableNextColumn();
                ImGuiHelper.Vector2SliderInt(L("Shift (x, y)", "Versatz (x, y)"), ImGui.GetColumnWidth(), ref this.Shift, -4000, 4000, -2500, 2500, ImGuiSliderFlags.Logarithmic);
                ImGui.TableNextColumn();
                ImGui.ColorEdit4(L("Healthbar", "Lebensbalken"), ref this.HealthbarColor);
                ImGui.TableNextColumn();
                ImGui.ColorEdit4(L("Background", "Hintergrund"), ref this.BackgroundColor);
                ImGui.TableNextColumn();
                if (ImGui.DragInt(L("Gradation Marks", "Graduationsmarken"), ref this.Graduations, 0.05f, 0, 9))
                {
                    this.UpdateGrauationsLocationData();
                }

                ImGuiHelper.ToolTip(L(
                    "Graduation thickness depends on Font size. Graduation marks are expensive to draw, on non rare/unique monsters keep it to 0.",
                    "Dicke haengt von der Schriftgroesse ab. Teuer zum Zeichnen - bei normalen Monstern auf 0 lassen."));
                ImGui.TableNextColumn();
                ImGui.ColorEdit4(L("ES Bar", "ES-Balken"), ref this.ESColor);
                ImGui.EndTable();
            }
        }

        private static string L(string english, string german) => OverlayLocalization.L(english, german);

        private void UpdateGrauationsLocationData()
        {
            this.GraduationsLocationStart = this.Scale.X / (this.Graduations + 1);
            this.GraduationsLocationEnd = Vector2.UnitY * this.Scale.Y;
            this.HalfOfScale = this.Scale / 2;
        }
    }
}
