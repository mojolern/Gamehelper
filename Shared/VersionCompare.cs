namespace Shared.UpdateSecurity
{
    using System;
    using System.Linq;

    internal static class VersionCompare
    {
        internal static string Normalize(string version) =>
            version.Trim().TrimStart('v', 'V');

        internal static bool IsGreater(string left, string right) =>
            Compare(left, right) > 0;

        internal static bool IsGreaterOrEqual(string left, string right) =>
            Compare(left, right) >= 0;

        internal static bool IsLess(string left, string right) =>
            Compare(left, right) < 0;

        internal static bool EqualsNormalized(string left, string right) =>
            Compare(left, right) == 0;

        internal static int Compare(string left, string right)
        {
            var a = Parse(Normalize(left));
            var b = Parse(Normalize(right));
            var maxLen = Math.Max(a.Length, b.Length);
            for (var i = 0; i < maxLen; i++)
            {
                var av = i < a.Length ? a[i] : 0;
                var bv = i < b.Length ? b[i] : 0;
                if (av != bv)
                {
                    return av.CompareTo(bv);
                }
            }

            return 0;
        }

        private static int[] Parse(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return new[] { 0 };
            }

            return version
                .Split('.')
                .Select(part => int.TryParse(part, out var n) ? n : 0)
                .ToArray();
        }
    }
}
