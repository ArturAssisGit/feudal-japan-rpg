using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Makes the sun look strong: bloom glow, lens flare with streaks, a big warm halo and slowly turning sun rays.
// DayNightCycle adds it to the Directional Light by itself. Everything is strongest when you look towards the sun
// and at sunrise / sunset, and fades out behind clouds, storms and at night.
// It needs Post Processing on the camera (it already is) and uses its own Volume, so your other settings are not touched.
[RequireComponent(typeof(Light))]
public class SunEffects : MonoBehaviour
{
    [Header("Master")]
    [Tooltip("Overall strength of everything below (0 = off, 1 = normal, 2 = very strong)")] [Range(0f, 2f)] public float strength = 1f;
    [Tooltip("How fast the effects follow when you turn the camera (higher = faster)")] public float smoothing = 4f;

    [Header("Bloom (glow of bright things)")]
    [Tooltip("Glow all the time")] public float bloomBase = 0.8f;
    [Tooltip("Extra glow while looking at the sun")] public float bloomWhenFacingSun = 1.6f;
    [Tooltip("Extra glow at sunrise / sunset (low sun)")] public float bloomGoldenHour = 1.2f;
    [Tooltip("Only pixels brighter than this glow (1 = only the sun and bright things)")] public float bloomThreshold = 0.95f;
    [Tooltip("How far the glow spreads (0 = tight, 1 = very wide)")] [Range(0.3f, 0.95f)] public float bloomSpread = 0.8f;
    [Tooltip("How much the glow takes the color of the sun (0 = white glow, 1 = sun colored)")] [Range(0f, 1f)] public float bloomSunTint = 1f;

    [Header("Lens flare (when looking at the sun)")]
    [Tooltip("Strength of the flare and streaks")] public float flareIntensity = 3f;
    [Tooltip("Horizontal light streaks across the screen (0 = none)")] public float streaks = 1.2f;
    [Tooltip("Rainbow color edge of the flare")] public float chromaticAberration = 0.8f;

    [Header("Halo and rays")]
    public bool showHalo = true;
    [Tooltip("Size of the warm glow around the sun (degrees of sky)")] public float haloSize = 70f;
    [Tooltip("Brightness of the halo")] public float haloBrightness = 2.2f;
    public bool showRays = true;
    [Tooltip("Size of the sun rays (degrees of sky)")] public float raysSize = 60f;
    [Tooltip("Brightness of the rays")] public float raysBrightness = 1.6f;
    [Tooltip("How fast the rays slowly turn (degrees per second)")] public float raysTurnSpeed = 1.5f;

    [Header("Grading (warm sunny look)")]
    [Tooltip("Tints the whole picture with the sun color when the sun is low (0 = off)")] [Range(0f, 1f)] public float warmTint = 0.6f;
    [Range(-20f, 40f)] public float contrast = 10f;
    [Range(-20f, 40f)] public float saturation = 12f;
    [Tooltip("Gives the picture a filmic look so very bright things do not turn flat white")] public bool tonemapping = true;

    [Header("Your color filter (always on, added to the warm look above)")]
    [Tooltip("Your own color filter over the whole picture. White = no filter. Pick a warm tone for a golden look, a cool one for a colder look")] public Color colorFilter = Color.white;
    [Tooltip("Brightness of the whole picture (0 = unchanged)")] [Range(-2f, 2f)] public float postExposure = 0f;
    [Tooltip("Extra contrast, always (added to the contrast above)")] [Range(-50f, 50f)] public float baseContrast = 0f;
    [Tooltip("Extra saturation, always (added to the saturation above). Negative = more muted colors")] [Range(-100f, 100f)] public float baseSaturation = 0f;
    [Tooltip("Shifts all the colors around the color wheel (0 = unchanged)")] [Range(-180f, 180f)] public float hueShift = 0f;

    [Header("Night only (added on top of the settings above, only at night)")]
    [Tooltip("Brightness of the whole picture at night (negative = darker). -1 is quite dark, -2 almost black")] [Range(-4f, 2f)] public float nightPostExposure = -0.5f;
    [Tooltip("Extra contrast at night: dark stays dark, lights (lantern, lamps, fire) stand out more")] [Range(-50f, 80f)] public float nightContrast = 15f;
    [Tooltip("Extra saturation at night (negative = fewer colors)")] [Range(-100f, 100f)] public float nightSaturation = 0f;
    [Tooltip("Color filter at night (white = none). A cold blue gives a moonlit look")] public Color nightColorFilter = Color.white;
    [Tooltip("Seconds the night look takes to fade in and out when the night starts or ends")] public float nightFadeSeconds = 40f;

    Light sun;
    float nightSmooth;
    Volume volume;
    VolumeProfile profile;
    Bloom bloom;
    ScreenSpaceLensFlare flare;
    ColorAdjustments color;
    Tonemapping tonemap;

