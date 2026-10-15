using System.Collections.Generic;
using UnityEngine;
using static WorldUtil;

// Spawns animals (birds, foxes...) on the ground around the player and removes them when he leaves.
// Put it on any object in the world scene (e.g. World Managers), fill "Animals" with the PREFABS from the Project
// (do NOT put the animals in the scene yourself: this script creates them).
public class AnimalSpawner : MonoBehaviour
{
    [System.Serializable]
    public class AnimalType
    {
        public string name = "Bird";
        [Tooltip("The prefab from the Project window (e.g. Data/Prefabs/Animals/bird)")] public GameObject prefab;
        [Tooltip("Higher = picked more often than the other animals")] [Min(0f)] public float weight = 1f;
        [Tooltip("How many appear together (a small flock). Random between Min and Max")] [Min(1)] public int minGroup = 1;
        [Min(1)] public int maxGroup = 3;
        [Tooltip("Hours of the day when it can appear (From = To means any time)")] [Range(0f, 24f)] public float fromHour = 0f;
        [Range(0f, 24f)] public float toHour = 0f;
    }

    [Header("Animals")]
    public List<AnimalType> animals = new List<AnimalType>();

    [Header("How many and how often")]
    [Tooltip("How many animals can exist at the same time")] [Min(1)] public int maxAnimals = 8;
    [Tooltip("Seconds between two spawns (random between X and Y)")] public Vector2 secondsBetweenSpawns = new Vector2(8f, 20f);
    [Tooltip("Seconds after the game starts before the first one")] public float firstDelay = 3f;
    [Tooltip("Spawn a few animals as soon as the game starts, so the world is not empty")] [Min(0)] public int startingAnimals = 3;

    [Header("Where")]
    [Tooltip("Distance from the player where animals appear (meters, min and max)")] public Vector2 spawnDistance = new Vector2(20f, 55f);
    [Tooltip("Animals farther than this from the player are removed (meters). Must be bigger than the max Spawn Distance")] public float despawnDistance = 90f;
    [Tooltip("Animals appear in FRONT of you, inside this angle (degrees) around the way you are walking/riding (or where the camera looks when you stand still). 360 = all around")]
    [Range(30f, 360f)] public float spawnAngle = 180f;
    [Tooltip("Never appear where the camera is looking (they pop in behind you or far away from the screen)")] public bool avoidCameraView = true;
    [Tooltip("Ground steeper than this (degrees) is skipped")] [Range(5f, 80f)] public float maxSlope = 35f;
    [Tooltip("Ground lower than this height (world Y) is skipped. Put the water level here so birds don't spawn in the sea")] public float minGroundY = -1000f;
    [Tooltip("Trigger colliders (e.g. big Box Colliders) where animals can appear. Empty = anywhere")] public List<Collider> allowedAreas = new List<Collider>();
    [Tooltip("Places where animals never appear (villages, camps...)")] public List<Transform> safePlaces = new List<Transform>();
    [Tooltip("Radius around each Safe Place (meters)")] public float safeRadius = 40f;
    [Tooltip("How far apart the animals of a group stand (meters)")] public float groupSpread = 2.5f;

    [Header("Test")]
    [Tooltip("Writes in the Console why a spawn did not happen (turn off when it works)")] public bool debugLog = true;

    private readonly List<GameObject> alive = new List<GameObject>();
    private Transform player;
    private float nextSpawn, nextCleanup;
    private bool startingDone;
    private string lastFailure = "";
    private Vector3 lastPlayerPosition, moveDirection;
    private float lastMoveTime = -10f;

    void Start() => nextSpawn = Time.time + Mathf.Max(firstDelay, 0.5f);

    // Every prefab this spawner can use
    public IEnumerable<GameObject> AllPrefabs()
    {
        foreach (AnimalType a in animals) if (a != null && a.prefab != null) yield return a.prefab;
    }

    void Update()
    {
        if (!PlayerRef.Find(ref player)) return;
        if (MultiplayerSession.IsClient) return; // online, the host makes them
        TrackMovement();

        if (Time.time >= nextCleanup)
        {
            nextCleanup = Time.time + 1f;
            CleanUp();
        }

        if (!startingDone && Time.time >= nextSpawn)
        {
            startingDone = true;
            for (int i = 0; i < startingAnimals; i++) TrySpawn(false);
        }

        if (Time.time < nextSpawn) return;
        nextSpawn = Time.time + Random.Range(Mathf.Min(secondsBetweenSpawns.x, secondsBetweenSpawns.y), Mathf.Max(secondsBetweenSpawns.x, secondsBetweenSpawns.y));
        if (alive.Count >= maxAnimals) return;
        if (!TrySpawn(true) && debugLog) Debug.Log("AnimalSpawner: " + lastFailure, this);
    }

    [ContextMenu("Spawn an animal now")]
    void SpawnNowFromMenu()
    {
        if (!Application.isPlaying) { Debug.Log("Press Play first."); return; }
        if (!TrySpawn(true)) Debug.Log("AnimalSpawner: " + lastFailure, this);
    }

