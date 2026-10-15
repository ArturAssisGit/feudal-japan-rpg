using System.Collections.Generic;
using UnityEngine;

// The one place that removes leftover objects from the world so they don't pile up:
// loot bags (from enemies and the ones you drop), arrows, enemy and animal bodies.
// Put it on any object of the scene to choose the times. Without one, it creates itself with the default times.
// PROTECTED (never removed): dropped backpacks, chests, and any bag with a backpack inside.
public class WorldCleanup : MonoBehaviour
{
    public enum Kind { EnemyLoot, PlayerDrop, Arrow, EnemyCorpse, AnimalCorpse }

    public static WorldCleanup Instance { get; private set; }

    [Header("Loot on the ground (minutes, 0 = never disappears)")]
    [Tooltip("Bags dropped by enemies")] [Min(0f)] public float enemyLootMinutes = 5f;
    [Tooltip("Bags with items you dropped from the inventory. Dropping more into a bag restarts its time")] [Min(0f)] public float playerDropMinutes = 5f;
    [Tooltip("Loot never disappears while the player is closer than this (meters), so it never vanishes in front of you or while you loot it")]
    [Min(0f)] public float keepLootWhilePlayerWithin = 10f;

    [Header("Other leftovers (seconds)")]
    [Min(0f)] public float arrowSeconds = 10f;
    [Tooltip("Dead enemies (time for the death animation)")] [Min(0f)] public float enemyCorpseSeconds = 5f;
    [Tooltip("Dead animals")] [Min(0f)] public float animalCorpseSeconds = 90f;

    private struct Entry
    {
        public Kind kind;
        public float dueTime;
    }

    private readonly Dictionary<GameObject, Entry> entries = new Dictionary<GameObject, Entry>();
    private readonly List<GameObject> toRemove = new List<GameObject>();
    private Transform player;
    private float nextCheck;
    private bool autoCreated;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // One placed in the scene wins over the automatic one (and keeps what it was tracking)
            if (Instance.autoCreated && !autoCreated)
            {
                WorldCleanup old = Instance;
                Instance = this;
                foreach (KeyValuePair<GameObject, Entry> e in old.entries) Track(e.Key, e.Value.kind);
                Destroy(old.gameObject);
                return;
            }
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ---------- Public ----------

    // Puts an object in the "trash": it is removed when its time is up. Adding it again restarts the time.
    public static void Add(GameObject go, Kind kind)
    {
        if (go == null) return;
        Get().Track(go, kind);
    }

    // Takes an object out of the trash (it stays in the world)
    public static void Keep(GameObject go)
    {
        if (go != null && Instance != null) Instance.entries.Remove(go);
    }

    // The backpack (and anything that holds one) must never be removed: the player would lose everything inside
    public static bool IsProtected(GameObject go)
    {
        if (go.GetComponentInParent<DroppedBackpack>() != null) return true;
        if (go.TryGetComponent(out LootBag bag))
        {
            if (bag.IsChest) return true;
            foreach (PlayerItemData item in bag.items)
                if (item != null && item.type == ItemType.Backpack) return true;
        }
        return false;
    }

    // ---------- Inside ----------

    static WorldCleanup Get()
    {
        if (Instance != null) return Instance;
        WorldCleanup inScene = FindAnyObjectByType<WorldCleanup>();
        if (inScene != null) { Instance = inScene; return inScene; }

        WorldCleanup created = new GameObject("WorldCleanup (auto)").AddComponent<WorldCleanup>();
        created.autoCreated = true;
        return created;
    }

    void Track(GameObject go, Kind kind)
    {
        if (go == null || IsProtected(go)) return;
        float seconds = SecondsFor(kind);
        if (seconds <= 0f) { entries.Remove(go); return; } // 0 = never
        entries[go] = new Entry { kind = kind, dueTime = Time.time + seconds };
    }

    float SecondsFor(Kind kind)
    {
        switch (kind)
        {
            case Kind.EnemyLoot: return enemyLootMinutes * 60f;
            case Kind.PlayerDrop: return playerDropMinutes * 60f;
            case Kind.Arrow: return arrowSeconds;
            case Kind.EnemyCorpse: return enemyCorpseSeconds;
            case Kind.AnimalCorpse: return animalCorpseSeconds;
            default: return 0f;
        }
    }

    static bool IsLoot(Kind kind) => kind == Kind.EnemyLoot || kind == Kind.PlayerDrop;

    void Update()
    {
        if (entries.Count == 0 || Time.time < nextCheck) return;
        nextCheck = Time.time + 0.2f;

        bool havePlayer = PlayerRef.Find(ref player);
        float keepSqr = keepLootWhilePlayerWithin * keepLootWhilePlayerWithin;

        toRemove.Clear();
        foreach (KeyValuePair<GameObject, Entry> e in entries)
        {
            GameObject go = e.Key;
            if (go == null) { toRemove.Add(go); continue; } // already gone (bag emptied, enemy removed...)
            if (Time.time < e.Value.dueTime) continue;

            if (IsLoot(e.Value.kind))
            {
                if (IsProtected(go)) { toRemove.Add(go); continue; } // a backpack ended up inside: keep it forever
                if (havePlayer && (go.transform.position - player.position).sqrMagnitude < keepSqr) continue; // waits until you walk away
            }

            Destroy(go);
            toRemove.Add(go);
        }
        foreach (GameObject go in toRemove) entries.Remove(go);
    }
}
