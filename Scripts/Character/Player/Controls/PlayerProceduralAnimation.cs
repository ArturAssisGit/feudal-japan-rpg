using UnityEngine;

// Procedural touches on top of the animations: foot IK, lean into turns, head look-at, backpack sway, hit jolt, hand on walls.
// Put on the Player; the Animator's Base Layer needs "IK Pass" ticked for Foot IK and Look-at.
public class PlayerProceduralAnimation : MonoBehaviour
{
    [Header("Foot IK (feet on slopes)")]
    public bool footIK = true;
    [Tooltip("Layers the feet can stand on (default: everything)")] public LayerMask groundLayers = ~0;
    [Tooltip("How far above the foot the ground check starts")] public float raycastUp = 0.6f;
    [Tooltip("How far below the foot it looks for ground")] public float raycastDown = 0.8f;
    [Tooltip("Max distance the hips can drop to reach a low foot")] public float maxHipDrop = 0.45f;
    [Tooltip("Foot IK strength while running (it's always full when standing still)")] [Range(0f, 1f)] public float runFootWeight = 0.35f;
    public float footSmoothing = 12f;

    [Header("Lean into turns")]
    public bool lean = true;
    [Tooltip("How much it leans (higher = more)")] public float leanStrength = 0.012f;
    [Tooltip("Maximum lean angle")] public float maxLean = 12f;
    public float leanSmoothing = 6f;

    [Header("Head look-at")]
    public bool lookAt = true;
    [Range(0f, 1f)] public float headWeight = 0.7f;
    [Range(0f, 1f)] public float bodyWeight = 0.1f;
    [Tooltip("Stops looking when the camera points more than this many degrees away from where the character faces")] public float maxLookAngle = 100f;

    [Header("Backpack sway")]
    public bool backpackSway = true;
    [Tooltip("How much the backpack swings")] public float swayStrength = 2.5f;
    public float swaySpring = 60f;
    public float swayDamping = 9f;
    public float maxSway = 10f;
    [Tooltip("How much going up and down (jumps, bouncing on a horse) swings the backpack")] public float verticalSwayStrength = 1.5f;
    [Tooltip("Swing multiplier while riding a horse")] public float mountedSwayMultiplier = 3f;
    [Tooltip("On a horse every hoof beat kicks the backpack (0 = off). Raise it for a wilder swing")] public float mountedBounceKick = 900f;
    [Tooltip("Max backpack swing in degrees while riding")] public float mountedMaxSway = 25f;

    [Header("Hit reaction")]
    public bool hitReaction = true;
    [Tooltip("How far the upper body jolts back when hit")] public float hitAngle = 12f;
    public float hitDuration = 0.35f;

    [Header("Hand on walls")]
    public bool handOnWalls = true;
    [Tooltip("Layers that count as walls (default: everything with a collider)")] public LayerMask wallLayers = ~0;
    [Tooltip("How close a wall must be to the shoulder for the hand to reach it (meters)")] public float wallReach = 0.8f;
    [Tooltip("How strongly the hand goes to the wall at the closest distance")] [Range(0f, 1f)] public float maxHandWeight = 0.85f;
    [Tooltip("Above this speed the hand doesn't touch walls (so it doesn't bounce while running)")] public float maxWallSpeed = 3.5f;
    [Tooltip("How far below the shoulder the hand touches the wall")] public float handHeightOffset = 0.1f;
    [Tooltip("Turns the hand if the palm isn't flat on the wall (try 90 / -90 on one axis)")] public Vector3 handRotationOffset = Vector3.zero;
    public float handSmoothing = 8f;

    private Animator anim;
    private CharacterController controller;
    private PlayerCombat combat;
    private PlayerStats stats;
    private Transform cam;
    private Transform spine, chest;

    private float leftFootOffset, rightFootOffset, hipOffset;
    private float lookWeight, currentLean, lastYaw;
    private Vector3 lastPosition, velocity, lastVelocity;
    private float verticalVelocity, lastVerticalVelocity;

    private GameObject swayObject;
    private Quaternion swayBaseRotation;
    private Vector2 swayAngle, swaySpeed;

    private float lastHealth, hitTimer = -1f;

    private float leftHandWeight, rightHandWeight;
    private Vector3 leftHandTarget, rightHandTarget;
    private Quaternion leftHandRotation = Quaternion.identity, rightHandRotation = Quaternion.identity;

