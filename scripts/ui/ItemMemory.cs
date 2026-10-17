using Godot;
using System.Collections.Generic;

/// <summary>
/// 物品记忆（第三轮）：背包里每件东西都有一段「当时的记忆」。
/// 数据来自 data/memories.json——点位是死的，回忆是活的。
/// </summary>
public static class ItemMemory
{
    private static Dictionary<string, string> cache;

    public static string Get(string itemId)
    {
        if (cache == null)
        {
            cache = new Dictionary<string, string>();
            const string path = "res://data/memories.json";
            if (FileAccess.FileExists(path))
            {
                var json = FileAccess.GetFileAsString(path);
                var parsed = Json.ParseString(json);
                if (parsed.VariantType == Variant.Type.Dictionary)
                {
                    foreach (var kv in parsed.AsGodotDictionary())
                        cache[kv.Key.AsString()] = kv.Value.AsString();
                }
            }
        }
        return cache.TryGetValue(itemId, out var text) ? text : "";
    }
}
