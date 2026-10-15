using System.Collections;
using UnityEngine;

// Put this on any object in the map scene (for example the PlayerUI) and drag items into "Items".
// When you press Play straight in the map scene, the player starts with them equipped (sword, hat, bow, armor, backpack, lantern).
// It does nothing when the game was started with New Game / Continue from the menu: New Game uses the
// World Save Game Manager's Starting Weapon / Starting Helmet, and Continue uses the saved equipment.
public class SceneStartingGear : MonoBehaviour
{
    [Tooltip("Items that start equipped. Each one goes to the right slot by its type (weapon, helmet, chest, bow, backpack, lantern)")]
    public PlayerItemData[] items;
    [Tooltip("Equip them even if the slot already has something (off = only fills empty slots)")] public bool replaceEquipped = false;
    [Tooltip("Also do it when the game was started from the menu (usually off)")] public bool alsoFromMenu = false;

    IEnumerator Start()
    {
        // Let the player and the UI start first
        for (int i = 0; i < 5; i++) yield return null;

        WorldSaveGameManager save = WorldSaveGameManager.instance;
        while (save != null && save.IsLoading) yield return null;
        if (save != null && save.StartedFromMenu && !alsoFromMenu) yield break;

        float timeout = Time.time + 5f;
        while ((PlayerEquipmentManager.Instance == null || PlayerInventoryManager.Instance == null) && Time.time < timeout) yield return null;
        if (PlayerEquipmentManager.Instance == null || PlayerInventoryManager.Instance == null) yield break;

        EquipAll();
    }

    void EquipAll()
    {
        if (items == null) return;
        SoundManager.Muted = true; // no equip sounds at the start
        try
        {
            // The backpack first: it decides how many backpack slots exist
            foreach (PlayerItemData item in items) if (item != null && item.type == ItemType.Backpack) Equip(item);
            foreach (PlayerItemData item in items) if (item != null && item.type != ItemType.Backpack) Equip(item);
        }
        finally { SoundManager.Muted = false; }
    }

    void Equip(PlayerItemData item)
    {
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        PlayerInventoryManager inv = PlayerInventoryManager.Instance;

        PlayerItemSlot gearSlot = GearSlotFor(gear, item.type);
        if (gearSlot == null) { Debug.LogWarning($"SceneStartingGear: '{item.name}' is not a piece of equipment (type {item.type}).", this); return; }
        if (gearSlot.itemData != null)
        {
            if (!replaceEquipped) return;
            gear.EquipItem(gearSlot); // clicking an equipped item unequips it
        }

        PlayerItemSlot temp = null;
        if (inv.inventorySlots != null)
            foreach (PlayerItemSlot s in inv.inventorySlots)
                if (s != null && s.IsEmpty) { temp = s; break; }
        if (temp == null) { Debug.LogWarning("SceneStartingGear: no free inventory slot to equip from.", this); return; }

        temp.SetItem(item, 1);
        gear.EquipItem(temp);
        if (temp.itemData == item) temp.ClearSlot();
    }

    static PlayerItemSlot GearSlotFor(PlayerEquipmentManager gear, ItemType type)
    {
        switch (type)
        {
            case ItemType.Weapon: return gear.weaponSlotUI;
            case ItemType.Chest: return gear.chestSlotUI;
            case ItemType.Helmet: return gear.helmetSlotUI;
            case ItemType.Bow: return gear.bowSlotUI;
            case ItemType.Backpack: return gear.backpackSlotUI;
            case ItemType.Lantern: return gear.lanternSlotUI;
            default: return null;
        }
    }
}
