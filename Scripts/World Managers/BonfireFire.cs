using UnityEngine;
using UnityEngine.Rendering;

// Bonfire flames, embers, smoke and night-strong flickering light; auto-added to '...fire...' CraftingStations, also previews in edit mode.
[ExecuteAlways]
public class BonfireFire : MonoBehaviour
{
    [Header("Position")]
    [Tooltip("Height of the fire above the bonfire's pivot (meters)")] public float height = 0.15f;
    [Tooltip("Size of the whole fire (1 = normal)")] [Range(0.2f, 3f)] public float fireSize = 1f;

    [Header("Flames")]
    [Tooltip("Flames per second")] public float flameRate = 45f;
    [Tooltip("Width of the fire base (meters)")] public float flameRadius = 0.22f;
    public Color flameColorBottom = new Color(1f, 0.85f, 0.35f);
    public Color flameColorTop = new Color(1f, 0.3f, 0.05f);

    [Header("Embers (small sparks)")]
    public bool embers = true;
    public float emberRate = 8f;
    public Color emberColor = new Color(1f, 0.55f, 0.15f);

    [Header("Smoke")]
    public bool smoke = true;
    public float smokeRate = 3f;
    public Color smokeColor = new Color(0.35f, 0.33f, 0.32f, 0.25f);

    [Header("Light")]
    public Color lightColor = new Color(1f, 0.55f, 0.22f);
    [Tooltip("How far the fire lights (meters)")] public float lightRange = 12f;
    [Tooltip("Light strength at night")] public float nightIntensity = 5f;
    [Tooltip("Light strength during the day")] public float dayIntensity = 1f;
    [Tooltip("How much the light flickers (0 = steady)")] [Range(0f, 1f)] public float flicker = 0.35f;
    [Tooltip("Shadows from the fire light (prettier, but costs performance)")] public bool castShadows = false;

    private Transform fireRoot;
    private Light fireLight;
    private float noiseSeed;
    private float nightFactor = -1f;

    private static Material flameMaterial, smokeMaterial;
    private static Texture2D softTexture;

    private const string RootName = "Fire (BonfireFire)";

    void OnEnable()
    {
        if (noiseSeed == 0f) noiseSeed = Random.value * 100f + 1f;
        Rebuild();
    }

    void OnDisable()
    {
        if (Application.isPlaying)
        {
            if (fireRoot != null) Destroy(fireRoot.gameObject);
            fireRoot = null;
            return;
        }
#if UNITY_EDITOR
        // Can't delete objects while Unity is turning them off: do it right after
        UnityEditor.EditorApplication.delayCall += RemoveIfDisabled;
#endif
    }

