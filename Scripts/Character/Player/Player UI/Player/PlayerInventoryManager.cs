using System.Collections.Generic;
using UnityEngine;

public class PlayerInventoryManager : MonoBehaviour
{
    // Same rule as PlayerEquipmentManager: the newest active one, never an old one from another scene
    private static PlayerInventoryManager instance;
    public static PlayerInventoryManager Instance
    {
        get
        {
            if ((instance == null || !instance.isActiveAndEnabled) && Time.unscaledTime >= nextSearch)
            {
                PlayerInventoryManager best = FindBest();
                if (best != null) instance = best;
                else nextSearch = Time.unscaledTime + 0.5f; // none in this scene (menu): don't search every frame
            }
            return instance;
        }
        set => instance = value;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; nextSearch = 0f; }
    private static float nextSearch;

    static PlayerInventoryManager FindBest()
    {
        PlayerInventoryManager[] all = FindObjectsByType<PlayerInventoryManager>();
        if (all.Length == 0) return null;
        foreach (PlayerInventoryManager m in all)
            if (m.inventorySlots != null && m.inventorySlots.Length > 0 && m.inventorySlots[0] != null) return m;
        return all[0];
    }

    void OnEnable() { if (instance == null || !instance.isActiveAndEnabled) instance = this; }

    void OnDestroy() { if (instance == this) instance = null; }

    [Header("Inventory Slots")]
    public PlayerItemSlot[] inventorySlots;

    [Header("Backpack Storage (only usable while wearing a backpack)")]
    [Tooltip("Leave empty to find the slots inside BackpackInventoryGrid automatically")] public PlayerItemSlot[] backpackSlots;
    [Tooltip("Leave empty to find BackpackInventoryGrid automatically")] public GameObject backpackGridRoot;

    [Header("Drop System")]
    public Transform playerTransform;
    [Tooltip("Model used for loot bags (yours, or enemies' when they have none)")] public GameObject lootBagPrefab;
    [Tooltip("Dropped items go into an existing bag within this distance")] public float bagMergeRadius = 2.5f;

    void Awake()
    {
        instance = this; // the newest (the one of the scene just loaded) wins
        FindBackpackSlots();
    }

    void Start() => RefreshBackpackGrid();

    void FindBackpackSlots()
    {
        if (backpackGridRoot == null)
        {
            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
                if (t.name == "BackpackInventoryGrid" && t.gameObject.scene.IsValid()) { backpackGridRoot = t.gameObject; break; }
        }

        if ((backpackSlots == null || backpackSlots.Length == 0) && backpackGridRoot != null)
            backpackSlots = backpackGridRoot.GetComponentsInChildren<PlayerItemSlot>(true);
        backpackSlots ??= new PlayerItemSlot[0];
    }

    // Every backpack gives all the backpack slots (0 = no backpack)
    public int BackpackCapacity => PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.HasBackpackEquipped ? backpackSlots.Length : 0;

    // Shows only the slots the current backpack has, hides the grid with no backpack
    public void RefreshBackpackGrid()
    {
        int capacity = BackpackCapacity;
        if (backpackGridRoot != null) backpackGridRoot.SetActive(capacity > 0);
        for (int i = 0; i < backpackSlots.Length; i++)
            if (backpackSlots[i] != null) backpackSlots[i].gameObject.SetActive(i < capacity);
    }

    // True for the normal inventory slots and the visible backpack slots (not gear, shop, loot...). Used by drag and drop
    public bool IsStorageSlot(PlayerItemSlot slot)
    {
        if (slot == null || !slot.gameObject.activeInHierarchy) return false;
        return System.Array.IndexOf(inventorySlots, slot) >= 0 || System.Array.IndexOf(backpackSlots, slot) >= 0;
    }

    public bool BackpackHasItems
    {
        get
        {
            foreach (PlayerItemSlot slot in backpackSlots)
                if (slot != null && slot.itemData != null) return true;
            return false;
        }
    }

    // Empties the backpack slots and returns what was inside (used when dropping the backpack)
    public List<PlayerItemData> TakeBackpackContents()
    {
        var contents = new List<PlayerItemData>();
        foreach (PlayerItemSlot slot in backpackSlots)
        {
            if (slot == null || slot.itemData == null) continue;
            for (int i = 0; i < slot.amount; i++) contents.Add(slot.itemData);
            slot.ClearSlot();
        }
        return contents;
    }

    // Puts items back into the backpack slots (used when picking the backpack up)
    public void RestoreBackpackContents(List<PlayerItemData> contents)
    {
        if (contents == null) return;
        int capacity = BackpackCapacity;
        foreach (PlayerItemData item in contents)
            if (item != null && AddToSlots(backpackSlots, capacity, item, 1) == 0)
                Debug.LogWarning("Backpack is too small for all its items: " + item.itemName + " was lost");
    }

    // ---------- Counting / removing (used by the shop) ----------

