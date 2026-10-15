using UnityEngine;

// On the Directional Light: moving sun by day, soft blue moonlight at night; sky, ambient and fog follow the hour.
[RequireComponent(typeof(Light))]
public class DayNightCycle : MonoBehaviour
{
    public static DayNightCycle Instance { get; private set; }

    [Header("Time")]
    [Tooltip("How many real minutes a full day (24h) lasts")] public float dayLengthMinutes = 24f;
    [Tooltip("Hour when the game starts (0-24)")] [Range(0f, 24f)] public float startHour = 8f;
    [Tooltip("Current hour (you can drag it during Play to test)")] [Range(0f, 24f)] public float currentHour;
    [Tooltip("Stop the clock")] public bool pauseTime;
    [Tooltip("Hold this key to make time pass 60x faster (for testing, None = off)")] public KeyCode fastForwardKey = KeyCode.None;

    [Header("Sun")]
    [Tooltip("Compass direction the sun rises from")] [Range(0f, 360f)] public float sunDirection = 170f;
    [Tooltip("Sun brightness during the day")] public float sunIntensity = 1.3f;
    public Gradient sunColor = DefaultSunColor();

    [Header("Moon (night light)")]
    public float moonIntensity = 0.18f;
    public Color moonColor = new Color(0.55f, 0.65f, 1f);

    [Header("Sky & ambient")]
    [Tooltip("Brightness of the ambient light over the day (0 = midnight, 0.5 = noon, 1 = midnight)")]
    public AnimationCurve ambientIntensity = new AnimationCurve(
        new Keyframe(0f, 0.25f), new Keyframe(0.23f, 0.3f), new Keyframe(0.3f, 0.85f), new Keyframe(0.5f, 1f),
        new Keyframe(0.7f, 0.85f), new Keyframe(0.78f, 0.3f), new Keyframe(1f, 0.25f));
    [Tooltip("Brightness of the skybox over the day, relative to the material's own Exposure (1 = as the material is)")]
    public AnimationCurve skyExposure = new AnimationCurve(
        new Keyframe(0f, 0.15f), new Keyframe(0.23f, 0.2f), new Keyframe(0.3f, 1f), new Keyframe(0.5f, 1.3f),
        new Keyframe(0.7f, 1f), new Keyframe(0.78f, 0.2f), new Keyframe(1f, 0.15f));
    [Tooltip("Fog color over the day (only used if fog is on in Lighting settings)")]
    public Gradient fogColor = DefaultFogColor();
    [Tooltip("How often the ambient light is recalculated from the sky (seconds)")] public float ambientUpdateInterval = 2f;

    [Header("Night visibility")]
    [Tooltip("Ambient light at night is multiplied by this (dark night skies give almost no light)")] public float nightAmbientBoost = 3f;
    [Tooltip("How much rain/storm darkens things at night compared to the day (0.4 = only 40% as much)")] [Range(0f, 1f)] public float weatherDarkenAtNight = 0.4f;
    [Tooltip("The moonlight never goes below this, even in a storm")] public float minNightLight = 0.08f;

    // Set by RainSystem (0 = clear weather)
    [System.NonSerialized] public float weatherSunDarken;
    [System.NonSerialized] public float weatherAmbientDarken;
    [System.NonSerialized] public float weatherFogBlend;
    [System.NonSerialized] public Color weatherFogColor = Color.grey;

    public bool IsNight => currentHour < 5.5f || currentHour > 19.5f;
    // True while the Directional Light is the moon (also at moonrise / just before dawn, a bit wider than IsNight)
    public bool MoonIsUp { get; private set; }

    private Light sun;
    private Material skyboxCopy;
    private bool skyHasExposure;
    private float baseSkyExposure = 1f;
    private float nextAmbientUpdate;

    void Awake()
    {
        Instance = this;
        sun = GetComponent<Light>();
        currentHour = startHour;
    }

    // 1 at night, 0 during the day, smooth at dusk and dawn (1 when there is no day/night cycle)
    public static float NightAmount()
    {
        if (Instance == null) return 1f;
        float h = Instance.currentHour;
        return Mathf.Clamp01(Mathf.Max(Mathf.InverseLerp(18.5f, 20f, h), Mathf.InverseLerp(6.5f, 5f, h)));
    }

