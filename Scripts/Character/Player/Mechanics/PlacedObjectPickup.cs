using TMPro;
using UnityEngine;

// Auto-created. Walk up to something you placed (bonfire, tent, crafting table...) and press the key to put it back in your inventory.
public class PlacedObjectPickup : MonoBehaviour
{
    public static PlacedObjectPickup Instance { get; private set; }

    [Tooltip("Key that picks the object up")] public KeyCode pickupKey = KeyCode.T;
    [Tooltip("How close you must be (meters, measured to the edge of the object)")] public float range = 2.5f;
    [Tooltip("Seconds the message ('added to inventory', 'inventory is full') stays on screen")] public float messageSeconds = 2f;

    private Transform player;
    private PlayerStats stats;
    private TextMeshProUGUI promptText, messageText;
    private float messageUntil;
    private PlacedObject shownTarget;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<PlacedObjectPickup>("PlacedObjectPickup");
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
        if (messageText.text.Length > 0 && Time.unscaledTime > messageUntil) messageText.text = "";

        PlacedObject target = FindTarget();
        promptText.gameObject.SetActive(target != null);
        if (target == null) { shownTarget = null; return; }

        if (target != shownTarget)
        {
            shownTarget = target;
            promptText.text = $"[{pickupKey}] Pick up {target.Item.itemName}"; // rebuilt only when the target changes
        }
        if (Input.GetKeyDown(pickupKey) && !ScreenFade.IsActive) PickUp(target);
    }

    bool FindPlayer()
    {
        if (!PlayerRef.Find(ref player)) return false;
        stats = PlayerRef.Stats;
        return true;
    }

    PlacedObject FindTarget()
    {
        if (PlacedObject.All.Count == 0 || !FindPlayer()) return null;
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if ((gear != null && gear.IsOpen) || PlacementSystem.IsPlacing || HorseMount.PlayerMounted || PlayerFinisher.IsActive) return null;
        if (stats != null && stats.IsDead) return null;

        PlacedObject best = null;
        float bestDistance = range;
        foreach (PlacedObject placed in PlacedObject.All)
        {
            if (placed == null || placed.Item == null) continue;
            float d = placed.DistanceTo(player.position);
            if (d <= bestDistance) { bestDistance = d; best = placed; }
        }
        return best;
    }

    void PickUp(PlacedObject target)
    {
        PlayerItemData item = target.Item;
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (item == null || inventory == null) return;

        if (inventory.AddItem(item, 1) < 1)
        {
            ShowMessage("Inventory is full");
            return;
        }
        Destroy(target.gameObject);
        GatherAnimation.PlayOnce();
        shownTarget = null;
        ShowMessage($"{item.itemName} added to inventory");
    }

    void ShowMessage(string text)
    {
        messageText.text = text;
        messageUntil = Time.unscaledTime + messageSeconds;
    }

    void BuildUI()
    {
        Transform canvas = UIKit.CreateCanvas("PlacedPickupCanvas", transform, 48).transform;
        promptText = UIKit.CornerText("PickupPrompt", canvas, 0, true);
        messageText = UIKit.CornerText("PickupMessage", canvas, 2);
    }
}
