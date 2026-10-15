using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using static WorldUtil;

// Put this on the horse in the scene (Horse3D_Ver4_Saddled).
//   [H]  calls the horse: if it is far away it appears 10-16 m from you (behind the camera when possible) and comes to you.
//   [E]  near the horse mounts / dismounts.
//   Riding: WASD steers (relative to the camera). Tap Shift = trot on/off, hold Shift = gallop (uses the horse's stamina).
// The rider sits on "Seat": an empty object, child of a spine bone of the horse (e.g. "SitMount"), placed on the saddle.
// The player's hips are put exactly on it, so move it to adjust how the player sits.
// The Animator Controller needs a float parameter "Speed": -1 backwards, 0 idle, 1 walk, 2 trot, 3 gallop.
[DefaultExecutionOrder(-100)] // places the rider before the camera follows him
public class HorseMount : MonoBehaviour
{
    // The horse that [H] calls (the first one in the scene)
    public static HorseMount Main { get; private set; }
    // True while the player is on a horse (or getting on / off): the player's movement, combat and dodge are off
    public static bool PlayerMounted { get; private set; }
    // The horse being ridden (null = on foot)
    public static HorseMount Ridden { get; private set; }
    // Where the nose points (world yaw) and the current speed (m/s): used by the camera
    public float Heading => heading;
    public float CurrentSpeed => speed;
    // Gait for the camera bob: 0 idle, 1 walk, 2 trot, 3 gallop
    public float Gait { get; private set; }
    // Rhythm of the hoof impacts (radians): |sin| = 0 at each impact. The camera uses it so everything bounces together
    public float BouncePhase { get; private set; }

    [Header("Keys")]
    public KeyCode callKey = KeyCode.H;
    public KeyCode mountKey = KeyCode.E;
    [Tooltip("Tap = trot on/off. Hold = gallop")] public KeyCode runKey = KeyCode.LeftShift;

    [Header("Seat (where the player sits)")]
    [Tooltip("Empty object, child of a spine bone of the horse, on the saddle. Empty = looks for a child called SitMount / Seat, or creates one")] public Transform seat;
    [Tooltip("Extra offset of the player's hips from the seat (meters). X = right, Y = up, Z = forward of the horse")] public Vector3 riderOffset = Vector3.zero;
    [Tooltip("How close you must be to the horse to mount it (meters)")] public float mountRange = 2.6f;
    [Tooltip("Seconds of the jump on / off the horse")] public float mountTime = 0.5f;
    [Tooltip("Height of the little jump when getting on / off (meters)")] public float mountJumpHeight = 0.35f;

    [Header("Rider pose")]
    [Tooltip("Optional humanoid clip for the player while riding (a sitting / horse-riding animation, e.g. from Mixamo). Empty = the player's idle with the legs around the horse and the hands on the reins")] public AnimationClip riderClip;
    [Tooltip("How far forward the thighs go (degrees)")] [Range(0f, 110f)] public float thighForward = 60f;
    [Tooltip("How far apart the legs are (degrees)")] [Range(0f, 70f)] public float legSpread = 30f;
    [Tooltip("How much the shins point back (degrees)")] [Range(-30f, 80f)] public float shinBack = 25f;
    [Tooltip("Hands forward holding the reins (0 = arms hang down)")] [Range(0f, 1f)] public float reins = 0.8f;
    [Tooltip("The rider leans forward when galloping (degrees)")] [Range(0f, 60f)] public float gallopLean = 12f;

    public enum Steering { KeysTurnTheHorse, FollowTheCamera }
    [Header("Steering")]
    [Tooltip("Keys Turn The Horse = W/S forward/back, A/D turn the horse; the mouse only moves the camera. Follow The Camera = the horse goes where the camera looks")] public Steering steering = Steering.KeysTurnTheHorse;
    [Tooltip("Speed walking backwards (S while stopped)")] public float backSpeed = 1.1f;
    [Tooltip("While walking forward without A/D, the horse slowly drifts to where the camera looks (0 = never, 1 = strongly)")] [Range(0f, 1f)] public float cameraSteer = 0.3f;
    [Tooltip("The camera only pulls the horse if it looks less than this many degrees away from the nose")] public float cameraSteerMaxAngle = 70f;
    [Tooltip("Seconds the horse waits before moving off from standing still, so the walk animation can start first")] public float startDelay = 0.3f;

    [Header("Speeds (meters per second)")]
    public float walkSpeed = 1.8f;
    public float trotSpeed = 4.2f;
    public float gallopSpeed = 9.5f;
    [Tooltip("How fast it speeds up")] public float acceleration = 4f;
    [Tooltip("How fast it slows down")] public float deceleration = 7f;
    [Tooltip("Turn speed when slow (degrees per second)")] public float turnSpeedSlow = 150f;
    [Tooltip("How sharply it turns at walking speed compared to the trot (1 = same, 0.5 = half). Lower = wider, calmer turns when walking")] [Range(0.2f, 1f)] public float walkTurnScale = 0.55f;
    [Tooltip("Turn speed when galloping (degrees per second)")] public float turnSpeedFast = 75f;
    [Tooltip("Animation speed (1 = as imported). Raise it a little if the hooves slide on the ground")] public float animationSpeed = 1f;
    [Tooltip("Extra animation speed per gait: X = walk, Y = trot, Z = gallop (1 = as imported, 1.2 = 20% faster). Raise it if the hooves slide on the ground")] public Vector3 gaitAnimationSpeed = new Vector3(1f, 1.2f, 1.2f);

    [Header("Stamina (gallop)")]
    public float maxStamina = 100f;
    [Tooltip("Stamina used per second of gallop")] public float gallopDrain = 9f;
    [Tooltip("Stamina recovered per second when not galloping")] public float staminaRegen = 12f;
    [Tooltip("After running out of stamina it can only gallop again above this")] [Range(0f, 1f)] public float gallopAgainAt = 0.3f;

    [Header("Calling the horse (H)")]
    [Tooltip("Distance from the player where the horse appears: X = min, Y = max (meters)")] public Vector2 spawnDistance = new Vector2(10f, 16f);
    [Tooltip("If the horse is closer than this it doesn't appear somewhere else, it just walks/runs to you")] public float comeWithoutTeleport = 35f;
    [Tooltip("It stops this far from you (meters)")] public float arriveDistance = 2.8f;
    [Tooltip("The horse is invisible until you call it the first time")] public bool hiddenUntilCalled = false;
    [Tooltip("After mounting, the controls hint stays this many seconds and then fades out (0 = never shows it, -1 = always visible)")] public float ridePromptSeconds = 6f;
    [Tooltip("How long the controls hint takes to fade out")] public float ridePromptFade = 1.5f;
    public float callCooldown = 2f;

    [Header("Ground")]
    public float gravity = -25f;
    [Tooltip("The horse tilts with slopes up to this angle")] public float maxSlopeTilt = 20f;
    [Tooltip("What the horse walks on and bumps into")] public LayerMask groundLayers = ~0;

    [Header("Model")]
    [Tooltip("Finds where the horse's nose points from the leg bones (Front.../Back...). Turn off to use Model Yaw Offset")] public bool autoDetectForward = true;
    [Tooltip("Angle of the nose relative to the object's blue arrow. Change it if the horse walks sideways or backwards")] public float modelYawOffset = 0f;
    [Tooltip("Animator float parameter for the gait")] public string speedParameter = "Speed";
    [Tooltip("Off = Speed goes -1 back, 0 idle, 1 walk, 2 trot, 3 gallop (use these as thresholds in the blend tree). On = Speed is the real speed in meters per second")] public bool speedInMetersPerSecond = false;
    [Tooltip("Animator float parameter for turning: -1 = turning left at full rate, 0 = straight, 1 = right (optional)")] public string turnParameter = "Turn";
    [Tooltip("Turning this many degrees per second gives Turn = 1 (or -1)")] public float fullTurnRate = 90f;
    [Tooltip("How softly Turn changes (seconds)")] public float turnDamping = 0.2f;

