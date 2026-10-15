using System.Collections;
using UnityEngine;

// Dodge in 4 directions (added automatically by PlayerCombat): dodge key + W/A/S/D, no direction = backwards.
// Cancels attacks and gives short i-frames. Animator states (no transitions needed):
//   Combat Dodge Forward, Combat Dodge Backward, Combat Dodge Left, Combat Dodge Right
public class PlayerDodge : MonoBehaviour
{
    public static PlayerDodge Instance { get; private set; }
    public static bool IsDodging => Instance != null && Instance.dodging;
    public static bool IsInvulnerable => Instance != null && Instance.dodging && Time.time < Instance.invulnerableUntil;

    [Header("Controls")]
    public KeyCode dodgeKey = KeyCode.LeftAlt;
    [Tooltip("Off = you can also dodge without a weapon drawn")] public bool onlyWhenArmed = false;
    [Tooltip("With the weapon drawn, W/A/S/D are relative to where the camera/mouse points (the body turns that way first), not to where the body faces")] public bool dodgeRelativeToCamera = true;

    [Header("Animator states")]
    public string forwardState = "Combat Dodge Forward";
    public string backwardState = "Combat Dodge Backward";
    public string leftState = "Combat Dodge Left";
    public string rightState = "Combat Dodge Right";
    [Tooltip("Blend time into the dodge animation")] public float crossFade = 0.08f;
    [Tooltip("Blend time back to idle/movement")] public float returnFade = 0.15f;

    [Header("Movement")]
    [Tooltip("How far a dodge moves you (meters)")] public float dodgeDistance = 2f;
    [Tooltip("How long the dodge lasts (seconds)")] public float dodgeDuration = 0.5f;
    public float staminaCost = 15f;
    [Tooltip("Seconds after a dodge before you can dodge again")] public float cooldown = 0.15f;

    [Header("Invulnerability")]
    [Tooltip("Seconds at the start of the dodge where enemies can't hurt you")] public float invulnerableTime = 0.3f;

    private CharacterController controller;
    private Animator anim;
    private PlayerCombat combat;
    private PlayerStats stats;
    private Transform cam;
    private bool dodging;
    private float invulnerableUntil, nextDodgeTime;

    void Awake() => Instance = this;

    void Start()
    {
        controller = GetComponentInParent<CharacterController>();
        if (controller == null) controller = GetComponentInChildren<CharacterController>();
        combat = GetComponentInParent<PlayerCombat>();
        stats = GetComponentInParent<PlayerStats>();
        if (stats == null) stats = FindAnyObjectByType<PlayerStats>();
        anim = combat != null && combat.anim != null ? combat.anim : GetComponentInChildren<Animator>();
        if (Camera.main != null) cam = Camera.main.transform;
    }

    void Update()
    {
        if (dodging || controller == null || !Input.GetKeyDown(dodgeKey) || Time.time < nextDodgeTime || PlayerFinisher.IsActive || SitAnimation.IsActive || ScreenFade.IsActive || HorseMount.PlayerMounted) return;

        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if ((gear != null && gear.IsOpen) || PlacementSystem.IsPlacing) return;
        if ((stats != null && stats.IsDead) || !controller.isGrounded) return;
        if ((onlyWhenArmed && (combat == null || !combat.IsArmed)) || (combat != null && combat.IsAiming)) return;
        if (stats != null && !stats.TryConsumeStamina(staminaCost)) return;

        StartCoroutine(Dodge());
    }

    IEnumerator Dodge()
    {
        dodging = true;
        SoundManager.Play(Sfx.Dodge, controller.transform.position);
        invulnerableUntil = Time.time + invulnerableTime;
        Transform body = controller.transform;
        if (anim != null) anim.SetBool("isBlocking", false);

        float h = Input.GetAxisRaw("Horizontal"), v = Input.GetAxisRaw("Vertical");
        bool armed = combat != null && combat.IsArmed;
        Vector3 camFwd = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : body.forward;
        if (armed && dodgeRelativeToCamera && camFwd.sqrMagnitude > 0.01f)
            body.rotation = Quaternion.LookRotation(camFwd); // face where the mouse points, so the dodge animation matches the direction
        Vector3 worldDir;
        string state;

        if (Mathf.Abs(h) < 0.1f && Mathf.Abs(v) < 0.1f)
        {
            // No direction: step back
            worldDir = -body.forward;
            state = backwardState;
        }
        else if (armed)
        {
            // In combat you face the camera: dodge relative to the body
            worldDir = (body.right * h + body.forward * v).normalized;
            if (Mathf.Abs(h) > Mathf.Abs(v)) state = h > 0 ? rightState : leftState;
            else state = v > 0 ? forwardState : backwardState;
        }
        else
        {
            // Without a weapon you turn towards the dodge and roll forward
            Vector3 camForward = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : body.forward;
            Vector3 camRight = cam != null ? Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized : body.right;
            worldDir = (camRight * h + camForward * v).normalized;
            body.rotation = Quaternion.LookRotation(worldDir);
            state = forwardState;
        }

        int hash = Animator.StringToHash(state);
        bool hasState = anim != null && anim.HasState(0, hash);
        if (hasState) anim.CrossFadeInFixedTime(hash, crossFade, 0);
        else if (anim != null) Debug.LogWarning($"PlayerDodge: no Animator state called '{state}' - dodging without animation.");

        // Fast at the start, slowing down at the end (the total adds up to dodgeDistance)
        float t = 0f;
        while (t < dodgeDuration)
        {
            float dt = Time.deltaTime;
            t += dt;
            float k = Mathf.Clamp01(t / dodgeDuration);
            float speed = dodgeDistance / dodgeDuration * 2f * (1f - k);
            controller.Move(worldDir * speed * dt);
            yield return null;
        }

        if (hasState && anim != null)
        {
            AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
            if (info.shortNameHash == hash || anim.IsInTransition(0))
                anim.CrossFadeInFixedTime(combat != null ? combat.IdleStateName : "Blend Tree", returnFade, 0);
        }

        dodging = false;
        nextDodgeTime = Time.time + cooldown;
    }
}