    // All usable slots: normal inventory + the backpack grid (only while wearing a backpack)
    public IEnumerable<PlayerItemSlot> UsableSlots()
    {
        foreach (PlayerItemSlot slot in inventorySlots) if (slot != null) yield return slot;
        int capacity = BackpackCapacity;
        for (int i = 0; i < capacity && i < backpackSlots.Length; i++) if (backpackSlots[i] != null) yield return backpackSlots[i];
    }

    public int CountItem(PlayerItemData item)
    {
        if (item == null) return 0;
        int count = 0;
        foreach (PlayerItemSlot slot in UsableSlots()) if (slot.itemData == item) count += slot.amount;
        return count;
    }

    // Removes up to 'amount' of the item (backpack first) and returns how many were removed
    public int RemoveItem(PlayerItemData item, int amount)
    {
        if (item == null || amount <= 0) return 0;
        var slots = new List<PlayerItemSlot>(UsableSlots());
        slots.Reverse();
        int removed = 0;
        foreach (PlayerItemSlot slot in slots)
        {
            while (removed < amount && slot.itemData == item)
            {
                slot.RemoveOne();
                removed++;
            }
            if (removed >= amount) break;
        }
        return removed;
    }

    // Every different item you carry (each one once), in inventory order
    public List<PlayerItemData> DistinctItems()
    {
        var result = new List<PlayerItemData>();
        foreach (PlayerItemSlot slot in UsableSlots())
            if (slot.itemData != null && !result.Contains(slot.itemData)) result.Add(slot.itemData);
        return result;
    }

    public bool AddItem(PlayerItemData itemData) => AddItem(itemData, 1) == 1;

    // Adds up to 'amount' items and returns how many fit.
    // Stackables fill existing stacks first, then empty slots (inventory first, then backpack).
    public int AddItem(PlayerItemData itemData, int amount)
    {
        if (itemData == null || amount <= 0) return 0;

        // A backpack never goes into the inventory: it goes straight onto your back (replacing the one you wear)
        if (itemData.type == ItemType.Backpack)
        {
            PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
            return gear != null && gear.EquipNewBackpack(itemData) ? 1 : 0;
        }

        int left = amount;
        if (itemData.IsStackable)
        {
            left -= FillStacks(inventorySlots, inventorySlots.Length, itemData, left);
            left -= FillStacks(backpackSlots, BackpackCapacity, itemData, left);
        }
        left -= FillEmpty(inventorySlots, inventorySlots.Length, itemData, left);
        left -= FillEmpty(backpackSlots, BackpackCapacity, itemData, left);

        if (left > 0) Debug.Log("Inventory is full!");
        return amount - left;
    }

    int AddToSlots(PlayerItemSlot[] slots, int count, PlayerItemData item, int amount)
    {
        int added = item.IsStackable ? FillStacks(slots, count, item, amount) : 0;
        return added + FillEmpty(slots, count, item, amount - added);
    }

    static int FillStacks(PlayerItemSlot[] slots, int count, PlayerItemData item, int amount)
    {
        int added = 0;
        for (int i = 0; i < count && i < slots.Length && added < amount; i++)
        {
            PlayerItemSlot slot = slots[i];
            if (slot == null || slot.itemData != item) continue;
            int put = Mathf.Min(slot.SpaceFor(item), amount - added);
            if (put <= 0) continue;
            slot.AddAmount(put);
            added += put;
        }
        return added;
    }

    static int FillEmpty(PlayerItemSlot[] slots, int count, PlayerItemData item, int amount)
    {
        int added = 0;
        for (int i = 0; i < count && i < slots.Length && added < amount; i++)
        {
            PlayerItemSlot slot = slots[i];
            if (slot == null || slot.itemData != null) continue;
            int put = Mathf.Min(item.MaxStack, amount - added);
            slot.SetItem(item, put);
            added += put;
        }
        return added;
    }

    public void DropItem(PlayerItemSlot slot) => DropItem(slot, false);

    // Drops the whole stack (or only one item) in a loot bag on the ground
    public void DropItem(PlayerItemSlot slot, bool onlyOne)
    {
        if (slot == null || slot.itemData == null) return;

        if (!PlayerRef.Find(ref playerTransform))
        {
            Debug.LogWarning("Can't drop: no object with the Player tag and Player Transform is empty in PlayerInventoryManager!");
            return;
        }

        // Equipped items can be dropped too
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (gear != null && gear.IsGearSlot(slot))
        {
            if (gear.IsBackpackSlot(slot))
            {
                gear.DropBackpack(); // the backpack falls with everything inside (pick it up again with E)
                return;
            }

            PlayerItemData equipped = slot.itemData;
            gear.ClearGearSlot(slot);
            LootBag.DropFromPlayer(playerTransform, equipped, lootBagPrefab, bagMergeRadius, 1);
            return;
        }

        LootBag.DropFromPlayer(playerTransform, slot.itemData, lootBagPrefab, bagMergeRadius, onlyOne ? 1 : slot.amount);
        if (onlyOne) slot.RemoveOne();
        else slot.ClearSlot();
    }
}
