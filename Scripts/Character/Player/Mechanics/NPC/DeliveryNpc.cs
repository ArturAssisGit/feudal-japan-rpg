using System.Collections.Generic;
using UnityEngine;

// Put this on an NPC (a new one, or a merchant that already has ShopNpc).
// "Gives packages": talk to it and it offers a delivery to another DeliveryNpc, for money.
// "Receives packages": you can deliver packages to it. One NPC can do both.
public class DeliveryNpc : MonoBehaviour
{
    public static readonly List<DeliveryNpc> All = new List<DeliveryNpc>();

    [Header("This NPC")]
    [Tooltip("Shown in the dialog, on the HUD and on the map. Give every delivery NPC a different name (it is also used by the save file)")] public string npcName = "Courier";
    [Tooltip("How close you must be to talk")] public float interactRange = 2.5f;
    [Tooltip("Key to talk to this NPC")] public KeyCode interactKey = KeyCode.E;
    [Tooltip("Used instead if this NPC is also a merchant (ShopNpc): the shop keeps its own key")] public KeyCode keyWhenMerchant = KeyCode.F;
    [Tooltip("Show this NPC as a dot on the big map (the NPC you must deliver to is always shown while you have a delivery)")] public bool showOnMap = true;

    [Header("Gives packages")]
    public bool givesPackages = true;
    [Tooltip("The Package item you carry to the destination")] public PlayerItemData packageItem;
    [Tooltip("The coins item - used to pay you")] public PlayerItemData coinItem;
    [Tooltip("What is inside, only for the text (one is picked at random)")] public string[] cargoNames = { "a sealed letter", "a bundle of herbs", "a small crate", "a roll of silk", "a jar of sake" };
    [Tooltip("The destination is at least this far away (meters)")] public float minDistance = 60f;
    [Tooltip("The destination is at most this far away (0 = any distance)")] public float maxDistance = 0f;
    [Tooltip("Reward without counting the distance (coins)")] public int baseReward = 20;
    [Tooltip("Extra coins for each meter of distance")] public float rewardPerMeter = 0.4f;
    [Tooltip("Random change of the reward (0.2 = up to 20% more or less)")] [Range(0f, 1f)] public float rewardVariation = 0.2f;

    [Header("Receives packages")]
    public bool receivesPackages = true;

    [Header("Texts  ({cargo} {target} {distance} {reward} {npc} {done})")]
    [TextArea(2, 4)] public string offerText = "I need {cargo} delivered to {target}, about {distance} m from here. It pays {reward} coins.";
    [TextArea(2, 4)] public string reminderText = "You are already carrying {cargo} to {target}, about {distance} m away.";
    [TextArea(2, 4)] public string thanksText = "Thank you! Here are {reward} coins for your trouble.\nDeliveries completed: {done}";
    [TextArea(2, 4)] public string noJobText = "Nobody needs anything delivered right now.";

    [Header("Idle animation (only if this NPC is not a merchant; merchants already have one)")]
    [Tooltip("Drag an idle clip here. Empty = uses the Animator Controller")] public AnimationClip idleClip;
    [Tooltip("Turn to face the player when you talk")] public bool facePlayer = true;

    private string saveKey;
    // Name + place, so two NPCs with the same name still differ (built once)
    public string SaveKey => saveKey ??= npcName + "@" + Mathf.RoundToInt(transform.position.x) + "," + Mathf.RoundToInt(transform.position.z);
    public bool IsMerchant { get; private set; }
    public KeyCode TalkKey => IsMerchant ? keyWhenMerchant : interactKey;

    // Only when this NPC is not a merchant (merchants already play their own idle)
    private NpcIdle idle;

    void Awake()
    {
        IsMerchant = GetComponent<ShopNpc>() != null;
        if (idleClip != null && !IsMerchant) idle = new NpcIdle(transform);
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Start() => idle?.Begin(idleClip, nameof(DeliveryNpc));
    void LateUpdate() => idle?.Hold();
    void OnDestroy() => idle?.Destroy();

    public void FaceTowards(Vector3 position)
    {
        if (facePlayer) NpcIdle.Face(transform, position);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactRange);
        if (givesPackages)
        {
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, minDistance);
            if (maxDistance > 0f) Gizmos.DrawWireSphere(transform.position, maxDistance);
        }
    }
}
