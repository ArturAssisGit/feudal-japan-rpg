using UnityEngine;

// Put on an empty GameObject (e.g. "Weather"): rain around the camera with splashes, sound, darker light and fog.
// Starts/stops on its own (or with a test key); each rain picks a random preset (light rain, rain, storm...).
public class RainSystem : MonoBehaviour
{
    [System.Serializable]
    public class RainPreset
    {
        public string name = "Rain";
        [Tooltip("How likely this preset is to be picked (higher = more often)")] public float chance = 1f;
        [Tooltip("Drops per second")] public float dropsPerSecond = 4000f;
        public float dropSpeed = 20f;
        [Tooltip("Slant of the rain (X / Z = sideways, Y = 0)")] public Vector3 wind = new(2f, 0f, 0.5f);
        [Tooltip("How much darker the sun gets (needs DayNightCycle on the light)")] [Range(0f, 1f)] public float sunDarken = 0.4f;
        [Tooltip("How much darker the ambient light gets")] [Range(0f, 1f)] public float ambientDarken = 0.2f;
        [Tooltip("Fog thickness")] public float fogDensity = 0.02f;
        [Tooltip("Fog color in this rain")] public Color fogColor = new(0.45f, 0.48f, 0.52f);
        [Tooltip("Player speed in this rain (0.8 = 20% slower, 1 = normal)")] [Range(0.1f, 1f)] public float playerSpeedMultiplier = 1f;
        [Tooltip("How much more trees and grass move (1 = normal, 3 = three times more)")] public float foliageWind = 1.5f;
        [Tooltip("Volumetric fog density in this rain (0-1). Needs the Volumetric Fog override in the Global Volume")] [Range(0f, 1f)] public float volumetricDensity = 0.5f;
        [Tooltip("Volumetric fog: how far you can see through it (meters, lower = thicker)")] public float volumetricVisibility = 40f;

        public RainPreset Copy() => (RainPreset)MemberwiseClone();

