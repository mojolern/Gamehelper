namespace FarmTracker
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using GameHelper;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using GameOffsets.Objects.States.InGameState;

    internal static class InventoryScanner
    {
        private static readonly InventoryName[] TrackedInventories =
        {
            InventoryName.MainInventory1,
            InventoryName.Currency1,
            InventoryName.EndgameSplinters1,
        };

        public static Dictionary<string, int> SnapshotCounts()
        {
            var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var serverData = Core.States.InGameStateObject.CurrentAreaInstance.ServerDataObject;
                if (serverData == null || serverData.Address == IntPtr.Zero)
                {
                    return totals;
                }

                var playerInventories = GetPlayerInventories(serverData);
                if (playerInventories == null)
                {
                    return totals;
                }

                var handleProp = typeof(GameProcess).GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic);
                var handle = handleProp?.GetValue(Core.Process);
                if (handle == null)
                {
                    return totals;
                }

                var handleType = handle.GetType();
                var readInvMethod = handleType.GetMethod("ReadMemory", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.MakeGenericMethod(typeof(InventoryStruct));
                var readVectorMethod = handleType.GetMethod("ReadStdVector", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.MakeGenericMethod(typeof(IntPtr));
                var readInvItemMethod = handleType.GetMethod("ReadMemory", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.MakeGenericMethod(typeof(InventoryItemStruct));
                if (readInvMethod == null || readVectorMethod == null || readInvItemMethod == null)
                {
                    return totals;
                }

                foreach (var invName in TrackedInventories)
                {
                    if (!playerInventories.TryGetValue(invName, out var invAddr) || invAddr == IntPtr.Zero)
                    {
                        continue;
                    }

                    var invObj = readInvMethod.Invoke(handle, new object[] { invAddr });
                    if (invObj is not InventoryStruct invStruct)
                    {
                        continue;
                    }

                    if (readVectorMethod.Invoke(handle, new object[] { invStruct.ItemList }) is not IntPtr[] slots)
                    {
                        continue;
                    }

                    foreach (var slotPtr in slots)
                    {
                        if (slotPtr == IntPtr.Zero)
                        {
                            continue;
                        }

                        if (readInvItemMethod.Invoke(handle, new object[] { slotPtr }) is not InventoryItemStruct invItem)
                        {
                            continue;
                        }

                        if (invItem.Item == IntPtr.Zero)
                        {
                            continue;
                        }

                        var item = Activator.CreateInstance(
                            typeof(Item),
                            BindingFlags.Instance | BindingFlags.NonPublic,
                            null,
                            new object[] { invItem.Item },
                            null) as Item;
                        if (item == null || string.IsNullOrEmpty(item.Path))
                        {
                            continue;
                        }

                        var key = ItemKey(item.Path);
                        var count = 1;
                        if (item.TryGetComponent<Stack>(out var stack, true) && stack.Count > 0)
                        {
                            count = stack.Count;
                        }

                        totals.TryGetValue(key, out var existing);
                        totals[key] = existing + count;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FarmTracker] inventory scan failed: {ex.Message}");
            }

            return totals;
        }

        private static Dictionary<InventoryName, IntPtr>? GetPlayerInventories(ServerData serverData)
        {
            var prop = typeof(ServerData).GetProperty("PlayerInventories", BindingFlags.Instance | BindingFlags.NonPublic);
            return prop?.GetValue(serverData) as Dictionary<InventoryName, IntPtr>;
        }

        public static string ItemKey(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            var slash = path.LastIndexOf('/');
            return slash >= 0 && slash < path.Length - 1 ? path[(slash + 1)..] : path;
        }

        public static string ReadableName(string path)
        {
            var key = ItemKey(path);
            if (FarmCurrencyCatalog.TryResolveItemName(key, out var catalogName))
            {
                return catalogName;
            }

            if (FarmPriceFetcher.TryResolveDisplayName(key, out var display) && !string.IsNullOrWhiteSpace(display))
            {
                return display;
            }

            return Humanize(key);
        }

        private static string Humanize(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "Unknown";
            }

            var result = new System.Text.StringBuilder();
            for (var i = 0; i < key.Length; i++)
            {
                var c = key[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(key[i - 1]))
                {
                    result.Append(' ');
                }

                result.Append(c);
            }

            return result.ToString();
        }
    }
}