    // Changing a value in the Inspector rebuilds the fire (in Play and outside Play)
    void OnValidate()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= DelayedRebuild;
        UnityEditor.EditorApplication.delayCall += DelayedRebuild;
#endif
    }

    void DelayedRebuild() { if (this != null && isActiveAndEnabled) Rebuild(); }

    void RemoveIfDisabled() { if (this != null && !isActiveAndEnabled) RemoveFire(); }

    public void Rebuild()
    {
        RemoveFire();
        Build();
    }

    // Removes the fire objects (also old ones left after the scripts reload)
    void RemoveFire()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (child.name != RootName) continue;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
        fireRoot = null;
        fireLight = null;
    }

    // Fire objects are never saved in the scene or the prefab: they are rebuilt from the settings
    static GameObject NewObject(string name) => new GameObject(name) { hideFlags = HideFlags.DontSave };

    void Update()
    {
        if (fireRoot == null || fireLight == null) return;

        // Keep the fire on the bonfire with its own size (even if the bonfire model is scaled)
        fireRoot.position = transform.position + Vector3.up * height;
        Vector3 parentScale = transform.lossyScale;
        fireRoot.localScale = new Vector3(fireSize / Safe(parentScale.x), fireSize / Safe(parentScale.y), fireSize / Safe(parentScale.z));

        // Light: strong at night, weak during the day, always flickering
        float targetNight = DayNightCycle.NightAmount();
        nightFactor = nightFactor < 0f ? targetNight : Mathf.MoveTowards(nightFactor, targetNight, Time.deltaTime * 0.5f);
        float baseIntensity = Mathf.Lerp(dayIntensity, nightIntensity, nightFactor);
        float t = Time.time * 7f + noiseSeed;
        float noise = Mathf.PerlinNoise(t, noiseSeed) * 0.7f + Mathf.PerlinNoise(t * 2.3f, noiseSeed + 5f) * 0.3f;
        fireLight.intensity = baseIntensity * (1f - flicker * 0.5f + flicker * noise);
        fireLight.range = lightRange * fireSize;
        fireLight.color = lightColor;
        fireLight.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
        fireLight.transform.localPosition = new Vector3(0f, 0.5f, 0f) + new Vector3(noise - 0.5f, 0f, Mathf.PerlinNoise(noiseSeed, t) - 0.5f) * 0.08f * flicker;
    }

    static float Safe(float v) => Mathf.Abs(v) < 0.0001f ? 1f : v;

    // ---------- Building the fire ----------

    void Build()
    {
        CreateMaterials();

        fireRoot = NewObject(RootName).transform;
        fireRoot.SetParent(transform, false);
        fireRoot.position = transform.position + Vector3.up * height;

        CreateFlames();
        if (embers) CreateEmbers();
        if (smoke) CreateSmoke();

        GameObject lightObject = NewObject("Fire Light");
        lightObject.transform.SetParent(fireRoot, false);
        fireLight = lightObject.AddComponent<Light>();
        fireLight.type = LightType.Point;
        fireLight.color = lightColor;
        fireLight.range = lightRange;
        fireLight.renderMode = LightRenderMode.ForcePixel;
        Update();
    }

    void CreateFlames()
    {
        ParticleSystem ps = NewSystem("Flames", flameMaterial, 0);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(flameColorBottom, Color.Lerp(flameColorBottom, flameColorTop, 0.5f));
        main.maxParticles = 200;

        SetEmitter(ps, flameRate, 6f, flameRadius);

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        SetColorOverLifetime(ps,
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(flameColorTop, 0.6f), new GradientColorKey(new Color(0.4f, 0.05f, 0f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.6f, 0.55f), new GradientAlphaKey(0f, 1f) });
        SetSizeAndSpin(ps, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.15f)), 1.5f);
        SetNoise(ps, 0.35f, 1.2f, 1.5f);
    }

    void CreateEmbers()
    {
        ParticleSystem ps = NewSystem("Embers", flameMaterial, 2);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
        main.startColor = emberColor;
        main.gravityModifier = -0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // embers fly away with the wind
        main.maxParticles = 100;

        SetEmitter(ps, emberRate, 20f, flameRadius * 0.8f);
        SetColorOverLifetime(ps,
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(emberColor, 0.3f), new GradientColorKey(new Color(0.6f, 0.1f, 0f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        SetNoise(ps, 0.8f, 0.8f, 0.6f);
    }

    void CreateSmoke()
    {
        ParticleSystem ps = NewSystem("Smoke", smokeMaterial, -1);
        ps.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 4.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = smokeColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 60;

        SetEmitter(ps, smokeRate, 10f, flameRadius * 0.6f);
        SetColorOverLifetime(ps,
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        SetSizeAndSpin(ps, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 2.6f)), 0.4f);
        SetNoise(ps, 0.3f, 0.4f, null); // smoke keeps the default scroll speed
    }

    // ---------- Shared particle module setup ----------

    static void SetEmitter(ParticleSystem ps, float rate, float coneAngle, float radius)
    {
        var emission = ps.emission;
        emission.rateOverTime = rate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = coneAngle;
        shape.radius = radius;
        shape.rotation = new Vector3(-90f, 0f, 0f);
    }

    static void SetColorOverLifetime(ParticleSystem ps, GradientColorKey[] colors, GradientAlphaKey[] alphas)
    {
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var g = new Gradient();
        g.SetKeys(colors, alphas);
        color.color = g;
    }

    // Size curve over lifetime + random spin between -spin and spin
    static void SetSizeAndSpin(ParticleSystem ps, AnimationCurve sizeCurve, float spin)
    {
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-spin, spin);
    }

    static void SetNoise(ParticleSystem ps, float strength, float frequency, float? scrollSpeed)
    {
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = strength;
        noise.frequency = frequency;
        if (scrollSpeed.HasValue) noise.scrollSpeed = scrollSpeed.Value;
    }

    ParticleSystem NewSystem(string name, Material material, int sortingFudge)
    {
        GameObject go = NewObject(name);
        go.transform.SetParent(fireRoot, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = material;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sortingFudge = sortingFudge;

        ps.Play();
        return ps;
    }

    // ---------- Materials (made once, shared by every bonfire) ----------

    static void CreateMaterials()
    {
        if (flameMaterial != null && smokeMaterial != null) return;
        if (softTexture == null) softTexture = CreateSoftTexture();
        flameMaterial = CreateParticleMaterial("Bonfire Flames", true);
        smokeMaterial = CreateParticleMaterial("Bonfire Smoke", false);
    }

    static Material CreateParticleMaterial(string name, bool additive)
    {
        Material m = ParticleMaterials.Create(name, softTexture, additive);
        m.hideFlags = HideFlags.HideAndDontSave;
        return m;
    }

    // Round soft dot: white in the middle, transparent at the edges
    static Texture2D CreateSoftTexture()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Bonfire Soft Dot", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3f - 2f * a);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(pixels);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }
}

// Gives every bonfire a BonfireFire automatically (also bonfires placed during the game)
public class BonfireFireSpawner : MonoBehaviour
{
    private float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<BonfireFireSpawner>() != null) return;
        var go = new GameObject("BonfireFireSpawner");
        DontDestroyOnLoad(go);
        go.AddComponent<BonfireFireSpawner>();
    }

    void Update()
    {
        if (Time.time < nextScan) return;
        nextScan = Time.time + 1f;

        foreach (CraftingStation station in CraftingStation.All)
        {
            if (station == null || !IsFire(station)) continue;
            if (station.GetComponentInChildren<BonfireFire>() != null || station.GetComponentInParent<BonfireFire>() != null) continue;
            station.gameObject.AddComponent<BonfireFire>();
        }
    }

    static bool IsFire(CraftingStation station)
    {
        string a = station.stationName != null ? station.stationName.ToLowerInvariant() : "";
        string b = station.gameObject.name.ToLowerInvariant();
        return a.Contains("fire") || b.Contains("fire");
    }
}
