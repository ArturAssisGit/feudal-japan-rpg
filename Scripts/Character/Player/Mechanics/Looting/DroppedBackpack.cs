using System.Collections.Generic;
using TMPro;
using UnityEngine;

// A backpack lying on the ground (dropped with R). Walk up to it: "[E] Equip backpack".
// Backpacks never go into the inventory: picking one up puts it on your back
// (if you wear another one, that one is left on the ground in its place, with its items).
public class DroppedBackpack : MonoBehaviour
{
    public PlayerItemData backpackItem;
    [Tooltip("Items that were inside the backpack when it was dropped")] public List<PlayerItemData> contents = new List<PlayerItemData>();
    public float pickupRange = 2f;
    [HideInInspector] [Tooltip("Set when the backpack was placed in the scene: it does not come back once picked up")] public string pickedKey;

    // Every backpack lying in the world (the map shows them)
    public static readonly List<DroppedBackpack> All = new List<DroppedBackpack>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    public static DroppedBackpack Spawn(PlayerItemData item, Vector3 position, Quaternion rotation, Vector3 worldScale = default)
    {
        GameObject obj = item.itemPrefab != null ? Instantiate(item.itemPrefab) : GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = item.itemName + " (Dropped)";
        obj.transform.SetPositionAndRotation(position, rotation);
        if (worldScale != Vector3.zero) obj.transform.localScale = worldScale;

        foreach (Rigidbody rb in obj.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
        foreach (ItemPickup p in obj.GetComponentsInChildren<ItemPickup>()) Destroy(p);
        LootBag.PlaceOnGround(obj.transform);

        DroppedBackpack dropped = obj.AddComponent<DroppedBackpack>();
        dropped.backpackItem = item;
        BackpackPickupPrompt.Ensure();
        return dropped;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, pickupRange);
    }
}

// Shows "[E] Equip backpack" next to the closest dropped backpack and picks it up (created automatically)
public class BackpackPickupPrompt : MonoBehaviour
{
    public static BackpackPickupPrompt Instance { get; private set; }

    public KeyCode pickupKey = KeyCode.E;
    [Tooltip("Text when you have no backpack on")] public string equipText = "Equip backpack";
    [Tooltip("Text when you already wear one (yours is left on the ground)")] public string swapText = "Swap backpack";

    private Transform player;
    private PlayerStats stats;
    private TextMeshProUGUI promptText;
    private DroppedBackpack shownTarget;
    private bool shownSwap;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() => Ensure();

    public static void Ensure()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<BackpackPickupPrompt>("BackpackPickupPrompt");
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start() => BuildUI();

    void Update()
    {
        if (promptText == null) return;

        DroppedBackpack target = FindTarget();
        bool show = target != null;
        if (promptText.gameObject.activeSelf != show) promptText.gameObject.SetActive(show);
        if (target == null) { shownTarget = null; return; }

        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        bool swap = gear != null && gear.HasBackpackEquipped;
        if (target != shownTarget || swap != shownSwap)
        {
            shownTarget = target;
            shownSwap = swap;
            promptText.text = $"[{pickupKey}] {(swap ? swapText : equipText)}"; // rebuilt only when something changes
        }

        if (Input.GetKeyDown(pickupKey)) PickUp(target);
    }

    bool FindPlayer()
    {
        if (!PlayerRef.Find(ref player)) return false;
        stats = PlayerRef.Stats;
        return true;
    }

    DroppedBackpack FindTarget()
    {
        if (DroppedBackpack.All.Count == 0 || !FindPlayer()) return null;
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (gear == null || gear.IsOpen || LootWindow.IsOpen || PlacementSystem.IsPlacing || HorseMount.PlayerMounted || PlayerFinisher.IsActive || ScreenFade.IsActive) return null;
        if (stats != null && stats.IsDead) return null;

        return WorldUtil.Nearest(DroppedBackpack.All, player.position, b => b.pickupRange, b => b.backpackItem != null);
    }

    void PickUp(DroppedBackpack target)
    {
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (gear == null || !gear.PickUpBackpack(target.backpackItem, target.contents)) return;
        GatherAnimation.PlayOnce();
        if (!string.IsNullOrEmpty(target.pickedKey)) ItemPickup.PickedKeys.Add(target.pickedKey); // saved with the game
        Destroy(target.gameObject);
        shownTarget = null;
    }

    void BuildUI()
    {
        Transform canvas = UIKit.CreateCanvas("BackpackPromptCanvas", transform, 48).transform;
        promptText = UIKit.CornerText("BackpackPrompt", canvas, 0, true);
    }
}