    void Start()
    {
        anim = GetComponentInChildren<Animator>();
        controller = GetComponent<CharacterController>();
        combat = GetComponentInChildren<PlayerCombat>();
        stats = GetComponentInChildren<PlayerStats>();
        if (Camera.main != null) cam = Camera.main.transform;

        if (anim == null || !anim.isHuman)
        {
            Debug.LogWarning("PlayerProceduralAnimation needs a Humanoid Animator on the player.");
            enabled = false;
            return;
        }

        spine = anim.GetBoneTransform(HumanBodyBones.Spine);
        chest = anim.GetBoneTransform(HumanBodyBones.Chest) ?? spine;

        // OnAnimatorIK is only called on the object that has the Animator: a tiny helper forwards it here
        ProceduralIKRelay relay = anim.GetComponent<ProceduralIKRelay>();
        if (relay == null) relay = anim.gameObject.AddComponent<ProceduralIKRelay>();
        relay.target = this;

        lastPosition = transform.position;
        lastYaw = transform.eulerAngles.y;
        if (stats != null) lastHealth = stats.currentHealth;
    }

    bool IsDead => stats != null && stats.IsDead;
    bool Grounded => controller == null || controller.isGrounded;

    void Update()
    {
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 move = transform.position - lastPosition;
        lastVerticalVelocity = verticalVelocity;
        verticalVelocity = move.y / dt;
        if (Mathf.Abs(verticalVelocity) > 30f) verticalVelocity = lastVerticalVelocity = 0f; // teleported
        move.y = 0f;
        lastVelocity = velocity;
        velocity = move / dt;
        lastPosition = transform.position;

        // Took damage? -> hit reaction
        if (stats != null)
        {
            if (hitReaction && stats.currentHealth < lastHealth - 0.01f && !IsDead) hitTimer = 0f;
            lastHealth = stats.currentHealth;
        }
        if (hitTimer >= 0f)
        {
            hitTimer += Time.deltaTime;
            if (hitTimer > hitDuration) hitTimer = -1f;
        }
    }

    // ---------- Foot IK + look-at (called by the Animator) ----------

    public void OnAnimatorIKFromRelay(int layerIndex)
    {
        if (layerIndex != 0) return;
        float speed01 = Mathf.Clamp01(velocity.magnitude / 5f);
        // On a horse the legs are posed by HorseMount: the foot IK would fight it (it hits the horse and flickers)
        bool feetActive = footIK && Grounded && !IsDead && !HorseMount.PlayerMounted && !SitAnimation.IsActive; // sitting / standing up: the clip poses the feet
        float footWeight = feetActive ? Mathf.Lerp(1f, runFootWeight, speed01) : 0f;

        float leftTarget = 0f, rightTarget = 0f;
        Quaternion leftRot = Quaternion.identity, rightRot = Quaternion.identity;
        bool leftHit = feetActive && SolveFoot(AvatarIKGoal.LeftFoot, out leftTarget, out leftRot);
        bool rightHit = feetActive && SolveFoot(AvatarIKGoal.RightFoot, out rightTarget, out rightRot);

        float t = 1f - Mathf.Exp(-footSmoothing * Time.deltaTime);
        leftFootOffset = Mathf.Lerp(leftFootOffset, leftHit ? leftTarget : 0f, t);
        rightFootOffset = Mathf.Lerp(rightFootOffset, rightHit ? rightTarget : 0f, t);

        // Lower the hips so the lowest foot can reach the ground (never raise them)
        float wantedHip = Mathf.Clamp(Mathf.Min(leftFootOffset, rightFootOffset, 0f), -maxHipDrop, 0f);
        hipOffset = Mathf.Lerp(hipOffset, feetActive ? wantedHip : 0f, t);
        anim.bodyPosition += Vector3.up * hipOffset * (feetActive ? 1f : 0f);

        ApplyFoot(AvatarIKGoal.LeftFoot, leftFootOffset, leftRot, leftHit ? footWeight : 0f);
        ApplyFoot(AvatarIKGoal.RightFoot, rightFootOffset, rightRot, rightHit ? footWeight : 0f);

        // Head looks where the camera looks
        float wantedLook = 0f;
        Vector3 lookPoint = transform.position + transform.forward * 10f;
        if (lookAt && cam != null && !IsDead && (combat == null || !combat.IsAiming))
        {
            Vector3 camForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            if (Vector3.Angle(camForward, transform.forward) < maxLookAngle) wantedLook = 1f;
            lookPoint = cam.position + cam.forward * 20f;
        }
        lookWeight = Mathf.MoveTowards(lookWeight, wantedLook, Time.deltaTime * 2f);
        anim.SetLookAtWeight(lookWeight, bodyWeight, headWeight, 0f, 0.5f);
        anim.SetLookAtPosition(lookPoint);

        // Hands touch nearby walls (only unarmed and walking)
        bool handsActive = handOnWalls && Grounded && !IsDead && velocity.magnitude <= maxWallSpeed && (combat == null || !combat.IsArmed);
        UpdateHand(AvatarIKGoal.LeftHand, HumanBodyBones.LeftUpperArm, -transform.right, handsActive, true, ref leftHandWeight, ref leftHandTarget, ref leftHandRotation);
        UpdateHand(AvatarIKGoal.RightHand, HumanBodyBones.RightUpperArm, transform.right, handsActive, false, ref rightHandWeight, ref rightHandTarget, ref rightHandRotation);
    }

