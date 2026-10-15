using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ChestLoot
{
    public PlayerItemData item;
    [Min(1)] public int minAmount = 1;
    [Min(1)] public int maxAmount = 1;
    [Tooltip("1 = always, 0.5 = half of the time")] [Range(0f, 1f)] public float chance = 1f;
}

// Chest prefab (adds a LootBag). Never disappears; LootBag "Items" are always inside, Random Loot is rolled at start.
[RequireComponent(typeof(LootBag))]
public class LootChest : MonoBehaviour
{
    [Header("Random Loot (rolled when the game starts)")]
    [Tooltip("Press + for each item that MAY be inside. The LootBag 'Items' list above = items that are ALWAYS inside.")]
    public List<ChestLoot> randomLoot = new List<ChestLoot>();
    [Tooltip("Maximum different random items in the chest (0 = no limit)")] [Min(0)] public int maxRandomItems = 0;

    [Header("Does the chest appear?")]
    [Tooltip("1 = always there, 0.5 = only in half of the games")] [Range(0f, 1f)] public float appearChance = 1f;

    [Header("Bandit Camp Lock")]
    [Tooltip("The chest can't be opened while enemies are alive near it")] public bool lockedUntilEnemiesDefeated;
    [Tooltip("Enemies within this distance keep the chest locked")] public float enemyCheckRadius = 20f;
    [Tooltip("Once the camp is cleared the chest stays open, even if new enemies walk by")] public bool stayUnlockedAfterClear = true;
    [Tooltip("Text shown instead of [E] Open while locked")] public string lockedText = "Defeat the bandits to open";

    [Header("Respawn")]
    [Tooltip("ON = loot only once: what you take is saved, and in this save the chest never fills again (also in a friend's world).\nOFF = the chest is full again every time the game loads (and also after Refill Minutes, if set)")]
    public bool lootOnlyOnce = true;
    [Tooltip("Minutes until the chest fills again after being emptied, while playing (0 = never). Only used when Loot Only Once is OFF")] public float refillMinutes = 0f;

    public bool IsLocked { get; private set; }

    private LootBag bag;
    private readonly List<PlayerItemData> fixedItems = new List<PlayerItemData>();
    private float refillTime = -1f, nextLockCheck;
    private bool hadEnemies, cleared;

    void Awake()
    {
        bag = GetComponent<LootBag>();
        bag.chest = this;

        if (Random.value > appearChance)
        {
            gameObject.SetActive(false);
            return;
        }

        foreach (PlayerItemData item in bag.items) if (item != null) fixedItems.Add(item);
        Fill();
        UpdateLock();
    }

    void Update()
    {
        if (Time.time >= nextLockCheck)
        {
            nextLockCheck = Time.time + 0.5f;
            UpdateLock();
        }

        if (lootOnlyOnce || refillMinutes <= 0f) return;
        if (bag.IsEmpty && refillTime < 0f) refillTime = Time.time + refillMinutes * 60f;
        else if (refillTime >= 0f && Time.time >= refillTime)
        {
            Fill();
            refillTime = -1f;
        }
    }

    // Puts the fixed items + a new random roll inside the chest
    void Fill()
    {
        bag.items.Clear();
        bag.items.AddRange(fixedItems);

        var rolled = new List<ChestLoot>();
        foreach (ChestLoot loot in randomLoot)
            if (loot != null && loot.item != null && Random.value <= loot.chance) rolled.Add(loot);

        if (maxRandomItems > 0)
            while (rolled.Count > maxRandomItems) rolled.RemoveAt(Random.Range(0, rolled.Count));

        foreach (ChestLoot loot in rolled)
        {
            int amount = Random.Range(loot.minAmount, Mathf.Max(loot.minAmount, loot.maxAmount) + 1);
            for (int i = 0; i < amount; i++) bag.items.Add(loot.item);
        }
    }

    void UpdateLock()
    {
        if (!lockedUntilEnemiesDefeated || cleared)
        {
            IsLocked = false;
            return;
        }

        bool enemiesNear = false;
        foreach (EnemyAI_01 enemy in EnemyAI_01.All)
        {
            if (!enemy.IsDead && Vector3.Distance(enemy.transform.position, transform.position) <= enemyCheckRadius)
            {
                enemiesNear = true;
                break;
            }
        }

        if (enemiesNear) hadEnemies = true;
        else if (hadEnemies && stayUnlockedAfterClear) cleared = true;
        IsLocked = enemiesNear;
    }

    void OnDrawGizmosSelected()
    {
        if (!lockedUntilEnemiesDefeated) return;
        Gizmos.color = new Color(1f, 0.3f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, enemyCheckRadius);
    }
}