    [Header("Bounce (feel of the gait)")]
    [Tooltip("How much the rider bounces up on the saddle (meters): X = walk, Y = trot, Z = gallop")] public Vector3 riderBounce = new Vector3(0.015f, 0.05f, 0.035f);
    [Tooltip("How much the horse's body dips at each hoof impact (meters): X = walk, Y = trot, Z = gallop")] public Vector3 horseBounce = new Vector3(0.005f, 0.015f, 0.02f);
    [Tooltip("Hoof impacts per second: X = walk, Y = trot, Z = gallop (the camera uses the same rhythm)")] public Vector3 bounceRate = new Vector3(1.8f, 2.9f, 2.3f);
    [Tooltip("The body rocks from side to side with the hooves while it moves (degrees each way): X = walk, Y = trot, Z = gallop. 0 = off")] public Vector3 sideRock = new Vector3(0.5f, 0.8f, 1f);

    [Header("Turning look")]
    [Tooltip("The neck and head bend into the turn (degrees at full turn, the head end bends the most)")] [Range(0f, 90f)] public float neckTurn = 50f;
    [Tooltip("Turning this many degrees per second already gives the full neck bend")] public float neckFullTurnRate = 60f;
    [Tooltip("While riding, the head also looks a bit to where the camera looks (0 = no, 1 = all the way up to Neck Turn)")] [Range(0f, 1f)] public float neckFollowsCamera = 0.4f;
    [Tooltip("Neck bones (from the withers to the head). Empty = found automatically (see the Console when you press Play)")] public Transform[] neckBones;
    [Tooltip("The body leans into the turn at speed (degrees at full turn, galloping)")] [Range(0f, 20f)] public float bodyLean = 8f;
    [Tooltip("Animator bool parameter set while someone rides it (optional)")] public string mountedParameter = "isMounted";

    [Header("Sounds (optional)")]
    [Tooltip("Played when you call the horse")] public AudioClip whistleSound;
    [Tooltip("Played when the horse arrives / when you get on")] public AudioClip neighSound;
    [Tooltip("Hoof beats (random one per step)")] public AudioClip[] hoofSounds;
    [Range(0f, 1f)] public float hoofVolume = 0.6f;

    [Header("UI")]
    public bool showStaminaBar = true;
    public Color staminaColor = new Color(0.85f, 0.7f, 0.35f, 0.95f);

    // ---------- State ----------
    private enum State { Idle, Coming, Ridden }
    private State state;
    private CharacterController body;
    private Animator animator;
    private int speedHash, mountedHash, turnHash;
    private bool hasSpeedParam, hasMountedParam, hasTurnParam;
    private readonly List<Renderer> renderers = new List<Renderer>();
    private bool hidden;

    private float heading, speed, verticalVelocity, pitch, pitchVelocity, stamina, halfLength = 1f;
    private float lastHeading, turnRate, turnSmooth, turnVelocity, roll, rock;
    private readonly List<Transform> neckChain = new List<Transform>();
    private Quaternion[] neckAnimated, neckApplied;
    private float neckLook, startTimer, bounceAmount;
    private Transform pelvis;
    private Vector3 pelvisAnimated, pelvisApplied;
    private int startingUp; // +1 / -1 while waiting to move off forward / backwards // what the animation gave / what we wrote last frame
    private bool trotOn, exhausted, galloping;
    private float runPressedAt = -1f, nextCallAllowed, stepDistance;

    // Calling
    private NavMeshPath path;
    private readonly List<Vector3> corners = new List<Vector3>();
    private int cornerIndex;
    private float nextPathTime, comingSince, lastProgressTime, bestDistance;

    // Rider
    private Transform player, cachedPlayer;
    private readonly List<Collider> playerColliders = new List<Collider>();
    private CharacterController playerController;
    private Animator playerAnim;
    private PlayerStats playerStats;
    private PlayerCombat playerCombat;
    private Transform hips, spine, leftUpperLeg, leftLowerLeg, leftFoot, rightUpperLeg, rightLowerLeg, rightFoot;
    private Transform leftUpperArm, leftLowerArm, leftHand, rightUpperArm, rightLowerArm, rightHand;
    private PlayableGraph riderGraph;
    private float mountBlend;      // 0 = on the ground, 1 = sitting
    private bool mounting, dismounting;
    private Vector3 groundPosition; // where the jump starts (mount) or ends (dismount)
    private Quaternion groundRotation;

    // UI
    private TextMeshProUGUI promptText, messageText;
    private GameObject staminaBar;
    private RectTransform staminaFill;
    private float messageUntil;
    private string mountPrompt, ridePrompt;
    private bool wasRidePrompt;
    private float ridePromptSince;
    private AudioSource audioSource;

