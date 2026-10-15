using UnityEngine;

// Draws the sun (moon at night) toward the Directional Light, since photo skyboxes don't; DayNightCycle adds it automatically.
[RequireComponent(typeof(Light))]
public class SunDisc : MonoBehaviour
{
    [Header("Sun")]
    public bool showSun = true;
    [Tooltip("Size of the sun in the sky (degrees). The real sun is about 0.5, games use 2-5")] public float sunSize = 3.5f;
    [Tooltip("Brightness (above 1 makes it glow with Bloom)")] public float sunBrightness = 4f;
    [Tooltip("Extra tint on top of the light color")] public Color sunTint = Color.white;
    [Tooltip("Size of the soft glow around the sun (1 = none, 3 = big halo)")] [Range(1f, 6f)] public float sunGlow = 3f;

    [Header("Moon (at night)")]
    public bool showMoon = true;
    public float moonSize = 2.5f;
    public float moonBrightness = 1.5f;
    public Color moonColor = new Color(0.85f, 0.9f, 1f);
    [Range(1f, 6f)] public float moonGlow = 1.8f;

    [Header("Optional textures (empty = soft round disc)")]
    public Texture2D sunTexture;
    public Texture2D moonTexture;

    private Light sun;
    private Transform disc;
    private Material material;
    private Texture2D defaultSunTex, defaultMoonTex;

    void Start()
    {
        sun = GetComponent<Light>();

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Sun Disc";
        Destroy(go.GetComponent<Collider>());
        disc = go.transform;

        // Sprites/Default: transparent, unlit and not hidden by fog; queue after the skybox, before other transparents
        material = new Material(Shader.Find("Sprites/Default")) { renderQueue = 2999 };

        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        defaultSunTex = MakeDiscTexture(0.18f);
        defaultMoonTex = MakeDiscTexture(0.3f);
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null || disc == null) return;

        // The light points FROM the sun, so the sun is in the opposite direction
        Vector3 toSun = -transform.forward;
        // The light is the moon (also while it rises, before IsNight starts): draw the moon, never a bright sun
        bool isNight = DayNightCycle.Instance != null ? DayNightCycle.Instance.MoonIsUp || DayNightCycle.Instance.IsNight : toSun.y < 0f;

        bool visible = isNight ? showMoon : showSun;
        float height = toSun.y; // fades out near/below the horizon
        float fade = Mathf.Clamp01((height + 0.02f) / 0.08f) * (sun.intensity > 0.001f ? 1f : 0f);
        fade *= 1f - SkyboxCycle.Cloudiness * 0.95f; // hidden behind clouds / storm
        if (!visible || fade <= 0f) { disc.gameObject.SetActive(false); return; }
        disc.gameObject.SetActive(true);

        float size = isNight ? moonSize : sunSize;
        float glow = isNight ? moonGlow : sunGlow;
        Color color = isNight ? moonColor * moonBrightness : sun.color * sunTint * sunBrightness;
        color.a = fade;

        // Far away (inside the camera's view distance), always facing the camera
        float distance = cam.farClipPlane * 0.85f;
        disc.position = cam.transform.position + toSun * distance;
        disc.rotation = Quaternion.LookRotation(disc.position - cam.transform.position);
        float worldSize = 2f * distance * Mathf.Tan(size * glow * 0.5f * Mathf.Deg2Rad);
        disc.localScale = new Vector3(worldSize, worldSize, 1f);

        material.mainTexture = isNight ? (moonTexture != null ? moonTexture : defaultMoonTex) : (sunTexture != null ? sunTexture : defaultSunTex);
        material.color = color;
    }

    // Bright core + soft glow that fades out
    static Texture2D MakeDiscTexture(float coreRadius)
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half; // 0 center, 1 edge
                float core = Mathf.Clamp01((coreRadius - d) / 0.02f + 1f);
                float glow = Mathf.Pow(Mathf.Clamp01(1f - d), 3f) * 0.45f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(core + glow)));
            }
        tex.Apply();
        return tex;
    }

    void OnDisable() { if (disc != null) disc.gameObject.SetActive(false); }

    void OnDestroy() { if (disc != null) Destroy(disc.gameObject); }
}