    void UpdateHand(AvatarIKGoal goal, HumanBodyBones shoulderBone, Vector3 side, bool active, bool mirror,
                    ref float weight, ref Vector3 target, ref Quaternion rotation)
    {
        float wanted = 0f;
        Transform shoulder = anim.GetBoneTransform(shoulderBone);

        if (active && shoulder != null && FindWall(shoulder.position, side, out RaycastHit hit))
        {
            // Closer wall = hand goes more to it
            wanted = (1f - hit.distance / wallReach) * maxHandWeight;

            Vector3 point = hit.point + hit.normal * 0.04f;
            point.y = shoulder.position.y - handHeightOffset;

            // Palm flat on the wall, fingers pointing up/forward along it
            Vector3 along = Vector3.ProjectOnPlane(transform.forward + Vector3.up, hit.normal).normalized;
            if (along.sqrMagnitude < 0.01f) along = Vector3.ProjectOnPlane(Vector3.up, hit.normal).normalized;
            Vector3 offset = mirror ? new Vector3(handRotationOffset.x, -handRotationOffset.y, -handRotationOffset.z) : handRotationOffset;
            Quaternion wantedRotation = Quaternion.LookRotation(along, hit.normal) * Quaternion.Euler(offset);

            // First touch: jump straight to the wall point, then follow it smoothly
            if (weight < 0.01f) { target = point; rotation = wantedRotation; }
            float follow = 1f - Mathf.Exp(-handSmoothing * 2f * Time.deltaTime);
            target = Vector3.Lerp(target, point, follow);
            rotation = Quaternion.Slerp(rotation, wantedRotation, follow);
        }

        weight = Mathf.Lerp(weight, wanted, 1f - Mathf.Exp(-handSmoothing * Time.deltaTime));
        anim.SetIKPositionWeight(goal, weight);
        anim.SetIKRotationWeight(goal, weight * 0.8f);
        if (weight > 0.001f)
        {
            anim.SetIKPosition(goal, target);
            anim.SetIKRotation(goal, rotation);
        }
    }

    // Reused every frame so the raycasts create no garbage
    private readonly RaycastHit[] rayHits = new RaycastHit[16];
    private readonly Vector3[] wallDirections = new Vector3[3];

    // Looks to the side and diagonally forward for a wall (not floors or slopes)
    bool FindWall(Vector3 from, Vector3 side, out RaycastHit best)
    {
        best = default;
        bool found = false;
        wallDirections[0] = side;
        wallDirections[1] = (side + transform.forward).normalized;
        wallDirections[2] = (side * 0.4f + transform.forward).normalized;
        for (int d = 0; d < wallDirections.Length; d++)
        {
            int count = Physics.RaycastNonAlloc(from, wallDirections[d], rayHits, wallReach, wallLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = rayHits[i];
                if (hit.collider.transform.IsChildOf(transform)) continue; // the player itself
                if (Mathf.Abs(hit.normal.y) > 0.6f) continue;              // floors/gentle slopes are not walls
                if (!found || hit.distance < best.distance) { best = hit; found = true; }
            }
        }
        return found;
    }

    // How far up/down the foot must move to touch the ground, and how to tilt it to the slope
    bool SolveFoot(AvatarIKGoal goal, out float offset, out Quaternion rotation)
    {
        offset = 0f;
        rotation = Quaternion.identity;
        // Foot moves by (ground under foot - ground under player), so steps keep their animated lift
        Vector3 footPos = anim.GetIKPosition(goal);
        Vector3 origin = new Vector3(footPos.x, transform.position.y + raycastUp, footPos.z);
        if (!GroundRaycast(origin, raycastUp + raycastDown, out RaycastHit hit)) return false;
        offset = Mathf.Clamp(hit.point.y - transform.position.y, -maxHipDrop, raycastUp);
        rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
        return true;
    }