    private static readonly RaycastHit[] hitBuffer = new RaycastHit[16];
    private static readonly Collider[] overlapBuffer = new Collider[16];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Main = null;
        Ridden = null;
        PlayerMounted = false;
    }

    // ---------- Setup ----------

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = false;
            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.name == speedParameter && p.type == AnimatorControllerParameterType.Float) hasSpeedParam = true;
                if (p.name == mountedParameter && p.type == AnimatorControllerParameterType.Bool) hasMountedParam = true;
                if (p.name == turnParameter && p.type == AnimatorControllerParameterType.Float) hasTurnParam = true;
            }
            speedHash = Animator.StringToHash(speedParameter);
            mountedHash = Animator.StringToHash(mountedParameter);
            turnHash = Animator.StringToHash(turnParameter);
            if (!hasSpeedParam) Debug.LogWarning($"{name}: the horse's Animator Controller has no float parameter \"{speedParameter}\" (0 idle, 1 walk, 2 trot, 3 gallop). The horse will move without walk animations.");
        }

        GetComponentsInChildren(true, renderers);
        if (autoDetectForward) DetectForward();

        Bounds bounds = ModelBounds();
        Vector3 nose = NoseRotation(transform.eulerAngles.y + modelYawOffset) * Vector3.forward;
        halfLength = Mathf.Max(0.6f, Mathf.Abs(Vector3.Dot(bounds.size, new Vector3(Mathf.Abs(nose.x), 0f, Mathf.Abs(nose.z)))) * 0.45f);

        SetupBody(bounds);
        FindOrCreateSeat(bounds);
        FindNeck();
        FindPelvis();

        heading = transform.eulerAngles.y + modelYawOffset;
        lastHeading = heading;
        stamina = maxStamina;
        path = new NavMeshPath();

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.minDistance = 3f;
        audioSource.maxDistance = 40f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
    }

    void OnEnable()
    {
        if (Main == null) Main = this;
    }

    void OnDisable()
    {
        if (Ridden == this) ForceDismount();
        if (Main == this)
        {
            Main = null;
            foreach (HorseMount h in FindObjectsByType<HorseMount>(FindObjectsSortMode.None))
                if (h != this && h.isActiveAndEnabled) { Main = h; break; }
        }
        if (promptText != null) promptText.gameObject.SetActive(false);
        if (staminaBar != null) staminaBar.SetActive(false);
    }

    void OnDestroy()
    {
        if (riderGraph.IsValid()) riderGraph.Destroy();
        if (promptText != null) Destroy(promptText.canvas.gameObject);
    }

    void Start()
    {
        BuildUI();
        if (hiddenUntilCalled) SetHidden(true);
    }

    // The nose direction from the bones: front legs minus back legs
    void DetectForward()
    {
        Vector3 front = Vector3.zero, back = Vector3.zero;
        int frontCount = 0, backCount = 0;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (n.Contains("end")) continue;
            if (n.StartsWith("Front")) { front += transform.InverseTransformPoint(t.position); frontCount++; }
            else if (n.StartsWith("Back")) { back += transform.InverseTransformPoint(t.position); backCount++; }
        }
        if (frontCount == 0 || backCount == 0) return;
        Vector3 dir = front / frontCount - back / backCount;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float snapped = Mathf.Round(yaw / 90f) * 90f;
        modelYawOffset = Mathf.Abs(Mathf.DeltaAngle(yaw, snapped)) < 15f ? snapped : yaw;
    }

    // The body dips a little at each hoof impact (on top of the animation, never adds up)
    void BounceHorse()
    {
        if (pelvis == null) return;
        Vector3 current = pelvis.localPosition;
        if ((current - pelvisApplied).sqrMagnitude < 1e-10f) current = pelvisAnimated; // the animation didn't write it
        pelvisAnimated = current;
        pelvis.localPosition = current;
        float dip = hidden ? 0f : ByGait(horseBounce, Gait) * bounceAmount * (1f - BounceShape);
        if (dip > 0.0001f) pelvis.position -= Vector3.up * dip;
        pelvisApplied = pelvis.localPosition;
    }

    // Neck and head into the turn, on top of the animation. Some bones are not written by the animation every frame
    // (constant curves are removed on import): then we start again from the animated pose we saved, so the bend never adds up
    void BendNeck()
    {
        int n = neckChain.Count;
        if (n == 0) return;
        if (neckAnimated == null || neckAnimated.Length != n)
        {
            neckAnimated = new Quaternion[n];
            neckApplied = new Quaternion[n];
            for (int i = 0; i < n; i++) neckAnimated[i] = neckApplied[i] = neckChain[i] != null ? neckChain[i].localRotation : Quaternion.identity;
        }

        float turnAmount = Mathf.Clamp(turnSmooth * Mathf.Max(1f, fullTurnRate) / Mathf.Max(1f, neckFullTurnRate), -1f, 1f);
        // While riding the head also looks a bit to where the camera looks
        float look = 0f;
        if (Ridden == this && !mounting && !dismounting && Camera.main != null)
            look = Mathf.Clamp(Mathf.DeltaAngle(heading, Camera.main.transform.eulerAngles.y), -90f, 90f) / 90f * neckFollowsCamera;
        neckLook = Mathf.Lerp(neckLook, look, 1f - Mathf.Exp(-3f * Time.deltaTime));
        float bend = hidden ? 0f : Mathf.Clamp(turnAmount + neckLook, -1f, 1f) * neckTurn;
        float weightSum = n * (n + 1) * 0.5f; // bone i gets (i+1)/sum: little at the withers, most near the head
        for (int i = 0; i < n; i++)
        {
            Transform bone = neckChain[i];
            if (bone == null) continue;
            Quaternion current = bone.localRotation;
            // Unchanged since we wrote it = the animation didn't touch it: use the saved animated pose
            if (Quaternion.Angle(current, neckApplied[i]) < 0.01f) current = neckAnimated[i];
            neckAnimated[i] = current;
            bone.localRotation = current;
            if (Mathf.Abs(bend) > 0.01f) bone.rotation = Quaternion.AngleAxis(bend * (i + 1) / weightSum, transform.up) * bone.rotation;
            neckApplied[i] = bone.localRotation;
        }
    }

    // The bones from the withers to the head: the bone furthest ahead of the front legs and high up, and its parents up to the shoulders
    void FindNeck()
    {
        neckChain.Clear();
        if (neckBones != null && neckBones.Length > 0)
        {
            foreach (Transform t in neckBones) if (t != null) neckChain.Add(t);
            return;
        }
        FindNeckAuto();
        string names = "";
        foreach (Transform t in neckChain) names += (names.Length > 0 ? " > " : "") + t.name;
        Debug.Log(neckChain.Count > 0
            ? $"{name}: neck bones found automatically: {names}. If they are wrong, drag the right ones to Neck Bones."
            : $"{name}: couldn't find the neck bones. Drag them (from the withers to the head) to Neck Bones on the HorseMount.");
    }

    // The shoulders' common parent is the chest; its child that leads forward (not a leg) is the neck. Chain = from there to the bone furthest ahead
    void FindNeckAuto()
    {
        Vector3 nose = Quaternion.Euler(0f, transform.eulerAngles.y + modelYawOffset, 0f) * Vector3.forward;
        Vector3 front = Vector3.zero;
        float frontTop = float.MinValue;
        int frontCount = 0;
        Transform[] all = GetComponentsInChildren<Transform>(true);
        foreach (Transform t in all)
        {
            if (!t.name.StartsWith("Front") || t.name.Contains("end")) continue;
            front += t.position;
            frontTop = Mathf.Max(frontTop, t.position.y);
            frontCount++;
        }
        if (frontCount == 0) return;
        front /= frontCount;

        // 1) Through the hierarchy: chest = common parent of the left and right front legs
        Transform leftShoulder = null, rightShoulder = null;
        foreach (Transform t in all)
        {
            if (!t.name.StartsWith("Front") || t.name.Contains("end")) continue;
            if (t.parent != null && t.parent.name.StartsWith("Front")) continue; // only the top bone of each leg
            if (t.name.EndsWith(".l") || t.name.EndsWith("_L") || t.name.EndsWith(".L")) leftShoulder = t;
            else if (t.name.EndsWith(".r") || t.name.EndsWith("_R") || t.name.EndsWith(".R")) rightShoulder = t;
        }
        Transform chest = null;
        if (leftShoulder != null && rightShoulder != null)
            for (Transform a = leftShoulder.parent; a != null && a != transform && chest == null; a = a.parent)
                if (rightShoulder.IsChildOf(a)) chest = a;
        if (chest != null)
        {
            Transform bestTip = null;
            float bestAlong = float.MinValue;
            foreach (Transform t in chest.GetComponentsInChildren<Transform>(true))
            {
                if (t == chest || HasLegAbove(t, chest)) continue;
                float along = Vector3.Dot(t.position - front, nose);
                if (along > bestAlong) { bestAlong = along; bestTip = t; }
            }
            if (bestTip != null && bestAlong > 0f)
            {
                for (Transform t = bestTip; t != null && t != chest; t = t.parent)
                    if (t.childCount > 0 && !t.name.Contains("end")) neckChain.Insert(0, t);
                if (neckChain.Count > 0) return;
            }
        }

        // 2) Fallback: by position
        Transform tip = null;
        float best = 0f;
        foreach (Transform t in all)
        {
            if (t == transform || t.name.StartsWith("Front") || t.name.StartsWith("Back") || t.GetComponent<Renderer>() != null) continue;
            float along = Vector3.Dot(t.position - front, nose);
            if (along > best && t.position.y > frontTop) { best = along; tip = t; }
        }
        if (tip == null) return;

        float limit = -halfLength * 0.4f; // the neck starts a little behind the front legs
        for (Transform t = tip; t != null && t != transform; t = t.parent)
        {
            if (Vector3.Dot(t.position - front, nose) < limit || t.name.StartsWith("Armature")) break;
            if (t.childCount > 0) neckChain.Insert(0, t); // root first; the end bone does nothing
        }
    }

    // The bone that carries the whole body: common parent of the front and back legs
    void FindPelvis()
    {
        Transform frontLeg = null, backLeg = null;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name.Contains("end")) continue;
            if (frontLeg == null && t.name.StartsWith("Front")) frontLeg = t;
            if (backLeg == null && t.name.StartsWith("Back")) backLeg = t;
        }
        if (frontLeg == null || backLeg == null) return;
        for (Transform a = frontLeg.parent; a != null && a != transform; a = a.parent)
            if (backLeg.IsChildOf(a)) { pelvis = a; break; }
        if (pelvis != null) pelvisAnimated = pelvisApplied = pelvis.localPosition;
    }

    // Value 0..3 for a speed (0 idle, 1 walk, 2 trot, 3 gallop)
    float GaitOf(float s)
    {
        s = Mathf.Abs(s);
        if (s <= walkSpeed) return s / Mathf.Max(0.01f, walkSpeed);
        if (s <= trotSpeed) return 1f + (s - walkSpeed) / Mathf.Max(0.01f, trotSpeed - walkSpeed);
        return 2f + Mathf.Clamp01((s - trotSpeed) / Mathf.Max(0.01f, gallopSpeed - trotSpeed));
    }

    // Walk / trot / gallop value of a Vector3 setting for the gait g
    static float ByGait(Vector3 v, float g)
    {
        if (g <= 1f) return v.x * g;
        if (g <= 2f) return Mathf.Lerp(v.x, v.y, g - 1f);
        return Mathf.Lerp(v.y, v.z, Mathf.Clamp01(g - 2f));
    }

    static float ByGaitRate(Vector3 v, float g)
    {
        if (g <= 1f) return v.x;
        if (g <= 2f) return Mathf.Lerp(v.x, v.y, g - 1f);
        return Mathf.Lerp(v.y, v.z, Mathf.Clamp01(g - 2f));
    }

    // 0 at each hoof impact, 1 in between
    float BounceShape => Mathf.Abs(Mathf.Sin(BouncePhase));

    static bool HasLegAbove(Transform t, Transform stop)
    {
        for (Transform a = t; a != null && a != stop; a = a.parent)
            if (a.name.StartsWith("Front") || a.name.StartsWith("Back")) return true;
        return false;
    }

    Bounds ModelBounds()
    {
        Bounds bounds = new Bounds(transform.position + Vector3.up, Vector3.one);
        bool first = true;
        foreach (Renderer r in renderers)
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            if (first) { bounds = r.bounds; first = false; }
            else bounds.Encapsulate(r.bounds);
        }
        return bounds;
    }

    // A CharacterController so the horse stands on the ground and bumps into things
    void SetupBody(Bounds bounds)
    {
        body = GetComponent<CharacterController>();
        if (body != null) return;

        body = gameObject.AddComponent<CharacterController>();
        Vector3 scale = transform.lossyScale;
        float sy = Mathf.Max(0.0001f, Mathf.Abs(scale.y));
        float sxz = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
        float worldHeight = Mathf.Clamp(bounds.size.y * 0.85f, 1.2f, 2.4f);
        float worldRadius = 0.55f;
        body.height = worldHeight / sy;
        body.radius = worldRadius / sxz;
        float bottom = Mathf.Max(0f, bounds.min.y - transform.position.y); // the hooves (usually 0 = the pivot)
        body.center = new Vector3(0f, (bottom + worldHeight * 0.5f) / sy, 0f);
        body.stepOffset = 0.45f / sy;
        body.slopeLimit = 50f;
        body.skinWidth = 0.04f / sy;
        body.minMoveDistance = 0f;
    }

    void FindOrCreateSeat(Bounds bounds)
    {
        if (seat != null) return;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant().Replace(" ", "");
            if (n == "sitmount" || n == "seat" || n == "mountpoint" || n == "saddlepoint") { seat = t; return; }
        }

        // No seat yet: put one on the saddle (middle of the horse, at back height), on the closest bone so it moves with the animation
        Vector3 point = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.66f, bounds.center.z);
        Transform bestBone = transform;
        float best = float.MaxValue;
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach (Transform bone in smr.bones)
            {
                if (bone == null) continue;
                float d = (bone.position - point).sqrMagnitude;
                if (d < best) { best = d; bestBone = bone; }
            }
        seat = new GameObject("SitMount (auto)").transform;
        seat.SetParent(bestBone, false);
        seat.position = point;
        Debug.Log($"{name}: no seat assigned, created 'SitMount (auto)' on bone '{bestBone.name}'. For a better result create your own empty object on the spine bone, put it on the saddle and drag it to Seat.");
    }

    // ---------- Update ----------

    void Update()
    {
        if (messageText != null && messageText.text.Length > 0 && Time.time > messageUntil) messageText.text = "";
        float dt = Time.deltaTime;
        bool hasPlayer = FindPlayer();

        if (hasPlayer && Main == this && Input.GetKeyDown(callKey) && CanAct() && Ridden == null && HorseOwnership.Allows(this)) CallHorse();

        if (pendingCall && hasPlayer)
        {
            pendingCall = false;
            CallHorse(true);
        }

        float wantedSpeed = 0f;
        float wantedHeading = heading;

        if (state == State.Ridden && Ridden == this)
        {
            if (!hasPlayer || (playerStats != null && playerStats.IsDead)) { ForceDismount(); }
            else RideInput(dt, ref wantedSpeed, ref wantedHeading);
        }
        else if (state == State.Coming && hasPlayer) ComeInput(ref wantedSpeed, ref wantedHeading);

        Drive(dt, wantedSpeed, wantedHeading);
        UpdateStamina(dt);
        UpdateAnimator(dt);
        UpdateHoofSounds(dt);
        UpdatePrompt(hasPlayer);
        UpdateStaminaBar();
    }

    bool CanAct()
    {
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (gear != null && gear.IsOpen) return false;
        if (PlacementSystem.IsPlacing || RespawnSystem.IsDeathScreenOpen || SleepSystem.IsSleeping) return false;
        if (PlayerDodge.IsDodging || PlayerFinisher.IsActive || ScreenFade.IsActive) return false;
        return playerStats == null || !playerStats.IsDead;
    }

    bool FindPlayer()
    {
        if (!PlayerRef.Find(ref player)) return false;
        if (player != cachedPlayer)
        {
            playerController = player.GetComponentInParent<CharacterController>();
            if (playerController == null) playerController = player.GetComponentInChildren<CharacterController>();
            if (playerController != null) player = playerController.transform; // the object that moves
            playerStats = player.GetComponentInParent<PlayerStats>();
            if (playerStats == null) playerStats = player.GetComponentInChildren<PlayerStats>();
            playerCombat = player.GetComponentInChildren<PlayerCombat>();
            PlayerMovement movement = player.GetComponentInChildren<PlayerMovement>();
            playerAnim = movement != null && movement.anim != null ? movement.anim : player.GetComponentInChildren<Animator>();
            CacheBones();
            cachedPlayer = player;
        }
        return true;
    }

    void CacheBones()
    {
        hips = spine = null;
        if (playerAnim == null || !playerAnim.isHuman) return;
        hips = playerAnim.GetBoneTransform(HumanBodyBones.Hips);
        spine = playerAnim.GetBoneTransform(HumanBodyBones.Chest) ?? playerAnim.GetBoneTransform(HumanBodyBones.Spine);
        leftUpperLeg = playerAnim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        leftLowerLeg = playerAnim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        leftFoot = playerAnim.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightUpperLeg = playerAnim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        rightLowerLeg = playerAnim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        rightFoot = playerAnim.GetBoneTransform(HumanBodyBones.RightFoot);
        leftUpperArm = playerAnim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        leftLowerArm = playerAnim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        leftHand = playerAnim.GetBoneTransform(HumanBodyBones.LeftHand);
        rightUpperArm = playerAnim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        rightLowerArm = playerAnim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        rightHand = playerAnim.GetBoneTransform(HumanBodyBones.RightHand);
    }

    // ---------- Riding ----------

    void RideInput(float dt, ref float wantedSpeed, ref float wantedHeading)
    {
        if (mounting || dismounting) return;

        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        bool menuOpen = gear != null && gear.IsOpen;
        if (!menuOpen && Input.GetKeyDown(mountKey)) { Dismount(); return; }

        float h = menuOpen ? 0f : Input.GetAxisRaw("Horizontal");
        float v = menuOpen ? 0f : Input.GetAxisRaw("Vertical");

        // Shift: tap = trot on/off, hold = gallop
        if (!menuOpen && Input.GetKeyDown(runKey)) runPressedAt = Time.time;
        bool held = !menuOpen && Input.GetKey(runKey) && runPressedAt >= 0f && Time.time - runPressedAt > 0.25f;
        if (Input.GetKeyUp(runKey))
        {
            if (runPressedAt >= 0f && Time.time - runPressedAt <= 0.25f) trotOn = !trotOn;
            runPressedAt = -1f;
        }

        if (steering == Steering.KeysTurnTheHorse)
        {
            // A/D turn the horse only while it is moving (W held or still rolling): standing still it does not spin on the spot
            bool moving = v > 0.1f || Mathf.Abs(speed) > 0.3f;
            if (Mathf.Abs(h) > 0.1f && moving) wantedHeading = heading + Mathf.Sign(h) * 90f;
            else if (v > 0.1f && cameraSteer > 0f && Camera.main != null)
            {
                // A mix of the two: the horse slowly bends towards where you look
                float toCamera = Mathf.DeltaAngle(heading, Camera.main.transform.eulerAngles.y);
                if (Mathf.Abs(toCamera) < cameraSteerMaxAngle)
                    wantedHeading = heading + toCamera * (1f - Mathf.Exp(-cameraSteer * 2f * dt));
            }
            if (v > 0.1f)
            {
                galloping = held && !exhausted;
                wantedSpeed = galloping ? gallopSpeed : trotOn ? trotSpeed : walkSpeed;
            }
            else
            {
                galloping = false;
                if (v < -0.1f && speed < 0.3f) wantedSpeed = -backSpeed;
            }
            return;
        }

        Vector3 input = new Vector3(h, 0f, v);
        if (input.sqrMagnitude < 0.01f)
        {
            galloping = false;
            return;
        }
        input.Normalize();

        Transform cam = Camera.main != null ? Camera.main.transform : null;
        float camYaw = cam != null ? cam.eulerAngles.y : heading;
        wantedHeading = camYaw + Mathf.Atan2(input.x, input.z) * Mathf.Rad2Deg;

        galloping = held && !exhausted;
        wantedSpeed = galloping ? gallopSpeed : trotOn ? trotSpeed : walkSpeed;

        // Asked to go the other way: slow down while turning
        float off = Mathf.Abs(Mathf.DeltaAngle(heading, wantedHeading));
        if (off > 60f) wantedSpeed *= Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(60f, 150f, off));
    }

    void UpdateStamina(float dt)
    {
        bool reallyGalloping = galloping && speed > trotSpeed + 0.5f;
        stamina = reallyGalloping
            ? Mathf.Max(0f, stamina - gallopDrain * dt)
            : Mathf.Min(maxStamina, stamina + staminaRegen * dt);
        if (stamina <= 0f) exhausted = true;
        else if (exhausted && stamina >= maxStamina * gallopAgainAt) exhausted = false;
    }

    // ---------- Moving (riding and coming) ----------

    void Drive(float dt, float wantedSpeed, float wantedHeading)
    {
        if (hidden || body == null || !body.enabled) return;

        float turnSpeed = Mathf.Lerp(turnSpeedSlow, turnSpeedFast, Mathf.InverseLerp(walkSpeed, gallopSpeed, speed));
        // Walking: wider, calmer turns; the full turn rate comes back by the trot
        turnSpeed *= Mathf.Lerp(walkTurnScale, 1f, Mathf.InverseLerp(walkSpeed, trotSpeed, Mathf.Abs(speed)));
        heading = Mathf.MoveTowardsAngle(heading, wantedHeading, turnSpeed * dt);

        // Moving off from standing still: wait a moment so the walk animation starts before the body moves
        startingUp = 0;
        if (Mathf.Abs(wantedSpeed) > 0.05f)
        {
            if (Mathf.Abs(speed) < 0.05f && startTimer < startDelay)
            {
                startTimer += dt;
                startingUp = wantedSpeed > 0f ? 1 : -1;
                wantedSpeed = 0f;
            }
            else startTimer = startDelay;
        }
        else if (Mathf.Abs(speed) < 0.05f) startTimer = 0f;

        float rate = Mathf.Abs(wantedSpeed) > Mathf.Abs(speed) ? acceleration : deceleration;
        speed = Mathf.MoveTowards(speed, wantedSpeed, rate * dt);

        Vector3 nose = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
        if (speed > 0.05f && Blocked(nose)) speed = Mathf.Min(speed, 0.3f);

        if (body.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * dt;
        body.Move((nose * speed + Vector3.up * verticalVelocity) * dt);

        // Tilt with the slope (raycasts at the front and back hooves)
        float wantedPitch = pitch;
        if (speed > 0.05f || state != State.Idle)
        {
            wantedPitch = 0f;
            if (GroundHeight(transform.position + nose * halfLength, out float front) && GroundHeight(transform.position - nose * halfLength, out float back))
                wantedPitch = Mathf.Clamp(Mathf.Atan2(back - front, halfLength * 2f) * Mathf.Rad2Deg, -maxSlopeTilt, maxSlopeTilt);
        }
        pitch = Mathf.SmoothDamp(pitch, wantedPitch, ref pitchVelocity, 0.25f);

        // Lean into the turn (more when fast)
        float wantedRoll = -turnSmooth * bodyLean * Mathf.InverseLerp(walkSpeed, gallopSpeed, speed);
        roll = Mathf.Lerp(roll, wantedRoll, 1f - Mathf.Exp(-6f * dt));

        // Rocks a little from side to side with each hoof beat (only while it moves: bounceAmount is 0 when standing)
        rock = Mathf.Cos(BouncePhase) * ByGait(sideRock, Gait) * bounceAmount;

        transform.rotation = NoseRotation(heading) * Quaternion.Euler(0f, -modelYawOffset, 0f);

        float dtSafe = Mathf.Max(dt, 0.0001f);
        turnRate = Mathf.DeltaAngle(lastHeading, heading) / dtSafe;
        lastHeading = heading;
    }

    // Yaw of the nose + slope tilt
    Quaternion NoseRotation(float yaw) => Quaternion.Euler(pitch, yaw, roll + rock);

    Vector3 Nose => Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
    Vector3 NoseRight => Quaternion.Euler(0f, heading, 0f) * Vector3.right;

    // A wall / tree / rock right in front of the head?
    bool Blocked(Vector3 nose)
    {
        Vector3 from = transform.position + Vector3.up * 1.1f;
        int count = Physics.SphereCastNonAlloc(from, 0.35f, nose, hitBuffer, halfLength + 0.2f, groundLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hitBuffer[i];
            if (IsOwnOrPlayer(hit.collider)) continue;
            if (hit.distance <= 0f) continue; // started inside it (e.g. a bush): don't get stuck
            if (hit.normal.y > 0.6f) continue; // walkable slope, not a wall
            return true;
        }
        return false;
    }

    bool GroundHeight(Vector3 point, out float height)
    {
        height = point.y;
        int count = Physics.RaycastNonAlloc(point + Vector3.up * 2f, Vector3.down, hitBuffer, 6f, groundLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (IsOwnOrPlayer(hitBuffer[i].collider)) continue;
            if (hitBuffer[i].distance < best) { best = hitBuffer[i].distance; height = hitBuffer[i].point.y; found = true; }
        }
        return found;
    }

    bool IsOwnOrPlayer(Collider c)
    {
        Transform t = c.transform;
        return t.IsChildOf(transform) || (player != null && t.IsChildOf(player));
    }

    // ---------- Calling ----------

    // Set by HorseSpawner: which owned horse this is (see HorseStable). Empty = a horse placed in the scene by hand
    [HideInInspector] public string ownershipKey = "";
    public bool IsRidden => Ridden == this;
    public bool IsPresent => !hidden && isActiveAndEnabled;

    // The stats of a bought horse (1.0 = a normal horse)
    public void ApplyStats(float speedMultiplier, float staminaMultiplier, float agilityMultiplier)
    {
        walkSpeed *= speedMultiplier;
        trotSpeed *= speedMultiplier;
        gallopSpeed *= speedMultiplier;
        maxStamina *= staminaMultiplier;
        stamina = maxStamina;
        turnSpeedSlow *= agilityMultiplier;
        turnSpeedFast *= agilityMultiplier;
    }

    // Used by HorseSpawner: call a horse that was just created
    public void RequestCall() { pendingCall = true; }
    private bool pendingCall;

    void CallHorse(bool forceSpawn = false)
    {
        if (Time.time < nextCallAllowed) return;
        nextCallAllowed = Time.time + callCooldown;
        if (whistleSound != null) AudioSource.PlayClipAtPoint(whistleSound, player.position, Volume());

        float distance = Flat(transform.position - player.position).magnitude;
        if (forceSpawn || hidden || distance > comeWithoutTeleport || !gameObject.activeInHierarchy)
        {
            if (!FindSpawnPoint(out Vector3 point))
            {
                ShowMessage("Your horse can't reach you here");
                return;
            }
            Teleport(point, Mathf.Atan2(player.position.x - point.x, player.position.z - point.z) * Mathf.Rad2Deg);
        }
        else if (distance <= arriveDistance + 0.5f)
        {
            return; // already here
        }

        state = State.Coming;
        comingSince = Time.time;
        lastProgressTime = Time.time;
        bestDistance = float.MaxValue;
        nextPathTime = 0f;
        ShowMessage("Your horse is coming");
    }

    void ComeInput(ref float wantedSpeed, ref float wantedHeading)
    {
        Vector3 toPlayer = Flat(player.position - transform.position);
        float distance = toPlayer.magnitude;

        if (distance <= arriveDistance)
        {
            state = State.Idle;
            if (neighSound != null) audioSource.PlayOneShot(neighSound, Volume());
            return;
        }

        // Gave up: stuck behind something for a while
        if (distance < bestDistance - 0.5f) { bestDistance = distance; lastProgressTime = Time.time; }
        if (Time.time - lastProgressTime > 4f || Time.time - comingSince > 40f)
        {
            state = State.Idle;
            return;
        }

        if (Time.time >= nextPathTime) RecalculatePath();
        Vector3 target = player.position;
        while (cornerIndex < corners.Count)
        {
            Vector3 corner = corners[cornerIndex];
            if (Flat(corner - transform.position).magnitude > 1.2f || cornerIndex == corners.Count - 1) { target = corner; break; }
            cornerIndex++;
        }

        Vector3 to = Flat(target - transform.position);
        if (to.sqrMagnitude > 0.01f) wantedHeading = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        wantedSpeed = distance > 20f ? gallopSpeed * 0.85f : distance > 7f ? trotSpeed : walkSpeed;
        // Brake near the player
        wantedSpeed = Mathf.Min(wantedSpeed, Mathf.Max(walkSpeed * 0.6f, (distance - arriveDistance) * 1.5f));
        float off = Mathf.Abs(Mathf.DeltaAngle(heading, wantedHeading));
        if (off > 70f) wantedSpeed *= 0.4f;
    }

    void RecalculatePath()
    {
        nextPathTime = Time.time + 1f;
        corners.Clear();
        cornerIndex = 0;
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit from, 4f, NavMesh.AllAreas) &&
            NavMesh.SamplePosition(player.position, out NavMeshHit to, 4f, NavMesh.AllAreas) &&
            NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) && path.status != NavMeshPathStatus.PathInvalid)
        {
            corners.AddRange(path.corners);
            if (corners.Count > 0) cornerIndex = 1; // corner 0 is where the horse already is
        }
        if (corners.Count == 0) corners.Add(player.position); // no NavMesh: straight to the player
    }

    // A spot 10-16 m away, on the ground, free of trees/rocks, reachable, preferably behind the camera
    bool FindSpawnPoint(out Vector3 point)
    {
        point = Vector3.zero;
        Transform cam = Camera.main != null ? Camera.main.transform : player;
        Vector3 look = Flat(cam.forward).normalized;
        bool navAvailable = NavMesh.SamplePosition(player.position, out NavMeshHit playerNav, 4f, NavMesh.AllAreas);

        float bestScore = float.MinValue;
        bool found = false;
        float start = Random.Range(0f, 360f);
        for (int i = 0; i < 24; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, start + i * 15f, 0f) * Vector3.forward;
            float distance = Random.Range(spawnDistance.x, Mathf.Max(spawnDistance.x, spawnDistance.y));
            Vector3 candidate = player.position + dir * distance;
            if (!RespawnSystem.FindGround(candidate + Vector3.up * 40f, out Vector3 ground)) continue;
            if (Mathf.Abs(ground.y - player.position.y) > 6f) continue;

            if (navAvailable)
            {
                if (!NavMesh.SamplePosition(ground, out NavMeshHit nav, 2f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(nav.position, playerNav.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                ground = nav.position;
            }
            if (!SpaceIsFree(ground)) continue;

            float score = -Vector3.Dot(look, dir) + Random.Range(0f, 0.3f); // behind the camera is best
            if (score > bestScore) { bestScore = score; point = ground; found = true; }
        }
        return found;
    }

    bool SpaceIsFree(Vector3 ground)
    {
        int count = Physics.OverlapCapsuleNonAlloc(ground + Vector3.up * 0.8f, ground + Vector3.up * 1.6f, 0.7f, overlapBuffer, groundLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = overlapBuffer[i];
            if (IsOwnOrPlayer(c) || c is TerrainCollider) continue;
            return false;
        }
        return true;
    }

    void Teleport(Vector3 point, float yaw)
    {
        SetHidden(false);
        body.enabled = false;
        heading = yaw;
        lastHeading = yaw;
        pitch = 0f;
        roll = 0f;
        turnSmooth = 0f;
        speed = 0f;
        verticalVelocity = 0f;
        transform.SetPositionAndRotation(point, NoseRotation(heading) * Quaternion.Euler(0f, -modelYawOffset, 0f));
        body.enabled = true;
        Physics.SyncTransforms();
    }

    void SetHidden(bool hide)
    {
        hidden = hide;
        foreach (Renderer r in renderers) if (r != null) r.enabled = !hide;
        if (body != null) body.enabled = !hide;
        if (animator != null) animator.enabled = !hide;
    }

    // ---------- Mount / dismount ----------

    void TryMount()
    {
        if (Ridden != null || PlayerMounted || player == null || playerController == null) return;
        if (!CanAct() || !HorseOwnership.Allows(this)) return;
        if (playerAnim == null || hips == null)
        {
            ShowMessage("The player needs a Humanoid Animator to ride");
            return;
        }

        state = State.Ridden;
        Ridden = this;
        PlayerMounted = true;
        mounting = true;
        dismounting = false;
        mountBlend = 0f;
        groundPosition = player.position;
        groundRotation = player.rotation;
        trotOn = false;
        galloping = false;
        runPressedAt = -1f;

        if (playerCombat != null) playerCombat.ResetCombatState();
        IgnorePlayerCollisions(true);
        playerController.enabled = false;
        ClearPlayerAnim();
        if (riderClip != null)
        {
            if (riderGraph.IsValid()) riderGraph.Destroy();
            AnimationPlayableUtilities.PlayClip(playerAnim, riderClip, out riderGraph);
        }
        if (hasMountedParam && animator != null) animator.SetBool(mountedHash, true);
        if (neighSound != null && Random.value < 0.5f) audioSource.PlayOneShot(neighSound, Volume() * 0.8f);
    }

    void Dismount()
    {
        if (!PlayerMounted || Ridden != this || mounting || dismounting) return;
        dismounting = true;
        galloping = false;
        groundPosition = FindDismountSpot();
        groundRotation = Quaternion.Euler(0f, heading, 0f);
    }

    // Instantly off the horse (death, scene change, horse disabled)
    void ForceDismount()
    {
        if (Ridden != this) return;
        Vector3 spot = player != null ? FindDismountSpot() : Vector3.zero;
        mountBlend = 0f;
        FinishDismount(spot);
    }

    void FinishDismount(Vector3 spot)
    {
        mounting = dismounting = false;
        state = State.Idle;
        Ridden = null;
        PlayerMounted = false;
        galloping = false;
        if (riderGraph.IsValid()) riderGraph.Destroy();
        if (hasMountedParam && animator != null) animator.SetBool(mountedHash, false);
        if (player != null)
        {
            player.SetPositionAndRotation(spot, Quaternion.Euler(0f, heading, 0f));
            Physics.SyncTransforms();
            if (playerController != null) playerController.enabled = true;
        }
        IgnorePlayerCollisions(false);
    }

    // The horse's body must not bump into the rider's own colliders while he sits on it
    void IgnorePlayerCollisions(bool ignore)
    {
        if (ignore)
        {
            playerColliders.Clear();
            if (player != null) player.GetComponentsInChildren(true, playerColliders);
        }
        if (body != null)
            foreach (Collider c in playerColliders)
                if (c != null && c != body && c.enabled && c.gameObject.activeInHierarchy && c.gameObject.scene.IsValid() && body.enabled && body.gameObject.scene.IsValid())
                    Physics.IgnoreCollision(body, c, ignore);
        if (!ignore) playerColliders.Clear();
    }

    // Left side of the horse, else right, else behind: a free spot on the ground
    Vector3 FindDismountSpot()
    {
        Vector3[] sides = { -NoseRight * 1.3f, NoseRight * 1.3f, -Nose * (halfLength + 1f), Nose * (halfLength + 1f) };
        Vector3 fallback = transform.position - NoseRight * 1.3f;
        for (int i = 0; i < sides.Length; i++)
        {
            Vector3 candidate = transform.position + sides[i];
            if (!RespawnSystem.FindGround(candidate + Vector3.up * 3f, out Vector3 ground)) continue;
            if (i == 0) fallback = ground;
            if (Mathf.Abs(ground.y - transform.position.y) > 1.5f) continue;
            if (!PlayerSpaceIsFree(ground)) continue;
            return ground;
        }
        return fallback;
    }

    bool PlayerSpaceIsFree(Vector3 ground)
    {
        int count = Physics.OverlapCapsuleNonAlloc(ground + Vector3.up * 0.5f, ground + Vector3.up * 1.6f, 0.3f, overlapBuffer, groundLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = overlapBuffer[i];
            if (IsOwnOrPlayer(c) || c is TerrainCollider) continue;
            return false;
        }
        return true;
    }

    // The player stands still in the idle while sitting (the legs and arms are posed in LateUpdate)
    void ClearPlayerAnim()
    {
        if (playerAnim == null) return;
        foreach (AnimatorControllerParameter p in playerAnim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Float && (p.name == "Speed" || p.name == "MovX" || p.name == "MovZ")) playerAnim.SetFloat(p.nameHash, 0f);
            if (p.type == AnimatorControllerParameterType.Bool && (p.name == "isBlocking" || p.name == "isAiming")) playerAnim.SetBool(p.nameHash, false);
        }
    }

    // ---------- Placing the rider (after the animations, before the camera) ----------

    void LateUpdate()
    {
        BendNeck();
        BounceHorse();

        if (Ridden != this || player == null || hips == null) return;

        float dt = Time.deltaTime;
        if (mounting)
        {
            mountBlend = Mathf.MoveTowards(mountBlend, 1f, dt / Mathf.Max(0.05f, mountTime));
            if (mountBlend >= 1f) mounting = false;
        }
        else if (dismounting)
        {
            mountBlend = Mathf.MoveTowards(mountBlend, 0f, dt / Mathf.Max(0.05f, mountTime));
        }

        float k = Mathf.SmoothStep(0f, 1f, mountBlend);
        Quaternion seated = Quaternion.Euler(pitch, heading, (roll + rock) * 0.6f);
        player.rotation = Quaternion.Slerp(groundRotation, seated, k);

        if (riderClip == null) PoseRider(k);

        // Put the hips on the seat
        Vector3 seatPoint = seat.position + Quaternion.Euler(0f, heading, 0f) * riderOffset;
        Vector3 hipsOffset = hips.position - player.position;
        Vector3 seatedRoot = seatPoint - hipsOffset;
        float jump = Mathf.Sin(k * Mathf.PI) * mountJumpHeight;
        // The rider bounces up on the saddle between the hoof impacts
        float bounce = ByGait(riderBounce, Gait) * bounceAmount * BounceShape * (mounting || dismounting ? 0f : 1f);
        player.position = Vector3.Lerp(groundPosition, seatedRoot, k) + Vector3.up * (jump + bounce);

        if (dismounting && mountBlend <= 0f) FinishDismount(groundPosition);
    }

    // Legs around the horse, hands forward on the reins, leaning forward at the gallop (in world space, works with any humanoid)
    void PoseRider(float weight)
    {
        if (weight <= 0f) return;
        Vector3 fwd = player.forward, right = player.right, down = -player.up;

        float lean = gallopLean * Mathf.InverseLerp(trotSpeed, gallopSpeed, speed);
        if (spine != null && lean > 0.01f) spine.rotation = Quaternion.AngleAxis(lean * weight, right) * spine.rotation;

        PoseLeg(leftUpperLeg, leftLowerLeg, leftFoot, -1f, fwd, right, down, weight);
        PoseLeg(rightUpperLeg, rightLowerLeg, rightFoot, 1f, fwd, right, down, weight);

        if (reins > 0f)
        {
            float w = weight * reins;
            PoseArm(leftUpperArm, leftLowerArm, leftHand, -1f, fwd, right, down, w);
            PoseArm(rightUpperArm, rightLowerArm, rightHand, 1f, fwd, right, down, w);
        }
    }

    void PoseLeg(Transform upper, Transform lower, Transform foot, float side, Vector3 fwd, Vector3 right, Vector3 down, float weight)
    {
        if (upper == null || lower == null || foot == null) return;
        Vector3 thigh = Quaternion.AngleAxis(side * legSpread, fwd) * Quaternion.AngleAxis(-thighForward, right) * down;
        Aim(upper, lower.position - upper.position, thigh, weight);
        Vector3 shin = Quaternion.AngleAxis(side * legSpread * 0.3f, fwd) * Quaternion.AngleAxis(shinBack, right) * down;
        Aim(lower, foot.position - lower.position, shin, weight);
    }

    void PoseArm(Transform upper, Transform lower, Transform hand, float side, Vector3 fwd, Vector3 right, Vector3 down, float weight)
    {
        if (upper == null || lower == null || hand == null) return;
        Vector3 arm = (down * 0.85f + fwd * 0.45f - right * side * 0.1f).normalized;
        Aim(upper, lower.position - upper.position, arm, weight);
        Vector3 forearm = (fwd * 0.9f + down * 0.35f - right * side * 0.35f).normalized;
        Aim(lower, hand.position - lower.position, forearm, weight);
    }

    static void Aim(Transform bone, Vector3 current, Vector3 wanted, float weight)
    {
        if (current.sqrMagnitude < 0.000001f) return;
        Quaternion turn = Quaternion.FromToRotation(current, wanted);
        bone.rotation = Quaternion.Slerp(Quaternion.identity, turn, weight) * bone.rotation;
    }

    // ---------- Animation & sounds ----------

    void UpdateAnimator(float dt)
    {
        // Rhythm of the hooves (also used by the camera and the bounces)
        float gait = hidden ? 0f : GaitOf(speed);
        BouncePhase = Mathf.Repeat(BouncePhase + ByGaitRate(bounceRate, gait) * Mathf.PI * dt, Mathf.PI * 2f);
        bounceAmount = Mathf.Lerp(bounceAmount, Mathf.Clamp01(gait), 1f - Mathf.Exp(-6f * dt));
        Gait = gait;

        float turn = Mathf.Clamp(turnRate / Mathf.Max(1f, fullTurnRate), -1f, 1f);
        if (hidden) turn = 0f;
        turnSmooth = Mathf.SmoothDamp(turnSmooth, turn, ref turnVelocity, Mathf.Max(0.01f, turnDamping), Mathf.Infinity, Mathf.Max(dt, 0.0001f));

        if (animator == null || !animator.enabled) return;
        animator.speed = animationSpeed * ByGaitRate(gaitAnimationSpeed, gait);
        if (hasTurnParam) animator.SetFloat(turnHash, turnSmooth);
        if (!hasSpeedParam) return;
        if (speedInMetersPerSecond)
        {
            animator.SetFloat(speedHash, startingUp != 0 ? startingUp * walkSpeed * 0.7f : speed, 0.12f, dt);
            return;
        }

        float value;
        if (speed <= walkSpeed) value = speed / Mathf.Max(0.01f, walkSpeed);
        else if (speed <= trotSpeed) value = 1f + (speed - walkSpeed) / Mathf.Max(0.01f, trotSpeed - walkSpeed);
        else value = 2f + Mathf.Clamp01((speed - trotSpeed) / Mathf.Max(0.01f, gallopSpeed - trotSpeed));

        if (speed < 0f) value = Mathf.Max(-1f, speed / Mathf.Max(0.01f, backSpeed));
        // Turning on the spot: step with the walk
        if (Mathf.Abs(speed) < walkSpeed * 0.5f && Mathf.Abs(turnRate) > 25f) value = Mathf.Max(value, 0.6f);
        if (startingUp != 0) value = startingUp * 0.7f; // the walk starts before the body moves
        Gait = Mathf.Max(0f, value);
        animator.SetFloat(speedHash, value, 0.12f, dt);
    }

    void UpdateHoofSounds(float dt)
    {
        if (hoofSounds == null || hoofSounds.Length == 0 || hidden || speed < 0.3f) { stepDistance = 0f; return; }
        stepDistance += speed * dt;
        float stepLength = speed <= walkSpeed + 0.2f ? 0.7f : speed <= trotSpeed + 0.5f ? 1.1f : 2.2f;
        if (stepDistance < stepLength) return;
        stepDistance = 0f;
        AudioClip clip = hoofSounds[Random.Range(0, hoofSounds.Length)];
        if (clip == null) return;
        audioSource.pitch = Random.Range(0.92f, 1.08f);
        audioSource.PlayOneShot(clip, Volume() * hoofVolume * Mathf.Lerp(0.6f, 1f, speed / gallopSpeed));
    }

    static float Volume()
    {
        SoundManager m = SoundManager.Instance;
        return (m != null ? m.masterVolume * m.effectsVolume : 1f) * GameSettings.Effects;
    }

    // ---------- UI ----------

    void UpdatePrompt(bool hasPlayer)
    {
        if (promptText == null) return;
        bool showMount = false, showRide = false;
        if (hasPlayer && !hidden)
        {
            if (Ridden == this) showRide = !mounting && !dismounting;
            else if (Ridden == null && !PlayerMounted && CanAct() && HorseOwnership.Allows(this))
            {
                float d = Flat((seat != null ? seat.position : transform.position) - player.position).magnitude;
                showMount = d <= mountRange;
                if (showMount && Input.GetKeyDown(mountKey) && !ScreenFade.IsActive) { TryMount(); showMount = false; }
            }
        }
        // The riding hint (dismount / trot / gallop) shows for a few seconds after mounting and then fades away
        float alpha = 1f;
        if (showRide)
        {
            if (!wasRidePrompt) ridePromptSince = Time.time;
            if (ridePromptSeconds == 0f) alpha = 0f;
            else if (ridePromptSeconds > 0f)
                alpha = 1f - Mathf.Clamp01((Time.time - ridePromptSince - ridePromptSeconds) / Mathf.Max(0.01f, ridePromptFade));
        }
        wasRidePrompt = showRide;
        if (promptText.alpha != alpha) promptText.alpha = alpha;

        bool show = showMount || (showRide && alpha > 0.001f);
        if (promptText.gameObject.activeSelf != show) promptText.gameObject.SetActive(show);
        if (!show) return;
        string text = showRide ? ridePrompt : mountPrompt;
        if (promptText.text != text) promptText.text = text;
    }

    void UpdateStaminaBar()
    {
        if (staminaBar == null) return;
        bool show = showStaminaBar && Ridden == this && stamina < maxStamina - 0.01f;
        if (staminaBar.activeSelf != show) staminaBar.SetActive(show);
        if (!show) return;
        staminaFill.anchorMax = new Vector2(Mathf.Clamp01(stamina / Mathf.Max(1f, maxStamina)), 1f);
    }

    void ShowMessage(string text, float seconds = 2.5f)
    {
        if (messageText == null) return;
        messageText.text = text;
        messageUntil = Time.time + seconds;
    }

    void BuildUI()
    {
        mountPrompt = $"[{mountKey}] Mount";
        ridePrompt = $"[{mountKey}] Dismount   [{runKey}] Trot / hold to Gallop";

        GameObject canvasObject = UIKit.CreateCanvas($"HorseUI ({name})", null, 140);
        Transform canvas = canvasObject.transform;

        promptText = UIKit.CornerText("HorsePrompt", canvas, 1, true);
        messageText = UIKit.CornerText("HorseMessage", canvas, 1);

        // Stamina bar: bottom center, only while it is not full
        staminaBar = UIKit.CreateImage("HorseStamina", canvas, new Color(0f, 0f, 0f, 0.55f));
        RectTransform barRect = staminaBar.GetComponent<RectTransform>();
        barRect.anchorMin = barRect.anchorMax = barRect.pivot = new Vector2(0.5f, 0f);
        barRect.anchoredPosition = new Vector2(0f, 70f);
        barRect.sizeDelta = new Vector2(260f, 8f);
        staminaBar.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;

        GameObject fill = UIKit.CreateImage("Fill", staminaBar.transform, staminaColor);
        staminaFill = fill.GetComponent<RectTransform>();
        UIKit.SetRect(staminaFill, Vector2.zero, Vector2.one, new Vector2(1f, 1f), new Vector2(-1f, -1f));
        fill.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        staminaBar.SetActive(false);
    }
}
