// <copyright file="LoadedFiles.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Threading;
    using System.Threading.Tasks;
    using Coroutine;
    using CoroutineEvents;
    using GameOffsets.Objects;
    using ImGuiNET;
    using Utils;

    /// <summary>
    ///     Gathers the files loaded in the game for the current area.
    /// </summary>
    public class LoadedFiles : RemoteObjectBase
    {
        private readonly ConcurrentDictionary<int, int> lastAreaCountHistogram = new();
        private readonly ConcurrentQueue<(string Name, int AreaChangeCount)> scannedPathCandidates = new();
        private readonly ConcurrentQueue<string> lastSampleNames = new();
        private bool areaAlreadyDone;
        private string areaHashCache = string.Empty;
        private string filename = string.Empty;
        private string searchText = string.Empty;
        private string[] searchTextSplit = Array.Empty<string>();
        private int lastBucketsScanned;
        private int lastFileNodesSeen;
        private int lastMatchedFiles;
        private int lastRejectedBeforeIgnore;
        private int lastRejectedCounterMismatch;
        private int lastRootObjectCount;
        private int lastAreaChangeCounter;
        private int lastFallbackAreaChangeCounter;
        private IntPtr lastAddress;
        private IntPtr lastRootPointer;
        private string lastScanReason = "never";
        private string lastScanAreaHash = string.Empty;
        private string lastScanAreaName = string.Empty;
        private string lastScanError = string.Empty;
        private DateTime lastScanUtc = DateTime.MinValue;

        /// <summary>
        ///     Initializes a new instance of the <see cref="LoadedFiles" /> class.
        /// </summary>
        /// <param name="address">address of the remote memory object.</param>
        internal LoadedFiles(IntPtr address)
            : base(address)
        {
            Core.CoroutinesRegistrar.Add(CoroutineHandler.Start(
                this.OnAreaChange(), "[LoadedFiles] Gather Preload Data", int.MaxValue - 1));
        }

        /// <summary>
        ///     Gets the pathname of the files.
        /// </summary>
        public ConcurrentDictionary<string, int> PathNames { get; }

            = new();

        /// <summary>
        ///     Converts the <see cref="LoadedFiles" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Total Loaded Files in current area: {this.PathNames.Count}");
            ImGui.TextWrapped("NOTE: The Overlay caches the preloads when you enter a new map. " +
                              "This cache is only cleared & updated when you enter a new Map. Going to town or " +
                              "hideout isn't considered a new Map. So basically you can find important preloads " +
                              "even after you have completed the whole map/gone to town/hideouts and " +
                              "entered the same Map again.");

            if (ImGui.CollapsingHeader("Diagnostics", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Text($"Last scan: {this.lastScanReason} {(this.lastScanUtc == DateTime.MinValue ? "never" : this.lastScanUtc.ToLocalTime().ToString("HH:mm:ss"))}");
                ImGui.Text($"Area: {this.lastScanAreaName} hash={this.lastScanAreaHash}");
                ImGuiHelper.IntPtrToImGui("File Root static address", this.lastAddress);
                ImGuiHelper.IntPtrToImGui("File Root table pointer", this.lastRootPointer);
                ImGui.Text($"AreaChangeCounter: {this.lastAreaChangeCounter}");
                if (this.lastFallbackAreaChangeCounter > 0)
                {
                    ImGui.TextColored(new Vector4(0.35f, 1f, 0.35f, 1f), $"Fallback AreaChangeCount: {this.lastFallbackAreaChangeCounter}");
                }
                ImGui.Text($"Root objects: {this.lastRootObjectCount}  Buckets scanned: {this.lastBucketsScanned}");
                ImGui.Text($"File nodes seen: {this.lastFileNodesSeen}  Matched current area: {this.lastMatchedFiles}");
                ImGui.Text($"Rejected before ignore threshold: {this.lastRejectedBeforeIgnore}  Counter mismatch: {this.lastRejectedCounterMismatch}");
                if (!string.IsNullOrWhiteSpace(this.lastScanError))
                {
                    ImGui.TextColored(new Vector4(1f, 0.35f, 0.25f, 1f), $"Last error: {this.lastScanError}");
                }

                if (ImGui.Button("Rescan loaded files now"))
                {
                    try
                    {
                        this.ScanCurrentArea("manual", force: true);
                    }
                    catch (Exception ex)
                    {
                        this.lastScanError = ex.Message;
                        Console.WriteLine($"[LoadedFiles.ManualRescan] {ex}");
                    }
                }

                if (this.lastAreaCountHistogram.Count > 0 && ImGui.TreeNode("AreaChangeCount samples"))
                {
                    foreach (var kv in this.lastAreaCountHistogram.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(20))
                    {
                        ImGui.Text($"{kv.Key}: {kv.Value}");
                    }

                    ImGui.TreePop();
                }

                if (!this.lastSampleNames.IsEmpty && ImGui.TreeNode("Matched file samples"))
                {
                    foreach (var sample in this.lastSampleNames.Take(20))
                    {
                        ImGui.TextWrapped(sample);
                    }

                    ImGui.TreePop();
                }
            }

            ImGui.Text("File Name: ");
            ImGui.SameLine();
            ImGui.InputText("##filename", ref this.filename, 100);
            ImGui.SameLine();
            if (!this.areaAlreadyDone)
            {
                if (ImGui.Button("Save"))
                {
                    var dir_name = "preload_dumps";
                    Directory.CreateDirectory(dir_name);
                    var dataToWrite = this.PathNames.Keys.ToList();
                    dataToWrite.Sort();
                    File.WriteAllText(
                        Path.Join(dir_name, this.filename),
                        string.Join("\n", dataToWrite));
                    this.areaAlreadyDone = true;
                }
            }
            else
            {
                ImGuiHelper.DrawDisabledButton("Save");
            }

            ImGui.Text("Search:    ");
            ImGui.SameLine();
            if (ImGui.InputText("##LoadedFiles", ref this.searchText, 50))
            {
                this.searchTextSplit = this.searchText.ToLower().Split(",", StringSplitOptions.RemoveEmptyEntries);
            }

            ImGui.Text("NOTE: Search is Case-Insensitive. Use commas (,) to narrow down the resulting files.");
            if (!string.IsNullOrEmpty(this.searchText))
            {
                var resultVisible = ImGui.BeginChild("Result##loadedfiles", Vector2.Zero, ImGuiChildFlags.Borders);
                if (resultVisible)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                    foreach (var kv in this.PathNames)
                    {
                        var containsAll = true;
                        for (var i = 0; i < this.searchTextSplit.Length; i++)
                        {
                            if (!kv.Key.ToLower().Contains(this.searchTextSplit[i]))
                            {
                                containsAll = false;
                            }
                        }

                        if (containsAll)
                        {
                            if (ImGui.SmallButton($"AreaId: {kv.Value} Path: {kv.Key}"))
                            {
                                ImGui.SetClipboardText(kv.Key);
                            }
                        }
                    }

                    ImGui.PopStyleColor();
                }

                ImGui.EndChild();
            }
        }

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            this.PathNames.Clear();
            this.areaHashCache = string.Empty;
            this.areaAlreadyDone = false;
            this.filename = string.Empty;
            this.ResetDiagnostics("cleanup");
        }

        /// <summary>
        ///     this function is overrided to do nothing because it's done @ AreaChange event.
        /// </summary>
        /// <param name="hasAddressChanged">ignore me.</param>
        protected override void UpdateData(bool hasAddressChanged) { }

        private LoadedFilesRootObject[] GetAllPointers()
        {
            var totalFiles = LoadedFilesRootObject.TotalCount;
            var reader = Core.Process.Handle;
            this.lastAddress = this.Address;
            this.lastRootPointer = reader.ReadMemory<IntPtr>(this.Address);
            this.lastRootObjectCount = totalFiles;
            return reader.ReadMemoryArray<LoadedFilesRootObject>(
                this.lastRootPointer, totalFiles);
        }

        private void ScanForFilesParallel(SafeMemoryHandle reader, LoadedFilesRootObject filesRootObj)
        {
            Interlocked.Increment(ref this.lastBucketsScanned);
            var filesPtr = reader.ReadStdBucket<FilesPointerStructure>(filesRootObj.LoadedFiles);
            Parallel.ForEach(filesPtr, fileNode =>
            {
                this.AddFileIfLoadedInCurrentArea(reader, fileNode.FilesPointer);
            });
        }

        private void AddFileIfLoadedInCurrentArea(SafeMemoryHandle reader, IntPtr address)
        {
            if (address == IntPtr.Zero)
            {
                return;
            }

            Interlocked.Increment(ref this.lastFileNodesSeen);
            var information = reader.ReadMemory<FileInfoValueStruct>(address);
            this.lastAreaCountHistogram.AddOrUpdate(information.AreaChangeCount, 1, (_, value) => value + 1);

            if (information.AreaChangeCount <= 0)
            {
                Interlocked.Increment(ref this.lastRejectedBeforeIgnore);
                return;
            }

            var name = reader.ReadStdWString(information.Name).Split('@')[0];
            this.scannedPathCandidates.Enqueue((name, information.AreaChangeCount));

            if (information.AreaChangeCount == Core.AreaChangeCounter.Value)
            {
                this.AddMatchedFile(name, information.AreaChangeCount);
                return;
            }

            if (information.AreaChangeCount <= FileInfoValueStruct.IGNORE_FIRST_X_AREAS)
            {
                Interlocked.Increment(ref this.lastRejectedBeforeIgnore);
                return;
            }

            Interlocked.Increment(ref this.lastRejectedCounterMismatch);
        }

        private bool ScanCurrentArea(string reason, bool force)
        {
            if (this.Address == IntPtr.Zero)
            {
                this.ResetDiagnostics(reason);
                this.lastScanError = "File Root address is zero";
                return false;
            }

            LoadedFilesRootObject[] filesRootObjs;
            SafeMemoryHandle reader;
            try
            {
                var areaHash = Core.States.InGameStateObject.CurrentAreaInstance.AreaHash;
                var iH = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails.IsHideout;
                var iT = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails.IsTown;
                var name = Core.States.AreaLoading.CurrentAreaName;
                if (!force && ((iH && Core.GHSettings.SkipPreloadedFilesInHideout) || iT || areaHash == this.areaHashCache))
                {
                    return false;
                }

                this.CleanUpData();
                this.ResetDiagnostics(reason);
                this.filename = $"{name}_{areaHash}.txt";
                this.areaAlreadyDone = false;
                this.areaHashCache = areaHash;
                this.lastScanAreaHash = areaHash;
                this.lastScanAreaName = name;
                this.lastAreaChangeCounter = Core.AreaChangeCounter.Value;

                filesRootObjs = this.GetAllPointers();
                reader = Core.Process.Handle;
            }
            catch (Exception ex)
            {
                this.lastScanError = ex.Message;
                Console.WriteLine($"[LoadedFiles.ScanCurrentArea.setup] {ex}");
                return false;
            }

            for (var i = 0; i < filesRootObjs.Length; i++)
            {
                try
                {
                    this.ScanForFilesParallel(reader, filesRootObjs[i]);
                }
                catch (Exception ex)
                {
                    this.lastScanError = ex.Message;
                    Console.WriteLine($"[LoadedFiles.ScanCurrentArea.scan] {ex}");
                }
            }

            if (this.lastMatchedFiles == 0)
            {
                this.ApplyFallbackLoadedFiles();
            }

            try
            {
                CoroutineHandler.RaiseEvent(HybridEvents.PreloadsUpdated);
            }
            catch (Exception ex)
            {
                this.lastScanError = ex.Message;
                Console.WriteLine($"[LoadedFiles.ScanCurrentArea.raise] {ex}");
            }

            return true;
        }

        private void AddMatchedFile(string name, int areaChangeCount)
        {
            this.PathNames.AddOrUpdate(name, areaChangeCount,
                (key, oldValue) => { return Math.Max(oldValue, areaChangeCount); });
            Interlocked.Increment(ref this.lastMatchedFiles);
            if (this.lastSampleNames.Count < 25)
            {
                this.lastSampleNames.Enqueue(name);
            }
        }

        private void ApplyFallbackLoadedFiles()
        {
            var candidates = this.scannedPathCandidates
                .Where(candidate => candidate.AreaChangeCount > 0)
                .ToArray();
            if (candidates.Length == 0)
            {
                return;
            }

            var fallbackAreaChangeCount = candidates
                .GroupBy(candidate => candidate.AreaChangeCount)
                .OrderByDescending(group => group.Key)
                .First().Key;

            foreach (var candidate in candidates.Where(candidate => candidate.AreaChangeCount == fallbackAreaChangeCount))
            {
                this.AddMatchedFile(candidate.Name, candidate.AreaChangeCount);
            }

            this.lastFallbackAreaChangeCounter = fallbackAreaChangeCount;
            this.lastScanError = $"Exact AreaChangeCounter match was empty; using newest loaded-file bucket {fallbackAreaChangeCount}.";
        }

        private void ResetDiagnostics(string reason)
        {
            this.lastScanReason = reason;
            this.lastScanUtc = DateTime.UtcNow;
            this.lastScanError = string.Empty;
            this.lastBucketsScanned = 0;
            this.lastFileNodesSeen = 0;
            this.lastMatchedFiles = 0;
            this.lastRejectedBeforeIgnore = 0;
            this.lastRejectedCounterMismatch = 0;
            this.lastRootObjectCount = 0;
            this.lastAreaChangeCounter = Core.AreaChangeCounter.Value;
            this.lastFallbackAreaChangeCounter = 0;
            this.lastAddress = this.Address;
            this.lastRootPointer = IntPtr.Zero;
            this.lastAreaCountHistogram.Clear();
            while (this.lastSampleNames.TryDequeue(out _))
            {
            }

            while (this.scannedPathCandidates.TryDequeue(out _))
            {
            }
        }

        private IEnumerator<Wait> OnAreaChange()
        {
            while (true)
            {
                yield return new Wait(RemoteEvents.AreaChanged);
                this.ScanCurrentArea("area-change", force: false);
                yield return new Wait(0d);
            }
        }
    }
}
