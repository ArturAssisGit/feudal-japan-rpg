using UnityEngine;
using static WorldUtil;

// A bird: it walks around on the ground (Idle <-> Walk). When the player gets close, it takes off (Flap),
// climbs and then alternates Flap and Glide while it flies away. When it is far from the player it is removed.
// Setup: put it on the bird prefab (the root) and drag the 4 clips from the bird FBX into the slots.
// No Animator Controller is needed: the clips are played from code (the model only needs its Animator + Avatar).
// The model must look toward its +Z (blue arrow) when the prefab has Rotation 0.
[DisallowMultipleComponent]
public class BirdAI : MonoBehaviour
{
    [Header("Animation clips (from the bird FBX)")]
    public AnimationClip idleClip;
    public AnimationClip walkClip;
    public AnimationClip flapClip;
    public AnimationClip glideClip;
    [Tooltip("Seconds to blend from one animation to the next")] public float blendSeconds = 0.25f;
    [Tooltip("Playback speed of the walk clip relative to the walk speed (raise it if the feet slide)")] public float walkAnimSpeed = 1f;
    [Tooltip("Playback speed of the flap clip")] public float flapAnimSpeed = 1f;

    [Header("On the ground")]
    [Tooltip("Seconds standing still (random between X and Y)")] public Vector2 idleSeconds = new Vector2(2f, 6f);
    [Tooltip("Seconds walking (random between X and Y)")] public Vector2 walkSeconds = new Vector2(2f, 5f);
    public float walkSpeed = 1.1f;
    [Tooltip("How far from where it appeared the bird walks (meters)")] public float wanderRadius = 6f;
    [Tooltip("Turn speed on the ground (degrees per second)")] public float groundTurnSpeed = 200f;
    [Tooltip("Raise or lower the model so the feet touch the ground (meters)")] public float groundOffset = 0f;

    [Header("Scare")]
    [Tooltip("The bird flies away when the player gets this close (meters)")] public float scareDistance = 7f;
    [Tooltip("Multiplier of that distance when the player is on the horse")] public float mountedScareMultiplier = 1.5f;

    [Header("Flight")]
    public float flySpeed = 8f;
    [Tooltip("Height above the ground it flies at (minimum, maximum) in meters")] public Vector2 heightRange = new Vector2(8f, 18f);
    [Tooltip("Meters per second it goes up while flapping")] public float climbSpeed = 3.5f;
    [Tooltip("Meters per second it goes down while gliding")] public float sinkSpeed = 1.2f;
    [Tooltip("Seconds of flapping each time (random between X and Y)")] public Vector2 flapSeconds = new Vector2(1.5f, 3f);
    [Tooltip("Seconds of gliding each time (random between X and Y)")] public Vector2 glideSeconds = new Vector2(2f, 4f);
    [Tooltip("Seconds of flapping right at take off, before it can glide")] public float takeoffSeconds = 2.5f;
    [Tooltip("Seconds it flies almost level (forward, only a little up) right after the scare, before it starts to climb")] public float levelRunSeconds = 1.5f;
    [Tooltip("Steepest climb angle in degrees (lower = flatter, more forward)")] [Range(5f, 60f)] public float maxClimbAngle = 22f;
    [Tooltip("The flight direction is 'away from the player' plus a random angle up to this (degrees)")] public float directionVariation = 45f;
    [Tooltip("Turn speed in flight (degrees per second)")] public float flyTurnSpeed = 90f;

    [Header("Clean up")]
    [Tooltip("The bird is removed when it is farther than this from the player (meters)")] public float despawnDistance = 90f;
    [Tooltip("After this many seconds of flight it is removed as soon as it is 40 m away")] public float maxFlightSeconds = 40f;

    enum Mode { Idle, Walk, Fly }
    enum Anim { Idle = 0, Walk = 1, Flap = 2, Glide = 3 }

    private Mode mode = Mode.Idle;
    private Transform player;
    private Vector3 home, walkTarget;
    private float modeTimer;

    // Flight
    private Vector3 flyDir;
    private bool flapping;
    private float phaseTimer, flightTime, vertical;

    // Animation (no Animator Controller needed)
    private ClipBlender blender;
    private readonly float[] speeds = { 1f, 1f, 1f, 1f };

    void Start()
    {
        home = transform.position;
        blender = ClipBlender.Create(this, new[] { "Idle", "Walk", "Flap", "Glide" }, new[] { idleClip, walkClip, flapClip, glideClip },
            new[] { true, true, true, true }, blendSeconds);
        EnterIdle();
        SnapToGround();
    }

    void OnDestroy() => blender?.Destroy();

    // ---------- Update ----------

    void Update()
    {
        float dt = Time.deltaTime;
        bool havePlayer = PlayerRef.Find(ref player);

        if (mode != Mode.Fly && havePlayer)
        {
            float scare = scareDistance * (HorseMount.PlayerMounted ? mountedScareMultiplier : 1f);
            if ((player.position - transform.position).sqrMagnitude < scare * scare) StartFlight();
        }

        switch (mode)
        {
            case Mode.Idle: UpdateIdle(dt); break;
            case Mode.Walk: UpdateWalk(dt); break;
            case Mode.Fly: UpdateFly(dt, havePlayer); break;
        }
        speeds[(int)Anim.Walk] = walkAnimSpeed;
        speeds[(int)Anim.Flap] = flapAnimSpeed;
        blender.Update(dt, speeds);
    }

