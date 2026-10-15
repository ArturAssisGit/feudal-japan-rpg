using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlayerCombat : MonoBehaviour
{
    [Header("References")]
    public Animator anim;
    public PlayerStats stats;

    [Header("Weapons")]
    public PlayerEquipmentManager equipmentManager;

    [Header("Stamina Costs")]
    public float basicAttackCost = 15f;
    public float basicAttackCost2 = 20f;
    public float strongAttackCost = 30f;

    [Header("Hitbox & Damage Settings")]
    public Transform attackPoint;
    public float attackRadius = 1f;
    [Tooltip("Used when Attack Point is empty: how far in front of the player the hit zone is")] public float attackDistance = 0.6f;
    [Tooltip("Used when Attack Point is empty: how high the hit zone is")] public float attackHeight = 0.6f;
    public float strongAttackMultiplier = 1.5f;
    public float attack2Multiplier = 1.2f;
    public LayerMask enemyLayer;

    [Header("Hit Timing")]
    [Tooltip("Off = damage after the delays below. On = damage only from the DealDamageEvent animation event")]
    public bool useAnimationEvents = false;
    public float basicHitDelay = 0.4f;
    public float strongHitDelay = 0.6f;
    public float attack2HitDelay = 0.7f;

    [Header("Basic Attack Combo (left click)")]
    [Tooltip("Animator state of the 1st basic swing")] public string basicAttackStateA = "Combat Basic Attack";
    [Tooltip("Animator state of the 2nd basic swing (create a state with exactly this name, Tag = Attack)")] public string basicAttackStateB = "Combat Basic Attack After";
    [Tooltip("The next swing can start once the current one reaches this point (0.55 = 55% of the animation)")] [Range(0.2f, 1f)] public float comboCancelPoint = 0.55f;
    [Tooltip("A click during a swing is remembered for this long and used as soon as possible")] public float inputBufferTime = 0.4f;
    [Tooltip("If you wait longer than this between clicks, the combo starts again from the 1st swing")] public float comboResetTime = 1.2f;
    [Tooltip("Seconds of blending between swings")] public float attackCrossFade = 0.1f;
    [Tooltip("Hit delay of the 2nd swing")] public float basicHitDelayB = 0.4f;
    [Tooltip("When the 2nd swing goes back to the combat idle (only if it has no transition of its own)")] [Range(0.3f, 1f)] public float swingReturnPoint = 0.75f;
    [Tooltip("Flow: the next swing can start earlier. Real start point = Combo Cancel Point x this (1 = old behaviour, 0.7 = 30% sooner)")] [Range(0.4f, 1f)] public float comboFlow = 0.7f;
    [Tooltip("Minimum seconds between the start of one swing and the start of the next (old value was 0.35)")] public float minSwingGap = 0.2f;
    [Tooltip("Blend time when chaining a swing into the next swing (faster = snappier). Used only inside a combo")] public float comboCrossFade = 0.06f;

    [Header("Random Kick (while fighting)")]
    [Tooltip("Chance that a left-click swing in the middle of a combo turns into a kick. 0.15 = 15%")] [Range(0f, 1f)] public float kickChance = 0.15f;
    [Tooltip("Animator state of the kick (Base Layer, Tag = Attack). If the state doesn't exist, there are no kicks")] public string kickState = "Combat Kick";
    [Tooltip("Seconds from the start of the kick until it hits")] public float kickHitDelay = 0.35f;
    [Tooltip("Kick damage compared to a normal swing (0.6 = 60%)")] public float kickDamageMultiplier = 0.6f;
    [Tooltip("How far a kick pushes the enemy back (meters)")] public float kickKnockback = 1.6f;
    [Tooltip("When the kick goes back to the combat idle (only if it has no transition of its own)")] [Range(0.3f, 1f)] public float kickReturnPoint = 0.85f;

    [Header("Camera Aim Assist (Ghost of Tsushima style)")]
    [Tooltip("Every attack turns the character towards the enemy the CAMERA is aiming at. Between attacks you move freely")] public bool aimAssist = true;
    [Tooltip("Enemies farther than this (meters) are ignored")] public float aimAssistRange = 7f;
    [Tooltip("Enemies must be inside this angle around the camera's aim (degrees, total = 2x)")] [Range(10f, 180f)] public float aimAssistAngle = 60f;
    [Tooltip("Seconds the character takes to turn at the start of an attack (0 = instant)")] public float aimTurnTime = 0.1f;
    [Tooltip("No enemy in the aim zone: off = attack where the mouse/camera points, on = attack towards the movement keys you are pressing")] public bool turnToInputWhenNoTarget = false;
    [Tooltip("Flicking the mouse left/right while you attack steers the hit that way (degrees per unit of mouse movement). 0 = off")] public float mouseFlickAngle = 4f;
    [Tooltip("Maximum steering from the mouse flick (degrees)")] public float mouseFlickMax = 35f;
    [Tooltip("How fast the flick fades (higher = shorter memory)")] public float mouseFlickFade = 6f;

    [Header("Lunge (guides the player to the enemy)")]
    [Tooltip("When you attack an enemy that is a bit too far for the hit, the player steps towards it so the sword connects")] public bool lungeToTarget = true;
    [Tooltip("Only enemies closer than this (meters) pull the player in")] public float lungeRange = 3.5f;
    [Tooltip("The player stops this far from the enemy (meters). Around 1.2 is where the sword hits for sure")] public float lungeStopDistance = 1.2f;
    [Tooltip("Seconds the step towards the enemy takes")] public float lungeTime = 0.2f;

    [Header("Hit Impact (feel)")]
    [Tooltip("The game freezes for a split second when your sword connects")] public bool hitStop = true;
    public float hitStopDuration = 0.06f;
    [Tooltip("Game speed during the freeze (0.05 = almost stopped)")] [Range(0f, 1f)] public float hitStopTimeScale = 0.05f;
    public float cameraShake = 0.06f;
    [Tooltip("How far enemies are pushed back by any hit (meters)")] public float knockback = 0.45f;
    [Tooltip("Small burst of sparks where the sword hits")] public bool hitSparks = true;
    public Color sparkColor = new Color(1f, 0.75f, 0.35f);

    [Header("Get Hit")]
    [Tooltip("Animator state played when YOU take damage (layer 0). Empty or missing state = no animation")] public string hitReactionState = "Combat Hit";
    [Tooltip("Seconds of blending into the hit animation")] public float hitReactionCrossFade = 0.08f;
    [Tooltip("When the hit animation goes back to idle (only if it has no transition of its own)")] [Range(0.3f, 1f)] public float hitReactionReturnPoint = 0.9f;
    [Tooltip("Hits closer together than this don't restart the animation")] public float hitReactionCooldown = 0.3f;

    [Header("Bow Crosshair")]
    [Tooltip("Small dot in the middle of the screen while aiming with the bow")] public bool showCrosshair = true;
    [Tooltip("Size of the dot in pixels")] public float crosshairSize = 4f;
    public Color crosshairColor = new Color(1f, 1f, 1f, 0.9f);
    [Tooltip("Optional: your own crosshair image (empty = round dot)")] public Sprite crosshairSprite;

    [Header("Bow Animator")]
    [Tooltip("Name of the Animator state that takes the bow from the back")] public string bowEquipState = "Bow Equip";

    [Header("Controls")]
    public KeyCode blockKey = KeyCode.Q;
    public KeyCode swordKey = KeyCode.Alpha1;
    public KeyCode bowDrawKey = KeyCode.Alpha2;

    [Header("Draw Sword")]
    [Tooltip("If the DrawSwordEvent animation event doesn't fire, the sword goes to the hand after this time")]
    public float drawFallbackDelay = 1f;

    [Header("Bow")]
    public GameObject arrowPrefab;
    public Transform arrowSpawnPoint;
    public float arrowSpeed = 45f;
    [Tooltip("Size of the arrow (1 = normal)")] public float arrowScale = 0.6f;
    public float defaultArrowDamage = 20f;
    [Tooltip("Seconds you must hold aim before you can shoot")] public float minDrawTime = 0.4f;
    public float shootCooldown = 0.7f;
    public float shootStaminaCost = 5f;
    public float arrowReleaseDelay = 0.05f;
    [Tooltip("Seconds into the equip animation when the bow moves to the hand")] public float bowDrawDelay = 0.6f;
    [Tooltip("Seconds into the disarm animation when the bow moves to the back")] public float bowSheathDelay = 0.6f;

    private bool isEquipped, isBowDrawn, isAiming;
    private float aimStartTime;
    private float lastShotTime = -999f;
    private readonly HashSet<string> animParams = new HashSet<string>();

    public bool IsArmed => isEquipped || isBowDrawn;
    public bool IsBowDrawn => isBowDrawn;
    // The right "idle" to go back to after a dodge / hit / swing
    public string IdleStateName => isEquipped ? "Armed Blend Tree" : isBowDrawn ? (isAiming ? "Bow Aim Blend Tree" : "Bow Blend Tree") : "Blend Tree";
    public bool IsAiming => isAiming;

    private int comboIndex = 1;
    private float lastBasicTime = -999f;
    private float bufferedUntil = -1f;

    void Start()
    {
        if (anim != null) foreach (AnimatorControllerParameter p in anim.parameters) animParams.Add(p.name);
        if (GetComponent<PlayerDodge>() == null) gameObject.AddComponent<PlayerDodge>();
    }

    void SetAnimBool(string param, bool value) { if (animParams.Contains(param)) anim.SetBool(param, value); }
    void SetAnimTrigger(string param) { if (animParams.Contains(param)) anim.SetTrigger(param); }

    public bool IsStartingAttack => Time.time < attackStartTime + 0.35f;
    private float attackStartTime = -999f;
    private float pendingMultiplier = 1f;
    private bool pendingKick;

    private float mouseFlick;

    void Update()
    {
        ReleaseSparks();
        // Remember recent sideways mouse movement: it steers the next attack
        mouseFlick = (mouseFlick + Input.GetAxis("Mouse X")) * Mathf.Exp(-mouseFlickFade * Time.unscaledDeltaTime);
        if (isEquipped && (equipmentManager == null || !equipmentManager.HasWeaponEquipped)) ClearSwordAnim();
        if (isBowDrawn && (equipmentManager == null || !equipmentManager.HasBowEquipped)) PutBowAway(false);

        // Inventory open, finisher playing or riding a horse: no attack, block or aim
        if ((equipmentManager != null && equipmentManager.IsOpen) || PlayerFinisher.IsActive || GatherAnimation.IsActive || SitAnimation.IsActive || ScreenFade.IsActive || HorseMount.PlayerMounted)
        {
            anim.SetBool("isBlocking", false);
            SetAiming(false);
            return;
        }

        // While placing a tent/bonfire the mouse buttons place/cancel instead of attacking
        if (PlacementSystem.IsPlacing)
        {
            SetAiming(false);
            return;
        }

        if (Input.GetKeyDown(bowDrawKey) && equipmentManager != null && equipmentManager.HasBowEquipped)
        {
            if (!isBowDrawn)
            {
                if (isEquipped) PutSwordAwayInstantly();
                isBowDrawn = true;
                SetAnimBool("isBowArmed", true);
                SetDrawAnimSpeed();
                StopCoroutine(nameof(BowToBack));
                StartCoroutine(nameof(BowToHand));
            }
            else PutBowAway(true);
        }

        if (Input.GetKeyDown(swordKey) && equipmentManager != null && equipmentManager.HasWeaponEquipped)
        {
            if (!isEquipped && isBowDrawn) PutBowAway(false);
            isEquipped = !isEquipped;
            anim.SetBool("isArmed", isEquipped);
            SetDrawAnimSpeed();
            if (isEquipped) StartCoroutine(DrawFallback());
            else anim.SetTrigger("SheathTrigger");
        }

        if (isBowDrawn) UpdateBow();
        SkipRepeatedBowEquip();

        ReturnFromHitReaction();
        if (!isEquipped) return;

        ReturnFromSwingB();
        ReturnFromKick();
        if (PlayerDodge.IsDodging) return;

        // A strong attack / attack 2 must finish completely before any new attack can start
        bool isAttacking = anim.GetCurrentAnimatorStateInfo(0).IsTag("Attack") || HeavyBusy();

        // Basic attack: click is buffered, swings alternate A -> B -> A ...
        if (Input.GetMouseButtonDown(0)) bufferedUntil = Time.time + inputBufferTime;
        if (Time.time <= bufferedUntil && CanStartBasicSwing(isAttacking))
        {
            bufferedUntil = -1f;
            DoBasicSwing();
        }
        float strongMultiplier = strongAttackMultiplier * (PlayerEquipmentManager.PerkActive(BackpackPerk.HeavyHitter) ? 1.3f : 1f);
        if (Input.GetKeyDown(KeyCode.F)) TryAttack(strongAttackCost, "StrongAttackTrigger", isAttacking, strongHitDelay, strongMultiplier);
        if (Input.GetMouseButtonDown(1)) TryAttack(basicAttackCost2, "BasicAttack2Trigger", isAttacking, attack2HitDelay, attack2Multiplier);

        if (Input.GetKeyDown(blockKey) || Input.GetKeyUp(blockKey)) anim.SetBool("isBlocking", Input.GetKey(blockKey));
    }

    // Strong attack / attack 2 lock: stays true from the click until the animation has played and ended
    private bool heavyActive, heavySeen;
    private float heavyStart;

    bool HeavyBusy()
    {
        if (!heavyActive) return false;
        bool inAttackState = anim.GetCurrentAnimatorStateInfo(0).IsTag("Attack");
        if (inAttackState) heavySeen = true;
        else if (heavySeen && !anim.IsInTransition(0)) heavyActive = false;           // the animation has ended
        if (gotHitTime >= heavyStart || PlayerDodge.IsDodging) heavyActive = false;    // interrupted by a hit / dodge
        if (Time.time - heavyStart > (heavySeen ? 6f : 1f)) heavyActive = false;       // safety: never stay locked
        return heavyActive;
    }

    void TryAttack(float cost, string trigger, bool isAttacking, float hitDelay, float multiplier)
    {
        if (isAttacking || heavyActive || stats == null || !stats.TryConsumeStamina(cost)) return;
        heavyActive = true;
        heavySeen = false;
        heavyStart = Time.time;
        anim.SetTrigger(trigger);
        BeginSwing(hitDelay, multiplier, false);
    }

    // Shared start of every swing/kick: sound, timing, pending damage, delayed hit
    void BeginSwing(float hitDelay, float multiplier, bool kick)
    {
        SoundManager.Play(Sfx.SwordSwing, transform.position + Vector3.up);
        attackStartTime = Time.time;
        if (aimAssist) FaceAim();
        pendingMultiplier = multiplier;
        pendingKick = kick;
        if (!useAnimationEvents) StartCoroutine(HitAfterDelay(hitDelay, multiplier, kick));
    }

    // Turns the player to the enemy nearest to where the camera looks (or to the pressed keys when there is none)
    void FaceAim()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 aim = cam.transform.forward;
        aim.y = 0f;
        if (aim.sqrMagnitude < 0.001f) return;
        aim.Normalize();
        if (mouseFlickAngle > 0f) aim = Quaternion.Euler(0f, Mathf.Clamp(mouseFlick * mouseFlickAngle, -mouseFlickMax, mouseFlickMax), 0f) * aim;

        int mask = enemyLayer.value != 0 ? enemyLayer.value : Physics.AllLayers;
        EnemyAI_01 best = null;
        float bestScore = float.MaxValue;
        Vector3 bestDir = aim;
        foreach (Collider hit in Physics.OverlapSphere(transform.position, aimAssistRange, mask, QueryTriggerInteraction.Ignore))
        {
            EnemyAI_01 enemy = hit.GetComponentInParent<EnemyAI_01>();
            if (enemy == null || enemy.IsDead) continue;
            Vector3 to = enemy.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist < 0.05f) continue;
            float angle = Vector3.Angle(aim, to / dist);
            if (angle > aimAssistAngle) continue;
            float score = angle + dist * 3f; // closest to the crosshair wins, distance breaks ties
            if (score < bestScore) { bestScore = score; best = enemy; bestDir = to / dist; }
        }

        Vector3 dir;
        if (best != null) dir = bestDir;
        else if (turnToInputWhenNoTarget && (Input.GetAxisRaw("Horizontal") != 0f || Input.GetAxisRaw("Vertical") != 0f))
            dir = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f) * new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).normalized;
        else dir = aim; // no enemy: attack where the mouse points

        StopCoroutine(nameof(TurnToRoutine));
        StartCoroutine(TurnToRoutine(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg));

        StopCoroutine(nameof(LungeRoutine));
        if (lungeToTarget && best != null) StartCoroutine(LungeRoutine(best.transform));
    }

    // Steps the player towards the enemy until the sword can reach it
    IEnumerator LungeRoutine(Transform enemy)
    {
        if (lungeController == null) lungeController = GetComponentInParent<CharacterController>();
        if (lungeController == null) yield break;

        float startTime = Time.time;
        for (float t = 0f; t < lungeTime && enemy != null; t += Time.deltaTime)
        {
            if (gotHitTime >= startTime || PlayerDodge.IsDodging || PlayerFinisher.IsActive) yield break;
            Vector3 to = enemy.position - transform.position;
            to.y = 0f;
            float over = to.magnitude - lungeStopDistance;
            if (over <= 0.02f || to.magnitude > lungeRange) yield break;
            float step = Mathf.Min(over, over * Time.deltaTime / Mathf.Max(lungeTime - t, Time.deltaTime));
            lungeController.Move(to.normalized * step);
            yield return null;
        }
    }
    private CharacterController lungeController;

    IEnumerator TurnToRoutine(float targetYaw)
    {
        float startYaw = transform.eulerAngles.y;
        for (float t = 0f; t < aimTurnTime; t += Time.deltaTime)
        {
            transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(startYaw, targetYaw, t / aimTurnTime), 0f);
            yield return null;
        }
        transform.rotation = Quaternion.Euler(0f, targetYaw, 0f);
    }

    IEnumerator HitAfterDelay(float delay, float multiplier, bool kick = false)
    {
        float startTime = Time.time;
        yield return new WaitForSeconds(delay);
        if (gotHitTime >= startTime) yield break; // got hit during the swing: attack cancelled
        if (isEquipped && !PlayerDodge.IsDodging) DealDamage(multiplier, kick);
    }

    // ---------- Basic attack combo ----------

    bool InBasicSwing(AnimatorStateInfo info) => info.IsName(basicAttackStateA) || info.IsName(basicAttackStateB);

    bool CanStartBasicSwing(bool isAttacking)
    {
        if (Time.time < attackStartTime + minSwingGap) return false;
        AnimatorStateInfo current = anim.GetCurrentAnimatorStateInfo(0);
        if (anim.IsInTransition(0))
        {
            // Already blending into a swing (or other attack): wait for it
            AnimatorStateInfo next = anim.GetNextAnimatorStateInfo(0);
            return !InBasicSwing(next) && !next.IsTag("Attack");
        }
        if (InBasicSwing(current)) return current.normalizedTime >= comboCancelPoint * comboFlow;
        return !isAttacking; // strong attack / attack 2 must finish first
    }

    void DoBasicSwing()
    {
        if (stats == null || !stats.TryConsumeStamina(basicAttackCost)) return;

        bool continueCombo = Time.time - lastBasicTime <= comboResetTime;
        lastBasicTime = Time.time;

        // Now and then, in the middle of a combo, the player kicks instead of swinging
        if (continueCombo && Random.value < kickChance && !string.IsNullOrEmpty(kickState) && anim.HasState(0, Animator.StringToHash(kickState)))
        {
            DoKick();
            return;
        }

        comboIndex = continueCombo ? 1 - comboIndex : 0;

        string state = comboIndex == 0 ? basicAttackStateA : basicAttackStateB;
        if (!anim.HasState(0, Animator.StringToHash(state)) && anim.HasState(0, Animator.StringToHash("Combat Basic Attack After")))
            state = basicAttackStateB = "Combat Basic Attack After"; // the Inspector still had an old name
        if (!anim.HasState(0, Animator.StringToHash(state))) state = basicAttackStateA; // no 2nd swing yet: repeat the 1st

        anim.CrossFadeInFixedTime(state, continueCombo ? comboCrossFade : attackCrossFade, 0);
        BeginSwing(state == basicAttackStateB ? basicHitDelayB : basicHitDelay, 1f, false);
    }

    // ---------- Random kick ----------

    void DoKick()
    {
        anim.CrossFadeInFixedTime(kickState, attackCrossFade, 0);
        BeginSwing(kickHitDelay, kickDamageMultiplier, true);
    }

    // States without a transition back: return to idle from code
    void ReturnFromKick() { if (!string.IsNullOrEmpty(kickState)) ReturnToIdleAfter(kickState, kickReturnPoint); }

    void ReturnToIdleAfter(string state, float point)
    {
        if (anim.IsInTransition(0)) return;
        AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
        if (info.IsName(state) && info.normalizedTime >= point) anim.CrossFadeInFixedTime(IdleStateName, 0.2f, 0);
    }

    // ---------- Bow equip only once ----------
    // After slide/jump/dodge/hit the Animator may re-enter "Bow Equip"; skip it if the bow is already in hand.

    private bool bowEquipDone;

    void SkipRepeatedBowEquip()
    {
        if (!isBowDrawn) { bowEquipDone = false; return; }

        AnimatorStateInfo current = anim.GetCurrentAnimatorStateInfo(0);
        bool inTransition = anim.IsInTransition(0);
        if (!inTransition && (current.IsName("Bow Blend Tree") || current.IsName("Bow Aim Blend Tree")))
        {
            bowEquipDone = true; // the first draw has finished
            return;
        }
        if (!bowEquipDone) return;

        if (current.IsName(bowEquipState) || (inTransition && anim.GetNextAnimatorStateInfo(0).IsName(bowEquipState)))
            anim.CrossFadeInFixedTime(IdleStateName, 0.15f, 0);
    }

    // ---------- Getting hit ----------

    float gotHitTime = -999f;

    // Called by PlayerStats when the player takes damage
    public void PlayHitReaction()
    {
        if (anim == null || string.IsNullOrEmpty(hitReactionState) || PlayerDodge.IsDodging) return;
        if (Time.time - gotHitTime < hitReactionCooldown || !anim.HasState(0, Animator.StringToHash(hitReactionState))) return;

        gotHitTime = Time.time;
        bufferedUntil = -1f; // forget clicks made before the hit
        anim.CrossFadeInFixedTime(hitReactionState, hitReactionCrossFade, 0, 0f);
    }

    void ReturnFromHitReaction() { if (!string.IsNullOrEmpty(hitReactionState)) ReturnToIdleAfter(hitReactionState, hitReactionReturnPoint); }

    void ReturnFromSwingB() => ReturnToIdleAfter(basicAttackStateB, swingReturnPoint);

    void UpdateBow()
    {
        bool wantsAim = Input.GetMouseButton(1);
        if (wantsAim && !isAiming) aimStartTime = Time.time;
        SetAiming(wantsAim);

        if (!isAiming || !Input.GetMouseButtonDown(0)) return;
        if (Time.time < aimStartTime + minDrawTime || Time.time < lastShotTime + shootCooldown) return;
        if (stats != null && !stats.TryConsumeStamina(shootStaminaCost)) return;

        lastShotTime = Time.time;
        SetAnimTrigger("ShootTrigger");
        StartCoroutine(ReleaseArrow());
    }

    void SetAiming(bool aiming)
    {
        bool wasAiming = isAiming;
        isAiming = aiming && isBowDrawn;
        if (isAiming && !wasAiming) SoundManager.Play(Sfx.BowDraw, transform.position + Vector3.up * 1.5f);
        SetAnimBool("isAiming", isAiming);
        UpdateCrosshair(isAiming && showCrosshair);
    }

    IEnumerator ReleaseArrow()
    {
        yield return new WaitForSeconds(arrowReleaseDelay);
        if (!isBowDrawn) yield break;

        Transform bow = equipmentManager != null ? equipmentManager.BowTransform : null;
        Vector3 spawnPos = arrowSpawnPoint != null ? arrowSpawnPoint.position
            : bow != null ? bow.position : transform.position + Vector3.up * 1.5f;
        Vector3 direction = (GetAimPoint() - spawnPos).normalized;

        GameObject arrow = arrowPrefab != null ? Instantiate(arrowPrefab) : Arrow.CreateDefault();
        arrow.transform.position = spawnPos + direction * 0.5f;
        arrow.transform.localScale *= arrowScale;

        if (!arrow.TryGetComponent(out Arrow arrowScript)) arrowScript = arrow.AddComponent<Arrow>();
        float bowDamage = equipmentManager != null ? equipmentManager.CurrentBowDamage : 0f;
        float arrowDamage = (bowDamage > 0f ? bowDamage : defaultArrowDamage) * (PlayerEquipmentManager.PerkActive(BackpackPerk.Quiver) ? 1.25f : 1f) * ItemUpgrades.DamageMultiplier(stats);
        arrowScript.Launch(direction * arrowSpeed, arrowDamage, transform);
        SoundManager.Play(Sfx.ArrowRelease, spawnPos);
    }

    Vector3 GetAimPoint()
    {
        Camera cam = Camera.main;
        if (cam == null) return transform.position + transform.forward * 50f;

        float skip = Vector3.Distance(cam.transform.position, transform.position) + 0.5f;
        Ray ray = new Ray(cam.transform.position + cam.transform.forward * skip, cam.transform.forward);
        return Physics.Raycast(ray, out RaycastHit hit, 200f, Physics.AllLayers, QueryTriggerInteraction.Ignore) ? hit.point : ray.GetPoint(200f);
    }

    void PutBowAway(bool animated)
    {
        isBowDrawn = false;
        SetAiming(false);
        SetAnimBool("isBowArmed", false);
        StopCoroutine(nameof(BowToHand));

        if (equipmentManager == null) return;
        if (animated)
        {
            SetDrawAnimSpeed();
            StartCoroutine(nameof(BowToBack));
        }
        else equipmentManager.SheathBow();
    }

    float DrawSpeedFactor => PlayerEquipmentManager.PerkActive(BackpackPerk.QuickDraw) ? 0.5f : 1f;

    void SetDrawAnimSpeed()
    {
        if (anim != null) anim.speed = PlayerEquipmentManager.PerkActive(BackpackPerk.QuickDraw) ? 2f : 1f;
        CancelInvoke(nameof(ResetAnimSpeed));
        Invoke(nameof(ResetAnimSpeed), Mathf.Max(bowDrawDelay, drawFallbackDelay) * DrawSpeedFactor + 0.3f);
    }

    void ResetAnimSpeed() { if (anim != null) anim.speed = 1f; }

    IEnumerator BowToHand()
    {
        yield return new WaitForSeconds(bowDrawDelay * DrawSpeedFactor);
        if (isBowDrawn) equipmentManager.DrawBow();
    }

    IEnumerator BowToBack()
    {
        yield return new WaitForSeconds(bowSheathDelay * DrawSpeedFactor);
        if (!isBowDrawn) equipmentManager.SheathBow();
    }

    // Used when the player dies: sword and bow go back to the sheath/back
    public void ResetCombatState()
    {
        // Everything that was in progress (draw timers, swings, lunges, hit freeze) is dropped, so nothing is left half done
        StopAllCoroutines();
        CancelInvoke(nameof(ResetAnimSpeed));
        if (inHitStop)
        {
            Time.timeScale = 1f;
            inHitStop = false;
        }
        heavyActive = false;
        heavySeen = false;
        bufferedUntil = -1f;
        if (anim != null) anim.speed = 1f;

        if (isBowDrawn) PutBowAway(false);
        ClearSwordAnim();
        if (equipmentManager != null) equipmentManager.SheathWeapon(); // the sword always goes back to the sheath
        SetAiming(false);
        if (anim != null) anim.SetBool("isBlocking", false);
    }

    void ClearSwordAnim()
    {
        isEquipped = false;
        anim.SetBool("isArmed", false);
        anim.SetBool("isBlocking", false);
    }

    void PutSwordAwayInstantly()
    {
        ClearSwordAnim();
        equipmentManager.SheathWeapon();
    }

    public void DrawSwordEvent() { if (isEquipped && equipmentManager != null) equipmentManager.DrawWeapon(); }

    IEnumerator DrawFallback()
    {
        yield return new WaitForSeconds(drawFallbackDelay * DrawSpeedFactor);
        DrawSwordEvent();
    }

    public void SwapSwordToWaistEvent() { if (equipmentManager != null) equipmentManager.SheathWeapon(); }

    public void DealDamageEvent() { if (useAnimationEvents) DealDamage(pendingMultiplier, pendingKick); }

    void DealDamage(float multiplier, bool kick = false)
    {
        float damage = (equipmentManager != null ? equipmentManager.CurrentWeaponDamage : 0f) * multiplier * ItemUpgrades.DamageMultiplier(stats);
        if (damage <= 0f) return;
        int mask = enemyLayer.value != 0 ? enemyLayer.value : Physics.AllLayers;

        var alreadyHit = new HashSet<EnemyAI_01>();
        var animalsHit = new HashSet<IAnimalTarget>();
        Vector3 center = HitCenter();
        Vector3 sparkPoint = center;
        foreach (Collider hit in Physics.OverlapSphere(center, attackRadius, mask, QueryTriggerInteraction.Ignore))
        {
            EnemyAI_01 enemy = hit.GetComponentInParent<EnemyAI_01>();
            if (enemy == null)
            {
                // Animals (fox...)
                IAnimalTarget animal = hit.GetComponentInParent<IAnimalTarget>();
                if (animal != null && !animal.IsDead && animalsHit.Add(animal))
                {
                    animal.Hit(damage, transform.position, kick ? kickKnockback : knockback);
                    if (alreadyHit.Count + animalsHit.Count == 1) sparkPoint = hit.ClosestPoint(center);
                }
                continue;
            }
            if (!alreadyHit.Add(enemy)) continue;
            PlayerFinisher.MeleeHit = true; // a kill from this hit can start a finisher
            enemy.TakeDamage(damage, transform.position, kick ? kickKnockback : knockback);
            PlayerFinisher.MeleeHit = false;
            if (alreadyHit.Count + animalsHit.Count == 1) sparkPoint = hit.ClosestPoint(center);
        }

        if (alreadyHit.Count + animalsHit.Count > 0)
        {
            HitImpact(sparkPoint);
            if (!kick) SoundManager.Play(Sfx.SwordHit, sparkPoint); // a kick only makes the enemy's hurt sound
            // Kitsune (Oni Stone): heal part of the damage dealt
            if (stats != null && ItemUpgrades.Active(OniSpirit.Kitsune)) stats.Heal(damage * ItemUpgrades.KitsuneLifeSteal * (alreadyHit.Count + animalsHit.Count));
        }
    }

    // ---------- Hit impact: freeze frame, camera shake, sparks ----------

    private static bool inHitStop;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetHitStop()
    {
        inHitStop = false;
        Time.timeScale = 1f;
    }

    void HitImpact(Vector3 point)
    {
        CameraController.Shake(cameraShake, 0.15f);
        if (hitSparks) SpawnSparks(point, 8);
        if (hitStop && !inHitStop) StartCoroutine(HitStop(hitStopDuration));
    }

    IEnumerator HitStop(float duration)
    {
        inHitStop = true;
        Time.timeScale = hitStopTimeScale;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1f;
        inHitStop = false;
    }

    void OnDisable()
    {
        UpdateCrosshair(false);
        if (inHitStop)
        {
            Time.timeScale = 1f;
            inHitStop = false;
        }
    }

    // ---------- Bow crosshair ----------

    private Image crosshair;
    private static Sprite dotSprite;

    void UpdateCrosshair(bool visible)
    {
        if (!visible)
        {
            if (crosshair != null) crosshair.enabled = false;
            return;
        }

        if (crosshair == null)
        {
            GameObject canvasObj = new GameObject("Crosshair Canvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            GameObject dot = new GameObject("Crosshair");
            dot.transform.SetParent(canvasObj.transform, false);
            crosshair = dot.AddComponent<Image>();
            crosshair.raycastTarget = false;
            RectTransform rect = crosshair.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
        }

        crosshair.sprite = crosshairSprite != null ? crosshairSprite : GetDotSprite();
        crosshair.color = crosshairColor;
        crosshair.rectTransform.sizeDelta = Vector2.one * Mathf.Max(1f, crosshairSize);
        crosshair.enabled = true;
    }

    static Sprite GetDotSprite()
    {
        if (dotSprite != null) return dotSprite;
        const int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)))));
        tex.Apply();
        return dotSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    void OnDestroy()
    {
        if (crosshair != null) Destroy(crosshair.canvas.gameObject);
        foreach (ParticleSystem ps in sparkPool) if (ps != null) Destroy(ps.gameObject);
        foreach (ParticleSystem ps in activeSparks) if (ps != null) Destroy(ps.gameObject);
    }

    private static Material sparkMaterial;

    // Hit sparks are recycled: a few particle systems are created once and reused for every hit
    private readonly Stack<ParticleSystem> sparkPool = new Stack<ParticleSystem>();
    private readonly List<ParticleSystem> activeSparks = new List<ParticleSystem>();
    private readonly List<float> sparkReleaseAt = new List<float>();
    private readonly ParticleSystem.Burst[] sparkBurst = new ParticleSystem.Burst[1];

    void SpawnSparks(Vector3 position, int count)
    {
        ParticleSystem ps = null;
        while (sparkPool.Count > 0 && ps == null) ps = sparkPool.Pop();
        if (ps == null) ps = CreateSparkSystem();

        ps.transform.position = position;
        ps.gameObject.SetActive(true);
        sparkBurst[0] = new ParticleSystem.Burst(0f, (short)count);
        var emission = ps.emission;
        emission.SetBursts(sparkBurst);
        ps.Clear();
        ps.Play();

        activeSparks.Add(ps);
        sparkReleaseAt.Add(Time.unscaledTime + 1f);
    }

    void ReleaseSparks()
    {
        for (int i = activeSparks.Count - 1; i >= 0; i--)
        {
            ParticleSystem ps = activeSparks[i];
            if (ps != null && Time.unscaledTime < sparkReleaseAt[i]) continue;
            if (ps != null)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.gameObject.SetActive(false);
                sparkPool.Push(ps);
            }
            activeSparks.RemoveAt(i);
            sparkReleaseAt.RemoveAt(i);
        }
    }

    ParticleSystem CreateSparkSystem()
    {
        GameObject go = new GameObject("HitSparks");
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.duration = 0.2f;
        main.loop = false;
        main.useUnscaledTime = true; // keeps flying during the freeze frame
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.01f);
        main.startColor = sparkColor;
        main.gravityModifier = 1.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 40;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.02f;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.015f;
        renderer.lengthScale = 0.5f;
        if (sparkMaterial == null) sparkMaterial = ParticleMaterials.Create("HitSparks", ParticleMaterials.SoftDot(), true);
        renderer.sharedMaterial = sparkMaterial;

        go.SetActive(false);
        return ps;
    }

    Vector3 HitCenter() => attackPoint != null ? attackPoint.position : transform.position + transform.forward * attackDistance + Vector3.up * attackHeight;

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(HitCenter(), attackRadius);
    }
}
