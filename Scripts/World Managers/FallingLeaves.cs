using System.Collections.Generic;
using UnityEngine;

// Leaves falling from the Terrain trees around the player, blown by the wind (more in the rain). Creates itself at game start;
// add it to a scene object to change settings. Leaf pictures: PNG with transparency ("Alpha Is Transparency" ticked).
public class FallingLeaves : MonoBehaviour
{
    public static FallingLeaves Instance { get; private set; }

    [Header("Look")]
    [Tooltip("Leaf pictures (PNG with transparency). Each leaf uses one of them at random. Empty = a simple leaf is drawn automatically")]
    public Texture2D[] leafTextures;
    [Tooltip("Optional: your own material for the leaves (it replaces the textures above)")] public Material leafMaterial;
    [Tooltip("Makes the background of the leaf pictures transparent (the color in the corners of the picture, e.g. white). Turn off if your pictures already have transparency")]
    public bool removeBackground = true;
    [Tooltip("How different from the background a pixel must be to stay (raise it if a light border is left around the leaf)")] [Range(0.02f, 0.5f)] public float backgroundTolerance = 0.15f;
    [Tooltip("Size of a leaf in meters (random between min and max)")] public Vector2 leafSize = new(0.08f, 0.16f);
    [Tooltip("Leaves get a random color between these two (white = the picture's own color)")] public Color colorA = Color.white;
    public Color colorB = new(0.85f, 0.75f, 0.55f);

    [Header("Amount")]
    [Tooltip("Leaves per second around you, always (also without rain)")] public float calmLeavesPerSecond = 12f;
    [Tooltip("How many times more leaves fall in full rain / storm")] public float rainMultiplier = 3f;
    [Tooltip("Leaves only fall from trees this close to the camera (meters)")] public float spawnRadius = 35f;
    [Tooltip("Most leaves on screen at once (for performance)")] public int maxLeaves = 400;
    [Tooltip("Terrain trees shorter than this (meters) are ignored, e.g. bushes")] public float minTreeHeight = 3f;

    [Header("Movement")]
    [Tooltip("How fast leaves fall (lower = floatier)")] public float fallSpeed = 1f;
    [Tooltip("Wind push when it's calm (meters per second)")] public float calmWind = 0.6f;
    [Tooltip("Extra wind push in full rain / storm")] public float rainWind = 3.5f;
    [Tooltip("Leaves never fly sideways faster than this (meters per second), even in a storm")] public float maxLeafWindSpeed = 5f;
    [Tooltip("Wind direction when it's not raining (the rain uses its own direction)")] public Vector3 calmWindDirection = new(1f, 0f, 0.3f);
    [Tooltip("How much the leaves swing and flutter")] public float flutter = 0.8f;
    [Tooltip("How fast the leaves spin (degrees per second)")] public float spin = 200f;

    [Header("On the ground")]
    [Tooltip("Leaves land on the ground and stay there a while")] public bool landOnGround = true;
    [Tooltip("What the leaves can land on")] public LayerMask groundLayers = ~0;
    [Tooltip("How long a leaf lives in total, in the air + on the ground (seconds, random between min and max)")] public Vector2 leafLifetime = new(35f, 55f);
    [Tooltip("How often a gust lifts a leaf from the ground (per second, per leaf, in a strong gust)")] public float gustLiftChance = 0.15f;
    [Tooltip("How high a gust throws the leaves (meters per second upwards)")] public Vector2 gustLiftSpeed = new(1.2f, 3f);
    [Tooltip("How much more often leaves are lifted when it rains / storms")] public float rainGustMultiplier = 3f;

    [Tooltip("Filled in automatically - leave it")] public Shader leafShader;

