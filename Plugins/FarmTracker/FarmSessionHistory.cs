namespace FarmTracker
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Newtonsoft.Json;

    internal static class FarmSessionHistory
    {
        public static List<ArchivedSessionSummary> LoadSummaries(string archiveDir)
        {
            var result = new List<ArchivedSessionSummary>();
            if (!Directory.Exists(archiveDir))
            {
                return result;
            }

            foreach (var path in Directory.EnumerateFiles(archiveDir, "session_*.json"))
            {
                try
                {
                    var rec = JsonConvert.DeserializeObject<SessionRecord>(File.ReadAllText(path));
                    if (rec == null)
                    {
                        continue;
                    }

                    var duration = (rec.EndUtc - rec.StartUtc).TotalSeconds;
                    if (duration < 0)
                    {
                        duration = rec.TotalActiveSeconds();
                    }

                    result.Add(new ArchivedSessionSummary
                    {
                        FileName = Path.GetFileName(path),
                        StartUtc = rec.StartUtc,
                        EndUtc = rec.EndUtc,
                        Maps = rec.Maps.Count,
                        ProfitDivine = rec.TotalDivine(),
                        DurationSec = duration,
                        ProfitPerHourDivine = rec.ProfitPerHourDivine(),
                    });
                }
                catch
                {
                    // skip corrupt files
                }
            }

            return result.OrderByDescending(s => s.StartUtc).ToList();
        }

        public static SessionRecord? LoadSession(string archiveDir, string fileName)
        {
            var path = Path.Join(archiveDir, fileName);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var rec = JsonConvert.DeserializeObject<SessionRecord>(File.ReadAllText(path));
                if (rec != null)
                {
                    rec.FilePath = path;
                }

                return rec;
            }
            catch
            {
                return null;
            }
        }

        public static void SaveSession(string archiveDir, SessionRecord rec)
        {
            Directory.CreateDirectory(archiveDir);
            var name = $"session_{rec.StartUtc:yyyyMMdd_HHmmss_fff}.json";
            var path = Path.Join(archiveDir, name);
            File.WriteAllText(path, JsonConvert.SerializeObject(rec, Formatting.Indented));
        }

        public static void DeleteSession(SessionRecord rec)
        {
            if (string.IsNullOrEmpty(rec.FilePath) || !File.Exists(rec.FilePath))
            {
                return;
            }

            try
            {
                File.Delete(rec.FilePath);
            }
            catch
            {
                // best effort
            }
        }

        public static void TrimSessions(string archiveDir, int maxSessions)
        {
            try
            {
                if (!Directory.Exists(archiveDir))
                {
                    return;
                }

                var files = Directory.GetFiles(archiveDir, "session_*.json");
                if (files.Length <= maxSessions)
                {
                    return;
                }

                Array.Sort(files, StringComparer.Ordinal);
                for (int i = 0; i < files.Length - maxSessions; i++)
                {
                    File.Delete(files[i]);
                }
            }
            catch
            {
                // ignore
            }
        }

        public static string FormatSummary(ArchivedSessionSummary s, Func<double, string> fmt, Func<TimeSpan, string> elapsed)
        {
            return $"{s.StartUtc.ToLocalTime():yyyy-MM-dd HH:mm} — {s.Maps} maps — {fmt(s.ProfitDivine)} — {elapsed(TimeSpan.FromSeconds(s.DurationSec))}";
        }
    }
}