    Transform halo, rays, rays2;
    Material haloMaterial, raysMaterial, rays2Material;
    Texture2D haloTexture, raysTexture, rays2Texture;

    float facingSmooth, lowSmooth, visibleSmooth;
    float roll;

    void Start()
    {
        sun = GetComponent<Light>();
        BuildVolume();
        haloTexture = MakeHaloTexture();
        raysTexture = MakeRaysTexture(7, 3.5f);
        rays2Texture = MakeRaysTexture(23, 2.2f);
        halo = MakeQuad("Sun Halo", 2996, haloTexture, out haloMaterial);
        rays = MakeQuad("Sun Rays", 2997, raysTexture, out raysMaterial);
        rays2 = MakeQuad("Sun Rays 2", 2997, rays2Texture, out rays2Material);
    }

    void BuildVolume()
    {
        var go = new GameObject("Sun Effects Volume");
        go.transform.SetParent(transform, false);
        volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        volume.weight = 1f;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        bloom = profile.Add<Bloom>(true);
        flare = profile.Add<ScreenSpaceLensFlare>(true);
        // Only the settings this script drives are taken over: the rest of Color Adjustments (and other effects) still come from your own Global Volume
        color = profile.Add<ColorAdjustments>(false);
        color.colorFilter.overrideState = true;
        color.contrast.overrideState = true;
        color.saturation.overrideState = true;
        color.postExposure.overrideState = true;
        color.hueShift.overrideState = true;
        tonemap = profile.Add<Tonemapping>(true);
        volume.sharedProfile = profile;

        bloom.highQualityFiltering.Override(true);
        bloom.intensity.Override(0f);
        flare.intensity.Override(0f);
        flare.streaksIntensity.Override(0f);
        flare.tintColor.Override(Color.white);
        tonemap.mode.Override(tonemapping ? TonemappingMode.Neutral : TonemappingMode.None);
    }

    Transform MakeQuad(string objectName, int queue, Texture2D texture, out Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = objectName;
        Destroy(go.GetComponent<Collider>());

        material = new Material(Shader.Find("Sprites/Default"));
        material.renderQueue = queue; // after the sky, before the sun disc
        material.mainTexture = texture;

        MeshRenderer r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        go.SetActive(false);
        return go.transform;
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null || sun == null || volume == null) return;

        // Where the sun is and how visible it is (fades near the horizon, at night, behind clouds)
        Vector3 toSun = -transform.forward;
        // No sun glare, rays or golden bloom from the moon (also while it rises)
        bool night = DayNightCycle.Instance != null ? DayNightCycle.Instance.IsNight || DayNightCycle.Instance.MoonIsUp : toSun.y < 0f;
        float visible = night ? 0f : Mathf.Clamp01((toSun.y + 0.02f) / 0.08f) * (sun.intensity > 0.001f ? 1f : 0f);
        visible *= 1f - SkyboxCycle.Cloudiness * 0.95f;

        float facing = Mathf.Pow(Mathf.Clamp01(Vector3.Dot(cam.transform.forward, toSun)), 3f); // 1 = looking straight at the sun
        float low = 1f - Mathf.Clamp01(toSun.y / 0.5f);                                          // 1 = at the horizon, 0 = high in the sky

        float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        facingSmooth = Mathf.Lerp(facingSmooth, facing, k);
        lowSmooth = Mathf.Lerp(lowSmooth, low, k);
        visibleSmooth = Mathf.Lerp(visibleSmooth, visible, k);

