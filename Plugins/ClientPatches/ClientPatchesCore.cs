namespace ClientPatches
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using ClickableTransparentOverlay.Win32;
    using Coroutine;
    using GameHelper;
    using GameHelper.CoroutineEvents;
    using GameHelper.Plugin;
    using GameHelper.Utils;
    using ImGuiNET;
    using Newtonsoft.Json;
    using CTOUtils = ClickableTransparentOverlay.Win32.Utils;

    public sealed class ClientPatchesCore : PCore<ClientPatchesSettings>
    {
        private readonly ZoomPatch zoomPatch = new();
        private readonly FogPatch fogPatch = new();
        private string settingsPath = string.Empty;
        private uint scannedPid;
        private ActiveCoroutine? onGameClose;

        public override void OnEnable(bool isGameOpened)
        {
            this.settingsPath = Path.Join(this.DllDirectory, "config", "settings.txt");
            if (File.Exists(this.settingsPath))
            {
                var content = File.ReadAllText(this.settingsPath);
                this.Settings = JsonConvert.DeserializeObject<ClientPatchesSettings>(content) ?? new ClientPatchesSettings();
            }

            this.onGameClose?.Cancel();
            this.onGameClose = CoroutineHandler.Start(this.RestoreOnGameClose());

            this.ScanAttachedProcess(force: true);
            if (isGameOpened)
            {
                if (this.Settings.ApplyOnGameAttach)
                {
                    this.Settings.InfiniteZoomEnabled = this.zoomPatch.Apply(Core.Process.Pid);
                }

                if (this.Settings.ApplyFogOnGameAttach)
                {
                    this.Settings.NoAtlasFogEnabled = this.fogPatch.Apply(Core.Process.Pid);
                }
            }

            this.SyncSettingsState();
        }

        public override void OnDisable()
        {
            this.onGameClose?.Cancel();
            this.onGameClose = null;
            this.zoomPatch.Restore();
            this.fogPatch.Restore();
            this.zoomPatch.Dispose();
            this.fogPatch.Dispose();
            this.Settings.InfiniteZoomEnabled = false;
            this.Settings.NoAtlasFogEnabled = false;
            this.scannedPid = 0;
        }

        public override void SaveSettings()
        {
            if (string.IsNullOrWhiteSpace(this.settingsPath))
            {
                this.settingsPath = Path.Join(this.DllDirectory, "config", "settings.txt");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(this.settingsPath) ?? string.Empty);
            File.WriteAllText(this.settingsPath, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
        }

        public override void DrawSettings()
        {
            this.ProcessRuntimeControls();

            ImGui.TextWrapped("Client patches write to the Path of Exile process. Use only for local R&D/testing. Original bytes are kept in memory and restored when the patch is disabled, the game closes, or this plugin unloads.");
            ImGui.Separator();

            ImGui.Text($"Game PID: {Core.Process.Pid}");
            this.DrawPatchStatus("Infinite Zoom", this.zoomPatch);
            this.DrawPatchStatus("No Atlas Fog", this.fogPatch);

            var showStatus = this.Settings.ShowStatusWindow;
            if (ImGui.Checkbox("Show small status window", ref showStatus))
            {
                this.Settings.ShowStatusWindow = showStatus;
            }

            ImGui.SeparatorText("Scan");
            if (ImGui.Button("Scan patches"))
            {
                this.ScanAttachedProcess(force: true);
            }

            ImGui.SeparatorText("Infinite Zoom");
            var applyZoomOnAttach = this.Settings.ApplyOnGameAttach;
            if (ImGui.Checkbox("Apply Infinite Zoom when game attaches", ref applyZoomOnAttach))
            {
                this.Settings.ApplyOnGameAttach = applyZoomOnAttach;
            }

            ImGui.Checkbox("Enable zoom hotkey", ref this.Settings.ToggleHotkeyEnabled);
            ImGui.SameLine();
            var tmpZoomKey = this.Settings.ToggleHotkey;
            if (ImGuiHelper.NonContinuousEnumComboBox("##ClientZoomToggleHotkey", ref tmpZoomKey))
            {
                this.Settings.ToggleHotkey = tmpZoomKey;
            }

            var zoomDesired = this.zoomPatch.Applied;
            if (ImGui.Checkbox("Infinite Zoom", ref zoomDesired))
            {
                this.SetZoomPatch(zoomDesired);
            }

            ImGui.SameLine();
            if (ImGui.Button("Apply##Zoom"))
            {
                this.SetZoomPatch(true);
            }

            ImGui.SameLine();
            if (ImGui.Button("Restore##Zoom"))
            {
                this.SetZoomPatch(false);
            }

            ImGui.SeparatorText("No Atlas Fog");
            var applyFogOnAttach = this.Settings.ApplyFogOnGameAttach;
            if (ImGui.Checkbox("Apply No Atlas Fog when game attaches", ref applyFogOnAttach))
            {
                this.Settings.ApplyFogOnGameAttach = applyFogOnAttach;
            }

            ImGui.Checkbox("Enable fog hotkey", ref this.Settings.FogToggleHotkeyEnabled);
            ImGui.SameLine();
            var tmpFogKey = this.Settings.FogToggleHotkey;
            if (ImGuiHelper.NonContinuousEnumComboBox("##ClientFogToggleHotkey", ref tmpFogKey))
            {
                this.Settings.FogToggleHotkey = tmpFogKey;
            }

            var fogDesired = this.fogPatch.Applied;
            if (ImGui.Checkbox("No Atlas Fog", ref fogDesired))
            {
                this.SetFogPatch(fogDesired);
            }

            ImGui.SameLine();
            if (ImGui.Button("Apply##Fog"))
            {
                this.SetFogPatch(true);
            }

            ImGui.SameLine();
            if (ImGui.Button("Restore##Fog"))
            {
                this.SetFogPatch(false);
            }
        }

        public override void DrawUI()
        {
            this.ProcessRuntimeControls();

            if (!this.Settings.ShowStatusWindow)
            {
                return;
            }

            ImGui.Begin("Client Patches", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
            ImGui.Text(this.zoomPatch.Applied ? "Infinite Zoom: ON" : "Infinite Zoom: OFF");
            ImGui.Text(this.fogPatch.Applied ? "No Atlas Fog: ON" : "No Atlas Fog: OFF");
            ImGui.Text($"Zoom toggle: {(this.Settings.ToggleHotkeyEnabled ? this.Settings.ToggleHotkey.ToString() : "disabled")}");
            ImGui.Text($"Fog toggle: {(this.Settings.FogToggleHotkeyEnabled ? this.Settings.FogToggleHotkey.ToString() : "disabled")}");
            ImGui.TextWrapped(this.zoomPatch.Status);
            ImGui.TextWrapped(this.fogPatch.Status);
            ImGui.End();
        }

        private void DrawPatchStatus(string label, ClientBytePatch patch)
        {
            ImGui.Text($"{label}: {patch.Status}");
            if (patch.PatchAddress != IntPtr.Zero)
            {
                ImGui.Text($"{label} address: 0x{patch.PatchAddress.ToInt64():X}");
            }
        }

        private void ProcessRuntimeControls()
        {
            this.ScanAttachedProcess(force: false);

            if (this.Settings.ToggleHotkeyEnabled && CTOUtils.IsKeyPressedAndNotTimeout(this.Settings.ToggleHotkey))
            {
                this.SetZoomPatch(!this.zoomPatch.Applied);
            }

            if (this.Settings.FogToggleHotkeyEnabled && CTOUtils.IsKeyPressedAndNotTimeout(this.Settings.FogToggleHotkey))
            {
                this.SetFogPatch(!this.fogPatch.Applied);
            }
        }

        private void ScanAttachedProcess(bool force)
        {
            var pid = Core.Process.Pid;
            if (pid == 0)
            {
                if (this.zoomPatch.Applied)
                {
                    this.SetZoomPatch(false);
                }

                if (this.fogPatch.Applied)
                {
                    this.SetFogPatch(false);
                }

                this.scannedPid = 0;
                return;
            }

            if (!force && this.scannedPid == pid)
            {
                return;
            }

            this.scannedPid = pid;
            this.zoomPatch.Scan(pid);
            this.fogPatch.Scan(pid);

            if (this.Settings.ApplyOnGameAttach && !this.zoomPatch.Applied)
            {
                this.Settings.InfiniteZoomEnabled = this.zoomPatch.Apply(pid);
            }

            if (this.Settings.ApplyFogOnGameAttach && !this.fogPatch.Applied)
            {
                this.Settings.NoAtlasFogEnabled = this.fogPatch.Apply(pid);
            }

            this.SyncSettingsState();
        }

        private void SetZoomPatch(bool enabled)
        {
            this.Settings.InfiniteZoomEnabled = enabled
                ? this.zoomPatch.Apply(Core.Process.Pid)
                : !this.zoomPatch.Restore() && this.zoomPatch.Applied;
        }

        private void SetFogPatch(bool enabled)
        {
            this.Settings.NoAtlasFogEnabled = enabled
                ? this.fogPatch.Apply(Core.Process.Pid)
                : !this.fogPatch.Restore() && this.fogPatch.Applied;
        }

        private void SyncSettingsState()
        {
            this.Settings.InfiniteZoomEnabled = this.zoomPatch.Applied;
            this.Settings.NoAtlasFogEnabled = this.fogPatch.Applied;
        }

        private IEnumerator<Wait> RestoreOnGameClose()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnClose);
                this.zoomPatch.Restore();
                this.fogPatch.Restore();
                this.Settings.InfiniteZoomEnabled = false;
                this.Settings.NoAtlasFogEnabled = false;
                this.scannedPid = 0;
            }
        }
    }
}
