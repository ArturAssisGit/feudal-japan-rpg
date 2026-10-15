using UnityEngine;

// Fades between 9 Skybox/Panoramic skies: Day/Sunset/Night x Clear/Cloudy/Storm (dry: random clear/cloudy; rain: storm).
public class SkyboxCycle : MonoBehaviour
{
    public static SkyboxCycle Instance { get; private set; }
    // 0 = clear sky, 0.6 = cloudy, 1 = storm (fades). SunDisc uses it to hide the sun behind clouds
    public static float Cloudiness { get; private set; }
    // SleepSystem raises this so the sky fades faster while time flies (1 = normal)
    public static float BlendSpeedMultiplier = 1f;

    [Header("Day")]
    public Material dayClear;
    public Material dayCloudy;
    public Material dayStorm;

    [Header("Sunset (also used at sunrise)")]
    public Material sunsetClear;
    public Material sunsetCloudy;
    public Material sunsetStorm;

    [Header("Night")]
    public Material nightClear;
    public Material nightCloudy;
    public Material nightStorm;

    [Header("Hours (0-24, from DayNightCycle)")]
    [Tooltip("Night ends, sunrise sky starts")] [Range(0f, 24f)] public float sunriseStart = 5f;
    [Tooltip("Sunrise ends, day sky starts")] [Range(0f, 24f)] public float dayStart = 7f;
    [Tooltip("Day ends, sunset sky starts")] [Range(0f, 24f)] public float sunsetStart = 17.5f;
    [Tooltip("Sunset ends, night sky starts")] [Range(0f, 24f)] public float nightStart = 19.5f;

    [Header("Weather")]
    [Tooltip("When it's dry, chance of the sky being cloudy instead of clear")] [Range(0f, 1f)] public float cloudyChance = 0.4f;
    [Tooltip("How often the dry sky may change between clear and cloudy (real minutes, random between min and max)")]
    public Vector2 weatherChangeMinutes = new Vector2(3f, 8f);
    [Tooltip("Rain presets that only make the sky cloudy (not storm), by name")] public string[] cloudyRainPresets = { "Drizzle" };

    [Header("Transition")]
    [Tooltip("Seconds to fade from one skybox to the next")] public float blendSeconds = 20f;
    [Tooltip("Brightness of the whole sky")] public float brightness = 1f;
    [Tooltip("Softens the vertical line where the left and right edges of the sky images meet (0 = off, 0.06 = soft, 0.15 = very soft)")]
    [Range(0f, 0.25f)] public float seamSmoothing = 0.06f;
    [Tooltip("Filled in automatically - leave it")] public Shader blendShader;

    [Header("Debug")]
    [SerializeField] private string currentSky;

    private enum Period { Day, Sunset, Night }
    private enum Weather { Clear, Cloudy, Storm }

    private Material skyMaterial;
    private Material shownA, shownB;   // fading from A to B
    private float cloudA, cloudB;
    private float blend = 1f;
    private bool dryIsCloudy;
    private float nextWeatherRoll;
    private bool canBlend = true;

