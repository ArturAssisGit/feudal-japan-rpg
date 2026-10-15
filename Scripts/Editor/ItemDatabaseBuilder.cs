using System.Linq;
using UnityEditor;
using UnityEngine;

// Keeps Assets/Resources/ItemDatabase.asset in sync with every PlayerItemData; runs on script reload and on Play.
[InitializeOnLoad]
public static class ItemDatabaseBuilder
{
    const string Folder = "Assets/Resources";
    const string AssetPath = Folder + "/ItemDatabase.asset";

    static ItemDatabaseBuilder()
    {
        EditorApplication.delayCall += Rebuild;
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode) Rebuild(); };
    }

    [MenuItem("Tools/Save System/Rebuild Item Database")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlaying) return;

        var items = AssetDatabase.FindAssets("t:PlayerItemData")
            .Select(guid => AssetDatabase.LoadAssetAtPath<PlayerItemData>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(item => item != null)
            .OrderBy(item => item.name)
            .ToList();

        // Two items with the same file name can't be told apart in the save file
        foreach (var group in items.GroupBy(i => i.name).Where(g => g.Count() > 1))
            Debug.LogWarning($"Save system: {group.Count()} items are called '{group.Key}'. Rename one of them so saves load the right item.");

        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Resources");

        var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetPath);
        if (db == null)
        {
            db = ScriptableObject.CreateInstance<ItemDatabase>();
            db.items = items;
            AssetDatabase.CreateAsset(db, AssetPath);
            AssetDatabase.SaveAssets();
            return;
        }

        if (db.items.SequenceEqual(items)) return;
        db.items = items;
        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();
    }
}
