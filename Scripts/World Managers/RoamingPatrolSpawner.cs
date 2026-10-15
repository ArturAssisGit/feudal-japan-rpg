using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using static WorldUtil;

// Surprise patrols: every now and then, while you WALK or RIDE, a group of enemies appears near you
// (not too close) and marches in one direction, crossing in front of you. If they see you, they fight.
// Put it on any object in the world scene (e.g. World Managers) and fill Patrol Groups.
public class RoamingPatrolSpawner : MonoBehaviour
{
    [System.Serializable]
    public class PatrolMember
    {
        [Tooltip("Enemy prefab (the same ones used by the Enemy Spawners)")] public GameObject prefab;
        [Min(0)] public int minCount = 1;
        [Min(0)] public int maxCount = 2;
    }

    [System.Serializable]
    public class PatrolGroup
    {
        public string name = "Bandit patrol";
        [Tooltip("Press + for each kind of enemy in this patrol (e.g. 3 katana + 1 archer)")] public List<PatrolMember> members = new List<PatrolMember>();
        [Tooltip("Higher = picked more often than the other groups")] [Min(0f)] public float weight = 1f;
        [Tooltip("Hours of the day when this patrol can appear (From = To means any time). E.g. From 20, To 5 = only at night")]
        [Range(0f, 24f)] public float fromHour = 0f;
        [Range(0f, 24f)] public float toHour = 0f;
    }

    [Header("Patrols")]
    public List<PatrolGroup> patrolGroups = new List<PatrolGroup>();

    [Header("How often (time)")]
    [Tooltip("Seconds between two tries (a random value between X and Y)")] public Vector2 secondsBetweenTries = new Vector2(90f, 180f);
    [Tooltip("Chance that a try really spawns a patrol (0-1)")] [Range(0f, 1f)] public float chance = 0.5f;
    [Tooltip("Seconds after the game starts before the first patrol can appear")] public float firstDelay = 60f;
    [Tooltip("How many patrols can exist at the same time")] [Min(1)] public int maxPatrols = 1;

    [Header("Where (the player's place)")]
    [Tooltip("Trigger colliders (e.g. big Box Colliders) where patrols can happen. Empty = anywhere on the NavMesh")] public List<Collider> allowedAreas = new List<Collider>();
    [Tooltip("Places where patrols never appear (villages, stables, camps...)")] public List<Transform> safePlaces = new List<Transform>();
    [Tooltip("Radius around each Safe Place (meters)")] public float safeRadius = 60f;
    [Tooltip("Patrols can also appear while the player is on the horse")] public bool allowWhileMounted = true;
    [Tooltip("On the horse the patrol appears this many times farther away (you travel faster, so it still has time to be seen)")] [Min(1f)] public float mountedDistanceMultiplier = 1.6f;
    [Tooltip("Only while the player is walking or running (standing still doesn't count)")] public bool onlyWhileMoving = true;

    [Header("Distance and route")]
    [Tooltip("Distance from the player where the patrol appears (meters, a random value between X and Y)")] public Vector2 spawnDistance = new Vector2(28f, 38f);
    [Tooltip("Chance (0-1) that the patrol appears in the direction the player is going. The rest appears anywhere around him")] [Range(0f, 1f)] public float forwardChance = 0.75f;
    [Tooltip("When it appears 'ahead': how wide that area is (degrees to each side of the direction)")] [Range(5f, 120f)] public float forwardArc = 50f;
    [Tooltip("How far from the player the patrol passes by (meters, random between X and Y). 0 = straight at him")] public Vector2 passBy = new Vector2(4f, 14f);
    [Tooltip("Never spawn closer than this to the player (meters)")] public float minSpawnDistance = 22f;
    [Tooltip("How far the patrol marches (meters)")] public float marchDistance = 70f;
    [Tooltip("Meters between the soldiers in the column")] public float spacing = 2.2f;

