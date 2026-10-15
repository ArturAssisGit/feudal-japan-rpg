using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class GatherDrop
{
    public PlayerItemData item;
    [Min(1)] public int minAmount = 1;
    [Min(1)] public int maxAmount = 1;
    [Tooltip("1 = always, 0.5 = half of the time")] [Range(0f, 1f)] public float chance = 1f;
}

// Put on a tree/bush/rock prefab; hold E near it to gather. Painted Terrain trees work too (add TerrainGatherables).
public class GatherNode : MonoBehaviour
{
    public static readonly List<GatherNode> All = new List<GatherNode>();

    [Header("Interaction")]
    [Tooltip("Word shown in the prompt: Gather, Chop, Pick, Mine...")] public string actionName = "Gather";
    [Tooltip("How close the player must be (meters)")] public float gatherRange = 2f;
    [Tooltip("Seconds you must hold E")] public float gatherTime = 1.5f;

    [Header("What it gives")]
    public List<GatherDrop> drops = new List<GatherDrop>();

    [Header("Uses & Respawn")]
    [Tooltip("How many times it can be gathered before it is empty")] [Min(1)] public int uses = 3;
    [Tooltip("Minutes until it can be gathered again (0 = never)")] public float respawnMinutes = 5f;
    [Tooltip("Objects hidden while empty (e.g. the berries on a bush)")] public GameObject[] hideWhenEmpty;
    [Tooltip("Hide the whole object while empty (e.g. a small rock)")] public bool hideAllWhenEmpty;

    public int UsesLeft { get; private set; }
    public bool IsEmpty => UsesLeft <= 0;

    private float refillTime = -1f;

    void Awake()
    {
        UsesLeft = uses;
        All.Add(this);
    }

    void OnDestroy() => All.Remove(this);

    // Used by TerrainGatherables to copy the settings from the tree prefab
    public void CopySettings(GatherNode source)
    {
        actionName = source.actionName;
        gatherRange = source.gatherRange;
        gatherTime = source.gatherTime;
        drops = source.drops;
        uses = source.uses;
        respawnMinutes = source.respawnMinutes;
        hideWhenEmpty = new GameObject[0];
        hideAllWhenEmpty = false;
        UsesLeft = uses;
    }

    // Rolls the drops, uses one charge and returns what the player gets
    public List<KeyValuePair<PlayerItemData, int>> Harvest()
    {
        var result = new List<KeyValuePair<PlayerItemData, int>>();
        if (IsEmpty) return result;

        foreach (GatherDrop drop in drops)
        {
            if (drop == null || drop.item == null || Random.value > drop.chance) continue;
            int amount = Random.Range(drop.minAmount, Mathf.Max(drop.minAmount, drop.maxAmount) + 1);
            if (amount > 0) result.Add(new KeyValuePair<PlayerItemData, int>(drop.item, amount));
        }

        UsesLeft--;
        if (IsEmpty)
        {
            SetEmptyLook(true);
            refillTime = respawnMinutes > 0f ? Time.time + respawnMinutes * 60f : -1f;
        }
        return result;
    }

    // Called a few times per second by GatherSystem
    public void TickRespawn()
    {
        if (!IsEmpty || refillTime < 0f || Time.time < refillTime) return;
        UsesLeft = uses;
        refillTime = -1f;
        SetEmptyLook(false);
    }

    void SetEmptyLook(bool empty)
    {
        if (hideWhenEmpty != null)
            foreach (GameObject obj in hideWhenEmpty)
                if (obj != null) obj.SetActive(!empty);

        if (!hideAllWhenEmpty) return;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = !empty;
        foreach (Collider c in GetComponentsInChildren<Collider>(true)) c.enabled = !empty;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, gatherRange);
    }
}
