// <copyright file="DeployedObjectCounter.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    ///     Counts deployed objects per object-type id. PoE2 changed the deployed-object "type"
    ///     field from a small (0-255) enum into a full dat-row id (e.g. 22938), so a fixed
    ///     <c>int[256]</c> can no longer hold it. This is backed by a dictionary instead, and the
    ///     indexer returns 0 for ids that are not currently deployed — preserving the old
    ///     "absent == 0, never throws" semantics that AutoHotKeyTrigger's
    ///     <c>DeployedObjectsCount[id]</c> dynamic conditions rely on.
    /// </summary>
    public sealed class DeployedObjectCounter : IEnumerable<KeyValuePair<int, int>>
    {
        private static readonly Dictionary<int, string> CategoryNames = new()
        {
            { 22938, "Djinns" },
            { 10966, "Skeletons" },
            { 58392, "Spectres" },
            { 48349, "Totems" },
            { 62792, "Offerings" },
        };

        private readonly Dictionary<int, int> counts = new();

        public static string CategoryName(int type)
        {
            return CategoryNames.TryGetValue(type, out var name) ? name : "NO NAME MAPPING";
        }

        public int this[int type] => this.counts.TryGetValue(type, out var count) ? count : 0;

        internal void Clear()
        {
            this.counts.Clear();
        }

        internal void Increment(int type)
        {
            this.counts[type] = this.counts.TryGetValue(type, out var count) ? count + 1 : 1;
        }

        public IEnumerator<KeyValuePair<int, int>> GetEnumerator()
        {
            return this.counts.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