    private struct Tree { public Vector3 position; public float height, radius; }
    private readonly List<Tree> allTrees = new(), nearTrees = new();
    private float averageTreeHeight = 8f;
    private readonly List<ParticleSystem> systems = new();
    private float nextNearScan, nextTreeReload, spawnBudget;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null || FindAnyObjectByType<FallingLeaves>() != null) return;
        if (Terrain.activeTerrain == null) return; // e.g. the menu scene
        new GameObject("FallingLeaves (auto)").AddComponent<FallingLeaves>();
    }

    // Saved in the scene so the shader is included in builds
    void OnValidate() { if (leafShader == null) leafShader = FindLeafShader(); }

    static Shader FindLeafShader()
    {
        Shader s = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
        if (s == null) s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (s == null) s = Shader.Find("Particles/Standard Unlit");
        return s;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        if (Instance != this) return;
        BuildSystems();
        LoadTerrainTrees();
    }

    // ---------- Trees ----------

    void LoadTerrainTrees()
    {
        allTrees.Clear();
        var prototypeSize = new Dictionary<GameObject, Vector2>();
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            TerrainData data = terrain.terrainData;
            if (data == null) continue;
            Vector3 origin = terrain.GetPosition(), size = data.size;
            TreePrototype[] prototypes = data.treePrototypes;

            foreach (TreeInstance tree in data.treeInstances)
            {
                if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length) continue;
                GameObject prefab = prototypes[tree.prototypeIndex].prefab;
                if (prefab == null) continue;
                if (!prototypeSize.TryGetValue(prefab, out Vector2 hr)) prototypeSize[prefab] = hr = MeasurePrefab(prefab);
                float height = hr.x * tree.heightScale;
                if (height < minTreeHeight) continue;
                allTrees.Add(new Tree
                {
                    position = origin + Vector3.Scale(tree.position, size),
                    height = height,
                    radius = Mathf.Max(0.5f, hr.y * tree.widthScale)
                });
            }
        }
        if (allTrees.Count > 0)
        {
            float sum = 0f;
            foreach (Tree t in allTrees) sum += t.height;
            averageTreeHeight = sum / allTrees.Count;
        }
        nextNearScan = 0f;
    }

    // Height and crown radius of a tree prefab (from its meshes)
    static Vector2 MeasurePrefab(GameObject prefab)
    {
        bool any = false;
        Bounds bounds = new Bounds();
        Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
        foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
            Bounds b = mf.sharedMesh.bounds;
            Vector3 c = b.center, e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
        if (!any) return new Vector2(8f, 2.5f);
        Vector3 scale = prefab.transform.localScale;
        return new Vector2(bounds.max.y * scale.y, Mathf.Max(bounds.extents.x * scale.x, bounds.extents.z * scale.z) * 0.8f);
    }

    void FindNearTrees(Vector3 center)
    {
        nearTrees.Clear();
        float r2 = spawnRadius * spawnRadius;
        foreach (Tree t in allTrees)
        {
            Vector3 d = t.position - center;
            d.y = 0f;
            if (d.sqrMagnitude <= r2) nearTrees.Add(t);
        }
    }

    // ---------- Every frame ----------

    void Update()
    {
        if (Instance != this || systems.Count == 0) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        // Trees may change (e.g. a new scene): reload now and then
        if (Time.time >= nextTreeReload)
        {
            nextTreeReload = Time.time + 30f;
            if (allTrees.Count == 0) LoadTerrainTrees();
        }
        if (Time.time >= nextNearScan)
        {
            nextNearScan = Time.time + 1f;
            // Leaves fall from trees upwind, so the wind brings them to where you are
            float fallTime = averageTreeHeight * 0.7f / (0.9f * Mathf.Max(0.05f, fallSpeed));
            Vector3 drift = Vector3.ClampMagnitude(windNow, maxLeafWindSpeed) * 0.8f * fallTime;
            drift.y = 0f;
            FindNearTrees(cam.transform.position - Vector3.ClampMagnitude(drift, spawnRadius * 1.5f));
        }

        float rain = Mathf.Clamp01(RainSystem.RainAmount);
        UpdateWind(rain);

        if (nearTrees.Count == 0) return;
        float rate = calmLeavesPerSecond * Mathf.Lerp(1f, Mathf.Max(1f, rainMultiplier), rain);
        spawnBudget += rate * Time.deltaTime;
        int count = Mathf.Min(Mathf.FloorToInt(spawnBudget), 20);
        spawnBudget -= Mathf.FloorToInt(spawnBudget);
        for (int i = 0; i < count; i++) SpawnLeaf();
    }

    private Vector3 windNow; // direction * strength
    private float windStrengthNow, rainNow;

    void UpdateWind(float rain)
    {
        Vector3 rainDir = RainSystem.WindDirection, calmDir = calmWindDirection;
        rainDir.y = 0f;
        calmDir.y = 0f;
        if (calmDir.sqrMagnitude < 0.001f) calmDir = Vector3.right;
        Vector3 dir = rain > 0.05f && rainDir.sqrMagnitude > 0.001f
            ? Vector3.Slerp(calmDir.normalized, rainDir.normalized, Mathf.Clamp01(rain * 2f))
            : calmDir.normalized;
        windStrengthNow = calmWind + rainWind * rain;
        windNow = dir * windStrengthNow;
        rainNow = rain;
    }

    // Gusts travel across the ground with the wind (0 = still, 1 = strong gust)
    float GustAt(Vector3 p, float t)
    {
        Vector3 d = windNow.sqrMagnitude > 0.001f ? windNow.normalized : Vector3.right;
        float along = Vector3.Dot(new Vector3(p.x, 0f, p.z), d);
        float across = Vector3.Dot(new Vector3(p.x, 0f, p.z), new Vector3(-d.z, 0f, d.x));
        float wave = Mathf.PerlinNoise(along * 0.08f - t * 0.6f, across * 0.08f); // gust fronts
        float global = Mathf.PerlinNoise(t * 0.12f, 7.3f);                        // calmer and windier moments
        return Mathf.Clamp01((wave * 0.6f + global * 0.6f) - 0.45f) * 2.5f;
    }

    static Vector3 RandomSpin() => new Vector3(Random.Range(-1f, 1f), Random.Range(-0.5f, 0.5f), Random.Range(-1f, 1f));

    // ---------- Leaf physics (done here, per leaf) ----------

    private ParticleSystem.Particle[] buffer;
    private readonly List<Vector4> states = new(); // x = 1 on the ground

    void LateUpdate()
    {
        if (Instance != this) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        float t = Time.time;
        float spinRate = spin;
        float fall = 0.9f * Mathf.Max(0.05f, fallSpeed);
        float flutterNow = flutter * (1f + rainNow);
        float drag = 1f - Mathf.Exp(-1.5f * dt);
        Vector3 windDir = windNow.sqrMagnitude > 0.001f ? windNow.normalized : Vector3.right;

        foreach (ParticleSystem ps in systems)
        {
            int count = ps.particleCount;
            if (count == 0) continue;
            if (buffer == null || buffer.Length < ps.main.maxParticles) buffer = new ParticleSystem.Particle[ps.main.maxParticles];
            count = ps.GetParticles(buffer);
            ps.GetCustomParticleData(states, ParticleSystemCustomData.Custom1);

            for (int i = 0; i < count; i++)
            {
                ParticleSystem.Particle p = buffer[i];
                Vector4 state = states[i];
                float seed = (p.randomSeed % 1000) * 0.37f;
                Vector3 pos = p.position;
                float gust = GustAt(pos, t);

                if (state.x > 0.5f)
                {
                    // ON THE GROUND: lies still until a gust lifts it
                    p.velocity = Vector3.zero;
                    p.angularVelocity3D = Vector3.zero;
                    float chance = gustLiftChance * gust * Mathf.Lerp(1f, Mathf.Max(1f, rainGustMultiplier), rainNow) * (0.5f + windStrengthNow * 0.5f);
                    if (Random.value < chance * dt)
                    {
                        state.x = 0f;
                        float lift = Random.Range(gustLiftSpeed.x, Mathf.Max(gustLiftSpeed.x, gustLiftSpeed.y)) * (0.7f + gust * 0.5f);
                        p.velocity = windDir * Mathf.Min(windStrengthNow * Random.Range(1f, 2f) + 0.5f, maxLeafWindSpeed * 1.3f) + Vector3.up * lift;
                        p.position = pos + Vector3.up * 0.05f;
                        p.angularVelocity3D = RandomSpin() * spinRate * 1.5f;
                    }
                }
                else
                {
                    // IN THE AIR: pushed by the wind, swinging, falling slowly
                    Vector3 flutterV = new Vector3(Mathf.PerlinNoise(t * 0.9f + seed, 0.5f) - 0.5f, 0f, Mathf.PerlinNoise(0.5f, t * 0.9f + seed) - 0.5f) * 2f * flutterNow;
                    Vector3 target = Vector3.ClampMagnitude(windNow * (0.8f + gust * 0.8f), maxLeafWindSpeed) + flutterV;
                    target.y = -fall + Mathf.Sin(t * 2.3f + seed) * 0.35f * flutterNow;
                    Vector3 v = p.velocity;
                    // Air drag towards the wind speed; upwards motion (after a gust) slows down quickly
                    v.x = Mathf.Lerp(v.x, target.x, drag);
                    v.z = Mathf.Lerp(v.z, target.z, drag);
                    v.y = Mathf.Lerp(v.y, target.y, 1f - Mathf.Exp(-(v.y > 0f ? 2.5f : 1.5f) * dt));
                    if (v.y > 0f) v.y -= 4f * dt; // gravity wins after being thrown up
                    p.velocity = v;
                    if (p.angularVelocity3D.sqrMagnitude < 1f) p.angularVelocity3D = RandomSpin() * spinRate;

                    // Landing
                    if (landOnGround && v.y < 0.2f)
                    {
                        float reach = Mathf.Max(0.15f, -v.y * dt * 2f + 0.08f);
                        if (Physics.Raycast(pos + Vector3.up * 0.05f, Vector3.down, out RaycastHit hit, reach + 0.05f, groundLayers, QueryTriggerInteraction.Ignore))
                        {
                            state.x = 1f;
                            p.position = hit.point + hit.normal * 0.02f;
                            p.velocity = Vector3.zero;
                            p.angularVelocity3D = Vector3.zero;
                            // Lie flat on the ground, facing up or down at random
                            Quaternion flat = Quaternion.FromToRotation(Vector3.back, hit.normal) * Quaternion.Euler(0f, 0f, seed * 97f % 360f);
                            p.rotation3D = flat.eulerAngles;
                        }
                    }
                }

                buffer[i] = p;
                states[i] = state;
            }

            // Custom data first: SetParticles removes expired leaves, then the lists wouldn't match
            ps.SetCustomParticleData(states, ParticleSystemCustomData.Custom1);
            ps.SetParticles(buffer, count);
        }
    }

    void SpawnLeaf()
    {
        // Of two random trees, use the one closer to the camera: more leaves where you can see them
        Tree tree = nearTrees[Random.Range(0, nearTrees.Count)];
        Tree other = nearTrees[Random.Range(0, nearTrees.Count)];
        Camera cam = Camera.main;
        if (cam != null && (other.position - cam.transform.position).sqrMagnitude < (tree.position - cam.transform.position).sqrMagnitude) tree = other;
        Vector2 offset = Random.insideUnitCircle * tree.radius;
        Vector3 position = tree.position + new Vector3(offset.x, tree.height * Random.Range(0.45f, 0.9f), offset.y);

        ParticleSystem ps = systems[Random.Range(0, systems.Count)];
        if (ps.particleCount >= ps.main.maxParticles) return;
        var emit = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = new Vector3(Random.Range(-0.3f, 0.3f), -0.2f, Random.Range(-0.3f, 0.3f)),
            startSize = Random.Range(leafSize.x, Mathf.Max(leafSize.x, leafSize.y)),
            startLifetime = Random.Range(leafLifetime.x, Mathf.Max(leafLifetime.x, leafLifetime.y)),
            angularVelocity3D = RandomSpin() * spin,
            rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
            startColor = Color.Lerp(colorA, colorB, Random.value),
            applyShapeToPosition = false
        };
        ps.Emit(emit, 1);
    }

    // ---------- Particle systems (one per leaf picture) ----------

    void BuildSystems()
    {
        foreach (ParticleSystem old in systems) if (old != null) Destroy(old.gameObject);
        systems.Clear();

        var materials = new List<Material>();
        if (leafMaterial != null) materials.Add(leafMaterial);
        else
        {
            if (leafShader == null) leafShader = FindLeafShader();
            if (leafTextures != null)
                foreach (Texture2D tex in leafTextures)
                    if (tex != null) materials.Add(MakeMaterial(removeBackground ? ParticleMaterials.RemoveBackground(tex, backgroundTolerance) : tex));
            if (materials.Count == 0) materials.Add(MakeMaterial(MakeDefaultLeafTexture()));
        }

        int perSystem = Mathf.Max(10, maxLeaves / materials.Count);
        foreach (Material mat in materials) systems.Add(CreateSystem(mat, perSystem));
    }

    Material MakeMaterial(Texture2D texture)
    {
        // Cut out the transparent part, both sides visible, lit by the sun (works the same in builds)
        return ParticleMaterials.Create("Leaf (" + texture.name + ")", texture, false, 0.4f, true, true);
    }

    ParticleSystem CreateSystem(Material material, int max)
    {
        GameObject go = new GameObject("Leaves");
        go.transform.SetParent(transform, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        main.startSpeed = 0f;
        main.startRotation3D = true;
        main.gravityModifier = 0f; // falling is done by this script
        main.scalingMode = ParticleSystemScalingMode.Shape;

        var emission = ps.emission;
        emission.enabled = false; // leaves are spawned by this script
        var shape = ps.shape;
        shape.enabled = false;
        // Movement and the on-ground flag (custom data) are handled in LateUpdate: the Custom Data module must stay OFF or it overwrites them
        var custom = ps.customData;
        custom.enabled = false;

        // Shrink away at the very end of their life
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.9f, 1f), new Keyframe(1f, 0f)));

        var leafRenderer = go.GetComponent<ParticleSystemRenderer>();
        leafRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        leafRenderer.mesh = ParticleMaterials.Quad();
        leafRenderer.alignment = ParticleSystemRenderSpace.World;
        leafRenderer.material = material;
        leafRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        leafRenderer.receiveShadows = true;

        ps.Play();
        return ps;
    }

    // A simple leaf shape, used until you assign your own pictures
    Texture2D MakeDefaultLeafTexture()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Default Leaf", wrapMode = TextureWrapMode.Clamp };
        Color leaf = new Color(0.55f, 0.42f, 0.18f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f; // -1..1 across
                float v = (y + 0.5f) / n;           // 0..1 along
                float width = Mathf.Sin(v * Mathf.PI) * 0.55f;
                bool inside = Mathf.Abs(u) < width;
                bool vein = Mathf.Abs(u) < 0.04f && v > 0.05f && v < 0.92f;
                Color c = vein ? leaf * 0.7f : leaf * (0.85f + 0.3f * v);
                c.a = inside ? 1f : 0f;
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return tex;
    }
}