    // ---------- Spawning ----------

    bool TrySpawn(bool useCameraRule)
    {
        if (player == null) { lastFailure = "no player."; return false; }
        AnimalType type = Pick();
        if (type == null) { lastFailure = "no usable animal: Animals is empty, a Prefab is missing, weight is 0 or it is outside its hours."; return false; }

        Camera cam = useCameraRule && avoidCameraView ? Camera.main : null;
        float lo = Mathf.Min(spawnDistance.x, spawnDistance.y), hi = Mathf.Max(spawnDistance.x, spawnDistance.y);
        int noGround = 0, steep = 0, low = 0, area = 0, safe = 0, seen = 0;
        Vector3 forward = AheadDirection();

        for (int attempt = 0; attempt < 20; attempt++)
        {
            float half = Mathf.Clamp(spawnAngle, 30f, 360f) * 0.5f;
            Vector3 dir = Quaternion.Euler(0f, Random.Range(-half, half), 0f) * forward;
            float dist = Random.Range(lo, hi);
            Vector3 p = player.position + dir * dist;

            if (!Ground(p, out Vector3 ground, out float slope)) { noGround++; continue; }
            if (slope > maxSlope) { steep++; continue; }
            if (ground.y < minGroundY) { low++; continue; }
            if (!InAnyArea(allowedAreas, ground)) { area++; continue; }
            if (NearAny(safePlaces, ground, safeRadius)) { safe++; continue; }
            if (cam != null && InView(cam, ground)) { seen++; continue; }

            int count = Random.Range(type.minGroup, Mathf.Max(type.minGroup, type.maxGroup) + 1);
            count = Mathf.Min(count, Mathf.Max(1, maxAnimals - alive.Count));
            for (int i = 0; i < count; i++)
            {
                Vector2 off = i == 0 ? Vector2.zero : Random.insideUnitCircle * groupSpread;
                Vector3 at = ground + new Vector3(off.x, 0f, off.y);
                if (i > 0 && Ground(at, out Vector3 g2, out _)) at = g2;
                GameObject go = Instantiate(type.prefab, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                alive.Add(go);
            }
            return true;
        }
        lastFailure = $"no good place in 20 tries: no ground {noGround}x, too steep {steep}x, too low {low}x, outside Allowed Areas {area}x, near a Safe Place {safe}x, in the camera view {seen}x.";
        return false;
    }

    // Which way the player is going (walking / riding), or where the camera looks when he stands still
    void TrackMovement()
    {
        Vector3 delta = Flat(player.position - lastPlayerPosition);
        lastPlayerPosition = player.position;
        float speed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
        if (speed > 1f && speed < 60f) // ignores standing still and teleports (respawn, loading)
        {
            moveDirection = moveDirection == Vector3.zero ? delta.normalized : Vector3.Slerp(moveDirection, delta.normalized, 1f - Mathf.Exp(-4f * Time.deltaTime));
            lastMoveTime = Time.time;
        }
    }

    Vector3 AheadDirection()
    {
        if (Time.time - lastMoveTime < 1f && moveDirection.sqrMagnitude > 0.01f) return moveDirection.normalized;
        Camera cam = Camera.main;
        Vector3 look = cam != null ? Flat(cam.transform.forward) : Flat(player.forward);
        return look.sqrMagnitude > 0.001f ? look.normalized : Vector3.forward;
    }

    AnimalType Pick()
    {
        float hour = CurrentHour;
        return PickWeighted(animals.FindAll(a => a != null && a.prefab != null && a.weight > 0f && HourInRange(hour, a.fromHour, a.toHour)), a => a.weight);
    }

    // ---------- Rules ----------

    // Ground under p and its steepness. With terrains in the scene, only points on a terrain count
    static bool Ground(Vector3 p, out Vector3 ground, out float slope)
    {
        ground = p;
        slope = 0f;
        if (TerrainHeight(p, out float h))
        {
            ground.y = h;
            slope = TerrainSlope(p);
            return true;
        }
        if (Terrain.activeTerrains.Length > 0) return false; // outside the terrain
        if (Physics.Raycast(p + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 300f, ~0, QueryTriggerInteraction.Ignore))
        {
            ground = hit.point;
            slope = Vector3.Angle(hit.normal, Vector3.up);
            return true;
        }
        return false;
    }

    static bool InView(Camera cam, Vector3 point)
    {
        Vector3 v = cam.WorldToViewportPoint(point + Vector3.up * 0.5f);
        return v.z > 0f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
    }

    void CleanUp()
    {
        for (int i = alive.Count - 1; i >= 0; i--)
        {
            GameObject go = alive[i];
            if (go == null) { alive.RemoveAt(i); continue; }
            if (Flat(go.transform.position - player.position).sqrMagnitude > despawnDistance * despawnDistance)
            {
                Destroy(go);
                alive.RemoveAt(i);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 1f, 0.4f);
        foreach (Transform safe in safePlaces) if (safe != null) Gizmos.DrawWireSphere(safe.position, safeRadius);
    }
}
