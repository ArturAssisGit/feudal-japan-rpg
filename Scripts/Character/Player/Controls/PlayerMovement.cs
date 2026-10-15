using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    public CharacterController controller;
    public Animator anim;
    public Transform cam;
    public PlayerStats stats;

    // True while sprinting (read by the cursed afterimage effect)
    public bool IsRunning { get; private set; }

    [Header("Normal Speeds")]
    public float walkSpeed = 2.5f;
    public float runSpeed = 7f;
    public float turnSmoothTime = 0.1f;
    float turnSmoothVelocity, swordSpeed;
    Vector3 lockVelocity;

    [Header("Combat Speeds")]
    public float combatWalkSpeed = 1.5f;
    public float combatRunSpeed = 5f;
    public float aimWalkSpeed = 1.2f;

    [Header("Sword Movement")]
    [Tooltip("Off = with the sword drawn you move like without it: the character turns to the direction you press (no aiming with the camera). On = old behaviour: the character always faces where the camera looks and strafes")]
    public bool strafeWithSword = false;

    [Tooltip("Seconds the character takes to turn to the direction you press with the sword drawn. Higher = heavier, slower turns")] public float swordTurnTime = 0.3f;
    [Tooltip("How fast he reaches full speed (meters/second per second). Lower = more weight when starting and stopping")] public float swordAcceleration = 14f;
    [Tooltip("Speed while he is still turning to a very different direction (0.4 = 40%). 1 = no slowdown")] [Range(0.1f, 1f)] public float swordTurnSlowdown = 0.5f;

    [Header("Boss Lock")]
    [Tooltip("While the camera is locked onto a boss (sword drawn), the character faces the boss and walks with the strafe animations: left, right and backwards. Sprinting (Shift) turns to where you run, to flee")] public bool strafeAroundBoss = true;
    [Tooltip("Seconds to turn towards the boss (lower = snappier)")] public float bossTurnTime = 0.12f;
    [Tooltip("Speed multiplier while strafing around the boss")] public float bossStrafeSpeed = 1f;

    [Header("Backpack")]
    [Tooltip("Speed multiplier while wearing a backpack (1 = no slowdown)")] public float backpackSpeedMultiplier = 0.8f;

    [Header("Stamina Settings")]
    public float runStaminaCost = 35f;
    public float jumpStaminaCost = 15f;
    public float slideStaminaCost = 20f;

    [Header("Physics")]
    public float gravity = -30f;
    public float jumpHeight = 1.5f;
    Vector3 velocity;
    PlayerCombat combat;

    // Sounds: footsteps, landing
    Vector3 lastSoundPosition;
    float stepDistance, airTime;

    void Start()
    {
        lastSoundPosition = transform.position;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // On a horse: HorseMount places the player on the saddle (the CharacterController is off)
        if (HorseMount.PlayerMounted) return;

        // The camera of the menu scene is gone after New Game: use the current main camera
        if (cam == null && Camera.main != null) cam = Camera.main.transform;
        if (cam == null) { ClampToGround(); ApplyGravity(); return; }

        // While dodging (PlayerDodge moves the player) or during a finisher: only keep gravity here
        if (PlayerDodge.IsDodging || PlayerFinisher.IsActive || GatherAnimation.IsActive || SitAnimation.IsActive || ScreenFade.IsActive) // (gathering / looting: stand still)
        {
            ClampToGround();
            ApplyGravity();
            return;
        }

        bool menuOpen = PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.IsOpen;
        float horizontal = menuOpen ? 0f : Input.GetAxisRaw("Horizontal");
        float vertical = menuOpen ? 0f : Input.GetAxisRaw("Vertical");
        Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;
        bool hasInput = direction.magnitude >= 0.1f;

        if (combat == null) combat = GetComponent<PlayerCombat>();
        bool isAimingBow = combat != null && combat.IsAiming;
        bool isArmed = anim.GetBool("isArmed") || (combat != null && combat.IsBowDrawn);
        bool isBlocking = anim.GetBool("isBlocking");
        bool isAttacking = IsAttacking();

        bool swordFree = !strafeWithSword && anim.GetBool("isArmed") && !(combat != null && combat.IsBowDrawn);
        bool canSprintThisWay = !isArmed || swordFree || vertical > 0.1f;
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        float sprintCost = runStaminaCost * (gear != null && gear.HasBackpackPerk(BackpackPerk.HalfSprintStamina) ? 0.5f : 1f);
        bool isRunning = Input.GetKey(KeyCode.LeftShift) && hasInput && canSprintThisWay && !isAimingBow && !isBlocking && !isAttacking && CanSpend(sprintCost * Time.deltaTime);
        IsRunning = isRunning;

        float currentSpeed = (isArmed, isRunning) switch
        {
            (true, true) => combatRunSpeed,
            (true, false) => combatWalkSpeed,
            (false, true) => runSpeed,
            _ => walkSpeed
        };
        if (isAimingBow) currentSpeed = aimWalkSpeed;
        if (gear != null && gear.HasBackpackEquipped && !gear.HasBackpackPerk(BackpackPerk.NoSpeedPenalty)) currentSpeed *= backpackSpeedMultiplier;
        if (gear != null && gear.HasBackpackPerk(BackpackPerk.HeavyHitter)) currentSpeed *= 0.9f;
        currentSpeed *= RainSystem.PlayerSpeedMultiplier; // storms slow you down
        if (ItemUpgrades.Active(OniSpirit.Tengu)) currentSpeed *= 1f + ItemUpgrades.TenguSpeedBonus; // Oni Stone perk

        Transform lockedBoss = strafeAroundBoss && anim.GetBool("isArmed") && !(combat != null && combat.IsBowDrawn) ? CameraController.LockedBoss : null;
        if (lockedBoss != null && !isAttacking) BossLockMove(direction, horizontal, vertical, hasInput && !isBlocking, isRunning, currentSpeed, lockedBoss);
        else if (swordFree && !isAttacking) CombatFreeMove(direction, hasInput && !isBlocking, isRunning, currentSpeed);
        else if (isArmed && !isAttacking) CombatMove(direction, horizontal, vertical, hasInput && !isBlocking, isRunning, currentSpeed);
        else FreeMove(direction, hasInput && !isAttacking, isBlocking, isRunning, currentSpeed);

        ClampToGround();

        bool canAct = !isAttacking && !isBlocking && !menuOpen;
        if (Input.GetKeyDown(KeyCode.Space) && controller.isGrounded && canAct && CanSpend(jumpStaminaCost))
        {
            anim.SetTrigger("JumpTrigger");
            SoundManager.Play(Sfx.Jump, transform.position);
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        if (Input.GetKeyDown(KeyCode.C) && canAct && CanSpend(slideStaminaCost))
        {
            anim.SetTrigger("SlideTrigger");
            SoundManager.Play(Sfx.Slide, transform.position);
        }

        ApplyGravity();
        UpdateMovementSounds(isRunning, isAttacking);
    }

    void ClampToGround() { if (controller.isGrounded && velocity.y < 0) velocity.y = -5f; }

    void ApplyGravity()
    {
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    // Footsteps based on the distance walked, and a sound when landing after a jump/fall
    void UpdateMovementSounds(bool isRunning, bool isAttacking)
    {
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 moved = transform.position - lastSoundPosition;
        lastSoundPosition = transform.position;
        moved.y = 0f;
        float distance = moved.magnitude;
        if (distance > 3f) distance = 0f; // teleported (respawn, load): not a step

        bool grounded = controller.isGrounded;
        if (grounded && airTime > 0.35f) SoundManager.Play(Sfx.Land, transform.position);
        airTime = grounded ? 0f : airTime + dt;

        bool walking = airTime < 0.15f && !isAttacking && distance / dt > 0.5f;
        if (!walking)
        {
            stepDistance = SoundManager.StepDistance(false) * 0.6f; // next step comes quickly after starting to move
            return;
        }

        stepDistance += distance;
        if (stepDistance >= SoundManager.StepDistance(isRunning))
        {
            stepDistance = 0f;
            SoundManager.Play(isRunning ? Sfx.FootstepRun : Sfx.FootstepWalk, transform.position);
        }
    }

    // Sword drawn but no camera aiming: turn to the pressed direction, always play the forward walk/run of the combat Animator
    void CombatFreeMove(Vector3 direction, bool moving, bool isRunning, float speed)
    {
        float wantedSpeed = 0f;
        if (moving)
        {
            // He turns slowly towards the pressed direction and walks the way he is facing: wide, heavy turns
            float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cam.eulerAngles.y;
            float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, angle, ref turnSmoothVelocity, Mathf.Max(0.01f, swordTurnTime));
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            float alignment = Mathf.Clamp01(Mathf.Cos(Mathf.DeltaAngle(yaw, angle) * Mathf.Deg2Rad) * 0.5f + 0.5f); // 1 = facing the wanted way
            wantedSpeed = speed * Mathf.Lerp(swordTurnSlowdown, 1f, alignment);
        }
        swordSpeed = Mathf.MoveTowards(swordSpeed, wantedSpeed, swordAcceleration * Time.deltaTime);
        if (swordSpeed > 0.01f) controller.Move(transform.forward * swordSpeed * Time.deltaTime);
        SetAnim("MovX", 0f);
        SetAnim("MovZ", moving ? (isRunning ? 1f : 0.5f) : 0f);
        SetAnim("Speed", 0f);
    }

    // Boss fight (camera locked): face the boss and use the walk left / right / back animations. Shift = run away
    void BossLockMove(Vector3 direction, float horizontal, float vertical, bool moving, bool isRunning, float speed, Transform boss)
    {
        Vector3 wanted = Vector3.zero;
        float movX = 0f, movZ = 0f;
        if (moving && isRunning)
        {
            float runAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cam.eulerAngles.y;
            Rotate(runAngle);
            wanted = Quaternion.Euler(0f, runAngle, 0f) * Vector3.forward * speed;
            movZ = 1f;
        }
        else
        {
            Vector3 toBoss = boss.position - transform.position;
            toBoss.y = 0f;
            if (toBoss.sqrMagnitude > 0.01f) Rotate(Mathf.Atan2(toBoss.x, toBoss.z) * Mathf.Rad2Deg, bossTurnTime);
            if (moving)
            {
                wanted = (transform.rotation * direction).normalized * speed * bossStrafeSpeed;
                movX = horizontal;
                movZ = vertical * 0.5f;
            }
        }
        lockVelocity = Vector3.MoveTowards(lockVelocity, wanted, swordAcceleration * Time.deltaTime);
        swordSpeed = lockVelocity.magnitude;
        if (swordSpeed > 0.01f) controller.Move(lockVelocity * Time.deltaTime);
        SetAnim("MovX", movX);
        SetAnim("MovZ", movZ);
        SetAnim("Speed", 0f);
    }

    void CombatMove(Vector3 direction, float horizontal, float vertical, bool moving, bool isRunning, float speed)
    {
        if (moving && isRunning)
        {
            float runAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cam.eulerAngles.y;
            Rotate(runAngle);
            controller.Move(Quaternion.Euler(0f, runAngle, 0f) * Vector3.forward * speed * Time.deltaTime);
            SetAnim("MovX", 0f);
            SetAnim("MovZ", 1f);
            SetAnim("Speed", 0f);
            return;
        }

        float targetAngle = cam.eulerAngles.y;
        Rotate(targetAngle);
        if (moving) controller.Move((Quaternion.Euler(0f, targetAngle, 0f) * direction).normalized * speed * Time.deltaTime);

        SetAnim("MovX", moving ? horizontal : 0f);
        SetAnim("MovZ", moving ? vertical * (isRunning ? 1f : 0.5f) : 0f);
        SetAnim("Speed", 0f);
    }

    void FreeMove(Vector3 direction, bool canTurn, bool isBlocking, bool isRunning, float speed)
    {
        SetAnim("MovX", 0f);
        SetAnim("MovZ", 0f);

        float animSpeed = 0f;
        if (canTurn)
        {
            float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cam.eulerAngles.y;
            Rotate(targetAngle);
            if (!isBlocking)
            {
                controller.Move((Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward).normalized * speed * Time.deltaTime);
                animSpeed = isRunning ? 1f : 0.5f;
            }
        }
        SetAnim("Speed", animSpeed);
    }

    void Rotate(float targetAngle, float smoothTime = -1f)
    {
        float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, smoothTime > 0f ? smoothTime : turnSmoothTime);
        transform.rotation = Quaternion.Euler(0f, angle, 0f);
    }

    void SetAnim(string param, float value) => anim.SetFloat(param, value, 0.1f, Time.deltaTime);

    bool CanSpend(float cost) => stats != null && stats.TryConsumeStamina(cost);

    bool IsAttacking()
    {
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (combat != null && combat.IsStartingAttack) return true;
        if (anim.GetCurrentAnimatorStateInfo(0).IsTag("Attack")) return true;
        return anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).IsTag("Attack");
    }

    void OnAnimatorMove()
    {
        if (anim == null || controller == null || !controller.enabled) return;
        if (!anim.GetCurrentAnimatorStateInfo(0).IsTag("Attack")) return;

        Vector3 delta = anim.deltaPosition;
        delta.y = 0f;
        if (delta.sqrMagnitude > 0f) controller.Move(delta);
    }
}