    [Header("Test")]
    [Tooltip("Writes in the Console WHY a try did not spawn anything (turn off when everything works)")] public bool debugLog = true;

    [Header("Clean up")]
    [Tooltip("Patrols farther than this from the player (and not fighting) are removed (meters)")] public float despawnDistance = 90f;
    [Tooltip("Seconds a patrol can exist before it is removed (when the player is not near). 0 = no limit")] public float maxLifetime = 300f;

    private class ActivePatrol
    {
        public readonly List<EnemyAI_01> members = new List<EnemyAI_01>();
        public float bornAt;
    }

    private readonly List<ActivePatrol> active = new List<ActivePatrol>();
    private Transform player;
    private float nextTry, nextCleanup;
    private Vector3 lastPlayerPosition, playerVelocity;

    void Start()
    {
        nextTry = Time.time + Mathf.Max(firstDelay, 1f);
        if (debugLog) Debug.Log($"RoamingPatrolSpawner ready: first try in {Mathf.Max(firstDelay, 1f):0} s, then every {secondsBetweenTries.x:0}-{secondsBetweenTries.y:0} s, {patrolGroups.Count} group(s).", this);
    }

    // Every prefab this spawner can use (the multiplayer clients need them to show the host's patrols)
    public IEnumerable<GameObject> AllPrefabs()
    {
        foreach (PatrolGroup g in patrolGroups)
            if (g != null)
                foreach (PatrolMember m in g.members)
                    if (m != null && m.prefab != null) yield return m.prefab;
    }

    void Update()
    {
        if (!PlayerRef.Find(ref player))
        {
            if (debugLog && Time.time >= nextTry) { nextTry = Time.time + 5f; Debug.Log("RoamingPatrolSpawner: player not found yet.", this); }
            return;
        }

        if (Time.deltaTime > 0f)
        {
            Vector3 delta = player.position - lastPlayerPosition;
            delta.y = 0f;
            playerVelocity = Vector3.Lerp(playerVelocity, delta / Time.deltaTime, 0.2f);
        }
        lastPlayerPosition = player.position;

        if (Time.time >= nextCleanup)
        {
            nextCleanup = Time.time + 1f;
            CleanUp();
        }

        if (Time.time < nextTry) return;
        nextTry = Time.time + Random.Range(Mathf.Min(secondsBetweenTries.x, secondsBetweenTries.y), Mathf.Max(secondsBetweenTries.x, secondsBetweenTries.y));
        string why = WhyNot();
        if (why != null) { if (debugLog) Debug.Log("RoamingPatrolSpawner: no patrol now - " + why, this); return; }
        if (Random.value > chance) { if (debugLog) Debug.Log("RoamingPatrolSpawner: the chance roll failed (Chance = " + chance + "). Next try soon.", this); return; }
        if (!TrySpawn() && debugLog) Debug.Log("RoamingPatrolSpawner: " + lastFailure, this);
    }

    // ---------- Rules ----------

    private string lastFailure = "";

    // null = a patrol can happen now; otherwise the reason it can't
    string WhyNot()
    {
        if (MultiplayerSession.IsClient) return "you are a client (online, the host makes the patrols).";
        if (patrolGroups.Count == 0) return "Patrol Groups is empty.";
        if (active.Count >= maxPatrols) return "there are already " + active.Count + " patrol(s) alive (Max Patrols = " + maxPatrols + ").";
        if (HorseMount.PlayerMounted && !allowWhileMounted) return "the player is on the horse (tick Allow While Mounted).";
        if (onlyWhileMoving && playerVelocity.magnitude < 0.5f) return "the player is standing still (keep walking when the try happens, or untick Only While Moving).";
        PlayerStats stats = player.GetComponentInChildren<PlayerStats>();
        if (stats != null && stats.IsDead) return "the player is dead.";
        if (SleepSystem.IsSleeping || RespawnSystem.IsDeathScreenOpen) return "sleeping / death screen.";
        foreach (EnemyAI_01 e in EnemyAI_01.All)
            if (e != null && e.IsInCombat) return "an enemy is in combat (" + e.name + ").";
        if (!InAnyArea(allowedAreas, player.position)) return "the player is outside the Allowed Areas.";
        foreach (Transform safe in safePlaces)
            if (safe != null && Flat(safe.position - player.position).sqrMagnitude < safeRadius * safeRadius) return "the player is inside the Safe Place '" + safe.name + "' (radius " + safeRadius + " m).";
        return null;
    }

