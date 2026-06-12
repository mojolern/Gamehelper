namespace Launcher
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    internal static class UpdateErrors
    {
        internal static string Format(Exception ex)
        {
            var parts = new List<string>();
            for (var current = ex; current != null; current = current.InnerException)
            {
                var message = current.Message?.Trim();
                if (!string.IsNullOrEmpty(message) && !parts.Contains(message, StringComparer.Ordinal))
                {
                    parts.Add(message);
                }
            }

            if (parts.Count == 0)
            {
                parts.Add(ex.GetType().Name);
            }

            return string.Join(Environment.NewLine, parts);
        }
    }
}
