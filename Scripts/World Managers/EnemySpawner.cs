using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using static WorldUtil;

public class EnemySpawner : MonoBehaviour
{
    [System.Serializable]
    public class SpawnGroup
    {
        [Tooltip("Enemy prefab (e.g. the katana enemy or the archer enemy)")] public GameObject prefab;
        [Tooltip("How many of this enemy this spawn point keeps alive")] [Min(0)] public int count = 1;
        [System.NonSerialized] public List<GameObject> alive = new List<GameObject>();
    }

    [Header("Enemies at this spawn point")]
    [Tooltip("e.g. Element 0 = katana enemy x4, Element 1 = archer enemy x2")]
    public List<SpawnGroup> enemyGroups = new List<SpawnGroup>();

    [Header("Old setup (only used if Enemy Groups is empty)")]
    public GameObject enemyPrefab;
    public int maxEnemies = 4;

    [Header("Spawn Rules")]
    [Tooltip("SECONDS (not minutes) until a dead enemy is replaced. The countdown starts when the enemy dies. 60 minutes = 3600")] public float spawnInterval = 5f;
    public float spawnRadius = 10f;
    [Tooltip("Enemies won't appear closer than this to the player")] public float minDistanceFromPlayer = 8f;
    [Tooltip("When the player first gets in range, spawn all the enemies at once (otherwise one every Spawn Interval)")] public bool spawnAllOnStart = true;
    [Tooltip("On = when the respawn time is up, ALL the missing enemies come back together (a full group). Off = one at a time")] public bool respawnAllAtOnce = true;

    [Header("Distance (performance)")]
    [Tooltip("This spawn point only spawns enemies while a player is closer than this (meters). 0 = always active, no matter the distance")] public float activationDistance = 80f;
    [Tooltip("Enemies farther than this from every player are removed, and come back when you return (meters). Always kept above Activation Distance. 0 = never remove")] public float despawnDistance = 150f;

    private readonly List<SpawnGroup> groups = new List<SpawnGroup>();
    private readonly List<SpawnGroup> missing = new List<SpawnGroup>();
    private float nextSpawnTime;
    private float nextCheckTime;
    private bool playerInRange;
    private bool filledOnce;
    private bool anyMissing;
    private bool refillWhenBack; // enemies were removed because everyone went far away: put them all back on return
    private Transform player; // nearest player, refreshed a few times per second

