using System.Collections.Generic;
using UnityEngine;

public class ItemPickup : MonoBehaviour
{
    [Header("Item")]
    public PlayerItemData itemData;

    [Header("Interaction")]
    public KeyCode pickupKey = KeyCode.E;
    public float pickupRange = 1.8f;

    [Header("Save")]
    [Tooltip("On = once picked up it never comes back in this save (also when playing in a friend's world). Off = it is back every time the game loads")]
    public bool pickOnlyOnce = true;

    private static Transform player;

    // Items placed in the world that THIS character already picked up (saved in its own save file)
    public static readonly HashSet<string> PickedKeys = new HashSet<string>();
    private string saveKey;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        PickedKeys.Clear();
        player = null;
    }

    // Same item at the same starting spot in the same scene = same key (works without IDs on the objects)
    void Awake()
    {
        Vector3 p = transform.position;
        saveKey = $"{gameObject.scene.name}|{(itemData != null ? itemData.name : name)}|{Mathf.RoundToInt(p.x * 10f)},{Mathf.RoundToInt(p.y * 10f)},{Mathf.RoundToInt(p.z * 10f)}";
    }

    void Start()
    {
        if (pickOnlyOnce && PickedKeys.Contains(saveKey)) { Destroy(gameObject); return; }

        // A backpack placed in the scene works like a dropped one: "[E] Equip backpack", never goes into the inventory
        if (itemData != null && itemData.type == ItemType.Backpack)
        {
            DroppedBackpack bag = GetComponent<DroppedBackpack>();
            if (bag == null) bag = gameObject.AddComponent<DroppedBackpack>();
            bag.backpackItem = itemData;
            bag.pickupRange = Mathf.Max(bag.pickupRange, pickupRange);
            bag.pickedKey = pickOnlyOnce ? saveKey : null;
            BackpackPickupPrompt.Ensure();
            enabled = false; // the pickup is made by the backpack prompt
        }
    }

    // Loading a save (or a new game with null): items already picked up by this character disappear
    public static void Restore(List<string> picked)
    {
        PickedKeys.Clear();
        if (picked != null) foreach (string key in picked) if (!string.IsNullOrEmpty(key)) PickedKeys.Add(key);
        foreach (ItemPickup pickup in FindObjectsByType<ItemPickup>())
            if (pickup.pickOnlyOnce && pickup.saveKey != null && PickedKeys.Contains(pickup.saveKey)) Destroy(pickup.gameObject);
    }

    void Update()
    {
        if (itemData == null || !Input.GetKeyDown(pickupKey) || ScreenFade.IsActive) return;

        if (!PlayerRef.Find(ref player)) return;

        if (WorldUtil.FlatDistance(transform.position, player.position) <= pickupRange) TryPickup();
    }

    void TryPickup()
    {
        bool added = PlayerInventoryManager.Instance != null && PlayerInventoryManager.Instance.AddItem(itemData);
        Debug.Log((added ? "Picked up: " : "Could not pick up (inventory full?): ") + itemData.itemName);
        if (!added) return;
        if (pickOnlyOnce && saveKey != null) PickedKeys.Add(saveKey);
        GatherAnimation.PlayOnce();
        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, pickupRange);
    }
}
