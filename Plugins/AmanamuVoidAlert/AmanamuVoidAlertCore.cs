namespace AmanamuVoidAlert
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using Coroutine;
    using GameHelper;
    using GameHelper.CoroutineEvents;
    using GameHelper.Localization;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteEnums.Entity;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using ImGuiNET;
    using Newtonsoft.Json;

    public sealed class AmanamuVoidAlertCore : PCore<AmanamuVoidAlertSettings>
    {
        private const string BuffPrefixAbyssLightlessWell = "abyss_lightless_well";
        private const string BuffInsideCloud = "abyss_lightless_well_immune";
        private const string ExpectedMonsterModId = "MonsterAbyssLightlessFaction1";
        private const string ExpectedMonsterModMetadata = "Metadata/Monsters/MonsterMods/LeagueAbyss/LightlessWells";

        private static readonly uint InsideColor = ImGui.ColorConvertFloat4ToU32(new Vector4(180f / 255f, 80f / 255f, 1f, 1f));
        private static readonly uint OutsideColor = ImGui.ColorConvertFloat4ToU32(new Vector4(80f / 255f, 1f, 120f / 255f, 1f));
        private static readonly uint TextShadowColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 230f / 255f));

        private readonly Dictionary<uint, TrackedMonster> tracked = new();
        private readonly Stopwatch enableTimer = Stopwatch.StartNew();
        private ActiveCoroutine? onAreaChange;

        private string SettingsPath => Path.Join(this.DllDirectory, "config", "settings.txt");

        public override void OnEnable(bool isGameOpened)
        {
            if (File.Exists(this.SettingsPath))
            {
                try
                {
                    var content = File.ReadAllText(this.SettingsPath);
                    this.Settings = JsonConvert.DeserializeObject<AmanamuVoidAlertSettings>(content) ?? new AmanamuVoidAlertSettings();
                }
                catch
                {
                    this.Settings = new AmanamuVoidAlertSettings();
                }
            }

            this.enableTimer.Restart();
            this.onAreaChange = CoroutineHandler.Start(this.OnAreaChange(), string.Empty, 0);
        }

        public override void OnDisable()
        {
            this.onAreaChange?.Cancel();
            this.onAreaChange = null;
            this.tracked.Clear();
        }

        public override void SaveSettings()
        {
            var dir = Path.GetDirectoryName(this.SettingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(this.SettingsPath, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
        }

        public override void DrawSettings()
        {
            ImGui.Checkbox(L("Enable overlay", "Overlay aktivieren"), ref this.Settings.EnableOverlay);
            ImGui.Checkbox(L("Show debug window", "Debug-Fenster anzeigen"), ref this.Settings.ShowDebugWindow);
            ImGui.Checkbox(L("Draw on-screen labels", "Labels auf dem Bildschirm"), ref this.Settings.DrawOnScreenLabels);
            ImGui.Checkbox(L("Draw off-screen / edge arrows", "Pfeile am Bildschirmrand"), ref this.Settings.DrawOffscreenArrows);
            ImGui.Checkbox(
                L("Draw edge arrow even when monster is on screen", "Rand-Pfeil auch bei sichtbarem Monster"),
                ref this.Settings.DrawEdgeArrowForOnScreenMonsters);
            ImGui.Checkbox(L("Draw circle around monster", "Kreis um Monster"), ref this.Settings.DrawCircle);
            ImGui.Checkbox(L("Only rare / unique monsters", "Nur selten / einzigartig"), ref this.Settings.OnlyRareOrUnique);
            ImGui.Checkbox(L("Log newly detected monsters", "Neue Monster in Konsole loggen"), ref this.Settings.LogNewDetections);

            ImGui.Separator();
            ImGui.SliderFloat(L("Max tracking distance", "Max. Tracking-Distanz"), ref this.Settings.MaxDistance, 500f, 8000f, "%.0f");
            ImGui.SliderFloat(L("Forget after seconds", "Vergessen nach Sekunden"), ref this.Settings.ForgetAfterSeconds, 1f, 30f, "%.1f");
            ImGui.SliderFloat(
                L("Forget missing live entity after seconds", "Lebendes Entity fehlt nach Sek."),
                ref this.Settings.MissingEntityForgetSeconds,
                0.2f,
                5f,
                "%.2f");
            ImGui.SliderFloat(L("Label Y offset", "Label Y-Offset"), ref this.Settings.LabelYOffset, 20f, 140f, "%.0f");
            ImGui.SliderFloat(L("Circle radius", "Kreisradius"), ref this.Settings.CircleRadius, 12f, 80f, "%.0f");
            ImGui.SliderFloat(L("Arrow edge margin", "Pfeil Randabstand"), ref this.Settings.ArrowEdgeMargin, 30f, 160f, "%.0f");
            ImGui.SliderFloat(L("Proxy distance", "Proxy-Distanz"), ref this.Settings.ProxyDistance, 200f, 2000f, "%.0f");

            ImGui.Separator();
            ImGui.TextWrapped(L(
                $"Primary detection: MonsterMod {ExpectedMonsterModId} or path contains LightlessWells.",
                $"Primaer-Erkennung: MonsterMod {ExpectedMonsterModId} oder Pfad enthaelt LightlessWells."));
            ImGui.TextWrapped(L(
                $"Inside cloud: buff contains '{BuffInsideCloud}'.",
                $"In der Cloud: Buff enthaelt '{BuffInsideCloud}'."));
            ImGui.TextWrapped(L(
                $"Fallback: any buff containing '{BuffPrefixAbyssLightlessWell}'.",
                $"Fallback: Buff mit '{BuffPrefixAbyssLightlessWell}'."));

            if (ImGui.Button(L("Clear tracked monsters", "Getrackte Monster loeschen")))
            {
                this.tracked.Clear();
            }
        }

        public override void DrawUI()
        {
            if (!this.Settings.EnableOverlay && !this.Settings.ShowDebugWindow)
            {
                return;
            }

            var gameState = Core.States.GameCurrentState;
            if (gameState is not (GameStateTypes.InGameState or GameStateTypes.EscapeState))
            {
                return;
            }

            if (gameState == GameStateTypes.EscapeState)
            {
                return;
            }

            var inGame = Core.States.InGameStateObject;
            var areaDetails = inGame.CurrentWorldInstance.AreaDetails;
            if (areaDetails.IsTown || areaDetails.IsHideout)
            {
                return;
            }

            var area = inGame.CurrentAreaInstance;
            var now = (float)this.enableTimer.Elapsed.TotalSeconds;

            this.MarkAllUnseen();
            this.ScanEntities(area, now);
            this.PruneOld(now);

            if (this.Settings.EnableOverlay)
            {
                this.DrawOverlay(area, now);
            }

            if (this.Settings.ShowDebugWindow)
            {
                this.DrawDebugWindow(area);
            }
        }

        private void MarkAllUnseen()
        {
            foreach (var key in this.tracked.Keys.ToList())
            {
                var tracked = this.tracked[key];
                tracked.SeenThisFrame = false;
                this.tracked[key] = tracked;
            }
        }

        private void ScanEntities(AreaInstance area, float now)
        {
            if (!area.Player.TryGetComponent<Render>(out var playerRender))
            {
                return;
            }

            var playerX = playerRender.WorldPosition.X;
            var playerY = playerRender.WorldPosition.Y;

            foreach (var entity in area.AwakeEntities.Values)
            {
                if (!this.IsMonsterCandidate(entity))
                {
                    continue;
                }

                if (!entity.TryGetComponent<Render>(out var render))
                {
                    continue;
                }

                var distance = Distance2D(playerX, playerY, render.WorldPosition.X, render.WorldPosition.Y);
                if (distance > this.Settings.MaxDistance)
                {
                    continue;
                }

                var buffResult = this.ScanBuffs(entity);
                var monsterModDebug = new List<string>();
                var foundByMonsterMod = this.HasAmanamuMonsterMod(entity, monsterModDebug);
                var foundByBuff = buffResult.HasAnyLightlessWellBuff;
                var alreadyKnown = this.tracked.ContainsKey(entity.Id);

                if (!foundByMonsterMod && !foundByBuff && !alreadyKnown)
                {
                    continue;
                }

                var isNew = !alreadyKnown;
                if (!this.tracked.TryGetValue(entity.Id, out var tracked))
                {
                    tracked = new TrackedMonster { Id = entity.Id };
                }

                tracked.Id = entity.Id;
                tracked.Path = entity.Path;
                tracked.SeenThisFrame = true;
                tracked.Distance = distance;
                tracked.LastSeenSeconds = now;
                tracked.LastLiveEntitySeconds = now;
                tracked.WasRecentlyLive = true;
                tracked.WorldX = render.WorldPosition.X;
                tracked.WorldY = render.WorldPosition.Y;
                tracked.WorldZ = render.WorldPosition.Z;
                tracked.ModelBoundsZ = MathF.Max(render.ModelBounds.Z, 80f);
                tracked.HasAmanamuMonsterMod = foundByMonsterMod || tracked.HasAmanamuMonsterMod;
                tracked.HasAnyLightlessBuff = buffResult.HasAnyLightlessWellBuff || tracked.HasAnyLightlessBuff;
                tracked.InsideCloud = buffResult.InsideCloud;
                tracked.LastBuffs = buffResult.BuffNames;
                if (monsterModDebug.Count > 0)
                {
                    tracked.LastMonsterMods = monsterModDebug;
                }

                this.tracked[entity.Id] = tracked;

                if (isNew && this.Settings.LogNewDetections)
                {
                    Console.WriteLine(
                        $"[AmanamuVoidAlert] Detected Lightless monster id={tracked.Id} byMod={foundByMonsterMod} byBuff={foundByBuff} path={tracked.Path}");
                }
            }
        }

        private void PruneOld(float now)
        {
            var eraseIds = new List<uint>();

            foreach (var kv in this.tracked)
            {
                var tracked = kv.Value;
                var ageSinceSeen = now - tracked.LastSeenSeconds;
                var ageSinceLive = now - tracked.LastLiveEntitySeconds;

                if (ageSinceSeen > this.Settings.ForgetAfterSeconds)
                {
                    eraseIds.Add(kv.Key);
                    continue;
                }

                if (tracked.WasRecentlyLive && ageSinceLive > this.Settings.MissingEntityForgetSeconds)
                {
                    eraseIds.Add(kv.Key);
                }
            }

            foreach (var id in eraseIds)
            {
                this.tracked.Remove(id);
            }
        }

        private void DrawOverlay(AreaInstance area, float now)
        {
            if (this.tracked.Count == 0)
            {
                return;
            }

            var fgDraw = ImGui.GetForegroundDrawList();
            var screenW = Core.Process.WindowArea.Width;
            var screenH = Core.Process.WindowArea.Height;
            if (screenW <= 0 || screenH <= 0)
            {
                return;
            }

            var screenCenter = new Vector2(screenW * 0.5f, screenH * 0.5f);
            var world = Core.States.InGameStateObject.CurrentWorldInstance;
            var eraseAfterDraw = new List<uint>();
            Render? playerRender = null;
            area.Player.TryGetComponent(out playerRender);

            foreach (var kv in this.tracked.ToList())
            {
                var tracked = kv.Value;
                Entity? liveEntity = null;
                foreach (var entity in area.AwakeEntities.Values)
                {
                    if (entity.Id == kv.Key)
                    {
                        liveEntity = entity;
                        break;
                    }
                }

                var hasLiveEntity = liveEntity is { IsValid: true };

                if (hasLiveEntity && liveEntity!.TryGetComponent<Life>(out var life, true) && !life.IsAlive)
                {
                    eraseAfterDraw.Add(kv.Key);
                    continue;
                }

                var worldX = tracked.WorldX;
                var worldY = tracked.WorldY;
                var worldZ = tracked.WorldZ;
                var modelBoundsZ = tracked.ModelBoundsZ;

                if (hasLiveEntity && liveEntity!.TryGetComponent<Render>(out var liveRender))
                {
                    tracked.LastLiveEntitySeconds = now;
                    tracked.WasRecentlyLive = true;
                    worldX = liveRender.WorldPosition.X;
                    worldY = liveRender.WorldPosition.Y;
                    worldZ = liveRender.WorldPosition.Z;
                    modelBoundsZ = MathF.Max(liveRender.ModelBounds.Z, 80f);
                    tracked.WorldX = worldX;
                    tracked.WorldY = worldY;
                    tracked.WorldZ = worldZ;
                    tracked.ModelBoundsZ = modelBoundsZ;

                    var liveMonsterModDebug = new List<string>();
                    if (this.HasAmanamuMonsterMod(liveEntity, liveMonsterModDebug))
                    {
                        tracked.HasAmanamuMonsterMod = true;
                    }

                    if (liveMonsterModDebug.Count > 0)
                    {
                        tracked.LastMonsterMods = liveMonsterModDebug;
                    }

                    var liveBuffResult = this.ScanBuffs(liveEntity);
                    tracked.InsideCloud = liveBuffResult.InsideCloud;
                    tracked.HasAnyLightlessBuff = liveBuffResult.HasAnyLightlessWellBuff || tracked.HasAnyLightlessBuff;
                    tracked.LastBuffs = liveBuffResult.BuffNames;
                }

                var markerZ = worldZ + MathF.Max(modelBoundsZ, 80f);
                var projectionReturned = world.Address != IntPtr.Zero;
                var screenPos = projectionReturned
                    ? world.WorldToScreen(new Vector2(worldX, worldY), markerZ)
                    : Vector2.Zero;

                this.UpdateCachedScreenDirection(tracked, screenPos, screenW, screenH, screenCenter);

                var color = tracked.InsideCloud ? InsideColor : OutsideColor;
                var visibleOnScreen = projectionReturned &&
                    screenPos.X >= 0f &&
                    screenPos.X <= screenW &&
                    screenPos.Y >= 0f &&
                    screenPos.Y <= screenH;

                if (visibleOnScreen)
                {
                    if (this.Settings.DrawCircle)
                    {
                        fgDraw.AddCircle(screenPos, this.Settings.CircleRadius, color, 48, 3f);
                    }

                    if (this.Settings.DrawOnScreenLabels)
                    {
                        var state = tracked.InsideCloud
                            ? L("INSIDE CLOUD", "IN DER CLOUD")
                            : L("OUTSIDE CLOUD", "AUSSERHALB CLOUD");
                        var label = $"AMANAMU VOID\n{state}\n{tracked.Distance:0}";
                        var textSize = ImGui.CalcTextSize(label);
                        var textPos = new Vector2(
                            screenPos.X - textSize.X * 0.5f,
                            screenPos.Y - this.Settings.LabelYOffset - textSize.Y);
                        fgDraw.AddText(textPos + Vector2.One, TextShadowColor, label);
                        fgDraw.AddText(textPos, color, label);
                    }
                }

                var shouldDrawEdgeArrow = this.Settings.DrawOffscreenArrows &&
                    (!visibleOnScreen || this.Settings.DrawEdgeArrowForOnScreenMonsters);

                if (shouldDrawEdgeArrow)
                {
                    var dir = this.GetArrowDirection(
                        tracked,
                        visibleOnScreen,
                        screenPos,
                        worldX,
                        worldY,
                        worldZ,
                        screenW,
                        screenH,
                        screenCenter,
                        playerRender,
                        world);

                    var arrowPos = ClampToScreenEdge(screenCenter, dir, screenW, screenH, this.Settings.ArrowEdgeMargin);
                    DrawArrow(fgDraw, arrowPos, dir, color);

                    var edgeText = tracked.InsideCloud
                        ? $"VOID {tracked.Distance:0} IN"
                        : $"VOID {tracked.Distance:0} OUT";
                    var edgeTextSize = ImGui.CalcTextSize(edgeText);
                    var edgeTextPos = new Vector2(arrowPos.X - edgeTextSize.X * 0.5f, arrowPos.Y + 22f);
                    fgDraw.AddText(edgeTextPos + Vector2.One, TextShadowColor, edgeText);
                    fgDraw.AddText(edgeTextPos, color, edgeText);
                }

                this.tracked[kv.Key] = tracked;
            }

            foreach (var id in eraseAfterDraw)
            {
                this.tracked.Remove(id);
            }
        }

        private void DrawDebugWindow(AreaInstance area)
        {
            ImGui.SetNextWindowSize(new Vector2(820f, 460f), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin(L("Amanamu Void Alert Debug", "Amanamu Void Alert Debug") + "###AmanamuVoidAlertDebug", ref this.Settings.ShowDebugWindow))
            {
                ImGui.End();
                return;
            }

            var areaName = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails.Name;
            ImGui.Text($"{L("Area", "Area")}: {areaName}");
            ImGui.Text($"{L("Tracked monsters", "Getrackte Monster")}: {this.tracked.Count}");
            ImGui.Text($"MonsterMod: {ExpectedMonsterModId}");
            ImGui.Text($"Metadata: {ExpectedMonsterModMetadata}");
            ImGui.Text($"{L("Inside-cloud buff", "In-Cloud-Buff")}: {BuffInsideCloud}");

            ImGui.Separator();
            if (ImGui.Button(L("Clear tracked", "Tracking loeschen")))
            {
                this.tracked.Clear();
            }

            ImGui.SameLine();
            if (ImGui.Button(L("Save settings", "Einstellungen speichern")))
            {
                this.SaveSettings();
            }

            ImGui.Separator();
            if (ImGui.BeginTable("##tracked_amanamu", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY, new Vector2(0f, 300f)))
            {
                ImGui.TableSetupColumn("ID");
                ImGui.TableSetupColumn(L("Path", "Pfad"));
                ImGui.TableSetupColumn(L("Dist", "Dist"));
                ImGui.TableSetupColumn(L("Cloud", "Cloud"));
                ImGui.TableSetupColumn(L("Mod", "Mod"));
                ImGui.TableSetupColumn(L("Buffs", "Buffs"));
                ImGui.TableHeadersRow();

                foreach (var tracked in this.tracked.Values.OrderBy(t => t.Distance))
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text($"{tracked.Id}");
                    ImGui.TableNextColumn();
                    ImGui.TextWrapped(tracked.Path);
                    ImGui.TableNextColumn();
                    ImGui.Text($"{tracked.Distance:0}");
                    ImGui.TableNextColumn();
                    ImGui.Text(tracked.InsideCloud ? "IN" : "OUT");
                    ImGui.TableNextColumn();
                    ImGui.Text(tracked.HasAmanamuMonsterMod ? "yes" : "no");
                    ImGui.TableNextColumn();
                    ImGui.TextWrapped(string.Join(", ", tracked.LastBuffs.Take(4)));
                }

                ImGui.EndTable();
            }

            ImGui.End();
        }

        private bool IsMonsterCandidate(Entity entity)
        {
            if (!entity.IsValid || entity.EntityType != EntityTypes.Monster)
            {
                return false;
            }

            if (entity.EntityState is EntityStates.MonsterFriendly or EntityStates.PinnacleBossHidden)
            {
                return false;
            }

            if (entity.TryGetComponent<Life>(out var life, true) && !life.IsAlive)
            {
                return false;
            }

            if (this.Settings.OnlyRareOrUnique)
            {
                if (!entity.TryGetComponent<ObjectMagicProperties>(out var omp, true))
                {
                    return false;
                }

                if (omp.Rarity is not (Rarity.Rare or Rarity.Unique))
                {
                    return false;
                }
            }

            return entity.TryGetComponent<Buffs>(out _, true) || entity.TryGetComponent<ObjectMagicProperties>(out _, true);
        }

        private BuffScanResult ScanBuffs(Entity entity)
        {
            var result = new BuffScanResult();
            if (!entity.TryGetComponent<Buffs>(out var buffs, true))
            {
                return result;
            }

            foreach (var kv in buffs.StatusEffects)
            {
                result.BuffNames.Add(kv.Key);
                if (ContainsInsensitive(kv.Key, BuffPrefixAbyssLightlessWell))
                {
                    result.HasAnyLightlessWellBuff = true;
                }

                if (ContainsInsensitive(kv.Key, BuffInsideCloud))
                {
                    result.InsideCloud = true;
                }
            }

            return result;
        }

        private bool HasAmanamuMonsterMod(Entity entity, List<string>? debugMods)
        {
            if (ContainsInsensitive(entity.Path, "LightlessWells") ||
                ContainsInsensitive(entity.Path, "MonsterAbyssLightless"))
            {
                return true;
            }

            if (!entity.TryGetComponent<ObjectMagicProperties>(out var omp, true))
            {
                return false;
            }

            var found = false;
            foreach (var modName in omp.ModNames)
            {
                debugMods?.Add(modName);
                if (string.Equals(modName, ExpectedMonsterModId, StringComparison.OrdinalIgnoreCase) ||
                    ContainsInsensitive(modName, "MonsterAbyssLightless") ||
                    ContainsInsensitive(modName, "LightlessWells"))
                {
                    found = true;
                }
            }

            foreach (var (name, _) in omp.Mods)
            {
                debugMods?.Add(name);
                if (string.Equals(name, ExpectedMonsterModId, StringComparison.OrdinalIgnoreCase) ||
                    ContainsInsensitive(name, "MonsterAbyssLightless") ||
                    ContainsInsensitive(name, "LightlessWells"))
                {
                    found = true;
                }
            }

            return found;
        }

        private void UpdateCachedScreenDirection(TrackedMonster tracked, Vector2 screenPos, float screenW, float screenH, Vector2 screenCenter)
        {
            if (!float.IsFinite(screenPos.X) || !float.IsFinite(screenPos.Y))
            {
                return;
            }

            if (MathF.Abs(screenPos.X) > screenW * 4f || MathF.Abs(screenPos.Y) > screenH * 4f)
            {
                return;
            }

            var dir = screenPos - screenCenter;
            if (!IsUsableDirection(dir))
            {
                return;
            }

            tracked.LastScreenDirection = Normalize(dir);
            tracked.HasLastScreenDirection = true;
        }

        private Vector2 GetArrowDirection(
            TrackedMonster tracked,
            bool visibleOnScreen,
            Vector2 screenPos,
            float worldX,
            float worldY,
            float worldZ,
            float screenW,
            float screenH,
            Vector2 screenCenter,
            Render? playerRender,
            WorldData world)
        {
            if (visibleOnScreen)
            {
                var dir = screenPos - screenCenter;
                if (IsUsableDirection(dir))
                {
                    tracked.LastScreenDirection = Normalize(dir);
                    tracked.HasLastScreenDirection = true;
                    return tracked.LastScreenDirection;
                }
            }

            if (float.IsFinite(screenPos.X) &&
                float.IsFinite(screenPos.Y) &&
                MathF.Abs(screenPos.X) < screenW * 4f &&
                MathF.Abs(screenPos.Y) < screenH * 4f)
            {
                var dir = screenPos - screenCenter;
                if (IsUsableDirection(dir))
                {
                    tracked.LastScreenDirection = Normalize(dir);
                    tracked.HasLastScreenDirection = true;
                    return tracked.LastScreenDirection;
                }
            }

            if (playerRender != null)
            {
                var dx = worldX - playerRender.WorldPosition.X;
                var dy = worldY - playerRender.WorldPosition.Y;
                var len = MathF.Sqrt(dx * dx + dy * dy);
                if (len > 1f)
                {
                    var nx = dx / len;
                    var ny = dy / len;
                    var proxyX = playerRender.WorldPosition.X + nx * this.Settings.ProxyDistance;
                    var proxyY = playerRender.WorldPosition.Y + ny * this.Settings.ProxyDistance;
                    var proxyZ = playerRender.WorldPosition.Z + 80f;
                    var playerScreen = world.WorldToScreen(
                        new Vector2(playerRender.WorldPosition.X, playerRender.WorldPosition.Y),
                        playerRender.WorldPosition.Z + 80f);
                    var proxyScreen = world.WorldToScreen(new Vector2(proxyX, proxyY), proxyZ);

                    if (playerScreen != Vector2.Zero && proxyScreen != Vector2.Zero)
                    {
                        var dir = proxyScreen - playerScreen;
                        if (IsUsableDirection(dir))
                        {
                            tracked.LastScreenDirection = Normalize(dir);
                            tracked.HasLastScreenDirection = true;
                            return tracked.LastScreenDirection;
                        }
                    }
                }
            }

            if (tracked.HasLastScreenDirection)
            {
                return Normalize(tracked.LastScreenDirection);
            }

            return new Vector2(0f, -1f);
        }

        private static void DrawArrow(ImDrawListPtr draw, Vector2 pos, Vector2 dir, uint color)
        {
            dir = Normalize(dir);
            var perp = new Vector2(-dir.Y, dir.X);
            const float size = 18f;
            var tip = pos + dir * size;
            var back = pos - dir * (size * 0.65f);
            var p2 = back + perp * (size * 0.55f);
            var p3 = back - perp * (size * 0.55f);
            draw.AddTriangleFilled(tip, p2, p3, color);
            draw.AddTriangle(tip, p2, p3, TextShadowColor, 2f);
        }

        private static Vector2 ClampToScreenEdge(Vector2 center, Vector2 dir, float width, float height, float margin)
        {
            dir = Normalize(dir);
            var tx = 999999f;
            var ty = 999999f;

            if (MathF.Abs(dir.X) > 0.001f)
            {
                var edgeX = dir.X > 0f ? width - margin : margin;
                tx = (edgeX - center.X) / dir.X;
            }

            if (MathF.Abs(dir.Y) > 0.001f)
            {
                var edgeY = dir.Y > 0f ? height - margin : margin;
                ty = (edgeY - center.Y) / dir.Y;
            }

            var t = MathF.Min(tx, ty);
            if (t < 0f || !float.IsFinite(t))
            {
                t = 0f;
            }

            return center + dir * t;
        }

        private static float Distance2D(float ax, float ay, float bx, float by)
        {
            var dx = ax - bx;
            var dy = ay - by;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        private static Vector2 Normalize(Vector2 v)
        {
            var len = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
            return len <= 0.001f ? new Vector2(0f, -1f) : v / len;
        }

        private static bool IsUsableDirection(Vector2 v) =>
            float.IsFinite(v.X) &&
            float.IsFinite(v.Y) &&
            (MathF.Abs(v.X) > 1f || MathF.Abs(v.Y) > 1f);

        private static bool ContainsInsensitive(string haystack, string needle) =>
            haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

        private IEnumerator<Wait> OnAreaChange()
        {
            while (true)
            {
                yield return new Wait(RemoteEvents.AreaChanged);
                this.tracked.Clear();
            }
        }

        private static string L(string english, string german) => OverlayLocalization.L(english, german);

        private sealed class BuffScanResult
        {
            public bool HasAnyLightlessWellBuff;
            public bool InsideCloud;
            public List<string> BuffNames = new();
        }

        private struct TrackedMonster
        {
            public uint Id;
            public string Path;
            public bool SeenThisFrame;
            public bool InsideCloud;
            public bool HasAnyLightlessBuff;
            public bool HasAmanamuMonsterMod;
            public float Distance;
            public float LastSeenSeconds;
            public float LastLiveEntitySeconds;
            public bool WasRecentlyLive;
            public float WorldX;
            public float WorldY;
            public float WorldZ;
            public float ModelBoundsZ;
            public bool HasLastScreenDirection;
            public Vector2 LastScreenDirection;
            public List<string> LastBuffs;
            public List<string> LastMonsterMods;

            public TrackedMonster()
            {
                this.Path = string.Empty;
                this.LastBuffs = new List<string>();
                this.LastMonsterMods = new List<string>();
            }
        }
    }
}
