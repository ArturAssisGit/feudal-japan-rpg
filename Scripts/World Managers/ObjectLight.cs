using UnityEngine;

// Put this on ANY object (a stone lantern, a torch, a window...) and it lights up: a warm flickering point light.
// - You see the light in the Scene view right away (no need to press Play), so you can place it with "Offset".
// - By default it only shines at night, fading on and off with the DayNightCycle hours.
// - It has no shadows by default and switches itself off when the camera is far away, so many of them stay cheap.
// The light is created by the script and is not saved in the scene: the only thing to set up is this component.
[ExecuteAlways]
[DisallowMultipleComponent]
public class ObjectLight : MonoBehaviour
{
    [Header("Light")]
    public Color color = new Color(1f, 0.62f, 0.28f);
    [Tooltip("Brightness (2-4 is a lantern, 6+ is a bonfire)")] public float intensity = 3f;
    [Tooltip("How far the light reaches (meters)")] public float range = 10f;
    [Tooltip("Where the light is, in meters, in the object's own directions (X right, Y up, Z forward). It ignores the object's scale, so 1 really is 1 meter. Select the object to see a yellow dot where the light is")]
    public Vector3 offset = new Vector3(0f, 1.2f, 0f);
    [Tooltip("Soft shadows from this light (looks nicer but is heavy: leave off for many lights)")] public bool shadows = false;

    [Header("Flicker")]
    [Tooltip("How much the flame flickers (0 = steady)")] [Range(0f, 1f)] public float flicker = 0.15f;
    public float flickerSpeed = 3f;

    [Header("When it shines")]
    [Tooltip("On = only at night (uses the DayNightCycle hours). Off = always on")] public bool onlyAtNight = true;
    [Tooltip("Hour it turns on (0-24)")] [Range(0f, 24f)] public float turnOnHour = 18f;
    [Tooltip("Hour it turns off (0-24)")] [Range(0f, 24f)] public float turnOffHour = 6f;
    [Tooltip("Hours it takes to fade in and out")] public float fadeHours = 0.5f;

    [Header("Performance")]
    [Tooltip("The light switches off when the camera is farther than this (meters, 0 = never)")] public float maxDistance = 45f;

    const string ChildName = "Object Light (auto)";

    Light lamp;
    float seed;
    float nextCheck;
    bool near = true;

    void OnEnable()
    {
        seed = Random.value * 100f;
        RemoveLight();
        CreateLight();
    }

    void OnDisable() => RemoveLight();

    // Changing a value in the Inspector updates the light on the spot, even outside Play
    void OnValidate()
    {
        range = Mathf.Max(0.1f, range);
        if (lamp != null) Setup();
    }

    void CreateLight()
    {
        var go = new GameObject(ChildName);
        go.hideFlags = HideFlags.HideAndDontSave; // not saved in the scene or the prefab
        go.transform.SetParent(transform, false);
        lamp = go.AddComponent<Light>();
        lamp.type = LightType.Point;
        Setup();
    }

    void RemoveLight()
    {
        lamp = null;
        Transform old = transform.Find(ChildName);
        if (old == null) return;
        if (Application.isPlaying) Destroy(old.gameObject);
        else DestroyImmediate(old.gameObject);
    }

    void Setup()
    {
        lamp.color = color;
        lamp.range = range;
        lamp.intensity = intensity;
        lamp.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        PlaceLight();
    }

    // The offset is in meters, whatever the scale of the object (lanterns here are scaled x100)
    void PlaceLight() => lamp.transform.position = transform.position + transform.rotation * offset;

    void Update()
    {
        if (lamp == null) return;
        bool playing = Application.isPlaying;
        if (!playing) PlaceLight(); // follows the object while you move it in the editor

        if (playing && maxDistance > 0f && Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.5f;
            Camera cam = Camera.main;
            near = cam == null || (cam.transform.position - transform.position).sqrMagnitude <= maxDistance * maxDistance;
        }

        float on = playing ? OnFactor() : 1f; // outside Play it is always on, so you can see where it is
        if (!near) on = 0f;

        float flick = 1f;
        if (playing && flicker > 0f) flick = 1f + (Mathf.PerlinNoise(seed, Time.time * flickerSpeed) - 0.5f) * 2f * flicker;

        lamp.intensity = intensity * on * flick;
        lamp.enabled = on > 0.001f;
    }

    // 0 = off, 1 = fully on, smooth at the edges of the turn on / turn off hours
    float OnFactor()
    {
        if (!onlyAtNight) return 1f;
        DayNightCycle clock = DayNightCycle.Instance;
        if (clock == null) return 1f;

        float length = Mathf.Repeat(turnOffHour - turnOnHour, 24f); // how many hours it stays on
        if (length < 0.01f) return 1f;
        float t = Mathf.Repeat(clock.currentHour - turnOnHour, 24f); // hours since it turned on
        if (t > length) return 0f;
        return Mathf.Clamp01(Mathf.Min(t, length - t) / Mathf.Max(fadeHours, 0.01f));
    }

    void OnDrawGizmosSelected()
    {
        Vector3 p = transform.position + transform.rotation * offset;
        Gizmos.color = new Color(1f, 0.85f, 0.2f);
        Gizmos.DrawSphere(p, 0.08f);
        Gizmos.color = new Color(color.r, color.g, color.b, 0.6f);
        Gizmos.DrawWireSphere(p, range);
    }
}