        public static RainPreset Lerp(RainPreset a, RainPreset b, float t)
        {
            RainPreset r = b.Copy();
            r.dropsPerSecond = Mathf.Lerp(a.dropsPerSecond, b.dropsPerSecond, t);
            r.dropSpeed = Mathf.Lerp(a.dropSpeed, b.dropSpeed, t);
            r.wind = Vector3.Lerp(a.wind, b.wind, t);
            r.sunDarken = Mathf.Lerp(a.sunDarken, b.sunDarken, t);
            r.ambientDarken = Mathf.Lerp(a.ambientDarken, b.ambientDarken, t);
            r.fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t);
            r.fogColor = Color.Lerp(a.fogColor, b.fogColor, t);
            r.playerSpeedMultiplier = Mathf.Lerp(a.playerSpeedMultiplier, b.playerSpeedMultiplier, t);
            r.foliageWind = Mathf.Lerp(a.foliageWind, b.foliageWind, t);
            r.volumetricDensity = Mathf.Lerp(a.volumetricDensity, b.volumetricDensity, t);
            r.volumetricVisibility = Mathf.Lerp(a.volumetricVisibility, b.volumetricVisibility, t);
            return r;
        }
    }

    public static RainSystem Instance { get; private set; }

    // 0 = dry, 1 = full rain (fades in/out). Other scripts read this (e.g. put out bonfires)
    public static float RainAmount { get; private set; }
    public static bool IsRaining => RainAmount > 0.3f;
    // PlayerMovement multiplies the speed by this (1 = normal)
    public static float PlayerSpeedMultiplier { get; private set; } = 1f;
    // Name of the rain preset happening now (empty when dry)
    public static string CurrentPresetName { get; private set; } = "";
    // FoliageWind multiplies tree/grass movement by this (1 = calm)
    public static float FoliageWindMultiplier { get; private set; } = 1f;
    // Direction the rain is blown (the preset's Wind)
    public static Vector3 WindDirection { get; private set; }
    // The rain preset happening now (blended), for VolumetricFogWeather. null before Start
    public static RainPreset Current => Instance != null ? Instance.current : null;

    [Header("Testing")]
    [Tooltip("Press this key to go: dry -> preset 1 -> preset 2 -> preset 3 -> dry (None = off)")] public KeyCode toggleKey = KeyCode.F2;
    [Tooltip("Start the game already raining")] public bool startRaining = false;

    [Header("Automatic weather")]
    [Tooltip("Rain starts and stops by itself")] public bool automaticWeather = true;
    [Tooltip("Each time a dry period ends, the chance that it starts raining (0.3 = 30%). If not, it stays dry for another dry period")]
    [Range(0f, 1f)] public float rainChance = 0.5f;
    [Tooltip("Dry time between rains (real minutes, random between min and max)")] public Vector2 dryMinutes = new(4f, 10f);
    [Tooltip("How long it rains (real minutes, random between min and max)")] public Vector2 rainMinutes = new(2f, 5f);
    [Tooltip("Seconds the rain takes to start or stop (and to change preset)")] public float fadeSeconds = 12f;
    [Tooltip("While it rains, it can change to another preset (e.g. rain becomes a storm)")] public bool changePresetDuringRain = true;
    [Tooltip("How often it may change preset while raining (real minutes, random between min and max)")] public Vector2 presetChangeMinutes = new(3f, 8f);

    [Header("Rain presets (one is picked at random)")]
    public RainPreset[] presets =
    {
        new() { name = "Drizzle", chance = 1f, dropsPerSecond = 100f, dropSpeed = 16f, wind = new(2f, 0f, 0.5f), sunDarken = 0.2f, ambientDarken = 0.1f, fogDensity = 0.01f, fogColor = new(0.62f, 0.66f, 0.7f), playerSpeedMultiplier = 1f, foliageWind = 1.3f, volumetricDensity = 0.3f, volumetricVisibility = 70f },
        new() { name = "Rain", chance = 1f, dropsPerSecond = 4000f, dropSpeed = 20f, wind = new(2f, 0f, 0.5f), sunDarken = 0.4f, ambientDarken = 0.2f, fogDensity = 0.02f, fogColor = new(0.45f, 0.48f, 0.52f), playerSpeedMultiplier = 1f, foliageWind = 2f },
        new() { name = "Storm", chance = 1f, dropsPerSecond = 10000f, dropSpeed = 20f, wind = new(10f, 0f, 0.5f), sunDarken = 0.8f, ambientDarken = 0.4f, fogDensity = 0.02f, fogColor = new(0.3f, 0.32f, 0.36f), playerSpeedMultiplier = 0.8f, foliageWind = 3.5f, volumetricDensity = 0.7f, volumetricVisibility = 25f },
    };

    [Header("Drops")]
    [Tooltip("Size of the area around the camera where it rains (meters)")] public float areaSize = 35f;
    [Tooltip("How high above the camera the drops start")] public float spawnHeight = 14f;
    [Tooltip("Thickness of a drop")] public float dropWidth = 0.015f;
    [Tooltip("How long the drop streak looks")] public float dropLength = 0.035f;
    public Color dropColor = new(0.75f, 0.8f, 0.9f, 0.35f);
    [Tooltip("Optional: your own material for the drops (empty = made automatically)")] public Material dropMaterial;

    [Header("Splashes")]
    [Tooltip("Drops stop on roofs/ground and make a tiny splash (costs a bit of performance)")] public bool splashes = true;
    [Tooltip("What the drops hit (untick the Player layer if you have one)")] public LayerMask splashLayers = ~0;
    public Color splashColor = new(0.8f, 0.85f, 0.95f, 0.4f);

    [Header("Sound")]
    [Tooltip("Looping rain sound (optional)")] public AudioClip rainSound;
    [Range(0f, 1f)] public float rainVolume = 0.6f;

    [Header("Atmosphere")]
    [Tooltip("Turn on/thicken the fog while raining")] public bool rainFog = true;
    [Tooltip("How strongly the fog takes each preset's Fog Color (1 = fully)")] [Range(0f, 1f)] public float fogColorStrength = 0.8f;

    private ParticleSystem rain, splash;
    private AudioSource audioSource;
    private float target, nextChange, baseFogDensity;
    private bool fogWasOn;
    private FogMode baseFogMode;
    private Color baseFogColor;

    private RainPreset current; // values used right now (blended)
    private RainPreset blendFrom, blendTo;
    private float blend = 1f;
    private int presetIndex = -1; // -1 = dry
    private float nextPresetChange;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        RainAmount = 0f;
        PlayerSpeedMultiplier = 1f;
        CurrentPresetName = "";
        FoliageWindMultiplier = 1f;
        WindDirection = Vector3.zero;
    }

    void Awake()
    {
        Instance = this;
        if (presets == null || presets.Length == 0) presets = new[] { new RainPreset() };
        current = presets[0].Copy();
        RainAmount = 0f;
        target = 0f;
        if (startRaining)
        {
            SetPreset(PickRandomPreset(-1), true);
            RainAmount = 1f;
        }
    }

    void Start()
    {
        fogWasOn = RenderSettings.fog;
        baseFogDensity = RenderSettings.fogDensity;
        baseFogMode = RenderSettings.fogMode;
        baseFogColor = RenderSettings.fogColor;

        CreateRain();
        if (GetComponent<FoliageWind>() == null) gameObject.AddComponent<FoliageWind>(); // trees/grass move more in the rain
        if (rainSound != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.clip = rainSound;
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
            audioSource.playOnAwake = false;
            audioSource.volume = 0f;
        }
        ScheduleNext();
    }

    void Update()
    {
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
        {
            // Cycle: dry -> preset 1 -> preset 2 -> ... -> dry
            int next = target > 0.5f ? presetIndex + 1 : 0;
            if (next >= presets.Length) StopRain();
            else SetPreset(next, false);
        }

        if (automaticWeather && Time.time >= nextChange)
        {
            if (target > 0.5f) StopRain();
            else if (Random.value < rainChance) SetPreset(PickRandomPreset(-1), false);
            else ScheduleNext(); // no rain this time: another dry period
        }

        // During a long rain the weather can get stronger or weaker
        if (automaticWeather && changePresetDuringRain && target > 0.5f && presets.Length > 1 && Time.time >= nextPresetChange)
            ChangePresetKeepTimer(PickRandomPreset(presetIndex));

        float fade = Time.deltaTime / Mathf.Max(0.1f, fadeSeconds);
        RainAmount = Mathf.MoveTowards(RainAmount, target, fade);
        if (blend < 1f && blendFrom != null && blendTo != null)
        {
            blend = Mathf.MoveTowards(blend, 1f, fade);
            current = RainPreset.Lerp(blendFrom, blendTo, blend);
        }
        Apply();
    }

    // The rain area follows the camera (drops already falling stay where they are)
    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam != null && rain != null) rain.transform.position = cam.transform.position + Vector3.up * spawnHeight;
    }

    void ScheduleNext()
    {
        Vector2 range = target > 0.5f ? rainMinutes : dryMinutes;
        nextChange = Time.time + Random.Range(range.x, range.y) * 60f;
    }

    // Already raining at full strength, no fade (new game starts in the rain)
    public void StartRainInstant(int presetIndex = -1)
    {
        SetPreset(presetIndex < 0 ? PickRandomPreset(-1) : presetIndex, true);
        RainAmount = 1f;
    }

    public void StopRain()
    {
        target = 0f;
        presetIndex = -1;
        CurrentPresetName = "";
        ScheduleNext();
    }

    // Starts raining with this preset (or changes to it if it's already raining)
    public void SetPreset(int index, bool instant)
    {
        if (presets.Length == 0) return;
        index = Mathf.Clamp(index, 0, presets.Length - 1);
        ChangePresetKeepTimer(index, instant);
        target = 1f;
        ScheduleNext();
    }

    void ChangePresetKeepTimer(int index, bool instant = false)
    {
        presetIndex = index;
        CurrentPresetName = presets[index].name;
        nextPresetChange = Time.time + Random.Range(presetChangeMinutes.x, presetChangeMinutes.y) * 60f;

        if (instant || RainAmount <= 0.01f)
        {
            // Nothing is falling yet: switch right away, the rain fades in anyway
            current = presets[index].Copy();
            blend = 1f;
        }
        else
        {
            blendFrom = current.Copy();
            blendTo = presets[index];
            blend = 0f;
        }
        Debug.Log($"Weather: {CurrentPresetName}");
    }

    // Random preset using the "chance" values (avoid = index not to pick again, -1 = any)
    int PickRandomPreset(int avoid)
    {
        float total = 0f;
        for (int i = 0; i < presets.Length; i++)
            if (i != avoid || presets.Length == 1) total += Mathf.Max(0f, presets[i].chance);
        if (total <= 0f) return 0;

        float roll = Random.Range(0f, total);
        for (int i = 0; i < presets.Length; i++)
        {
            if (i == avoid && presets.Length > 1) continue;
            roll -= Mathf.Max(0f, presets[i].chance);
            if (roll <= 0f) return i;
        }
        return presets.Length - 1;
    }

    // Module structs write straight into the particle system (wind can be changed during Play)
    static void SetVelocityWind(ParticleSystem.VelocityOverLifetimeModule velocity, Vector3 wind)
    {
        velocity.x = new ParticleSystem.MinMaxCurve(wind.x);
        velocity.y = new ParticleSystem.MinMaxCurve(wind.y);
        velocity.z = new ParticleSystem.MinMaxCurve(wind.z);
    }

    void Apply()
    {
        if (rain != null)
        {
            var emission = rain.emission;
            emission.rateOverTime = current.dropsPerSecond * RainAmount;
            var main = rain.main;
            float speed = Mathf.Max(1f, current.dropSpeed);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.9f, speed * 1.1f);
            main.startLifetime = (spawnHeight + 6f) / speed;
            SetVelocityWind(rain.velocityOverLifetime, current.wind);
        }

        if (audioSource != null)
        {
            audioSource.volume = rainVolume * RainAmount;
            if (RainAmount > 0f && !audioSource.isPlaying) audioSource.Play();
            else if (RainAmount <= 0f && audioSource.isPlaying) audioSource.Stop();
        }

        PlayerSpeedMultiplier = Mathf.Lerp(1f, current.playerSpeedMultiplier, RainAmount);
        FoliageWindMultiplier = Mathf.Lerp(1f, current.foliageWind, RainAmount);
        WindDirection = current.wind;

        // Light: DayNightCycle reads these every frame
        if (DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.weatherSunDarken = current.sunDarken * RainAmount;
            DayNightCycle.Instance.weatherAmbientDarken = current.ambientDarken * RainAmount;
        }

        if (!rainFog) return;
        if (RainAmount > 0f)
        {
            if (!fogWasOn)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
            }
            float startDensity = fogWasOn ? baseFogDensity : 0f;
            RenderSettings.fogDensity = Mathf.Lerp(startDensity, Mathf.Max(startDensity, current.fogDensity), RainAmount);
            if (DayNightCycle.Instance != null)
            {
                DayNightCycle.Instance.weatherFogColor = current.fogColor;
                DayNightCycle.Instance.weatherFogBlend = RainAmount * fogColorStrength;
            }
            else RenderSettings.fogColor = Color.Lerp(fogWasOn ? baseFogColor : current.fogColor, current.fogColor, RainAmount * fogColorStrength);
        }
        else if (RenderSettings.fog != fogWasOn || RenderSettings.fogDensity != baseFogDensity)
        {
            RenderSettings.fog = fogWasOn;
            RenderSettings.fogDensity = baseFogDensity;
            RenderSettings.fogMode = baseFogMode;
            if (DayNightCycle.Instance == null) RenderSettings.fogColor = baseFogColor;
            else DayNightCycle.Instance.weatherFogBlend = 0f;
        }
    }

    // ---------- Building the particle systems ----------

    void CreateRain()
    {
        GameObject go = new GameObject("Rain Drops");
        go.transform.SetParent(transform, false);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // emit downwards
        rain = go.AddComponent<ParticleSystem>();
        rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = rain.main;
        main.loop = true;
        // Room for the heaviest preset
        float maxDrops = 0f, minSpeed = float.MaxValue;
        foreach (RainPreset p in presets)
        {
            maxDrops = Mathf.Max(maxDrops, p.dropsPerSecond);
            minSpeed = Mathf.Min(minSpeed, Mathf.Max(1f, p.dropSpeed));
        }
        float longestLife = (spawnHeight + 6f) / minSpeed;
        main.startLifetime = (spawnHeight + 6f) / Mathf.Max(1f, current.dropSpeed);
        main.startSpeed = new ParticleSystem.MinMaxCurve(current.dropSpeed * 0.9f, current.dropSpeed * 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(dropWidth * 0.7f, dropWidth * 1.3f);
        main.startColor = dropColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.CeilToInt(maxDrops * longestLife * 1.2f) + 100;
        main.scalingMode = ParticleSystemScalingMode.Shape;

        var emission = rain.emission;
        emission.rateOverTime = 0f;

        var shape = rain.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(areaSize, areaSize, 0.1f);

        var velocity = rain.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        SetVelocityWind(velocity, current.wind);

        var renderer = rain.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = dropLength;
        renderer.lengthScale = 1f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.material = dropMaterial != null ? dropMaterial : MakeMaterial();

        if (splashes)
        {
            var collision = rain.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Low;
            collision.collidesWith = splashLayers;
            collision.lifetimeLoss = 1f; // the drop disappears when it hits something
            collision.bounce = 0f;
            collision.sendCollisionMessages = false;

            splash = CreateSplash(go.transform);
            var sub = rain.subEmitters;
            sub.enabled = true;
            sub.AddSubEmitter(splash, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing);
        }

        rain.Play();
    }

    ParticleSystem CreateSplash(Transform parent)
    {
        GameObject go = new GameObject("Rain Splash");
        go.transform.SetParent(parent, false);
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // splash upwards
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.025f);
        main.startColor = splashColor;
        main.gravityModifier = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 1500;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 2, 3) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.02f;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.material = dropMaterial != null ? dropMaterial : MakeMaterial();
        return ps;
    }

    private static Material generatedMaterial;

    static Material MakeMaterial()
    {
        if (generatedMaterial != null) return generatedMaterial;

        // Soft round texture so drops don't look like hard squares
        Material m = ParticleMaterials.Create("Rain", ParticleMaterials.SoftDot());

        return generatedMaterial = m;
    }

    void OnDisable()
    {
        PlayerSpeedMultiplier = 1f;
        FoliageWindMultiplier = 1f;
        if (DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.weatherSunDarken = 0f;
            DayNightCycle.Instance.weatherAmbientDarken = 0f;
            DayNightCycle.Instance.weatherFogBlend = 0f;
        }
    }
}
