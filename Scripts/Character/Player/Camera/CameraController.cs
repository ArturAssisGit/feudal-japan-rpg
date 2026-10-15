using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("Target Tracking")]
    public Transform target;
    public Vector3 targetOffset = new Vector3(0, 1.5f, 0); // Aim for the chest/head, not the feet

    [Header("Camera Settings")]
    public float distance = 2.5f;
    public float mouseSensitivityX = 2f;
    public float mouseSensitivityY = 2f;

    [Header("Combat Zoom")]
    public float combatDistance = 1.8f;
    [Tooltip("X = right, Y = up (relative to the camera)")] public Vector2 combatOffset = new Vector2(0.5f, 0f);
    public float zoomSpeed = 4f;

    [Header("Dynamic Combat Camera (Ghost of Tsushima style)")]
    [Tooltip("With the sword drawn the player can move around inside a zone of the screen before the camera follows. Off = the old rigid camera")] public bool dynamicCombatCamera = true;
    [Tooltip("No enemy nearby (sword drawn): how much closer than the normal camera (meters)")] public float calmCloser = 0.3f;
    [Tooltip("No enemy nearby (sword drawn): X = how far the player sits to the left of the center (meters), Y = up")] public Vector2 calmOffset = new Vector2(0.5f, 0f);
    [Tooltip("Enemies closer than this (meters) make the camera open up for combat")] public float enemyRange = 12f;
    [Tooltip("How fast the camera opens/closes when enemies appear/leave (higher = faster)")] public float openSpeed = 1.2f;
    [Tooltip("Extra camera distance with the sword drawn (meters)")] public float combatExtraDistance = 0.5f;
    [Tooltip("Raises the camera with the sword drawn (meters). The player then sits lower on the screen")] public float combatHeight = 0.6f;
    [Tooltip("Sideways free zone: how far (meters) the player can go left/right from the camera's center before it follows")] public float deadZoneSide = 1.1f;
    [Tooltip("Forward/back free zone: how far (meters) the player can go towards/away from the camera before it follows")] public float deadZoneDepth = 1.0f;
    [Tooltip("Slow floating movement of the camera while the sword is drawn (meters). 0 = none")] public float swayAmount = 0.1f;
    [Tooltip("Speed of the floating movement")] public float swaySpeed = 0.5f;
    [Tooltip("How slowly the camera catches up with the player (seconds). Higher = heavier")] public float catchUpTime = 0.35f;
    [Tooltip("In a fight the camera also drifts a little after the player, even inside the free zone (0 = stays still until he leaves the zone, 1 = glued to him, above 1 = tighter and faster still, up to 3)")] [Range(0f, 3f)] public float combatFollow = 0.4f;

    [Header("Combat Camera (normal enemies): closer, cinematic, soft lock")]
    [Tooltip("With the sword drawn, the camera gently keeps the enemy closest to the middle of the screen in view")] public bool softLock = true;
    [Tooltip("Enemies further than this (meters) are never picked")] public float softLockRange = 14f;
    [Tooltip("How close to the middle of the screen an enemy must be to get picked (0.1 = very close to the center, 0.5 = anywhere on screen)")] [Range(0.05f, 0.5f)] public float softLockPickRadius = 0.22f;
    [Tooltip("The picked enemy is let go when it gets this far from the middle of the screen")] [Range(0.1f, 1f)] public float softLockReleaseRadius = 0.6f;
    [Tooltip("Another enemy must be this much closer to the middle to steal the lock (stops it jumping between enemies)")] [Range(0f, 0.3f)] public float softLockSwitchMargin = 0.08f;
    [Tooltip("How strongly the camera turns towards the enemy. 0 = not at all, 1 = fully locked (the boss uses 0.9). Small values feel like a light assist")] [Range(0f, 1f)] public float softLockStrength = 0.3f;
    [Tooltip("How fast it turns towards the enemy (higher = snappier)")] public float softLockTurnSpeed = 3f;
    [Tooltip("Seconds the assist pauses after you move the mouse (so you are always in control)")] public float softLockMousePause = 0.35f;
    [Tooltip("Camera distance in a fight (meters). Smaller = closer")] public float softLockDistance = 1.85f;
    [Tooltip("X = right (over the shoulder), Y = up (meters)")] public Vector2 softLockOffset = new Vector2(0.55f, 0.05f);
    [Tooltip("Raises the camera in a fight (meters)")] public float softLockHeight = 0.2f;
    [Tooltip("Camera pitch the assist leans towards (degrees, positive = looking down)")] public float softLockPitch = 9f;
    [Tooltip("Changes the field of view in a fight (degrees). Negative = tighter, more cinematic")] public float softLockFov = -5f;
    [Tooltip("Seconds to blend into / out of the combat camera")] public float softLockBlendTime = 0.5f;

    [Header("Boss Camera (only while fighting a boss: tick Is Boss on the enemy)")]
    public bool bossCamera = true;
    [Tooltip("The camera starts locking onto the boss when you are closer than this (meters)")] public float bossEnterDistance = 12f;
    [Tooltip("Fleeing further than this from the boss (meters) goes back to the normal combat camera. Must be bigger than Enter Distance")] public float bossExitDistance = 20f;
    [Tooltip("Also go back to the normal camera when you are further than the boss's Max Chase Distance from where it started")] public bool exitAtChaseLimit = true;
    [Tooltip("Camera distance during the boss fight (meters). Smaller = closer")] public float bossDistance = 2.2f;
    [Tooltip("X = right, Y = up (meters)")] public Vector2 bossOffset = new Vector2(0.45f, 0.1f);
    [Tooltip("Raises the camera a bit (meters)")] public float bossHeight = 0.25f;
    [Tooltip("How strongly the camera turns to keep the boss in view. 0 = free camera, 1 = fully locked")] [Range(0f, 1f)] public float lockOnStrength = 0.9f;
    [Tooltip("How fast it turns towards the boss (higher = more responsive)")] public float lockOnTurnSpeed = 7f;
    [Tooltip("Mouse sensitivity multiplier while locked (1 = normal). Lower = you can only look around a little")] [Range(0.05f, 1f)] public float lockedMouseFactor = 0.4f;
    [Tooltip("Camera pitch while locked (degrees, positive = looking down)")] public float bossPitch = 12f;
    [Tooltip("How fast the camera follows the player (higher = tighter, more responsive)")] public float bossFollowSpeed = 12f;
    [Tooltip("How fast it zooms in (higher = faster)")] public float bossZoomSpeed = 6f;
    [Tooltip("Seconds to blend in/out of the boss camera")] public float bossBlendTime = 0.6f;

    [Header("Finisher Camera")]
    [Tooltip("During a finisher the camera zooms in close, then goes back to normal")] public bool finisherCamera = true;
    [Tooltip("Camera distance during the finisher (meters)")] public float finisherDistance = 1.2f;
    [Tooltip("X = right, Y = up during the finisher (meters)")] public Vector2 finisherOffset = new Vector2(0.45f, 0.05f);
    [Tooltip("How fast it zooms in (higher = faster)")] public float finisherZoomInSpeed = 5f;
    [Tooltip("How fast it goes back to the normal camera (higher = faster)")] public float finisherZoomOutSpeed = 2f;

    [Header("Bow Aim Zoom")]
    public float aimDistance = 1.4f;
    public Vector2 aimOffset = new Vector2(0.7f, 0.15f);

    [Header("Scenic View (look at the landscape)")]
    [Tooltip("Press to zoom the camera far out, press again to come back")] public KeyCode scenicViewKey = KeyCode.CapsLock;
    public float scenicDistance = 7f;
    [Tooltip("Raises the camera a bit to see more of the landscape")] public float scenicHeight = 1f;
    [Tooltip("How fast the scenic zoom happens")] public float scenicZoomSpeed = 6f;
    [Tooltip("Black bars at the top and bottom of the screen in scenic view (cinema look), on foot and on the horse")] public bool scenicBars = true;
    [Tooltip("Height of EACH bar (part of the screen: 0.1 = 10% at the top and 10% at the bottom)")] [Range(0f, 0.3f)] public float scenicBarSize = 0.1f;
    [Tooltip("Seconds the bars take to slide in / out")] public float scenicBarTime = 0.8f;

    [Header("Scenic View on the Horse (separate from the one on foot)")]
    [Tooltip("Camera distance in scenic view while riding (meters)")] public float horseScenicDistance = 7f;
    [Tooltip("X = right, Y = up in scenic view while riding (meters). Added on top of the horse height")] public Vector2 horseScenicOffset = new Vector2(0f, 1f);
    [Tooltip("How fast the scenic zoom happens while riding")] public float horseScenicZoomSpeed = 6f;
    [Tooltip("Extra field of view in scenic view while riding (degrees). 0 = same as normal, 10 = wider landscape")] public float horseScenicFov = 0f;
    [Tooltip("Hoof bounce of the camera in scenic view (1 = same as normal riding, 0 = smooth, no bounce)")] [Range(0f, 1f)] public float horseScenicBounce = 1f;
    [Tooltip("In scenic view the camera still turns behind the horse when you leave the mouse alone")] public bool horseScenicAutoCenter = true;

    [Header("Horse")]
    [Tooltip("Camera distance while riding (meters)")] public float mountDistance = 4.2f;
    [Tooltip("Raises the camera while riding (meters)")] public float mountHeight = 0.6f;
    [Tooltip("How softly the camera follows the rider (seconds). Higher = smoother, less shaking with the gallop")] public float mountFollowTime = 0.08f;
    [Tooltip("While riding and not moving the mouse, the camera slowly turns behind the horse")] public bool mountAutoCenter = true;
    [Tooltip("Seconds without moving the mouse before it starts turning behind the horse")] public float autoCenterDelay = 1.5f;
    [Tooltip("How fast it turns behind the horse")] public float autoCenterSpeed = 1.2f;
    [Tooltip("Camera bounce with the hooves (meters): X = walk, Y = trot, Z = gallop")] public Vector3 mountBob = new Vector3(0.012f, 0.055f, 0.04f);
    [Tooltip("The camera also nods a little with each impact (degrees per 0.1 m of bounce)")] public float mountBobTilt = 1.5f;

    [Header("Vertical Limits")]
    public float minYAngle = -15f; // Look up limit
    public float maxYAngle = 60f;  // Look down limit

    [Header("Camera Shake (hits)")]
    public bool enableShake = true;
    [Tooltip("Multiplies every shake (0.5 = half as strong)")] public float shakeMultiplier = 1f;

    // The boss the camera is locked onto right now (null = normal camera). PlayerMovement uses it to strafe around the boss
    public static Transform LockedBoss { get; private set; }
    // The normal enemy the combat camera is keeping in view (null = none). Other scripts can use it (e.g. to aim attacks)
    public static Transform SoftTarget { get; private set; }

    void OnDisable() { LockedBoss = null; SoftTarget = null; }

    private static CameraController instance;
    private static float shakeStrength, shakeDuration, shakeUntil;
    private static bool overrideActive;
    private static Vector3 overridePosition;
    private static Quaternion overrideRotation;

    // Used by SleepSystem: the camera sits at a fixed spot (e.g. looking at the sky) until ClearOverride
    public static void SetOverride(Vector3 position, Quaternion rotation)
    {
        overridePosition = position;
        overrideRotation = rotation;
        overrideActive = true;
    }

    public static void ClearOverride() => overrideActive = false;

    // Call from anywhere: CameraController.Shake(0.1f, 0.15f)
    public static void Shake(float strength, float duration)
    {
        if (instance == null || !instance.enableShake || !GameSettings.CameraShake) return;
        float remaining = shakeUntil - Time.unscaledTime;
        if (remaining > 0f && shakeStrength * (remaining / shakeDuration) > strength) return; // a stronger shake is already playing
        shakeStrength = strength * instance.shakeMultiplier;
        shakeDuration = Mathf.Max(0.01f, duration);
        shakeUntil = Time.unscaledTime + shakeDuration;
    }

    private float currentX = 0f, currentY = 15f, currentDistance;
    private Vector2 currentOffset;
    private PlayerCombat targetCombat;
    private bool scenicView;
    private float scenicWeight, barWeight;
    private GameObject barCanvas;
    private RectTransform barTop, barBottom;
    private Vector3 followPos, smoothVelocity;
    private float swayWeight, easeOutUntil, heightWeight, alert, nextEnemyScan, finisherWeight;
    private bool enemyNearby;
    private bool wasDynamic;
    private EnemyAI_01 boss;
    private float bossWeight, nextBossScan, bossLostTime;
    private float mountWeight, lastMouseTime, bobPhase, bobAmount;
    private EnemyAI_01 softEnemy;
    private float softWeight, nextSoftScan, baseFov = -1f;
    private Camera cam;

    void Awake()
    {
        instance = this;
        shakeUntil = 0f; // values from a previous Play session must not carry over
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        shakeStrength = shakeDuration = shakeUntil = 0f;
        overrideActive = false;
        LockedBoss = null;
        SoftTarget = null;
    }

    // Smooth random offset that fades out (uses real time, so it also works during hit-stop)
    Vector3 ShakeOffset(Quaternion rotation)
    {
        float remaining = shakeUntil - Time.unscaledTime;
        if (remaining <= 0f || shakeDuration <= 0f) return Vector3.zero;
        if (remaining > shakeDuration) { shakeUntil = 0f; return Vector3.zero; } // leftover from an old session: ignore it
        float k = Mathf.Clamp01(remaining / shakeDuration);
        float t = Time.unscaledTime * 30f;
        Vector3 noise = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, 0f) * 2f;
        return rotation * noise * shakeStrength * k;
    }

    void Start()
    {
        // Lock the cursor to the center of the screen and hide it
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        currentDistance = distance;
    }

    // Combat camera: the player moves freely inside a dead zone; the camera only follows (slowly) when he leaves it
    void UpdateDynamic(bool dynamic)
    {
        Vector3 pos = target.position;
        if ((followPos - pos).sqrMagnitude > 25f)
        {
            followPos = pos;
            smoothVelocity = Vector3.zero;
            return;
        }
        if (!dynamic)
        {
            if (wasDynamic) easeOutUntil = Time.time + 0.6f;
            wasDynamic = false;
            // Riding: a soft follow, so the gallop doesn't shake the camera
            if (HorseMount.PlayerMounted) { followPos = Vector3.SmoothDamp(followPos, pos, ref smoothVelocity, mountFollowTime); return; }
            // Normal camera: glued to the player. Only right after leaving combat it eases back
            if (Time.time < easeOutUntil) followPos = Vector3.SmoothDamp(followPos, pos, ref smoothVelocity, 0.15f);
            else { followPos = pos; smoothVelocity = Vector3.zero; }
            return;
        }
        wasDynamic = true;

        Quaternion yaw = Quaternion.Euler(0f, currentX, 0f);
        Vector3 right = yaw * Vector3.right, forward = yaw * Vector3.forward;
        Vector3 delta = pos - followPos;
        float x = Vector3.Dot(delta, right), z = Vector3.Dot(delta, forward);
        float follow = combatFollow * alert;
        float extra = 1f + Mathf.Max(0f, follow - 1f) * 2f; // above 1: smaller free zones and a faster catch up
        float exX = Mathf.Sign(x) * Mathf.Max(0f, Mathf.Abs(x) - deadZoneSide * alert / extra);
        float exZ = Mathf.Sign(z) * Mathf.Max(0f, Mathf.Abs(z) - deadZoneDepth * alert / extra);
        Vector3 goal = followPos + right * exX + forward * exZ;
        goal.y = pos.y;
        goal = Vector3.Lerp(goal, pos, Mathf.Min(1f, follow)); // gentle drift after the player while fighting
        followPos = Vector3.SmoothDamp(followPos, goal, ref smoothVelocity, Mathf.Max(0.01f, Mathf.Lerp(0.12f, catchUpTime, alert) / extra));
    }

    // Is there a living enemy close enough to make the camera open up? (checked 4 times a second)
    void ScanEnemies(Vector3 playerPos)
    {
        if (Time.unscaledTime < nextEnemyScan) return;
        nextEnemyScan = Time.unscaledTime + 0.25f;
        enemyNearby = false;
        foreach (EnemyAI_01 e in EnemyAI_01.All)
            if (!e.IsDead && (e.transform.position - playerPos).sqrMagnitude <= enemyRange * enemyRange) { enemyNearby = true; break; }
    }

    // Boss fight: lock when the boss is chasing us and close, release when he dies, gives up, or we flee far away
    bool BossFight(Vector3 playerPos)
    {
        if (!bossCamera) { boss = null; return false; }
        if (boss != null)
        {
            bool alive = boss.gameObject.activeInHierarchy && !boss.IsDead;
            float d = Vector3.Distance(boss.transform.position, playerPos);
            bool tooFar = d > Mathf.Max(bossExitDistance, bossEnterDistance + 1f);
            if (exitAtChaseLimit && Vector3.Distance(playerPos, boss.HomePosition) > boss.maxChaseDistance) tooFar = true;
            bossLostTime = boss.IsInCombat ? 0f : bossLostTime + Time.unscaledDeltaTime;
            if (!alive || tooFar || bossLostTime > 1.5f) { boss = null; nextBossScan = Time.unscaledTime + 0.5f; }
            return boss != null;
        }
        if (Time.unscaledTime < nextBossScan) return false;
        nextBossScan = Time.unscaledTime + 0.25f;
        float best = bossEnterDistance * bossEnterDistance;
        foreach (EnemyAI_01 e in EnemyAI_01.All)
        {
            if (!e.isBoss || e.IsDead || !e.IsInCombat) continue;
            if (exitAtChaseLimit && Vector3.Distance(playerPos, e.HomePosition) > e.maxChaseDistance) continue;
            float sq = (e.transform.position - playerPos).sqrMagnitude;
            if (sq <= best) { best = sq; boss = e; bossLostTime = 0f; }
        }
        return boss != null;
    }

    // Combat camera: the enemy closest to the middle of the screen (an invisible crosshair) is kept in view.
    // Sticky: it stays picked until it leaves the screen area, dies or gets far; another one must be clearly more central to take over
    void UpdateSoftTarget(bool active, Vector3 playerPos)
    {
        if (!active || cam == null) { softEnemy = null; return; }

        float currentScore = float.MaxValue;
        if (softEnemy != null)
        {
            bool valid = softEnemy.gameObject.activeInHierarchy && !softEnemy.IsDead && !softEnemy.isBoss &&
                         (softEnemy.transform.position - playerPos).sqrMagnitude <= (softLockRange + 3f) * (softLockRange + 3f);
            currentScore = valid ? ScreenScore(softEnemy) : float.MaxValue;
            if (currentScore > softLockReleaseRadius) { softEnemy = null; currentScore = float.MaxValue; }
        }

        if (Time.unscaledTime < nextSoftScan) return;
        nextSoftScan = Time.unscaledTime + 0.1f;

        EnemyAI_01 best = null;
        float bestScore = softLockPickRadius;
        float rangeSqr = softLockRange * softLockRange;
        foreach (EnemyAI_01 e in EnemyAI_01.All)
        {
            if (e == null || e == softEnemy || e.IsDead || e.isBoss) continue;
            if ((e.transform.position - playerPos).sqrMagnitude > rangeSqr) continue;
            float score = ScreenScore(e);
            if (score < bestScore) { bestScore = score; best = e; }
        }
        if (best != null && (softEnemy == null || bestScore < currentScore - softLockSwitchMargin)) softEnemy = best;
    }

    // Distance of the enemy's chest from the middle of the screen (0 = dead center); behind the camera = never
    float ScreenScore(EnemyAI_01 e)
    {
        Vector3 v = cam.WorldToViewportPoint(e.transform.position + Vector3.up * 1.2f);
        if (v.z <= 0.5f) return float.MaxValue;
        float dx = (v.x - 0.5f) * cam.aspect, dy = v.y - 0.5f;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    // The player can be created after the scene (menu -> New Game) or replaced: find it again when the target is gone
    void FindTarget()
    {
        if (target != null && target.gameObject.activeInHierarchy) return;
        if (Time.unscaledTime < nextTargetSearch) return;
        nextTargetSearch = Time.unscaledTime + 0.5f;

        GameObject found = PlayerRef.Object;
        if (found == null)
        {
            PlayerStats stats = FindAnyObjectByType<PlayerStats>();
            if (stats != null) found = stats.gameObject;
        }
        if (found == null || found.transform == target) return;

        target = found.transform;
        targetCombat = null;
        followPos = target.position;
        smoothVelocity = Vector3.zero;
    }
    private float nextTargetSearch;

    void LateUpdate()
    {
        FindTarget();
        if (target == null) return;
        // 1. Get mouse input
        bool menuOpen = PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.IsOpen;
        if (!menuOpen)
        {
            float mouseK = Mathf.Lerp(1f, lockedMouseFactor, bossWeight);
            float mouseX = Input.GetAxis("Mouse X"), mouseY = Input.GetAxis("Mouse Y");
            if (Mathf.Abs(mouseX) + Mathf.Abs(mouseY) > 0.01f) lastMouseTime = Time.unscaledTime;
            float userK = GameSettings.MouseSensitivity; // Settings > Controls
            currentX += mouseX * mouseSensitivityX * mouseK * userK;
            currentY -= mouseY * mouseSensitivityY * mouseK * userK * (GameSettings.InvertY ? -1f : 1f);
            if (Input.GetKeyDown(scenicViewKey)) scenicView = !scenicView;
        }

        // 2. Clamp vertical rotation (no flipping upside down)
        currentY = Mathf.Clamp(currentY, minYAngle, maxYAngle);

        // 3. Calculate new rotation and position
        if (targetCombat == null) targetCombat = target.GetComponentInParent<PlayerCombat>();
        if (targetCombat == null) targetCombat = target.GetComponentInChildren<PlayerCombat>();
        bool armed = targetCombat != null && targetCombat.IsArmed;
        bool aiming = targetCombat != null && targetCombat.IsAiming;
        if (armed) scenicView = false; // drawing a weapon brings the camera back for combat

        bool dynamic = dynamicCombatCamera && armed && !aiming && !scenicView && !overrideActive;
        if (dynamic) ScanEnemies(target.position);
        alert = Mathf.MoveTowards(alert, dynamic && enemyNearby ? 1f : 0f, openSpeed * Time.deltaTime);
        UpdateDynamic(dynamic);

        // Boss camera: blend weight + lock-on (real time based blend, so it also works in hit-stop)
        bool bossOn = BossFight(target.position) && !scenicView && !overrideActive && !(targetCombat != null && aiming);

        // Normal enemies: closer camera + soft lock on the enemy in the middle of the screen
        if (cam == null) cam = GetComponent<Camera>();
        if (cam != null && baseFov < 0f) baseFov = cam.fieldOfView;
        UpdateSoftTarget(softLock && dynamic && !bossOn && !menuOpen && !HorseMount.PlayerMounted, target.position);
        softWeight = Mathf.MoveTowards(softWeight, softEnemy != null ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, softLockBlendTime));
        float sw = Mathf.SmoothStep(0f, 1f, softWeight);
        SoftTarget = softEnemy != null ? softEnemy.transform : null;
        if (softEnemy != null && sw > 0f && Time.unscaledTime - lastMouseTime > softLockMousePause)
        {
            Vector3 toEnemy = softEnemy.transform.position - followPos;
            float flat = new Vector2(toEnemy.x, toEnemy.z).magnitude;
            float k = (1f - Mathf.Exp(-softLockTurnSpeed * Time.unscaledDeltaTime)) * sw * softLockStrength;
            if (flat > 0.5f) currentX = Mathf.LerpAngle(currentX, Mathf.Atan2(toEnemy.x, toEnemy.z) * Mathf.Rad2Deg, k);
            float wantPitch = softLockPitch - Mathf.Atan2(toEnemy.y, Mathf.Max(flat, 1f)) * Mathf.Rad2Deg * 0.5f;
            currentY = Mathf.Lerp(currentY, Mathf.Clamp(wantPitch, minYAngle, maxYAngle), k * 0.5f);
        }
        bossWeight = Mathf.MoveTowards(bossWeight, bossOn ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, bossBlendTime));
        float bw = Mathf.SmoothStep(0f, 1f, bossWeight);
        LockedBoss = (bossOn && boss != null && bossWeight > 0.3f) ? boss.transform : null;
        if (bw > 0f && boss != null)
        {
            Vector3 toBoss = boss.transform.position - target.position;
            float flat = new Vector2(toBoss.x, toBoss.z).magnitude;
            float k = 1f - Mathf.Exp(-lockOnTurnSpeed * Time.unscaledDeltaTime);
            k *= bw * lockOnStrength;
            if (flat > 0.3f)
            {
                float wantYaw = Mathf.Atan2(toBoss.x, toBoss.z) * Mathf.Rad2Deg;
                currentX = Mathf.LerpAngle(currentX, wantYaw, k);
            }
            // A bit more pitch if the boss is below, less if he is above
            float wantPitch = bossPitch - Mathf.Atan2(toBoss.y, Mathf.Max(flat, 1f)) * Mathf.Rad2Deg * 0.5f;
            currentY = Mathf.Lerp(currentY, Mathf.Clamp(wantPitch, minYAngle, maxYAngle), k);
            // Tight follow: the player stays well framed while running around the boss
            followPos = Vector3.Lerp(followPos, target.position, bw * (1f - Mathf.Exp(-bossFollowSpeed * Time.unscaledDeltaTime)));
        }

        // Riding weight (0 = on foot, 1 = on the horse), blends smoothly when you mount / dismount
        mountWeight = Mathf.MoveTowards(mountWeight, HorseMount.PlayerMounted ? 1f : 0f, 1.5f * Time.deltaTime);
        float mw = Mathf.SmoothStep(0f, 1f, mountWeight);

        // Scenic view: on foot and on the horse each have their own settings
        float scenicDist = Mathf.Lerp(scenicDistance, horseScenicDistance, mw);
        Vector2 scenicOff = Vector2.Lerp(new Vector2(0f, scenicHeight), horseScenicOffset, mw);
        float scenicSpeed = Mathf.Lerp(scenicZoomSpeed, horseScenicZoomSpeed, mw);
        scenicWeight = Mathf.MoveTowards(scenicWeight, scenicView ? 1f : 0f, Time.deltaTime / 0.6f);

        float blend = (scenicView ? scenicSpeed : zoomSpeed) * Time.deltaTime;
        blend = Mathf.Min(1f, Mathf.Lerp(blend, bossZoomSpeed * Time.deltaTime, bw));
        float wantedDistance = scenicView ? scenicDist : aiming ? aimDistance : armed ? combatDistance : distance;
        Vector2 wantedOffset = scenicView ? scenicOff : aiming ? aimOffset : armed ? combatOffset : Vector2.zero;
        if (dynamic)
        {
            // No enemy: almost the normal camera, a bit closer, player to the left. Enemy near: opens up
            wantedDistance = Mathf.Lerp(distance - calmCloser, combatDistance + combatExtraDistance, alert);
            wantedOffset = Vector2.Lerp(calmOffset, combatOffset, alert);
        }
        // In a fight with a normal enemy: closer, over the shoulder
        wantedDistance = Mathf.Lerp(wantedDistance, softLockDistance, sw);
        wantedOffset = Vector2.Lerp(wantedOffset, softLockOffset, sw);
        // Riding: further away and a bit higher; turns behind the horse when the mouse is left alone
        if (mw > 0f && !scenicView) wantedDistance = Mathf.Lerp(wantedDistance, mountDistance, mw);
        HorseMount horse = HorseMount.Ridden;
        if (mountAutoCenter && (!scenicView || horseScenicAutoCenter) && horse != null && horse.CurrentSpeed > 1f && !menuOpen && Time.unscaledTime - lastMouseTime > autoCenterDelay)
            currentX = Mathf.LerpAngle(currentX, horse.Heading, 1f - Mathf.Exp(-autoCenterSpeed * Mathf.InverseLerp(1f, 5f, horse.CurrentSpeed) * Time.deltaTime));
        wantedDistance = Mathf.Lerp(wantedDistance, bossDistance, bw);
        wantedOffset = Vector2.Lerp(wantedOffset, bossOffset, bw);
        currentDistance = Mathf.Lerp(currentDistance, wantedDistance, blend);
        currentOffset = Vector2.Lerp(currentOffset, wantedOffset, blend);

        // Finisher: zoom in close (real time, so the slow motion doesn't slow the zoom), then smoothly back
        bool finisher = finisherCamera && PlayerFinisher.IsActive;
        float fSpeed = finisher ? finisherZoomInSpeed : finisherZoomOutSpeed;
        finisherWeight = Mathf.MoveTowards(finisherWeight, finisher ? 1f : 0f, fSpeed * Time.unscaledDeltaTime);
        float fw = Mathf.SmoothStep(0f, 1f, finisherWeight);
        float useDistance = Mathf.Lerp(currentDistance, finisherDistance, fw);
        Vector2 useOffset = Vector2.Lerp(currentOffset, finisherOffset, fw);

        Quaternion rotation = Quaternion.Euler(currentY, currentX, 0);
        Vector3 sideOffset = rotation * new Vector3(useOffset.x, useOffset.y, 0f);
        heightWeight = Mathf.MoveTowards(heightWeight, dynamic ? 1f : 0f, 2f * Time.deltaTime);
        Vector3 position = followPos + targetOffset + Vector3.up * (mountHeight * mw + Mathf.Lerp(combatHeight * heightWeight * alert, softLockHeight, sw) * (1f - fw) * (1f - bw) + bossHeight * bw * (1f - fw)) + sideOffset - (rotation * Vector3.forward * useDistance);

        // Slow floating while in combat (fades in and out)
        swayWeight = Mathf.MoveTowards(swayWeight, dynamic ? 1f : 0f, 2f * Time.deltaTime);
        if (swayWeight > 0f && swayAmount > 0f)
        {
            float t = Time.time * swaySpeed;
            Vector3 sway = new Vector3(Mathf.PerlinNoise(t, 11f) - 0.5f, Mathf.PerlinNoise(23f, t) - 0.5f, 0f) * 2f * swayAmount * swayWeight * Mathf.Lerp(0.5f, 1f, alert) * (1f - bw * 0.7f);
            position += rotation * sway;
        }

        // Riding: up/down "tac tac" with the hooves (sharp dip at each impact, rounded at the top)
        if (horse != null && mw > 0f)
        {
            float g = horse.Gait;
            float amp = g <= 1f ? mountBob.x * g : g <= 2f ? Mathf.Lerp(mountBob.x, mountBob.y, g - 1f) : Mathf.Lerp(mountBob.y, mountBob.z, Mathf.Clamp01(g - 2f));
            amp *= Mathf.Lerp(1f, horseScenicBounce, scenicWeight);
            bobAmount = Mathf.Lerp(bobAmount, amp * mw, 1f - Mathf.Exp(-6f * Time.deltaTime));
            bobPhase = horse.BouncePhase; // same rhythm as the horse and the rider
            float dip = -bobAmount * (1f - Mathf.Abs(Mathf.Sin(bobPhase)));
            position += Vector3.up * dip;
            rotation = rotation * Quaternion.Euler(-dip * 10f * mountBobTilt, 0f, 0f);
        }
        else bobAmount = 0f;

        // Tighter field of view in a fight (cinematic)
        if (cam != null && baseFov > 0f) cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, (GameSettings.Fov > 0f ? GameSettings.Fov : baseFov) + softLockFov * sw + horseScenicFov * mw * Mathf.SmoothStep(0f, 1f, scenicWeight), 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));

        // 4. Apply to the camera
        transform.position = position + ShakeOffset(rotation);
        transform.rotation = rotation;

        if (overrideActive) transform.SetPositionAndRotation(overridePosition, overrideRotation);

        UpdateScenicBars(scenicBars && GameSettings.ScenicBars && scenicView && !overrideActive);
    }

    // ---------- Scenic view black bars ----------

    // The top bar slides down and the bottom bar slides up (smooth in and out)
    void UpdateScenicBars(bool show)
    {
        barWeight = Mathf.MoveTowards(barWeight, show ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, scenicBarTime));
        if (barCanvas == null)
        {
            if (barWeight <= 0f) return;
            BuildScenicBars();
        }

        float h = Mathf.SmoothStep(0f, 1f, barWeight) * scenicBarSize;
        bool visible = h > 0.0005f;
        if (barCanvas.activeSelf != visible) barCanvas.SetActive(visible);
        if (!visible) return;
        barTop.anchorMin = new Vector2(0f, 1f - h);
        barBottom.anchorMax = new Vector2(1f, h);
    }

    void BuildScenicBars()
    {
        barCanvas = UIKit.CreateCanvas("ScenicBarsCanvas", transform, 30); // under the game windows and messages
        barTop = ScenicBar("Top", new Vector2(0f, 1f), Vector2.one);
        barBottom = ScenicBar("Bottom", Vector2.zero, new Vector2(1f, 0f));
    }

    RectTransform ScenicBar(string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject go = UIKit.CreateImage(name, barCanvas.transform, Color.black);
        go.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        RectTransform rect = go.GetComponent<RectTransform>();
        UIKit.SetRect(rect, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
        return rect;
    }
}
