namespace GameHelper.Utils
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    ///     Ring buffer for live activity messages (key input, logout, etc.).
    /// </summary>
    public static class ActivityLog
    {
        private const int MaxEntries = 500;
        private static readonly object Sync = new();
        private static readonly List<string> Entries = new();

        public static void Write(string category, string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] [{category}] {message}";
            lock (Sync)
            {
                Entries.Add(line);
                if (Entries.Count > MaxEntries)
                {
                    Entries.RemoveRange(0, Entries.Count - MaxEntries);
                }
            }

        }

        public static string[] Snapshot()
        {
            lock (Sync)
            {
                return Entries.ToArray();
            }
        }

        public static void Clear()
        {
            lock (Sync)
            {
                Entries.Clear();
            }
        }
    }
}