    void Start()
    {
        RenderSettings.sun = sun;
        if (GetComponent<SunDisc>() == null) gameObject.AddComponent<SunDisc>(); // visible sun/moon (photo skyboxes don't draw one)
        if (GetComponent<SunEffects>() == null) gameObject.AddComponent<SunEffects>(); // glow, flare and rays around the sun

        // Work on a skybox copy (never the real file); skip if SkyboxCycle controls the sky
        if (RenderSettings.skybox != null && FindAnyObjectByType<SkyboxCycle>() == null)
        {
            skyboxCopy = new Material(RenderSettings.skybox);
            RenderSettings.skybox = skyboxCopy;
            skyHasExposure = skyboxCopy.HasProperty("_Exposure");
            if (skyHasExposure) baseSkyExposure = skyboxCopy.GetFloat("_Exposure"); // e.g. HDR skies use tiny values like 0.0035
        }
        Apply();
    }

    void Update()
    {
        if (!pauseTime && dayLengthMinutes > 0f)
        {
            float speed = fastForwardKey != KeyCode.None && Input.GetKey(fastForwardKey) ? 60f : 1f;
            currentHour += 24f / (dayLengthMinutes * 60f) * Time.deltaTime * speed;
            if (currentHour >= 24f) currentHour -= 24f;
        }
        Apply();
    }

    void Apply()
    {
        float t = currentHour / 24f; // 0 = midnight, 0.25 = 6h, 0.5 = noon, 0.75 = 18h

        // Sun angle: below the horizon at night, highest at noon
        float sunAngle = t * 360f - 90f;
        float sunHeight = Mathf.Sin(sunAngle * Mathf.Deg2Rad); // -1..1

        // Day: sun fading in/out at the horizon. Night: the same light becomes the moon, on the opposite side
        bool day = sunHeight > -0.05f;
        MoonIsUp = !day;
        transform.rotation = Quaternion.Euler(day ? sunAngle : sunAngle + 180f, sunDirection, 0f);
        float fade = day ? Mathf.Clamp01((sunHeight + 0.05f) / 0.25f) : Mathf.Clamp01((-sunHeight - 0.05f) / 0.25f);
        sun.color = day ? sunColor.Evaluate(t) : moonColor;
        sun.intensity = (day ? sunIntensity : moonIntensity) * fade;
        float lightFade = day ? 0f : fade;

        // 0 = day, 1 = night
        float nightness = Mathf.Clamp01((-sunHeight + 0.05f) / 0.25f);
        float weatherK = Mathf.Lerp(1f, weatherDarkenAtNight, nightness);

        // Bad weather darkens everything a bit (less at night, it's already dark)
        sun.intensity *= 1f - weatherSunDarken * weatherK;
        if (lightFade > 0f) sun.intensity = Mathf.Max(sun.intensity, minNightLight * lightFade);
        float ambient = ambientIntensity.Evaluate(t) * (1f - weatherAmbientDarken * weatherK);
        ambient *= Mathf.Lerp(1f, nightAmbientBoost, nightness);
        RenderSettings.ambientIntensity = ambient;
        if (RenderSettings.fog) RenderSettings.fogColor = Color.Lerp(fogColor.Evaluate(t), weatherFogColor * Mathf.Clamp01(ambient + 0.1f), weatherFogBlend);
        if (skyHasExposure) skyboxCopy.SetFloat("_Exposure", baseSkyExposure * skyExposure.Evaluate(t) * (1f - weatherAmbientDarken * weatherK));

        if (Time.time >= nextAmbientUpdate)
        {
            nextAmbientUpdate = Time.time + ambientUpdateInterval;
            DynamicGI.UpdateEnvironment();
        }
    }

    static Gradient DefaultSunColor() => OpaqueGradient(
        new GradientColorKey(new Color(1f, 0.45f, 0.25f), 0.23f), // sunrise - orange
        new GradientColorKey(new Color(1f, 0.85f, 0.7f), 0.32f),  // morning - warm
        new GradientColorKey(new Color(1f, 0.97f, 0.9f), 0.5f),   // noon - almost white
        new GradientColorKey(new Color(1f, 0.8f, 0.6f), 0.68f),   // afternoon - golden
        new GradientColorKey(new Color(1f, 0.4f, 0.2f), 0.77f));  // sunset - deep orange

    static Gradient DefaultFogColor() => OpaqueGradient(
        new GradientColorKey(new Color(0.05f, 0.07f, 0.12f), 0.15f), // night - dark blue
        new GradientColorKey(new Color(0.85f, 0.55f, 0.4f), 0.25f),  // sunrise - pink/orange
        new GradientColorKey(new Color(0.7f, 0.78f, 0.85f), 0.5f),   // day - light blue-grey
        new GradientColorKey(new Color(0.9f, 0.5f, 0.3f), 0.76f),    // sunset - orange
        new GradientColorKey(new Color(0.05f, 0.07f, 0.12f), 0.85f)); // night

    static Gradient OpaqueGradient(params GradientColorKey[] colors)
    {
        var g = new Gradient();
        g.SetKeys(colors, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }
}
