using System;
using System.Collections.Generic;
using UnityEngine;

// One horse for sale at a stable
[Serializable]
public class HorseOffer
{
    [Tooltip("The 'Horse' item (a PlayerItemData asset): its icon, name, description and Price are shown in the stable")] public PlayerItemData item;
    [Tooltip("The horse prefab (with HorseMount on it) that comes when you press H")] public GameObject prefab;
    [Tooltip("0 = uses the Price of the item")] [Min(0)] public int priceOverride = 0;
    [Tooltip("Empty = uses the description of the item")] [TextArea(2, 4)] public string description;
    [Tooltip("The player already owns this horse from the start (a free starter horse)")] public bool ownedFromStart = false;

    [Header("Stats (1-10, 5 = a normal horse)")]
    [Range(1, 10)] public int speed = 5;
    [Range(1, 10)] public int stamina = 5;
    [Range(1, 10)] public int agility = 5;

    // The name saved with the player (never change it after players own the horse)
    public string Key => item != null ? item.name : prefab != null ? prefab.name : "";
    public string DisplayName => item != null && !string.IsNullOrEmpty(item.itemName) ? item.itemName : prefab != null ? prefab.name : "Horse";

    public float SpeedMultiplier => 1f + (speed - 5) * 0.06f;
    public float StaminaMultiplier => 1f + (stamina - 5) * 0.1f;
    public float AgilityMultiplier => 1f + (agility - 5) * 0.06f;
}

// What goes into the save file about horses (part of SaveData)
[Serializable]
public class HorseSave
{
    public bool hasData;
    public string active = "";
    public List<string> owned = new List<string>();

    [Tooltip("The horse was standing in the world when you saved: it comes back at the same place")] public bool horseInWorld;
    public string horseKey = "";
    public Vector3 horsePosition;
    public float horseYaw;
}

// Which horses the player owns and which one answers to H. Saved with the game (see WorldSaveGameManager / SaveData.horses)
public static class HorseOwnership
{
    private static readonly HashSet<string> owned = new HashSet<string>();
    private static readonly Dictionary<string, HorseOffer> offers = new Dictionary<string, HorseOffer>();
    private static string active = "";

    // On when a stable that needs purchases is in the scene: then H only works with a horse you own
    public static bool Required { get; set; }

    public static string Active => active;
    public static bool HasActive => !string.IsNullOrEmpty(active);
    public static HorseOffer ActiveOffer => OfferOf(active);

    public static HorseOffer OfferOf(string key) => !string.IsNullOrEmpty(key) && offers.TryGetValue(key, out HorseOffer o) ? o : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        owned.Clear();
        offers.Clear();
        active = "";
        Required = false;
    }

    public static void Register(HorseOffer offer)
    {
        if (offer != null && !string.IsNullOrEmpty(offer.Key)) offers[offer.Key] = offer;
    }

    public static bool IsOwned(string key) => owned.Contains(key);

    // The horse answers H only when it is the one you chose (or when no stable asks for purchases)
    public static bool Allows(HorseMount horse)
    {
        if (!Required) return true;
        return horse != null && !string.IsNullOrEmpty(active) && horse.ownershipKey == active;
    }

    public static void Grant(string key)
    {
        if (string.IsNullOrEmpty(key) || owned.Contains(key)) return;
        owned.Add(key);
        if (string.IsNullOrEmpty(active)) active = key;
    }

    public static void Select(string key)
    {
        if (owned.Contains(key)) active = key;
    }

    public static void Clear()
    {
        owned.Clear();
        active = "";
    }

    // ---------- Save / load ----------

    public static HorseSave Capture()
    {
        HorseSave save = new HorseSave { hasData = true, active = active };
        save.owned.AddRange(owned);

        HorseMount horse = HorseMount.Main;
        if (Required && horse != null && horse.IsPresent && !string.IsNullOrEmpty(horse.ownershipKey))
        {
            Vector3 position = horse.transform.position;
            if (horse.IsRidden) position += horse.transform.right * 2f; // you are on it: it comes back standing beside you
            save.horseInWorld = true;
            save.horseKey = horse.ownershipKey;
            save.horsePosition = position;
            save.horseYaw = horse.transform.eulerAngles.y;
        }
        return save;
    }

    // null / an old save without horses = a new game: only the free starter horses
    public static void Restore(HorseSave save)
    {
        owned.Clear();
        active = "";
        if (save != null && save.hasData)
        {
            foreach (string key in save.owned) owned.Add(key);
            if (owned.Contains(save.active)) active = save.active;
            else foreach (string key in owned) { active = key; break; }
        }
        else
        {
            foreach (HorseOffer offer in offers.Values)
                if (offer.ownedFromStart) Grant(offer.Key);
        }

        if (HorseSpawner.Instance != null) HorseSpawner.Instance.ApplySavedHorse(save);
    }
}

// Put this on the stable master NPC prefab: it stands still, and E opens the stable window where you buy and choose your horse.
public class HorseStable : MonoBehaviour
{
    public static readonly List<HorseStable> All = new List<HorseStable>();

    [Header("Stable master")]
    public string stableName = "Stable";
    [Tooltip("How close you must be to press E")] public float interactRange = 2.5f;

    [Header("Money")]
    [Tooltip("The coins item (the one with the coin icon) - used to pay")] public PlayerItemData coinItem;
    [Tooltip("1 = normal prices, 1.5 = 50% more expensive")] public float priceMultiplier = 1f;

    [Header("Horses for sale")]
    public List<HorseOffer> horses = new List<HorseOffer>();
    [Tooltip("On = the H key only works with a horse you bought (or got for free) here. Off = H works with any horse like before")] public bool horsesNeedPurchase = true;

    [Header("Idle animation (no Animator Controller needed)")]
    [Tooltip("Drag an idle clip here. Empty = uses the Animator Controller")] public AnimationClip idleClip;
    [Tooltip("Turn to face the player when the window opens")] public bool facePlayer = true;

    private NpcIdle idle;

    public int PriceOf(HorseOffer offer)
    {
        if (offer == null) return 0;
        int basePrice = offer.priceOverride > 0 ? offer.priceOverride : offer.item != null ? offer.item.price : 0;
        return Mathf.Max(0, Mathf.RoundToInt(basePrice * priceMultiplier));
    }

    void Awake()
    {
        foreach (HorseOffer offer in horses)
        {
            if (offer == null) continue;
            HorseOwnership.Register(offer);
            if (offer.ownedFromStart) HorseOwnership.Grant(offer.Key);
        }
        if (horsesNeedPurchase) HorseOwnership.Required = true;
        idle = new NpcIdle(transform);
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Start() => idle.Begin(idleClip, nameof(HorseStable));
    void LateUpdate() => idle.Hold();
    void OnDestroy() => idle?.Destroy();

    public void FaceTowards(Vector3 position)
    {
        if (facePlayer) NpcIdle.Face(transform, position);
    }

    [ContextMenu("Forget all owned horses (test)")]
    void ForgetOwnedHorses()
    {
        HorseOwnership.Clear();
        Debug.Log("HorseStable: owned horses cleared.");
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.9f, 0.6f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
