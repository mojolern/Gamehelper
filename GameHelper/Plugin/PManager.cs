// <copyright file="PManager.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Plugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Threading.Tasks;
    using Shared.UpdateSecurity;
    using Coroutine;
    using CoroutineEvents;
    using Newtonsoft.Json.Linq;
    using CTOUtils = ClickableTransparentOverlay.Win32.Utils;
    using Settings;
    using Ui;
    using Utils;

    internal record PluginWithName(string Name, IPCore Plugin, PluginAssemblyLoadContext Alc);

    internal record PluginContainer(string Name, IPCore Plugin, PluginMetadata Metadata, PluginAssemblyLoadContext Alc);

    /// <summary>
    ///     Finds, loads and unloads the plugins.
    /// </summary>
    internal static class PManager
    {
        private static bool disableRendering = false;
#if DEBUG
        internal static readonly List<string> PluginNames = new();
#endif
        internal static readonly List<PluginContainer> Plugins = new();

        /// <summary>
        ///     Initlizes the plugin manager by loading all the plugins and their Metadata.
        /// </summary>
        internal static void InitializePlugins()
        {
            State.PluginsDirectory.Create(); // doesn't do anything if already exists.
            LoadPluginMetadata(LoadPlugins());
#if DEBUG
            GetAllPluginNames();
#endif
            // F-079: replaced Parallel.ForEach with foreach. Plugin OnEnable is rare
            // (once at startup), should be single-threaded for plugin authors who
            // assume ImGui / coroutine-registration semantics work on the render thread.
            PluginContainer[] snapshot;
            lock (Plugins)
            {
                snapshot = Plugins.ToArray();
            }

            foreach (var container in snapshot)
            {
                EnablePluginIfRequired(container);
            }
            CoroutineHandler.Start(SavePluginSettingsCoroutine());
            CoroutineHandler.Start(SavePluginMetadataCoroutine());
            Core.CoroutinesRegistrar.Add(CoroutineHandler.Start(
                DrawPluginUiRenderCoroutine(), "[PManager] Draw Plugins UI"));
        }

        private static List<PluginWithName> LoadPlugins()
        {
            return GetPluginsDirectories()
                  .AsParallel()
                  .Select(LoadPlugin)
                  .Where(x => x != null)
                  .Select(x => x!)
                  .OrderBy(x => x.Name)
                  .ToList();
        }

#if DEBUG
        private static void GetAllPluginNames()
        {
            PluginContainer[] snapshot;
            lock (Plugins)
            {
                snapshot = Plugins.ToArray();
            }

            foreach (var plugin in snapshot)
            {
                PluginNames.Add(plugin.Name);
            }
        }

        /// <summary>
        ///     Cleans up the already loaded plugins.
        /// </summary>
        internal static bool UnloadPlugin(string name)
        {
            PluginContainer? target;
            lock (Plugins)
            {
                target = Plugins.FirstOrDefault(p => p.Name == name);
            }

            if (target == null)
            {
                return false;
            }

            target.Plugin.SaveSettings();
            target.Plugin.OnDisable();

            lock (Plugins)
            {
                Plugins.Remove(target);
            }

            // F-075: actually unload the assembly via the collectible ALC tracked
            // in the PluginContainer (F-074 made the ALC collectible).
            var alcRef = new WeakReference(target.Alc);
            target.Alc.Unload();

            // Release the strong reference to the PluginContainer (and its Alc field)
            // BEFORE the GC loop. The .NET docs require this — without it, the JIT
            // can keep `target` rooted across the loop and alcRef.IsAlive stays true
            // forever (spurious warning log). See:
            // https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability
            target = null;

            // Run GC repeatedly until the ALC is unreachable (or we give up after
            // 10 attempts).
            for (var i = 0; i < 10 && alcRef.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            if (alcRef.IsAlive)
            {
                Console.WriteLine($"[PManager.UnloadPlugin] {name}: ALC still alive after 10 GC cycles - likely a static reflection cache pinning a type. Plugin removed from manager but assembly remains loaded.");
            }

            return true;
        }

        internal static bool LoadPlugin(string name)
        {
            try
            {
                var container = GetPluginsDirectories()
                                .Where(x => x.Name.Contains(name))
                                .Select(LoadPlugin)
                                .Where(y => y != null)
                                .Select(y => y!)
                                .ToList();
                if (container.Count > 0)
                {
                    LoadPluginMetadata(container);
                    container[0].Plugin.OnEnable(Core.Process.Address != IntPtr.Zero);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }
#endif

        private static List<DirectoryInfo> GetPluginsDirectories()
        {
            return State.PluginsDirectory.GetDirectories().Where(
                x => (x.Attributes & FileAttributes.Hidden) == 0).ToList();
        }

        private static (Assembly assembly, PluginAssemblyLoadContext alc)? ReadPluginFiles(DirectoryInfo pluginDirectory)
        {
            try
            {
                var pluginsRoot = Path.GetFullPath(State.PluginsDirectory.FullName);
                var pluginRoot = Path.GetFullPath(pluginDirectory.FullName);
                if (!UpdatePathSecurity.IsPathInsideRoot(pluginsRoot, pluginRoot))
                {
                    Console.WriteLine($"Rejected plugin directory outside Plugins root: {pluginDirectory.FullName}");
                    return null;
                }

                var dllFile = pluginDirectory.GetFiles(
                    $"{pluginDirectory.Name}*.dll",
                    SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (dllFile == null)
                {
                    Console.WriteLine($"Couldn't find plugin dll with name {pluginDirectory.Name}" +
                                      $" in directory {pluginDirectory.FullName}." +
                                      " Please make sure DLL & the plugin got same name.");
                    return null;
                }

                if (!UpdatePathSecurity.IsPathInsideRoot(pluginRoot, dllFile.FullName))
                {
                    Console.WriteLine($"Rejected plugin dll outside plugin folder: {dllFile.FullName}");
                    return null;
                }

                if (!VerifyPluginDllHash(dllFile.FullName))
                {
                    Console.WriteLine($"Rejected plugin dll due to hash mismatch: {dllFile.FullName}");
                    return null;
                }

                var alc = new PluginAssemblyLoadContext(dllFile.FullName);
                var assembly = alc.LoadFromAssemblyPath(dllFile.FullName);
                return (assembly, alc);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to load plugin {pluginDirectory.FullName} due to {e}");
                return null;
            }
        }

        private static bool VerifyPluginDllHash(string dllPath)
        {
            var installRoot = AppContext.BaseDirectory;
            var relativePath = Path.GetRelativePath(installRoot, dllPath).Replace('\\', '/');
            // No catalog entry: allow (legacy installs / dev). Entries come from signed ZIP manifests.
            if (!UpdateFileHashesCatalog.TryGetExpectedHash(installRoot, relativePath, out var expectedHash))
            {
                return true;
            }

            using var sha = SHA256.Create();
            using var stream = File.OpenRead(dllPath);
            var actualHash = Convert.ToHexString(sha.ComputeHash(stream));
            return actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
        }


        private static PluginWithName? LoadPlugin(DirectoryInfo pluginDirectory)
        {
            var loaded = ReadPluginFiles(pluginDirectory);
            if (loaded != null)
            {
                var relativePluginDir = pluginDirectory.FullName.Replace(
                    State.PluginsDirectory.FullName, State.PluginsDirectory.Name);
                return LoadPlugin(loaded.Value.assembly, loaded.Value.alc, relativePluginDir);
            }

            return null;
        }

        private static PluginWithName? LoadPlugin(Assembly assembly, PluginAssemblyLoadContext alc, string pluginRootDirectory)
        {
            try
            {
                var types = assembly.GetTypes();
                if (types.Length <= 0)
                {
                    Console.WriteLine($"Plugin (in {pluginRootDirectory}) {assembly} doesn't " +
                                      "contain any types (i.e. classes/stuctures).");
                    return null;
                }

                var iPluginClasses = types.Where(
                    type => typeof(IPCore).IsAssignableFrom(type) &&
                            type.IsSealed).ToList();
                if (iPluginClasses.Count != 1)
                {
                    Console.WriteLine($"Plugin (in {pluginRootDirectory}) {assembly} contains" +
                                      $" {iPluginClasses.Count} sealed classes derived from CoreBase<TSettings>." +
                                      " It should have one sealed class derived from IPlugin.");
                    return null;
                }

                var pluginCore = Activator.CreateInstance(iPluginClasses[0]) as IPCore;
                if (pluginCore == null)
                {
                    Console.WriteLine($"Plugin (in {pluginRootDirectory}) {assembly} failed to instantiate IPCore-derived class.");
                    return null;
                }

                pluginCore.SetPluginDllLocation(pluginRootDirectory);
                return new PluginWithName(assembly.GetName().Name ?? string.Empty, pluginCore, alc);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error loading plugin {assembly.FullName} due to {e}");
                return null;
            }
        }

        internal static void RequestSaveAllSettings()
        {
            CoroutineHandler.RaiseEvent(GameHelperEvents.TimeToSaveAllSettings);
        }

        private static Dictionary<string, PluginMetadata> LoadPluginsMetadataDictionary()
        {
            var file = State.PluginsMetadataFile;
            file.Directory?.Create();
            if (!file.Exists)
            {
                return new Dictionary<string, PluginMetadata>();
            }

            try
            {
                var root = JObject.Parse(File.ReadAllText(file.FullName));
                var result = new Dictionary<string, PluginMetadata>(StringComparer.Ordinal);
                foreach (var prop in root.Properties())
                {
                    var enable = prop.Value["Enable"]?.Value<bool>() ?? false;
                    var autoStart = prop.Value["AutoStart"]?.Value<bool>() ?? enable;
                    result[prop.Name] = new PluginMetadata
                    {
                        Enable = autoStart,
                        AutoStart = autoStart,
                    };
                }

                if (result.Remove("RunecraftHelper", out var legacyRunecraft) && !result.ContainsKey("RuneforgeHelper"))
                {
                    result["RuneforgeHelper"] = legacyRunecraft;
                }

                if (result.Remove("Autopot", out var legacyAutopot) && !result.ContainsKey("AutoPot"))
                {
                    result["AutoPot"] = legacyAutopot;
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PManager] plugins.json load failed: {ex.Message}");
                return new Dictionary<string, PluginMetadata>();
            }
        }

        private static void LoadPluginMetadata(IEnumerable<PluginWithName> plugins)
        {
            var metadata = LoadPluginsMetadataDictionary();
            var newContainers = plugins.Select(
                x => new PluginContainer(
                    x.Name,
                    x.Plugin,
                    metadata.GetValueOrDefault(
                        x.Name,
                        new PluginMetadata()),
                    x.Alc)).ToList();

            lock (Plugins)
            {
                Plugins.AddRange(newContainers);
            }

            SavePluginMetadata();
        }

        private static void EnablePluginIfRequired(PluginContainer container)
        {
            if (!container.Metadata.AutoStart)
            {
                container.Metadata.Enable = false;
                return;
            }

            container.Metadata.Enable = true;

            try
            {
                container.Plugin.OnEnable(Core.Process.Address != IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Plugin '{container.Name}' konnte nicht gestartet werden: {ex.Message}");
                container.Metadata.Enable = false;
                container.Metadata.AutoStart = false;
                SavePluginMetadata();
            }
        }

        private static void SavePluginMetadata()
        {
            Dictionary<string, PluginMetadata> snapshot;
            lock (Plugins)
            {
                snapshot = Plugins.ToDictionary(x => x.Name, x => x.Metadata);
            }

            foreach (var meta in snapshot.Values)
            {
                meta.Enable = meta.AutoStart;
            }

            JsonHelper.SafeToFile(snapshot, State.PluginsMetadataFile);
        }

        private static IEnumerator<Wait> SavePluginMetadataCoroutine()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.TimeToSaveAllSettings);
                SavePluginMetadata();
            }
        }

        private static IEnumerator<Wait> SavePluginSettingsCoroutine()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.TimeToSaveAllSettings);
                PluginContainer[] snapshot;
                lock (Plugins)
                {
                    snapshot = Plugins.ToArray();
                }

                foreach (var container in snapshot)
                {
                    try
                    {
                        container.Plugin.SaveSettings();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[PManager.SavePluginSettingsCoroutine] {container.Name} threw on save: {ex}");
                    }
                }
            }
        }

        private static IEnumerator<Wait> DrawPluginUiRenderCoroutine()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnRender);
                if (CTOUtils.IsKeyPressedAndNotTimeout(Core.GHSettings.DisableAllRenderingKey))
                {
                    disableRendering = !disableRendering;
                }

                if (disableRendering)
                {
                    continue;
                }

                PluginContainer[] snapshot;
                lock (Plugins)
                {
                    snapshot = Plugins.ToArray();
                }

                foreach (var container in snapshot)
                {
                    if (container.Metadata.Enable)
                    {
                        try
                        {
                            using var _ = PerformanceProfiler.Profile(container.Plugin.GetType().FullName ?? string.Empty, "DrawUI");
                            container.Plugin.DrawUI();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[PManager.DrawPluginUiRenderCoroutine] {container.Name} threw: {ex}");
                        }
                    }
                }
            }
        }
    }
}
