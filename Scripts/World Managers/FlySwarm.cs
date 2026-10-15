using UnityEngine;

// A few tiny black flies buzzing around the player, just for atmosphere. Creates itself at game start;
// add it to a scene object only if you want to change the settings. Costs almost nothing (a handful of particles).
public class FlySwarm : MonoBehaviour
{
    [Header("Amount")]
    [Tooltip("How many flies")] [Range(1, 40)] public int flyCount = 6;
    [Tooltip("Flies never go farther than this from the player (meters)")] public float radius = 2.2f;
    [Tooltip("Size of the cloud the flies buzz in (meters). Small = they stay close together like around a spot")] public float cloudSize = 0.7f;
    [Tooltip("Height of the cloud above the player's feet (meters, random between min and max over time)")] public Vector2 heightRange = new(0.7f, 1.9f);

    [Header("Look")]
    [Tooltip("Size of a fly in meters (random between min and max). Around 0.02 looks like a small black dot")] public Vector2 flySize = new(0.014f, 0.024f);
    public Color color = new(0.02f, 0.02f, 0.02f, 1f);

    [Header("Movement")]
    [Tooltip("Top speed when darting from one spot to another (meters per second)")] public float speed = 3.2f;
    [Tooltip("How quickly they turn and change speed. Higher = jerkier, more fly-like")] public float agility = 9f;
    [Tooltip("How long a fly flies towards a spot before choosing another one (seconds, min and max)")] public Vector2 retargetTime = new(0.12f, 0.45f);
    [Tooltip("Chance (0-1) that a fly hovers in place for a moment instead of darting")] [Range(0f, 1f)] public float hoverChance = 0.12f;
    [Tooltip("The tiny constant shaking of the fly while it buzzes (meters)")] public float buzz = 0.01f;
    [Tooltip("How slowly the cloud follows the player (seconds). Higher = flies stay behind more when you run")] public float followTime = 0.5f;
    [Tooltip("How far the cloud drifts around the player on its own (meters)")] public float drift = 1.0f;

    [Header("Conditions")]
    [Tooltip("Flies disappear while it rains")] public bool hideInRain = true;
    [Tooltip("Flies disappear (shrink) when you are moving faster than this (meters per second). 0 = never")] public float hideAboveSpeed = 0f;
    [Tooltip("Optional: your own material. Empty = a black dot is created automatically")] public Material flyMaterial;

    [Header("Butterflies")]
    [Tooltip("Butterflies fluttering farther away from the player and lower than the flies")] public bool butterflies = true;
    [Tooltip("How many butterflies")] [Range(0, 30)] public int butterflyCount = 5;
    [Tooltip("Butterflies stay between these two distances from the player (meters, min and max)")] public Vector2 butterflyDistance = new(4f, 14f);
    [Tooltip("Height above the ground (meters, min and max). Lower than the flies")] public Vector2 butterflyHeight = new(0.4f, 1.3f);
    [Tooltip("Wingspan in meters (random between min and max)")] public Vector2 butterflySize = new(0.09f, 0.15f);
    [Tooltip("Each butterfly gets one of these colors")] public Color[] butterflyColors = { new Color(1f, 0.95f, 0.75f), new Color(1f, 0.65f, 0.25f), new Color(0.6f, 0.75f, 1f), new Color(1f, 0.85f, 0.3f) };
    [Tooltip("How fast they travel (meters per second)")] public float butterflySpeed = 1.1f;
    [Tooltip("Wing beats per second")] public float flapsPerSecond = 5f;
    [Tooltip("Butterflies only come out in daylight")] public bool butterfliesOnlyByDay = true;
    [Tooltip("Optional: your own material. Empty = a butterfly shape is created automatically")] public Material butterflyMaterial;
    [Tooltip("Filled in automatically - leave it")] public Shader flyShader;

    class Fly { public Vector3 pos, vel, target; public float nextRetarget, size, seed; }
    class Butterfly { public Vector3 pos, vel, target; public float nextRetarget, size, seed, phase; public Color color; }