        float v = visibleSmooth * strength;
        UpdateVolume(v);
        UpdateSkyObjects(cam, toSun, v);
    }

    void UpdateVolume(float v)
    {
        Color sunColor = sun.color;

        bloom.intensity.value = bloomBase * Mathf.Min(strength, 1f) + v * (bloomWhenFacingSun * facingSmooth + bloomGoldenHour * lowSmooth);
        bloom.threshold.value = bloomThreshold;
        bloom.scatter.value = bloomSpread;
        bloom.tint.value = Color.Lerp(Color.white, sunColor, bloomSunTint * Mathf.Clamp01(v));

        float flareAmount = v * facingSmooth;
        flare.intensity.value = flareIntensity * flareAmount;
        flare.streaksIntensity.value = streaks * flareAmount;
        flare.chromaticAbberationIntensity.value = chromaticAberration;
        flare.tintColor.value = Color.Lerp(Color.white, sunColor, 0.5f);

        float warm = warmTint * v * (0.35f + 0.65f * lowSmooth);
        float nightTarget = DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight ? 1f : 0f;
        nightSmooth = Mathf.MoveTowards(nightSmooth, nightTarget, Time.deltaTime / Mathf.Max(0.1f, nightFadeSeconds));

        color.colorFilter.value = Color.Lerp(Color.white, sunColor, warm) * colorFilter * Color.Lerp(Color.white, nightColorFilter, nightSmooth);
        color.contrast.value = baseContrast + contrast * Mathf.Clamp01(v) + nightContrast * nightSmooth;
        color.saturation.value = baseSaturation + saturation * Mathf.Clamp01(v) + nightSaturation * nightSmooth;
        color.postExposure.value = postExposure + nightPostExposure * nightSmooth;
        color.hueShift.value = hueShift;
        tonemap.mode.value = tonemapping ? TonemappingMode.Neutral : TonemappingMode.None;
    }

    void UpdateSkyObjects(Camera cam, Vector3 toSun, float v)
    {
        bool anything = v > 0.001f && (showHalo || showRays);
        halo.gameObject.SetActive(anything && showHalo);
        rays.gameObject.SetActive(anything && showRays);
        rays2.gameObject.SetActive(anything && showRays);
        if (!anything) return;

        Color c = sun.color;
        float boost = 0.4f + 0.6f * facingSmooth + 0.5f * lowSmooth;
        roll += raysTurnSpeed * Time.deltaTime;

        Place(halo, cam, toSun, haloSize, 0.87f, 0f);
        SetColor(haloMaterial, c, haloBrightness, Mathf.Clamp01(v * boost * 0.6f));

        Place(rays, cam, toSun, raysSize, 0.86f, roll);
        SetColor(raysMaterial, c, raysBrightness, Mathf.Clamp01(v * boost * 0.5f));
        Place(rays2, cam, toSun, raysSize * 0.8f, 0.86f, -roll * 1.7f);
        SetColor(rays2Material, c, raysBrightness, Mathf.Clamp01(v * boost * 0.35f));
    }

    static void SetColor(Material m, Color sunColor, float brightness, float alpha)
    {
        Color c = sunColor * brightness;
        c.a = alpha;
        m.color = c;
    }

    // Far away in the direction of the sun, facing the camera (like the sun disc)
    static void Place(Transform t, Camera cam, Vector3 toSun, float degrees, float distanceFactor, float roll)
    {
        float distance = cam.farClipPlane * distanceFactor;
        t.position = cam.transform.position + toSun * distance;
        t.rotation = Quaternion.LookRotation(t.position - cam.transform.position) * Quaternion.Euler(0f, 0f, roll);
        float size = 2f * distance * Mathf.Tan(degrees * 0.5f * Mathf.Deg2Rad);
        t.localScale = new Vector3(size, size, 1f);
    }

    // ---------- Textures made in code (no files needed) ----------

    // Soft glow: bright in the middle, fading smoothly to nothing at the edge
    static Texture2D MakeHaloTexture()
    {
        const int size = 128;
        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Clamp01(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half);
                float a = Mathf.Pow(1f - d, 2.6f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        return ToTexture(pixels, size);
    }

    // Rays going out of the center: smooth noise around the angle, fading with the distance
    static Texture2D MakeRaysTexture(int rayCount, float sharpness)
    {
        const int size = 256;
        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        float seed = rayCount * 3.7f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = 0f;
                if (d < 1f)
                {
                    float angle = Mathf.Atan2(dy, dx);
                    // periodic in the angle, so there is no seam
                    float n = Mathf.PerlinNoise(seed + Mathf.Cos(angle) * rayCount * 0.35f, seed + Mathf.Sin(angle) * rayCount * 0.35f);
                    float ray = Mathf.Pow(Mathf.Clamp01((n - 0.25f) * 1.6f), sharpness);
                    float falloff = Mathf.Pow(1f - d, 1.4f) * Mathf.Clamp01(d * 6f);
                    a = ray * falloff;
                }
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        return ToTexture(pixels, size);
    }

    static Texture2D ToTexture(Color32[] pixels, int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    void OnDisable()
    {
        if (halo != null) halo.gameObject.SetActive(false);
        if (rays != null) rays.gameObject.SetActive(false);
        if (rays2 != null) rays2.gameObject.SetActive(false);
        if (volume != null) volume.enabled = false;
    }

    void OnEnable()
    {
        if (volume != null) volume.enabled = true;
    }

    void OnDestroy()
    {
        if (halo != null) Destroy(halo.gameObject);
        if (rays != null) Destroy(rays.gameObject);
        if (rays2 != null) Destroy(rays2.gameObject);
        if (profile != null) Destroy(profile);
        if (haloTexture != null) Destroy(haloTexture);
        if (raysTexture != null) Destroy(raysTexture);
        if (rays2Texture != null) Destroy(rays2Texture);
        if (haloMaterial != null) Destroy(haloMaterial);
        if (raysMaterial != null) Destroy(raysMaterial);
        if (rays2Material != null) Destroy(rays2Material);
    }
}