    // Multiplayer host: the other players (their copies), so enemies also appear and stay around them
    public static readonly List<Transform> ExtraPlayers = new List<Transform>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        ExtraPlayers.Clear();
    }

    void Awake()
    {
        foreach (SpawnGroup g in enemyGroups)
            if (g != null) { g.alive = new List<GameObject>(); groups.Add(g); }

        // Old spawners (only Enemy Prefab set) keep working
        if (groups.Count == 0 && enemyPrefab != null)
            groups.Add(new SpawnGroup { prefab = enemyPrefab, count = maxEnemies });
    }

    void Start()
    {
        nextSpawnTime = Time.time + spawnInterval;
        nextCheckTime = Time.time + Random.value * 0.5f; // spread the checks of many spawn points over different frames
    }

    void Update()
    {
        // Distance checks are cheap but there is no need to run them every frame
        if (Time.time >= nextCheckTime)
        {
            nextCheckTime = Time.time + 0.5f;
            player = NearestPlayer();
            playerInRange = activationDistance <= 0f || (player != null && Flat(player.position - transform.position).sqrMagnitude <= activationDistance * activationDistance);
            DespawnFarEnemies();

            // Is any enemy of this spawn point missing? While none is, the respawn timer keeps resetting,
            // so it only starts counting from the moment one dies (not from the last spawn)
            anyMissing = false;
            foreach (SpawnGroup group in groups)
            {
                group.alive.RemoveAll(IsDestroyed);
                if (group.prefab != null && group.alive.Count < group.count) anyMissing = true;
            }
        }
        if (filledOnce && !anyMissing) nextSpawnTime = Time.time + spawnInterval;

        if (!playerInRange) return;

        // First time the player gets close: fill the spawn point
        if (!filledOnce)
        {
            filledOnce = true;
            if (spawnAllOnStart) SpawnAllMissing();
            nextSpawnTime = Time.time + spawnInterval;
            return;
        }

        // Coming back to a spawn point whose enemies were removed for being far away: the whole group is back at once
        if (refillWhenBack)
        {
            refillWhenBack = false;
            SpawnAllMissing();
            nextSpawnTime = Time.time + spawnInterval;
            return;
        }

        if (Time.time < nextSpawnTime) return;

        if (respawnAllAtOnce)
        {
            nextSpawnTime = Time.time + spawnInterval;
            SpawnAllMissing();
            return;
        }

        // Pick a group that is missing enemies (random, so the types mix)
        missing.Clear();
        foreach (SpawnGroup group in groups)
        {
            group.alive.RemoveAll(IsDestroyed);
            if (group.prefab != null && group.alive.Count < group.count) missing.Add(group);
        }
        if (missing.Count == 0) return;

        nextSpawnTime = Time.time + spawnInterval;
        TrySpawn(missing[Random.Range(0, missing.Count)]);
    }

    static bool IsDestroyed(GameObject e) => e == null;

    // Every missing enemy of every group, in the same frame
    void SpawnAllMissing()
    {
        foreach (SpawnGroup group in groups)
        {
            group.alive.RemoveAll(IsDestroyed);
            if (group.prefab == null) continue;
            int missingCount = group.count - group.alive.Count;
            for (int i = 0; i < missingCount; i++) TrySpawn(group);
        }
    }

    Transform NearestPlayer()
    {
        Transform local = LocalPlayer();
        if (ExtraPlayers.Count == 0) return local;

        Transform best = local;
        float bestDistance = local != null ? Flat(local.position - transform.position).sqrMagnitude : float.MaxValue;
        foreach (Transform other in ExtraPlayers)
        {
            if (other == null || !other.gameObject.activeInHierarchy) continue;
            float d = Flat(other.position - transform.position).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = other; }
        }
        return best;
    }

    static Transform LocalPlayer() => PlayerRef.Transform;

    // Far from every player (in multiplayer an enemy near another player stays)
    bool FarFromEveryPlayer(Vector3 position, float limitSqr)
    {
        if (Flat(position - player.position).sqrMagnitude <= limitSqr) return false;
        Transform local = LocalPlayer();
        if (local != null && Flat(position - local.position).sqrMagnitude <= limitSqr) return false;
        foreach (Transform other in ExtraPlayers)
            if (other != null && Flat(position - other.position).sqrMagnitude <= limitSqr) return false;
        return true;
    }

    // Enemies left far behind are removed so the whole map is not simulated at once
    void DespawnFarEnemies()
    {
        if (despawnDistance <= 0f || player == null) return;
        float limit = Mathf.Max(despawnDistance, activationDistance + spawnRadius + 10f);
        float limitSqr = limit * limit;

        foreach (SpawnGroup group in groups)
        {
            for (int i = group.alive.Count - 1; i >= 0; i--)
            {
                GameObject enemy = group.alive[i];
                if (enemy == null) { group.alive.RemoveAt(i); continue; }
                if (FarFromEveryPlayer(enemy.transform.position, limitSqr))
                {
                    Destroy(enemy);
                    group.alive.RemoveAt(i);
                    refillWhenBack = true;
                }
            }
        }
    }

    void TrySpawn(SpawnGroup group)
    {
        if (group.prefab == null) { Debug.LogWarning($"{name}: a spawn group has no prefab assigned!"); return; }

        // The spawner's own ground point: every enemy must be able to walk from it (so never on a roof)
        bool hasAnchor = SpawnPlacement.GroundNavPoint(transform.position, 6f, out Vector3 anchor);

        // More tries so a whole group always appears (the last tries accept a spot closer to the player)
        for (int attempt = 0; attempt < 25; attempt++)
        {
            Vector2 random = Random.insideUnitCircle * spawnRadius;
            Vector3 candidate = SpawnPlacement.OnTerrain(transform.position + new Vector3(random.x, 0f, random.y));

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
            if (hasAnchor ? !SpawnPlacement.IsGoodSpot(hit.position, anchor) : !SpawnPlacement.NotTooHigh(hit.position)) continue; // roof, wall top...
            float minDistance = attempt < 18 ? minDistanceFromPlayer : minDistanceFromPlayer * 0.5f;
            if (player != null && Vector3.Distance(hit.position, player.position) < minDistance) continue;

            group.alive.Add(Instantiate(group.prefab, hit.position + Vector3.up, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)));
            return;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
        if (activationDistance > 0f)
        {
            Gizmos.color = new Color(1f, 0.9f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, activationDistance);
        }
    }
}