    PatrolGroup PickGroup()
    {
        float hour = CurrentHour;
        return PickWeighted(patrolGroups.FindAll(g => g != null && g.weight > 0f && g.members.Count > 0 && HourInRange(hour, g.fromHour, g.toHour)), g => g.weight);
    }

    // ---------- Spawning ----------

    // Test: right-click the component (or its 3 dots) > "Spawn a patrol now" while playing
    [ContextMenu("Spawn a patrol now")]
    void SpawnNowFromMenu()
    {
        if (!Application.isPlaying) { Debug.Log("Press Play first."); return; }
        if (!TrySpawn()) Debug.Log("RoamingPatrolSpawner: " + lastFailure, this);
    }

    // Can be called from other scripts to force a patrol now
    public bool TrySpawn()
    {
        if (player == null) { lastFailure = "no player."; return false; }
        PatrolGroup group = PickGroup();
        if (group == null) { lastFailure = "no usable group: every group has no members, weight 0 or is outside its hours (current hour " + CurrentHour.ToString("0.0") + ")."; return false; }
        bool anyPrefab = false;
        foreach (PatrolMember m in group.members) if (m != null && m.prefab != null && m.maxCount > 0) anyPrefab = true;
        if (!anyPrefab) { lastFailure = "group '" + group.name + "' has no Prefab filled (or the counts are 0)."; return false; }
        int noStart = 0, noEnd = 0, tooClose = 0, safe = 0, noPath = 0;
        float scale = HorseMount.PlayerMounted ? mountedDistanceMultiplier : 1f;

        Vector3 forward = playerVelocity.sqrMagnitude > 0.25f ? playerVelocity.normalized : Flat(player.forward).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

        // A few tries: they appear around the player (mostly where he is heading) and march past him
        for (int attempt = 0; attempt < 10; attempt++)
        {
            bool ahead = Random.value <= forwardChance;
            float angle = ahead ? Random.Range(-forwardArc, forwardArc) : Random.Range(0f, 360f);
            Vector3 toSpawn = Quaternion.Euler(0f, angle, 0f) * forward;
            float distance = Random.Range(Mathf.Min(spawnDistance.x, spawnDistance.y), Mathf.Max(spawnDistance.x, spawnDistance.y)) * scale;
            Vector3 start = player.position + toSpawn * distance;

            // They walk toward a point beside the player, so they pass near him (and notice him) and keep going
            Vector3 lateral = Vector3.Cross(Vector3.up, toSpawn) * (Random.value < 0.5f ? -1f : 1f) * Random.Range(Mathf.Min(passBy.x, passBy.y), Mathf.Max(passBy.x, passBy.y));
            Vector3 march = Flat(player.position + lateral - start).normalized;
            if (march.sqrMagnitude < 0.01f) march = -toSpawn;
            Vector3 end = start + march * (distance + marchDistance * scale);

            if (!SpawnPlacement.GroundNavPoint(start, 12f, out Vector3 startPoint)) { noStart++; continue; }
            if (!SpawnPlacement.GroundNavPoint(end, 15f, out Vector3 endPoint)) { noEnd++; continue; }
            NavMeshHit startHit = new NavMeshHit { position = startPoint }, endHit = new NavMeshHit { position = endPoint };
            if (SpawnPlacement.GroundNavPoint(player.position, 4f, out Vector3 playerPoint) && !SpawnPlacement.IsGoodSpot(startPoint, playerPoint)) { noPath++; continue; } // roof / closed place
            if (Flat(startHit.position - player.position).magnitude < minSpawnDistance * scale) { tooClose++; continue; }
            if (NearAny(safePlaces, startHit.position, safeRadius)) { safe++; continue; }
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, path) || path.status == NavMeshPathStatus.PathInvalid) { noPath++; continue; }