    Fly[] flies;
    ParticleSystem ps;
    ParticleSystemRenderer psRenderer;
    Butterfly[] bflies;
    ParticleSystem bps;
    ParticleSystemRenderer bpsRenderer;
    ParticleSystem.Particle[] bbuffer;
    float butterflyVisibility = 1f;
    ParticleSystem.Particle[] buffer;
    Transform player;
    Vector3 center, centerVelocity;
    float nextPlayerSearch, visibility = 1f, lastHeight, heightSeed;
    Vector3 lastPlayerPos;
    bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<FlySwarm>() != null) return;
        if (FindAnyObjectByType<PlayerMovement>() == null) return; // e.g. the menu scene
        new GameObject("FlySwarm (auto)").AddComponent<FlySwarm>();
    }

    // Saved in the scene so the shader is included in builds
    void OnValidate() { if (flyShader == null) flyShader = FindShader(); }

    static Shader FindShader()
    {
        Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (s == null) s = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
        if (s == null) s = Shader.Find("Particles/Standard Unlit");
        return s;
    }

    void Start() { heightSeed = Random.value * 100f; }

    void OnDestroy() { if (ps != null) Destroy(ps.gameObject); if (bps != null) Destroy(bps.gameObject); }

    void LateUpdate()
    {
        if (player == null)
        {
            if (Time.unscaledTime < nextPlayerSearch) return;
            nextPlayerSearch = Time.unscaledTime + 1f;
            PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
            if (pm == null) return;
            player = pm.transform;
            initialized = false;
        }
        if (!initialized) Initialize();

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Rain / running: fade the flies out by shrinking them
        float playerSpeed = (player.position - lastPlayerPos).magnitude / dt;
        lastPlayerPos = player.position;
        bool hide = (hideInRain && RainSystem.IsRaining) || (hideAboveSpeed > 0f && playerSpeed > hideAboveSpeed && playerSpeed < 30f);
        visibility = Mathf.MoveTowards(visibility, hide ? 0f : 1f, dt * 1.5f);
        psRenderer.enabled = visibility > 0.01f;

        MoveCenter(dt);
        if (visibility > 0.01f) UpdateFlies(dt);
        UpdateButterflies(dt);
    }

    void UpdateFlies(float dt)
    {
        float maxDist = Mathf.Max(radius, cloudSize);
        for (int i = 0; i < flies.Length; i++)
        {
            Fly f = flies[i];
            if (Time.time >= f.nextRetarget) PickTarget(f);

            Vector3 toTarget = f.target - f.pos;
            float distance = toTarget.magnitude;
            // Slow down near the target so the fly "lands" on the spot instead of overshooting
            Vector3 wanted = distance > 0.001f ? toTarget / distance * speed * Mathf.Clamp01(distance / 0.25f) : Vector3.zero;
            f.vel = Vector3.Lerp(f.vel, wanted, 1f - Mathf.Exp(-agility * dt));
            f.pos += f.vel * dt;

            // Too far from the player (he ran away): come back close to the cloud
            if ((f.pos - player.position).sqrMagnitude > (maxDist + 1f) * (maxDist + 1f)) Respawn(f);

            // Never below the player's feet level
            float minY = player.position.y + 0.25f;
            if (f.pos.y < minY) { f.pos.y = minY; f.vel.y = Mathf.Abs(f.vel.y); }

            // The tiny shaking (only visual, it doesn't change the real position)
            float t = Time.time * 40f + f.seed;
            Vector3 shake = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, Mathf.PerlinNoise(t, t) - 0.5f) * (buzz * 2f);

            buffer[i].position = f.pos + shake;
            buffer[i].startSize = f.size * visibility;
            buffer[i].startColor = color;
            buffer[i].remainingLifetime = 1000000f;
            buffer[i].startLifetime = 1000000f;
        }
        ps.SetParticles(buffer, flies.Length);
    }

    // The cloud follows the player with a delay and drifts slowly around him, at a changing height
    void MoveCenter(float dt)
    {
        float t = Time.time * 0.15f;
        float limit = Mathf.Max(0f, radius - cloudSize);
        Vector3 offset = new Vector3(Mathf.PerlinNoise(t, heightSeed) - 0.5f, 0f, Mathf.PerlinNoise(heightSeed, t) - 0.5f) * 2f * Mathf.Min(drift, limit);
        float h = Mathf.Lerp(heightRange.x, Mathf.Max(heightRange.x, heightRange.y), Mathf.PerlinNoise(t * 0.7f, heightSeed + 7f));
        Vector3 wanted = player.position + offset + Vector3.up * h;
        center = Vector3.SmoothDamp(center, wanted, ref centerVelocity, Mathf.Max(0.01f, followTime));
    }

    void PickTarget(Fly f)
    {
        bool hover = Random.value < hoverChance;
        f.target = hover ? f.pos : center + Random.insideUnitSphere * cloudSize;
        f.nextRetarget = Time.time + (hover ? Random.Range(0.4f, 1.1f) : Random.Range(retargetTime.x, Mathf.Max(retargetTime.x, retargetTime.y)));
    }

    void Respawn(Fly f)
    {
        f.pos = center + Random.insideUnitSphere * cloudSize;
        f.vel = Vector3.zero;
        PickTarget(f);
    }

    // ---------- Butterflies ----------

    float GroundHeight(Vector3 p)
    {
        return WorldUtil.TerrainHeight(p, out float h) ? h : player.position.y;
    }

    Vector3 RandomButterflySpot()
    {
        float angle = Random.value * Mathf.PI * 2f;
        float dist = Random.Range(butterflyDistance.x, Mathf.Max(butterflyDistance.x, butterflyDistance.y));
        Vector3 p = player.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
        p.y = GroundHeight(p) + Random.Range(butterflyHeight.x, Mathf.Max(butterflyHeight.x, butterflyHeight.y));
        return p;
    }

    void PickTarget(Butterfly b)
    {
        b.target = RandomButterflySpot();
        b.nextRetarget = Time.time + Random.Range(5f, 9f);
    }

    void Respawn(Butterfly b)
    {
        b.pos = RandomButterflySpot();
        b.vel = Vector3.zero;
        PickTarget(b);
    }

    void UpdateButterflies(float dt)
    {
        if (bflies == null) return;

        bool night = butterfliesOnlyByDay && DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;
        bool hide = night || (hideInRain && RainSystem.IsRaining);
        butterflyVisibility = Mathf.MoveTowards(butterflyVisibility, hide ? 0f : 1f, dt * 0.5f);
        bpsRenderer.enabled = butterflyVisibility > 0.01f;
        if (butterflyVisibility <= 0.01f) return;

        float far = Mathf.Max(butterflyDistance.x, butterflyDistance.y) + 6f;
        for (int i = 0; i < bflies.Length; i++)
        {
            Butterfly b = bflies[i];
            Vector3 toTarget = b.target - b.pos;
            if (Time.time >= b.nextRetarget || toTarget.sqrMagnitude < 0.36f) { PickTarget(b); toTarget = b.target - b.pos; }

            // Slow, lazy steering with a sideways wobble and an up-and-down bob
            float t = Time.time;
            Vector3 dir = toTarget.normalized;
            Vector3 side = Vector3.Cross(dir, Vector3.up) * Mathf.Sin(t * 1.7f + b.seed);
            Vector3 wanted = (dir + side * 0.6f).normalized * butterflySpeed;
            wanted.y += Mathf.Sin(t * 3.1f + b.seed * 2f) * 0.45f;
            b.vel = Vector3.Lerp(b.vel, wanted, 1f - Mathf.Exp(-1.5f * dt));
            b.pos += b.vel * dt;

            float minY = GroundHeight(b.pos) + 0.2f;
            if (b.pos.y < minY) { b.pos.y = minY; b.vel.y = Mathf.Abs(b.vel.y); }

            // Too far from the player: come back to a spot around him
            Vector3 flat = b.pos - player.position; flat.y = 0f;
            if (flat.sqrMagnitude > far * far) Respawn(b);

            // Wing beat: the wings open and close (the size on one axis goes up and down)
            float flap = Mathf.Abs(Mathf.Cos(t * flapsPerSecond * Mathf.PI + b.phase));
            float wingSpan = b.size * (0.2f + 0.8f * flap) * butterflyVisibility;
            bbuffer[i].position = b.pos;
            bbuffer[i].startSize3D = new Vector3(wingSpan, b.size * butterflyVisibility, 1f);
            bbuffer[i].rotation = -Mathf.Clamp(Vector3.Dot(b.vel, Vector3.Cross(Vector3.up, dir)) * 12f, -25f, 25f);
            bbuffer[i].startColor = b.color;
            bbuffer[i].remainingLifetime = 1000000f;
            bbuffer[i].startLifetime = 1000000f;
        }
        bps.SetParticles(bbuffer, bflies.Length);
    }

    // ---------- Setup ----------

    void Initialize()
    {
        initialized = true;
        lastPlayerPos = player.position;
        center = player.position + Vector3.up * Mathf.Lerp(heightRange.x, heightRange.y, 0.5f);
        centerVelocity = Vector3.zero;

        if (ps != null) Destroy(ps.gameObject);
        ps = CreateSystem(Mathf.Max(1, flyCount));
        psRenderer = ps.GetComponent<ParticleSystemRenderer>();

        flies = new Fly[Mathf.Max(1, flyCount)];
        buffer = new ParticleSystem.Particle[flies.Length];
        ps.Emit(flies.Length);
        ps.GetParticles(buffer);
        for (int i = 0; i < flies.Length; i++)
        {
            flies[i] = new Fly { size = Random.Range(flySize.x, Mathf.Max(flySize.x, flySize.y)), seed = Random.value * 100f };
            Respawn(flies[i]);
        }

        if (bps != null) Destroy(bps.gameObject);
        bflies = null;
        if (butterflies && butterflyCount > 0) InitializeButterflies();
    }

    void InitializeButterflies()
    {
        int n = butterflyCount;
        bps = CreateSystem(n, true);
        bpsRenderer = bps.GetComponent<ParticleSystemRenderer>();
        bbuffer = new ParticleSystem.Particle[n];
        bps.Emit(n);
        bps.GetParticles(bbuffer);
        bflies = new Butterfly[n];
        Color[] palette = butterflyColors != null && butterflyColors.Length > 0 ? butterflyColors : new[] { Color.white };
        for (int i = 0; i < n; i++)
        {
            bflies[i] = new Butterfly
            {
                size = Random.Range(butterflySize.x, Mathf.Max(butterflySize.x, butterflySize.y)),
                seed = Random.value * 100f,
                phase = Random.value * 6.28f,
                color = palette[Random.Range(0, palette.Length)]
            };
            Respawn(bflies[i]);
        }
        butterflyVisibility = 1f;
    }

    ParticleSystem CreateSystem(int count, bool butterfly = false)
    {
        GameObject go = new GameObject(butterfly ? "Butterflies" : "Flies");
        go.transform.SetParent(transform, false);
        ParticleSystem system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = system.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count;
        main.startSpeed = 0f;
        main.startSize3D = butterfly;
        main.startLifetime = 1000000f;
        main.gravityModifier = 0f;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        var emission = system.emission; emission.enabled = false;
        var shape = system.shape; shape.enabled = false;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.material = butterfly ? (butterflyMaterial != null ? butterflyMaterial : MakeButterflyMaterial()) : (flyMaterial != null ? flyMaterial : MakeMaterial());
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        system.Play();
        return system;
    }

    // A round black dot: solid inside, cut out outside (no transparency sorting needed)
    Material MakeMaterial()
    {
        if (flyShader == null) flyShader = FindShader();
        const int size = 16;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "FlyDot", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, d < 0.85f ? 1f : 0f));
            }
        tex.Apply();

        Material mat = ParticleMaterials.Create("Fly", tex, false, 0.5f);
        return mat;
    }

    // A simple butterfly shape (two pairs of wings and a body), grey so the color of each butterfly tints it
    Material MakeButterflyMaterial()
    {
        if (flyShader == null) flyShader = FindShader();
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "ButterflyShape", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float au = Mathf.Abs(u);
                // Upper and lower wing as ellipses (value < 1 = inside), the body as a thin vertical bar
                float upper = Mathf.Pow((au - 0.5f) / 0.48f, 2f) + Mathf.Pow((v - 0.3f) / 0.6f, 2f);
                float lower = Mathf.Pow((au - 0.38f) / 0.34f, 2f) + Mathf.Pow((v + 0.45f) / 0.42f, 2f);
                float wing = Mathf.Min(upper, lower);
                bool body = au < 0.06f && Mathf.Abs(v) < 0.72f;
                float shade = 1f, alpha = 0f;
                if (body) { shade = 0.25f; alpha = 1f; }
                else if (wing < 1f) { alpha = 1f; shade = wing > 0.7f ? 0.55f : Mathf.Lerp(1f, 0.8f, wing); } // darker edge
                tex.SetPixel(x, y, new Color(shade, shade, shade, alpha));
            }
        tex.Apply();

        Material mat = ParticleMaterials.Create("Butterfly", tex, false, 0.5f);
        return mat;
    }
}
