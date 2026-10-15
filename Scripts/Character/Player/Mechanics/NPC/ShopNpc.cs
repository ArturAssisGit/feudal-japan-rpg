using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ShopStock
{
    public PlayerItemData item;
    [Tooltip("How many the merchant has (-1 = never runs out)")] public int amount = -1;
}

// Put this on an NPC prefab: it stands still playing its idle animation, and E opens the shop.
public class ShopNpc : MonoBehaviour
{
    public static readonly List<ShopNpc> All = new List<ShopNpc>();

    [Header("Merchant")]
    public string merchantName = "Merchant";
    [Tooltip("How close you must be to press E")] public float interactRange = 2.5f;

    [Header("Money")]
    [Tooltip("The coins item (the one with the coin icon) - used to pay and get paid")] public PlayerItemData coinItem;
    [Tooltip("1 = sells items for their Price, 1.5 = 50% more expensive")] public float buyPriceMultiplier = 1f;
    [Tooltip("How much of the Price the merchant pays when you sell (0.5 = half)")] [Range(0f, 1f)] public float sellPriceMultiplier = 0.5f;

    [Header("Items for sale")]
    public List<ShopStock> stock = new List<ShopStock>();
    [Tooltip("Items you sell appear in the merchant's list so you can buy them back")] public bool resellSoldItems = true;

    [Header("Idle animation (no Animator Controller needed)")]
    [Tooltip("Drag an idle clip here (e.g. the Idle from Character_Template@Idle). Empty = uses the Animator Controller")] public AnimationClip idleClip;
    [Tooltip("Turn to face the player when the shop opens")] public bool facePlayer = true;

    private NpcIdle idle;

    void Awake() => idle = new NpcIdle(transform);

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    // A merchant never moves: no physics pushing it around, only its idle animation
    void Start() => idle.Begin(idleClip, nameof(ShopNpc));
    void LateUpdate() => idle.Hold();
    void OnDestroy() => idle?.Destroy();

    public int BuyPrice(PlayerItemData item) => item == null ? 0 : Mathf.Max(1, Mathf.RoundToInt(item.price * buyPriceMultiplier));

    // 0 = the merchant doesn't buy it
    public int SellPrice(PlayerItemData item) =>
        item == null || item == coinItem || item.price <= 0 ? 0 : Mathf.Max(1, Mathf.FloorToInt(item.price * sellPriceMultiplier));

    public ShopStock FindStock(PlayerItemData item) => stock.Find(s => s != null && s.item == item);

    public void TakeOne(ShopStock entry)
    {
        if (entry == null || entry.amount < 0) return;
        entry.amount--;
        if (entry.amount <= 0) stock.Remove(entry);
    }

    public void AddSoldItem(PlayerItemData item)
    {
        if (!resellSoldItems || item == null) return;
        ShopStock entry = FindStock(item);
        if (entry == null) stock.Add(new ShopStock { item = item, amount = 1 });
        else if (entry.amount >= 0) entry.amount++;
    }

    public void FaceTowards(Vector3 position)
    {
        if (facePlayer) NpcIdle.Face(transform, position);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
