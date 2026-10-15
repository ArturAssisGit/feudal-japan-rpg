using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Put this on the water plane (the object with the water material).
// The plane's height is the water surface; its size (seen from above) is where the water is ("Infinite" = everywhere).
// The player can walk into the water, but when it gets too deep the screen fades to black,
// he is put back on land a few meters behind, and the screen fades back in. So nobody tries to cross the sea.
// The plane must NOT have a solid collider (or the player walks on the water): one on it is switched off when the game starts.
public class WaterVolume : MonoBehaviour
{
    public static readonly List<WaterVolume> All = new List<WaterVolume>();

    [Header("Water area")]
    [Tooltip("Moves the surface up or down from the plane (meters), if the visible water line is a bit off")] public float surfaceOffset = 0f;
    [Tooltip("The water goes on forever around the plane (a sea that reaches the edge of the map)")] public bool infinite = false;
    [Tooltip("Extra meters around the plane that still count as water")] public float extraBorder = 0f;

    [Header("Too deep: back to land")]
    [Tooltip("How deep the player can walk in: when his feet are this many meters under the water line, he is sent back to land")] public float maxDepth = 1.2f;
    [Tooltip("Seconds of fade to black")] public float fadeOutTime = 0.8f;
    [Tooltip("Seconds the screen stays black")] public float blackTime = 0.15f;
    [Tooltip("Seconds of fade back in")] public float fadeInTime = 0.8f;
    [Tooltip("How far back on land he appears, from where he went into the deep water (meters)")] public float backMeters = 10f;
    [Tooltip("Also fades the sound out and in")] public bool fadeSound = false;

    private Bounds area;

    public float SurfaceY => transform.position.y + surfaceOffset;

