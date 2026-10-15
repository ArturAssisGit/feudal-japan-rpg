using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class LootEntry
{
    public PlayerItemData item;
    [Range(0f, 1f)] public float dropChance = 0.5f;
    [Tooltip("Fewest copies dropped when the item drops (0 = may drop nothing)")] [Min(0)] public int minAmount = 1;
    [Tooltip("Most copies dropped when the item drops. Same as Min = always that exact amount (1 and 3 = between 1 and 3)")] [Min(0)] public int maxAmount = 1;

    // Drop Chance decides IF it drops, then the amount is a random number between Min and Max
    public int RollAmount()
    {
        int min = Mathf.Max(0, minAmount);
        int max = Mathf.Max(min, maxAmount);
        return Random.Range(min, max + 1);
    }
}

public class LootBag : MonoBehaviour
{
    public static readonly List<LootBag> All = new List<LootBag>();

    [Header("Contents")]
    public List<PlayerItemData> items = new List<PlayerItemData>();

    [Header("Interaction")]
    public float lootRange = 2f;

    public bool IsEmpty => items.Count == 0;

    // Set by LootChest: chests stay in the world when empty and can be locked
    [System.NonSerialized] public LootChest chest;
    public bool IsChest => chest != null;
    public bool IsLocked => chest != null && chest.IsLocked;

    // Bags dropped during the game (enemies, player) are not part of the scene's own loot
    [System.NonSerialized] public bool spawnedAtRuntime;
    private Vector3 startPosition;

    // Same bag/chest of the scene = same key in the save file
    public string SaveKey => gameObject.scene.name + "/" + name + "@" + Mathf.RoundToInt(startPosition.x * 10f) + "," + Mathf.RoundToInt(startPosition.y * 10f) + "," + Mathf.RoundToInt(startPosition.z * 10f);

    void Awake() => startPosition = transform.position;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Start()
    {
        items.RemoveAll(i => i == null);
        if (IsEmpty && !IsChest) Destroy(gameObject);
    }

    public int CountOf(PlayerItemData item) => items.FindAll(i => i == item).Count;

    // The different items in the bag (each one once), in the order they were added
    public List<PlayerItemData> DistinctItems()
    {
        var result = new List<PlayerItemData>();
        foreach (PlayerItemData item in items) if (item != null && !result.Contains(item)) result.Add(item);
        return result;
    }

    // Takes every copy of this item that fits in the inventory. Returns true if all of them were taken.
    public bool TakeItem(PlayerItemData item)
    {
        int before = CountOf(item);
        return before > 0 && TakeItemCount(item) == before;
    }

    // Returns how many copies of the item were taken
    int TakeItemCount(PlayerItemData item)
    {
        if (item == null || PlayerInventoryManager.Instance == null) return 0;

        int count = CountOf(item);
        if (count == 0) return 0;
        int taken = PlayerInventoryManager.Instance.AddItem(item, count);
        for (int i = 0; i < taken; i++) items.Remove(item);
        if (taken > 0) Debug.Log($"Looted: {item.itemName} x{taken}");
        if (IsEmpty && !IsChest) Destroy(gameObject);
        return taken;
    }

    // Returns how many items were taken in total
    public int TakeAll()
    {
        int taken = 0;
        foreach (PlayerItemData item in DistinctItems()) taken += TakeItemCount(item);
        return taken;
    }

    // ---------- Spawning ----------

    public static LootBag Spawn(GameObject prefab, Vector3 position, IEnumerable<PlayerItemData> contents, WorldCleanup.Kind cleanup = WorldCleanup.Kind.EnemyLoot)
    {
        GameObject bag = prefab != null ? Instantiate(prefab) : CreateDefaultBag();
        bag.name = "LootBag";
        bag.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

        foreach (Rigidbody rb in bag.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
        foreach (ItemPickup p in bag.GetComponentsInChildren<ItemPickup>()) Destroy(p);
        PlaceOnGround(bag.transform);

        if (!bag.TryGetComponent(out LootBag loot)) loot = bag.AddComponent<LootBag>();
        loot.spawnedAtRuntime = true;
        foreach (PlayerItemData item in contents) if (item != null) loot.items.Add(item);
        WorldCleanup.Add(bag, cleanup); // disappears after a while (WorldCleanup decides the time)
        return loot;
    }

    public static void DropFromPlayer(Transform player, PlayerItemData item, GameObject prefab, float mergeRadius, int amount = 1)
    {
        if (item == null || amount <= 0) return;
        var dropped = new List<PlayerItemData>();
        for (int i = 0; i < amount; i++) dropped.Add(item);

        LootBag nearby = FindNearest(player.position, mergeRadius);
        if (nearby != null && !nearby.IsEmpty)
        {
            nearby.items.AddRange(dropped);
            if (nearby.spawnedAtRuntime) WorldCleanup.Add(nearby.gameObject, WorldCleanup.Kind.PlayerDrop); // restarts its time
            Debug.Log($"Dropped into nearby bag: {item.itemName} x{amount}");
            return;
        }

        Spawn(prefab, player.position + player.forward * 1.2f, dropped, WorldCleanup.Kind.PlayerDrop);
        Debug.Log($"Dropped in a new bag: {item.itemName} x{amount}");
    }

    // A bag with something inside is close enough to loot (E opens it: other E actions wait)
    public static bool AnyInRange(Vector3 position) => WorldUtil.Nearest(All, position, b => b.lootRange, b => !b.IsEmpty) != null;

    public static LootBag FindNearest(Vector3 position, float maxDistance)
    {
        LootBag best = null;
        float bestDistance = maxDistance;
        foreach (LootBag bag in All)
        {
            if (bag == null || bag.IsChest) continue; // dropped items never go into chests
            float d = WorldUtil.FlatDistance(position, bag.transform.position);
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = bag;
            }
        }
        return best;
    }

    public static void PlaceOnGround(Transform bag)
    {
        RaycastHit[] hits = Physics.RaycastAll(bag.position + Vector3.up * 2f, Vector3.down, 20f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(bag) || hit.collider.GetComponentInParent<CharacterController>() != null
                || hit.collider.GetComponentInParent<EnemyAI_01>() != null) continue;
            bag.position = hit.point;
            break;
        }

        // Lift the bag so its bottom sits on the ground, wherever the model's pivot is
        Renderer[] renderers = bag.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
        bag.position += Vector3.up * (bag.position.y - bounds.min.y);
    }

    static GameObject CreateDefaultBag()
    {
        GameObject bag = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bag.transform.localScale = new Vector3(0.4f, 0.3f, 0.4f);
        bag.GetComponent<Renderer>().material.color = new Color(0.45f, 0.3f, 0.15f);
        return bag;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, lootRange);
    }
}
