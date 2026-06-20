namespace FarmTracker
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Newtonsoft.Json;

    /// <summary>Maps game metadata ids to poe.ninja art ids (language-independent pricing).</summary>
    internal static class FarmMetaArt
    {
        private static Dictionary<string, string> metaToArt = new(StringComparer.Ordinal);

        internal static void Load(string dllDirectory)
        {
            metaToArt = new Dictionary<string, string>(StringComparer.Ordinal);
            var path = Path.Join(dllDirectory, "metaArt.json");
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                var map = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (map != null)
                {
                    metaToArt = new Dictionary<string, string>(map, StringComparer.Ordinal);
                }
            }
            catch
            {
                // optional bridge
            }
        }

        internal static string PriceKey(string itemKey)
        {
            if (string.IsNullOrEmpty(itemKey))
            {
                return string.Empty;
            }

            var seg = itemKey;
            if (metaToArt.TryGetValue(seg, out var art))
            {
                return art;
            }

            int s = seg.Length;
            while (s > 0 && seg[s - 1] >= '0' && seg[s - 1] <= '9')
            {
                s--;
            }

            if (s > 0 && s < seg.Length)
            {
                var stem = seg[..s];
                if (metaToArt.TryGetValue(stem, out var stemArt))
                {
                    return stemArt + seg[s..];
                }
            }

            return seg;
        }

        internal static bool HasBridge => metaToArt.Count > 0;
    }
}
