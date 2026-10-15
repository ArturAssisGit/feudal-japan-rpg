using UnityEngine;

// Gather / loot animation, driven by the player's Animator (creates itself).
// This script only turns the Bool parameter "isGathering" on and off; the Animator does the rest:
//   Any State -> Gathering Objects   (condition: isGathering = true, Has Exit Time off, "Can Transition To Self" off)
//   Gathering Objects -> Blend Tree  (condition: isGathering = false, Has Exit Time off)
// Gathering (hold E on a plant, tree, rock...): on until it finishes or you stop.
// Looting a chest, picking up an item, a backpack or a placed object: on for "Pickup Seconds".
// While it is on, the player stands still (PlayerMovement and PlayerCombat check IsActive).
public class GatherAnimation : MonoBehaviour
{
    public static GatherAnimation Instance { get; private set; }
    // True while the animation is on: the player stands still
    public static bool IsActive => Instance != null && Instance.playing;

    [Tooltip("Bool parameter in the player's Animator")] public string boolName = "isGathering";
    [Tooltip("Seconds the animation plays when you loot or pick something up")] public float pickupSeconds = 1.4f;
    [Tooltip("Safety: gathering never lasts longer than this (seconds)")] public float maxLoopSeconds = 90f;

    private Animator anim;
    private Transform player;
    private float until, bornAt;
    private bool playing, looping, warned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null || FindAnyObjectByType<GatherAnimation>() != null) return;
        WorldUtil.CreatePersistent<GatherAnimation>("GatherAnimation");
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    // ---------- Called by the other scripts ----------

    // Loot / pick up: on for a moment
    public static void PlayOnce()
    {
        if (Instance != null && !Instance.playing) Instance.Begin(false);
    }

    // Gathering: on until Stop()
    public static void StartLoop()
    {
        if (Instance != null) Instance.Begin(true);
    }

    public static void Stop()
    {
        if (Instance != null) Instance.End();
    }

    // ---------- On / off ----------

    void Begin(bool loop)
    {
        if (!FindPlayer() || HorseMount.PlayerMounted || PlayerDodge.IsDodging) return;
        if (!HasBool())
        {
            if (!warned) Debug.LogWarning($"GatherAnimation: the player's Animator has no Bool parameter called '{boolName}'. Add it and the two transitions (see the top of GatherAnimation.cs).");
            warned = true;
            return;
        }
        if (!playing) bornAt = Time.time;
        playing = true;
        looping = loop;
        until = loop ? float.MaxValue : Time.time + pickupSeconds;
        anim.SetBool(boolName, true);
    }

    void End()
    {
        playing = false;
        looping = false;
        if (anim != null && HasBool()) anim.SetBool(boolName, false);
    }

    void Update()
    {
        if (!playing) return;
        if (anim == null || !anim.isActiveAndEnabled) { playing = false; return; }
        if (HorseMount.PlayerMounted || Time.time >= until || Time.time - bornAt > maxLoopSeconds) End();
    }

    bool HasBool()
    {
        foreach (AnimatorControllerParameter p in anim.parameters)
            if (p.name == boolName && p.type == AnimatorControllerParameterType.Bool) return true;
        return false;
    }

    bool FindPlayer()
    {
        if (player != null && player.gameObject.activeInHierarchy && anim != null) return true;
        if (!PlayerRef.Find(ref player)) return false;
        anim = PlayerRef.Animator;
        return anim != null;
    }
}