    // ---------- Shared by every water plane: where the player last stood on dry land ----------
    private struct SafeSpot { public Vector3 position; public float time; }
    private static readonly List<SafeSpot> safeSpots = new List<SafeSpot>();
    private static int lastFrame = -1;
    private static float nextRecord;
    private static bool sendingBack;
    private static Transform player;
    private static CharacterController controller;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        All.Clear();
        safeSpots.Clear();
        lastFrame = -1;
        nextRecord = 0f;
        sendingBack = false;
        player = null;
        controller = null;
    }

    void Awake()
    {
        foreach (Collider c in GetComponents<Collider>())
        {
            if (c.isTrigger) continue;
            c.enabled = false;
            Debug.Log($"WaterVolume: the solid collider on '{name}' was switched off, so the player goes into the water instead of walking on it. You can remove it.", this);
        }
        UpdateArea();
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void UpdateArea()
    {
        Renderer r = GetComponent<Renderer>();
        if (r != null) area = r.bounds;
        else area = new Bounds(transform.position, new Vector3(10f * transform.lossyScale.x, 1f, 10f * transform.lossyScale.z)); // Unity plane = 10 x 10
    }

    public bool Contains(Vector3 position)
    {
        if (infinite) return true;
        if (transform.hasChanged) { UpdateArea(); transform.hasChanged = false; }
        return position.x >= area.min.x - extraBorder && position.x <= area.max.x + extraBorder
            && position.z >= area.min.z - extraBorder && position.z <= area.max.z + extraBorder;
    }

    // The water that covers this point (the highest one). Null = no water here
    public static WaterVolume At(Vector3 position)
    {
        WaterVolume best = null;
        foreach (WaterVolume w in All)
            if (w != null && w.Contains(position) && (best == null || w.SurfaceY > best.SurfaceY)) best = w;
        return best;
    }

    // ---------- Checking the player ----------

    void Update()
    {
        if (lastFrame == Time.frameCount) return; // one check per frame, even with several water planes
        lastFrame = Time.frameCount;
        if (sendingBack || !FindPlayer()) return;
        if (HorseMount.PlayerMounted || ScreenFade.IsActive || TitleScreenManager.IsOpen) return;
        PlayerStats stats = PlayerRef.Stats;
        if (stats != null && stats.IsDead) return;

        Vector3 feet = player.position;
        WaterVolume water = At(feet);
        float underWater = water != null ? water.SurfaceY - feet.y : -1f;

        // Dry land (or only wet feet): remember this spot
        if (underWater <= 0.05f)
        {
            if (Time.time >= nextRecord && controller.isGrounded)
            {
                nextRecord = Time.time + 0.25f;
                safeSpots.Add(new SafeSpot { position = feet, time = Time.time });
                if (safeSpots.Count > 80) safeSpots.RemoveAt(0); // the last ~20 seconds
            }
            return;
        }

        if (underWater > water.maxDepth) water.StartCoroutine(water.SendBack(feet));
    }

    static bool FindPlayer()
    {
        Transform before = player;
        if (!PlayerRef.Find(ref player)) return false;
        if (player != before || controller == null)
        {
            controller = player.GetComponentInParent<CharacterController>();
            if (controller == null) controller = player.GetComponentInChildren<CharacterController>();
            safeSpots.Clear();
        }
        return controller != null;
    }

    IEnumerator SendBack(Vector3 wentIn)
    {
        sendingBack = true;
        ScreenFade.FadeOut(fadeOutTime, fadeSound); // the player can't move while the screen fades
        StopAnimation(false); // he stops walking while the screen goes black
        float waited = 0f;
        while (!ScreenFade.IsBlack && waited < fadeOutTime + 1f) { waited += Time.unscaledDeltaTime; yield return null; }

        if (player != null && controller != null && TryFindLand(wentIn, out Vector3 land))
        {
            Vector3 away = land - wentIn; // face the land, back to the water
            away.y = 0f;
            Quaternion facing = away.sqrMagnitude > 0.01f ? Quaternion.LookRotation(away) : controller.transform.rotation;
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            controller.transform.SetPositionAndRotation(land + Vector3.up * 0.05f, facing);
            Physics.SyncTransforms();
            controller.enabled = wasEnabled;
        }
        StopAnimation(true); // standing still (idle) when the screen comes back

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, blackTime));
        ScreenFade.FadeIn(fadeInTime, fadeSound);
        safeSpots.Clear(); // start fresh from the new spot
        sendingBack = false;
    }

    // Walk/run animation to 0. snap = jump straight to the idle (behind the black screen)
    static void StopAnimation(bool snap)
    {
        Animator anim = PlayerRef.Animator;
        if (anim == null) return;
        foreach (AnimatorControllerParameter p in anim.parameters)
            if (p.type == AnimatorControllerParameterType.Float && (p.name == "Speed" || p.name == "MovX" || p.name == "MovZ"))
                anim.SetFloat(p.name, 0f);
        if (!snap) return;
        PlayerCombat combat = anim.GetComponentInParent<PlayerCombat>();
        string idle = combat != null ? combat.IdleStateName : "Blend Tree";
        if (anim.HasState(0, Animator.StringToHash(idle))) anim.Play(idle, 0, 0f);
    }

    // The newest dry spot at least Back Distance away from where he went in (or the oldest one we have)
    bool TryFindLand(Vector3 wentIn, out Vector3 land)
    {
        land = Vector3.zero;
        if (safeSpots.Count == 0) return false;
        for (int i = safeSpots.Count - 1; i >= 0; i--)
        {
            Vector3 p = safeSpots[i].position;
            if (WorldUtil.FlatDistance(p, wentIn) >= backMeters) { land = p; return true; }
        }
        land = safeSpots[0].position;
        return true;
    }

    void OnDrawGizmosSelected()
    {
        UpdateArea();
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.35f);
        Vector3 size = area.size + new Vector3(extraBorder * 2f, 0f, extraBorder * 2f);
        Gizmos.DrawCube(new Vector3(area.center.x, SurfaceY, area.center.z), new Vector3(size.x, 0.05f, size.z));
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.35f);
        Gizmos.DrawCube(new Vector3(area.center.x, SurfaceY - maxDepth, area.center.z), new Vector3(size.x, 0.02f, size.z));
    }
}
