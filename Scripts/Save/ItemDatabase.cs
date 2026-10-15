using System.Collections.Generic;
using UnityEngine;

// List of every item, so saves can store items by name; auto-filled in the Editor (Assets/Resources/ItemDatabase.asset).
public class ItemDatabase : ScriptableObject
{
    public List<PlayerItemData> items = new List<PlayerItemData>();

    private static Dictionary<string, PlayerItemData> lookup;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetLookup() => lookup = null;

    // The name saved in the file for this item (its asset file name)
    public static string IdOf(PlayerItemData item) => item != null ? item.name : null;

    public static PlayerItemData Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Build();
        if (lookup.TryGetValue(id, out PlayerItemData item) && item != null) return item;

        // Not found: maybe it was loaded later - look again once
        lookup = null;
        Build();
        lookup.TryGetValue(id, out item);
        if (item == null) Debug.LogWarning($"Save: item '{id}' not found (renamed or deleted?)");
        return item;
    }

    static void Build()
    {
        if (lookup != null) return;
        lookup = new Dictionary<string, PlayerItemData>();

        var db = Resources.Load<ItemDatabase>("ItemDatabase");
        if (db != null)
            foreach (PlayerItemData item in db.items)
                if (item != null && !lookup.ContainsKey(item.name)) lookup.Add(item.name, item);

        // Backup: every item already loaded in memory
        foreach (PlayerItemData item in Resources.FindObjectsOfTypeAll<PlayerItemData>())
            if (item != null && !item.IsUpgraded && !lookup.ContainsKey(item.name)) lookup.Add(item.name, item);
    }
}
