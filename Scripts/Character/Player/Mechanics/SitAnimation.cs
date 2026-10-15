using UnityEngine;

// Sit down / stand up, driven by the player's Animator (creates itself).
// Press the Sit Key (B): sits down, stays sitting still. Move (W/A/S/D), Space or press B again: stands up.
// This script sets the Trigger "SitTrigger" (sit down) and the Bool "isSitting" (true while sitting); the Animator does the rest (see the setup steps):
//   Any State         -> Idle To Situp   (SitTrigger,        Has Exit Time OFF)
//   Idle To Situp     -> Sitting Idle    (Has Exit Time ON, no condition)
//   Sitting Idle      -> Situp To Idle   (isSitting = false, Has Exit Time OFF)
//   Situp To Idle     -> Blend Tree      (Has Exit Time ON, no condition)
// While sitting (and while standing up) the player stands still: PlayerMovement, PlayerCombat and PlayerDodge check IsActive.
public class SitAnimation : MonoBehaviour
{
    public static SitAnimation Instance { get; private set; }
    // True from the moment you sit until the stand-up animation ends: the player stays still
    public static bool IsActive => Instance != null && Instance.active;
    public static bool IsSitting => Instance != null && Instance.active && !Instance.standing;

    [Header("Controls")]
    public KeyCode sitKey = KeyCode.B;
    [Tooltip("When the game starts sitting (new game): seconds before moving can make you stand up")] public float startSeatedLockSeconds = 3f;
    [Tooltip("Seconds after sitting down before moving can make you stand up (so a key still held does not cancel it)")] public float minSitSeconds = 0.4f;

    [Header("Animator")]
    [Tooltip("Bool parameter in the player's Animator (true while sitting)")] public string boolName = "isSitting";
    [Tooltip("Trigger parameter in the player's Animator (starts the sit down)")] public string triggerName = "SitTrigger";
    [Tooltip("Names of the three states in the Animator (the clip names)")] public string sitDownState = "Idle To Situp";
    public string sittingState = "Sitting Idle";
    public string standUpState = "Situp To Idle";
    [Tooltip("The stand-up ends (you can walk) when this part of the clip has played")] [Range(0.3f, 1f)] public float standEndAt = 0.9f;

    private Animator anim;
    private Transform player;
    private PlayerStats stats;
    private float sitAt, standAt;
    private bool active, standing, warned, pendingSeated;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null || FindAnyObjectByType<SitAnimation>() != null) return;
        WorldUtil.CreatePersistent<SitAnimation>("SitAnimation");
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    // The game starts with the player already sitting (no sit-down animation): used by New Game
    public static void StartSeated()
    {
        if (Instance == null) return;
        Instance.pendingSeated = true;
    }

    void Update()
    {
        if (!FindPlayer()) { if (!pendingSeated) { active = false; standing = false; } return; }

        if (pendingSeated)
        {
            pendingSeated = false;
            if (HasBool()) SitInstant();
            return;
        }

        if (!active)
        {
            if (Input.GetKeyDown(sitKey) && CanSit()) Sit();
            return;
        }

        if (HorseMount.PlayerMounted || (stats != null && stats.IsDead)) { Finish(); return; }

        if (!standing)
        {
            bool menuOpen = PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.IsOpen;
            bool moveInput = !menuOpen && (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f || Input.GetKeyDown(KeyCode.Space));
            if (!ScreenFade.IsActive && Time.time - sitAt > minSitSeconds && (moveInput || Input.GetKeyDown(sitKey))) StandUp();
            return;
        }

        // Standing up: wait for the animation to end
        AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
        if (info.IsName(standUpState))
        {
            if (!anim.IsInTransition(0) && info.normalizedTime >= standEndAt) Finish();
        }
        else if (Time.time - standAt > 0.5f && !info.IsName(sitDownState) && !info.IsName(sittingState) && !anim.IsInTransition(0))
            Finish(); // already back to the normal movement
        else if (Time.time - standAt > 12f) Finish(); // safety
    }

    bool CanSit()
    {
        if (ScreenFade.IsActive || HorseMount.PlayerMounted || PlayerDodge.IsDodging || PlayerFinisher.IsActive || GatherAnimation.IsActive) return false;
        if (stats != null && stats.IsDead) return false;
        if (PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.IsOpen) return false;
        if (PlacementSystem.IsPlacing) return false;
        if (anim.GetBool("isBlocking")) return false;
        CharacterController cc = player.GetComponentInParent<CharacterController>();
        if (cc != null && !cc.isGrounded) return false;
        if (!HasBool())
        {
            if (!warned) Debug.LogWarning($"SitAnimation: the player's Animator is missing the Bool '{boolName}' or the Trigger '{triggerName}'. Add them and the transitions (see the top of SitAnimation.cs).");
            warned = true;
            return false;
        }
        return true;
    }

    void Sit()
    {
        active = true;
        standing = false;
        sitAt = Time.time;
        anim.SetBool(boolName, true);
        anim.ResetTrigger(triggerName);
        anim.SetTrigger(triggerName);
    }

    void SitInstant()
    {
        active = true;
        standing = false;
        sitAt = Time.time + Mathf.Max(0f, startSeatedLockSeconds - minSitSeconds);
        anim.ResetTrigger(triggerName);
        anim.SetBool(boolName, true);
        anim.Play(sittingState, 0, 0f); // straight into the sitting pose
    }

    void StandUp()
    {
        standing = true;
        standAt = Time.time;
        anim.ResetTrigger(triggerName);
        anim.SetBool(boolName, false);
    }

    void Finish()
    {
        active = false;
        standing = false;
        if (anim != null && HasBool())
        {
            anim.ResetTrigger(triggerName);
            anim.SetBool(boolName, false);
        }
    }

    bool HasBool()
    {
        bool hasBool = false, hasTrigger = false;
        foreach (AnimatorControllerParameter p in anim.parameters)
        {
            if (p.name == boolName && p.type == AnimatorControllerParameterType.Bool) hasBool = true;
            if (p.name == triggerName && p.type == AnimatorControllerParameterType.Trigger) hasTrigger = true;
        }
        return hasBool && hasTrigger;
    }

    bool FindPlayer()
    {
        if (player != null && player.gameObject.activeInHierarchy && anim != null) return true;
        if (!PlayerRef.Find(ref player)) return false;
        stats = PlayerRef.Stats;
        anim = PlayerRef.Animator;
        return anim != null;
    }
}