    // ---------- Ground ----------

    void EnterIdle()
    {
        mode = Mode.Idle;
        modeTimer = Random.Range(Mathf.Min(idleSeconds.x, idleSeconds.y), Mathf.Max(idleSeconds.x, idleSeconds.y));
        Play(Anim.Idle);
    }

    void EnterWalk()
    {
        mode = Mode.Walk;
        modeTimer = Random.Range(Mathf.Min(walkSeconds.x, walkSeconds.y), Mathf.Max(walkSeconds.x, walkSeconds.y));
        Vector2 circle = Random.insideUnitCircle * wanderRadius;
        walkTarget = home + new Vector3(circle.x, 0f, circle.y);
        Play(Anim.Walk);
    }

    void UpdateIdle(float dt)
    {
        modeTimer -= dt;
        if (modeTimer <= 0f) EnterWalk();
    }

    void UpdateWalk(float dt)
    {
        modeTimer -= dt;
        Vector3 to = walkTarget - transform.position;
        to.y = 0f;
        if (modeTimer <= 0f || to.sqrMagnitude < 0.1f) { EnterIdle(); return; }

        Vector3 dir = to.normalized;
        Quaternion want = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, groundTurnSpeed * dt);
        // Only walks once it faces the target (no sliding sideways)
        if (Vector3.Angle(transform.forward, dir) < 60f)
        {
            Vector3 pos = transform.position + transform.forward * walkSpeed * dt;
            pos.y = GroundHeight(pos) + groundOffset;
            transform.position = pos;
        }
    }

    void SnapToGround()
    {
        Vector3 pos = transform.position;
        pos.y = GroundHeight(pos) + groundOffset;
        transform.position = pos;
    }

    // ---------- Flight ----------

    [ContextMenu("Scare now (fly away)")]
    void StartFlight()
    {
        if (mode == Mode.Fly) return;
        mode = Mode.Fly;
        flightTime = 0f;
        vertical = 0f;

        Vector3 away = player != null ? transform.position - player.position : -transform.forward;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = transform.forward;
        flyDir = Quaternion.Euler(0f, Random.Range(-directionVariation, directionVariation), 0f) * away.normalized;

        flapping = true;
        phaseTimer = takeoffSeconds;
        Play(Anim.Flap);
    }

    void UpdateFly(float dt, bool havePlayer)
    {
        flightTime += dt;
        phaseTimer -= dt;

        Vector3 pos = transform.position;
        float ground = GroundHeight(pos);
        float height = pos.y - ground;
        float low = Mathf.Min(heightRange.x, heightRange.y), high = Mathf.Max(heightRange.x, heightRange.y);

        // Flap up, glide down, flap again...
        if (flapping)
        {
            if ((phaseTimer <= 0f && height >= low) || height >= high) { flapping = false; phaseTimer = Random.Range(Mathf.Min(glideSeconds.x, glideSeconds.y), Mathf.Max(glideSeconds.x, glideSeconds.y)); Play(Anim.Glide); }
        }
        else if (phaseTimer <= 0f || height < low)
        {
            flapping = true;
            phaseTimer = Random.Range(Mathf.Min(flapSeconds.x, flapSeconds.y), Mathf.Max(flapSeconds.x, flapSeconds.y));
            Play(Anim.Flap);
        }

        // First it goes forward almost level, then it starts to climb
        float climbBlend = Mathf.Clamp01((flightTime - levelRunSeconds * 0.4f) / Mathf.Max(0.1f, levelRunSeconds));
        float targetVertical = flapping ? (height >= high ? 0f : climbSpeed * climbBlend) : -sinkSpeed;
        vertical = Mathf.Lerp(vertical, targetVertical, 2f * dt);

        float speedFactor = Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(flightTime / 1.2f)); // a bit slower at the take off
        float horizontal = flySpeed * speedFactor;
        vertical = Mathf.Min(vertical, horizontal * Mathf.Tan(maxClimbAngle * Mathf.Deg2Rad)); // never steeper than Max Climb Angle
        pos += flyDir * horizontal * dt + Vector3.up * vertical * dt;
        float clearance = Mathf.Lerp(0f, 2f, Mathf.Clamp01(flightTime / 2f)); // over hills (no sudden jump at the start)
        if (pos.y < ground + clearance) pos.y = ground + clearance;
        transform.position = pos;

        // Looks where it really goes (nose up when climbing, down when sinking)
        Vector3 velocity = flyDir * horizontal + Vector3.up * vertical;
        Quaternion want = Quaternion.LookRotation(velocity.normalized);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, flyTurnSpeed * dt);

        if (havePlayer)
        {
            float d2 = Flat(pos - player.position).sqrMagnitude;
            if (d2 > despawnDistance * despawnDistance || (flightTime > maxFlightSeconds && d2 > 40f * 40f)) Destroy(gameObject);
        }
        else if (flightTime > maxFlightSeconds) Destroy(gameObject);
    }

    void Play(Anim anim) => blender.Play((int)anim);

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f);
        Gizmos.DrawWireSphere(transform.position, scareDistance);
        Gizmos.color = new Color(0.3f, 0.8f, 1f);
        Gizmos.DrawWireSphere(Application.isPlaying ? home : transform.position, wanderRadius);
    }
}