    void ApplyFoot(AvatarIKGoal goal, float offset, Quaternion slope, float weight)
    {
        anim.SetIKPositionWeight(goal, weight);
        anim.SetIKRotationWeight(goal, weight);
        if (weight <= 0f) return;
        anim.SetIKPosition(goal, anim.GetIKPosition(goal) + Vector3.up * offset);
        anim.SetIKRotation(goal, slope * anim.GetIKRotation(goal));
    }

    bool GroundRaycast(Vector3 origin, float distance, out RaycastHit result)
    {
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, rayHits, distance, groundLayers, QueryTriggerInteraction.Ignore);
        bool found = false;
        result = default;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = rayHits[i];
            if (hit.collider.transform.IsChildOf(transform)) continue; // ignore the player itself
            if (!found || hit.distance < result.distance) { result = hit; found = true; } // the closest one
        }
        return found;
    }

    // ---------- Lean, hit reaction, backpack sway (after the animation) ----------

    void LateUpdate()
    {
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        // Lean into turns: turning speed x moving speed
        float yaw = transform.eulerAngles.y;
        float yawSpeed = Mathf.DeltaAngle(lastYaw, yaw) / dt;
        lastYaw = yaw;
        float wantedLean = lean && Grounded && !IsDead ? Mathf.Clamp(yawSpeed * velocity.magnitude * leanStrength, -maxLean, maxLean) : 0f;
        currentLean = Mathf.Lerp(currentLean, wantedLean, 1f - Mathf.Exp(-leanSmoothing * dt));
        if (spine != null && Mathf.Abs(currentLean) > 0.01f)
            spine.rotation = Quaternion.AngleAxis(-currentLean, transform.forward) * spine.rotation;

        // Hit reaction: quick jolt back that eases out
        if (hitTimer >= 0f && chest != null)
        {
            float k = Mathf.Sin(Mathf.Clamp01(hitTimer / hitDuration) * Mathf.PI);
            chest.rotation = Quaternion.AngleAxis(-hitAngle * k, transform.right) * chest.rotation;
        }

        UpdateBackpackSway(dt);
    }

    void UpdateBackpackSway(float dt)
    {
        GameObject backpack = PlayerEquipmentManager.Instance != null ? PlayerEquipmentManager.Instance.CurrentBackpackObject : null;
        if (backpack != swayObject)
        {
            swayObject = backpack;
            if (swayObject != null) swayBaseRotation = swayObject.transform.localRotation;
            swayAngle = swaySpeed = Vector2.zero;
        }
        if (swayObject == null || !backpackSway) return;

        // Local acceleration pushes the backpack the other way (spring)
        Vector3 accel = transform.InverseTransformDirection((velocity - lastVelocity) / dt);
        // Up/down too: bouncing on a horse or landing swings it forward/back
        float verticalAccel = (verticalVelocity - lastVerticalVelocity) / dt;
        Vector2 force = new Vector2(-accel.z + verticalAccel * verticalSwayStrength, accel.x) * swayStrength;
        if (HorseMount.PlayerMounted) force *= mountedSwayMultiplier;
        float limit = maxSway;
        HorseMount horse = HorseMount.PlayerMounted ? HorseMount.Ridden : null;
        if (horse != null)
        {
            // Hoof beats: the backpack is kicked up and down with the horse's rhythm (and a little sideways)
            float beat = Mathf.Clamp01(horse.Gait / 2f);
            force.x += Mathf.Sin(horse.BouncePhase * 2f) * mountedBounceKick * beat;
            force.y += Mathf.Sin(horse.BouncePhase) * mountedBounceKick * 0.5f * beat;
            limit = Mathf.Max(maxSway, mountedMaxSway);
        }
        swaySpeed += (force - swayAngle * swaySpring - swaySpeed * swayDamping) * dt;
        swayAngle += swaySpeed * dt;
        swayAngle = Vector2.ClampMagnitude(swayAngle, limit);

        swayObject.transform.localRotation = swayBaseRotation;
        swayObject.transform.rotation = Quaternion.AngleAxis(swayAngle.x, transform.right) * Quaternion.AngleAxis(swayAngle.y, transform.forward) * swayObject.transform.rotation;
    }
}

// Forwards OnAnimatorIK from the Animator's object to PlayerProceduralAnimation (added automatically)
public class ProceduralIKRelay : MonoBehaviour
{
    [HideInInspector] public PlayerProceduralAnimation target;
    void OnAnimatorIK(int layerIndex)
    {
        if (target != null && target.isActiveAndEnabled) target.OnAnimatorIKFromRelay(layerIndex);
    }
}