            Spawn(group, startHit.position, endHit.position, march);
            return true;
        }
        lastFailure = $"no good place in 10 tries: no NavMesh at the start {noStart}x, no NavMesh at the end {noEnd}x, too close {tooClose}x, near a Safe Place {safe}x, no path {noPath}x. (Is the NavMesh baked where you walk?)";
        return false;
    }

    void Spawn(PatrolGroup group, Vector3 start, Vector3 end, Vector3 march)
    {
        var patrol = new ActivePatrol { bornAt = Time.time };
        Vector3 right = Vector3.Cross(Vector3.up, march);
        Quaternion facing = Quaternion.LookRotation(march);
        int index = 0;

        foreach (PatrolMember member in group.members)
        {
            if (member == null || member.prefab == null) continue;
            int count = Random.Range(member.minCount, Mathf.Max(member.minCount, member.maxCount) + 1);
            for (int i = 0; i < count; i++, index++)
            {
                // Column of two: leader in front, the others behind
                int row = index / 2;
                float col = index % 2 == 0 ? -0.5f : 0.5f;
                Vector3 offset = -march * (row * spacing) + right * (col * spacing * 0.8f);

                Vector3 spawnAt = start + offset;
                spawnAt = NavMesh.SamplePosition(SpawnPlacement.OnTerrain(spawnAt), out NavMeshHit hit, 3f, NavMesh.AllAreas) && SpawnPlacement.IsGoodSpot(hit.position, start) ? hit.position : start;
                GameObject go = Instantiate(member.prefab, spawnAt + Vector3.up * 0.1f, facing);
                EnemyAI_01 ai = go.GetComponent<EnemyAI_01>();
                if (ai == null) continue;
                ai.StartPatrol(end + offset);
                patrol.members.Add(ai);
            }
        }

        if (patrol.members.Count > 0)
        {
            active.Add(patrol);
            Debug.Log($"Roaming patrol '{group.name}' ({patrol.members.Count} enemies) appeared {Flat(start - player.position).magnitude:0} m from the player.");
        }
    }

    // ---------- Clean up ----------

    void CleanUp()
    {
        for (int p = active.Count - 1; p >= 0; p--)
        {
            ActivePatrol patrol = active[p];
            patrol.members.RemoveAll(e => e == null);

            bool anyAlive = false, anyFighting = false, allFar = true;
            foreach (EnemyAI_01 e in patrol.members)
            {
                if (e.IsDead) continue;
                anyAlive = true;
                if (e.IsInCombat) anyFighting = true;
                if (Flat(e.transform.position - player.position).sqrMagnitude < despawnDistance * despawnDistance) allFar = false;
            }

            // All dead: the bodies stay (loot); the patrol no longer counts
            if (!anyAlive)
            {
                active.RemoveAt(p);
                continue;
            }

            bool tooOld = maxLifetime > 0f && Time.time - patrol.bornAt > maxLifetime;
            if (!anyFighting && (allFar || (tooOld && FarFromPlayer(patrol, 40f))))
            {
                foreach (EnemyAI_01 e in patrol.members) if (e != null && !e.IsDead) Destroy(e.gameObject);
                active.RemoveAt(p);
            }
        }
    }

    bool FarFromPlayer(ActivePatrol patrol, float meters)
    {
        foreach (EnemyAI_01 e in patrol.members)
            if (e != null && Flat(e.transform.position - player.position).sqrMagnitude < meters * meters) return false;
        return true;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 1f, 0.4f);
        foreach (Transform safe in safePlaces) if (safe != null) Gizmos.DrawWireSphere(safe.position, safeRadius);
    }
}