    private static readonly int TexA = Shader.PropertyToID("_TexA"), TexB = Shader.PropertyToID("_TexB");
    private static readonly int ExpA = Shader.PropertyToID("_ExposureA"), ExpB = Shader.PropertyToID("_ExposureB");
    private static readonly int TintA = Shader.PropertyToID("_TintA"), TintB = Shader.PropertyToID("_TintB");
    private static readonly int RotA = Shader.PropertyToID("_RotationA"), RotB = Shader.PropertyToID("_RotationB");
    private static readonly int BlendId = Shader.PropertyToID("_Blend"), BrightId = Shader.PropertyToID("_Brightness");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Cloudiness = 0f; BlendSpeedMultiplier = 1f; }

    void OnValidate()
    {
        // Saved in the scene so the shader is included in builds
        if (blendShader == null) blendShader = Shader.Find("Skybox/Panoramic Blend");
    }

    void Awake()
    {
        Instance = this;
        if (blendShader == null) blendShader = Shader.Find("Skybox/Panoramic Blend");
        if (blendShader == null)
        {
            Debug.LogWarning("SkyboxCycle: shader 'Skybox/Panoramic Blend' not found - skies will switch without fading.");
            canBlend = false;
        }
        else skyMaterial = new Material(blendShader) { name = "Sky (SkyboxCycle)" };

        RollDryWeather();
    }

    void RollDryWeather()
    {
        dryIsCloudy = Random.value < cloudyChance;
        nextWeatherRoll = Time.time + Random.Range(weatherChangeMinutes.x, weatherChangeMinutes.y) * 60f;
    }

    void Start()
    {
        // Start directly on the right sky (DayNightCycle has its hour by now)
        shownA = shownB = Desired(out float cloud);
        cloudA = cloudB = cloud;
        blend = 1f;
        Apply();
    }

    void Update()
    {
        // Dry weather: sometimes clear, sometimes cloudy
        if (Time.time >= nextWeatherRoll) RollDryWeather();

        Material desired = Desired(out float desiredCloud);

        if (blend >= 1f)
        {
            if (desired != null && desired != shownB)
            {
                // Start fading to the new sky
                shownA = shownB;
                cloudA = cloudB;
                shownB = desired;
                cloudB = desiredCloud;
                blend = canBlend && CanBlend(shownA) && CanBlend(shownB) ? 0f : 1f;
            }
        }
        else blend = Mathf.MoveTowards(blend, 1f, Time.deltaTime * BlendSpeedMultiplier / Mathf.Max(0.1f, blendSeconds));

        Apply();
    }

    Material Desired(out float cloud)
    {
        float hour = DayNightCycle.Instance != null ? DayNightCycle.Instance.currentHour : 12f;
        Period period =
            hour >= dayStart && hour < sunsetStart ? Period.Day :
            (hour >= sunriseStart && hour < dayStart) || (hour >= sunsetStart && hour < nightStart) ? Period.Sunset :
            Period.Night;

        Weather weather = dryIsCloudy ? Weather.Cloudy : Weather.Clear;
        if (RainSystem.IsRaining)
        {
            bool lightRain = false;
            if (cloudyRainPresets != null)
                foreach (string n in cloudyRainPresets)
                    if (!string.IsNullOrEmpty(n) && n == RainSystem.CurrentPresetName) lightRain = true;
            weather = lightRain ? Weather.Cloudy : Weather.Storm;
        }

        cloud = weather switch { Weather.Storm => 1f, Weather.Cloudy => 0.6f, _ => 0f };
        Material m = Get(period, weather);
        currentSky = $"{period} / {weather}";
        return m;
    }

    // Missing materials fall back: storm -> cloudy -> clear, then to the day skies
    Material Get(Period p, Weather w)
    {
        var (clear, cloudy, storm) = p switch
        {
            Period.Day => (dayClear, dayCloudy, dayStorm),
            Period.Sunset => (sunsetClear, sunsetCloudy, sunsetStorm),
            _ => (nightClear, nightCloudy, nightStorm)
        };

        Material m = w == Weather.Storm ? (storm != null ? storm : cloudy != null ? cloudy : clear)
                   : w == Weather.Cloudy ? (cloudy != null ? cloudy : clear)
                   : clear;
        if (m == null && p != Period.Day) m = Get(Period.Day, w);
        return m;
    }

    static bool CanBlend(Material m) => m != null && m.HasProperty("_MainTex") && m.GetTexture("_MainTex") is Texture2D;

    void Apply()
    {
        Cloudiness = Mathf.Lerp(cloudA, cloudB, blend);
        if (shownB == null) return;

        // A sky that isn't a 360 image (e.g. Cubemap or Procedural): just use it as it is
        if (!canBlend || !CanBlend(shownB) || (blend < 1f && !CanBlend(shownA)))
        {
            if (RenderSettings.skybox != shownB) RenderSettings.skybox = shownB;
            return;
        }

        SetSlot(shownA != null && CanBlend(shownA) ? shownA : shownB, TexA, ExpA, TintA, RotA);
        SetSlot(shownB, TexB, ExpB, TintB, RotB);
        skyMaterial.SetFloat(BlendId, blend);
        skyMaterial.SetFloat(BrightId, brightness);
        skyMaterial.SetFloat("_SeamBlend", seamSmoothing);
        if (RenderSettings.skybox != skyMaterial) RenderSettings.skybox = skyMaterial;
    }

    void SetSlot(Material source, int tex, int exposure, int tint, int rotation)
    {
        skyMaterial.SetTexture(tex, source.GetTexture("_MainTex"));
        skyMaterial.SetFloat(exposure, source.HasProperty("_Exposure") ? source.GetFloat("_Exposure") : 1f);
        skyMaterial.SetColor(tint, source.HasProperty("_Tint") ? source.GetColor("_Tint") : new Color(0.5f, 0.5f, 0.5f, 0.5f));
        skyMaterial.SetFloat(rotation, source.HasProperty("_Rotation") ? source.GetFloat("_Rotation") : 0f);
    }

    void OnDisable() => Cloudiness = 0f;
}
