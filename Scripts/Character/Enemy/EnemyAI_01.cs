using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI_01 : MonoBehaviour
{
    public enum CombatStyle { Melee, Archer }

    [Header("Combat Style")]
    [Tooltip("Melee = fights up close with the sword. Archer = keeps its distance and shoots arrows")]
    public CombatStyle combatStyle = CombatStyle.Melee;
    public bool IsArcher => combatStyle == CombatStyle.Archer;

    [Header("References")]
    public Transform player;
    public Animator anim;
    private NavMeshAgent agent;

    [Header("Health")]
    public float maxHealth = 100f;
    private float currentHealth;

    [Header("Boss")]
    [Tooltip("Tick on the boss. While fighting it, the camera gets closer and locks onto it")] public bool isBoss;
    [Tooltip("Name shown above the boss health bar at the top of the screen (empty = the object's name)")] public string bossName = "";

    public float CurrentHealth => currentHealth;
    public float HealthFraction => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;
    public string DisplayName => !string.IsNullOrEmpty(bossName) ? bossName : NetVisuals.KeyOf(gameObject.name);
    // Last time it took damage (the boss bar shows while you fight it)
    public float LastDamagedTime { get; private set; } = -999f;

    [Header("AI Vision & Ranges")]
    public float chaseRange = 10f;
    public float attackRange = 2f;
    public float loseSightRange = 15f;
    [Tooltip("Gives up the chase when this far from its starting point")] public float maxChaseDistance = 20f;
    [Tooltip("Seconds the enemy ignores the player after giving up")] public float giveUpTime = 4f;

    [Header("Wandering")]
    public float wanderRadius = 8f;
    public float wanderSpeed = 1.5f;
    public float chaseSpeed = 3.5f;
    public float wanderWaitMin = 2f;
    public float wanderWaitMax = 5f;

    [Header("Combat Settings")]
    public float attackCooldown = 2f;
    public float attackDamage = 15f;
    public float attackWindup = 0.4f;

    [Header("Attack Variety (needs the Animator triggers named below, otherwise skipped)")]
    [Tooltip("Chance (0-1) that a normal attack is the 2nd attack instead. Needs an Animator Trigger called 'Attack2Trigger'")] [Range(0f, 1f)] public float attack2Chance = 0.4f;
    [Tooltip("Damage of the 2nd attack compared to the normal one")] public float attack2DamageMultiplier = 1.25f;
    [Tooltip("Seconds from the start of the 2nd attack until it hits (match your animation)")] public float attack2Windup = 0.55f;
    [Tooltip("Boss only: chance (0-1) of the special attack. Needs an Animator Trigger called 'SpecialAttackTrigger'")] [Range(0f, 1f)] public float specialChance = 0.3f;
    [Tooltip("Boss only: minimum seconds between two special attacks")] public float specialCooldown = 8f;
    [Tooltip("Damage of the special attack compared to the normal one")] public float specialDamageMultiplier = 2f;
    [Tooltip("Seconds from the start of the special attack until it hits (match your animation)")] public float specialWindup = 0.9f;
    [Tooltip("The special attack's hit zone is this many times bigger")] public float specialRadiusMultiplier = 1.6f;
    private float lastSpecialTime = -999f;

    [Header("Hit Armor (stops attack spamming from locking the enemy)")]
    [Tooltip("If the enemy takes a lot of damage without getting to attack, it stops flinching for a while and keeps attacking")] public bool hitArmor = true;
    [Tooltip("On = only the boss gets this. Off = every enemy")] public bool hitArmorOnlyBoss = true;
    [Tooltip("Damage taken (without the enemy attacking in between) that triggers the armor")] public float armorDamageThreshold = 200f;
    [Tooltip("The damage counter is forgotten if no hit lands for this many seconds")] public float armorForgetTime = 3f;
    [Tooltip("Seconds the enemy ignores hit reactions (no flinch, no stun, no knockback) once the armor triggers")] public float armorDuration = 4f;
    private float armorDamage, armorLastHit = -999f, armorUntil;
    public bool IsArmored => Time.time < armorUntil;

    [Header("Hit Zone (red sphere in front of the enemy)")]
    [Tooltip("How far in front of the enemy the hit zone is")] public float attackDistance = 0.6f;
    [Tooltip("How high the hit zone is")] public float attackHeight = 0.6f;
    [Tooltip("Size of the hit zone - the player gets hit if any part of them is inside it")] public float attackRadius = 0.6f;
    private float lastAttackTime;

    [Header("Archer (only used when Combat Style = Archer)")]
    [Tooltip("Shoots when the player is closer than this")] public float shootRange = 16f;
    [Tooltip("If the player gets closer than this, the archer walks backwards")] public float tooCloseDistance = 5f;
    [Tooltip("Seconds between shots")] public float shootCooldown = 2.5f;
    [Tooltip("Seconds from the start of the shoot animation until the arrow leaves the bow")] public float drawTime = 0.7f;
    [Tooltip("Seconds the archer stays still after the arrow leaves (stands still for Draw Time + this)")] public float shootStandTime = 0.4f;
    public float arrowDamage = 10f;
    public float arrowSpeed = 30f;
    [Tooltip("Random miss angle in degrees (0 = perfect aim)")] public float aimError = 2.5f;
    [Tooltip("Trigger played when shooting (use an Animator Override Controller to put a bow animation on 'Attack')")] public string shootTrigger = "AttackTrigger";
    [Tooltip("Optional: your arrow prefab (empty = simple arrow)")] public GameObject arrowPrefab;
    [Tooltip("Where the arrow comes out (e.g. an empty object on the bow). Empty = in front of the chest")] public Transform arrowSpawnPoint;
    [Tooltip("Size of the default arrow")] public float arrowScale = 0.6f;
    private float lastShotTime = -999f;
    private float nextSightCheck;
    private bool canSeePlayer = true;
    private float standStillUntil;

    [Header("Group Combat (one at a time)")]
    [Tooltip("How many enemies can fight the player at the same time - the others wait around")] [Min(1)] public int maxAttackers = 1;
    [Tooltip("Distance from the player where waiting enemies stand")] public float waitRadius = 4.5f;
    [Tooltip("Speed while circling around the player")] public float circleSpeed = 1.2f;
    [Tooltip("Seconds before the next enemy charges in after one dies")] public float nextAttackerDelay = 0.4f;
    [Tooltip("Hitting a waiting enemy makes it fight you right away (swaps with the current attacker)")] public bool hitEnemyJoinsFight = true;
    [Tooltip("If you get this close to a waiting enemy, it becomes the attacker (0 = off)")] public float closeSwapDistance = 2.2f;
    [Tooltip("Minimum seconds between attacker swaps (so they don't keep switching)")] public float swapCooldown = 1.5f;

    [Header("Loot")]
    [Tooltip("Items this enemy can drop, each with its own chance (1 = always)")] public List<LootEntry> lootTable = new List<LootEntry>();
    [Tooltip("Leave empty to use the Loot Bag Prefab from PlayerInventoryManager")] public GameObject lootBagPrefab;

    [Header("Hit Feedback")]
    public Color hitColor = Color.red;
    public float hitFlashTime = 0.15f;

    [Header("Animation")]
    [Tooltip("Seconds the enemy stays stunned after being hit")] public float hitStunTime = 0.5f;

    private Renderer[] renderers;
    private readonly HashSet<string> animParams = new HashSet<string>();
    private float stunUntil, nextWanderTime;
    private bool hasWanderTarget;
    private float ignorePlayerUntil;
    private Color[] originalColors;
    private Vector3 homePosition;
    private bool isChasing, isDead;
    public bool IsDead => isDead;
    public Vector3 HomePosition => Application.isPlaying ? homePosition : transform.position;
    // Chasing or fighting the player (used for the combat music)
    public bool IsInCombat => isChasing && !isDead;

    // ---------- Group combat: attack "tokens" shared by all enemies ----------
    // Every active enemy (cheaper than FindObjectsByType every time something needs to look at them)
    public static readonly List<EnemyAI_01> All = new List<EnemyAI_01>();

    // ---------- Multiplayer (see MultiplayerSync) ----------
    // On a client: this enemy is a copy of the host's enemy (no AI here, hits are sent to the host)
    [System.NonSerialized] public bool isNetworkMirror;
    [System.NonSerialized] public int netId;
    // A copy was hit by this player: (enemy, damage, attacker position, knockback)
    public static System.Action<EnemyAI_01, float, Vector3, float> MirrorHit;
    // A real enemy took damage: (enemy, where the hit came from)
    public static event System.Action<EnemyAI_01, Vector3> Damaged;
    // On the host with other players: picks which player to go after (null = the local player, as in single player)
    public static System.Func<Vector3, Transform> TargetPicker;
    private bool noFinisher;
    private float nextTargetPick;
    private static readonly RaycastHit[] sightHits = new RaycastHit[24];
    private float nextPathTime;
    private static readonly List<EnemyAI_01> engaged = new List<EnemyAI_01>();   // enemies fighting the player
    private static readonly List<EnemyAI_01> attackers = new List<EnemyAI_01>(); // the ones allowed to attack now
    private static float slotFreeAt;
    private static float lastSwapTime = -999f;
    private bool HasToken => attackers.Contains(this);

    // Statics survive between Play sessions (domain reload off): clear them on every Play
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetGroupCombat()
    {
        All.Clear();
        engaged.Clear();
        attackers.Clear();
        slotFreeAt = 0f;
        lastSwapTime = -999f;
        MirrorHit = null;
        Damaged = null;
        TargetPicker = null;
    }
    private float defaultStoppingDistance, nextCircleUpdate;
    private float strafeDirection = 1f;
    private float nextStrafeFlip;
    private bool isWaiting; // circling around the player (walks instead of runs)

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.stoppingDistance = attackRange * 0.8f;
        defaultStoppingDistance = agent.stoppingDistance;
        strafeDirection = Random.value < 0.5f ? -1f : 1f;

        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim != null) foreach (AnimatorControllerParameter p in anim.parameters) animParams.Add(p.name);

        renderers = GetComponentsInChildren<Renderer>();
        originalColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) originalColors[i] = renderers[i].material.color;

        currentHealth = maxHealth;
        homePosition = transform.position;
    }

    void Update()
    {
        if (isNetworkMirror) return; // the host moves it
        if (isDead) return;
        if (!agent.isOnNavMesh)
        {
            if (Time.frameCount % 300 == 0) Debug.LogWarning(name + " is not on a NavMesh. Bake a NavMesh Surface!");
            return;
        }
        if (Time.time < stunUntil)
        {
            agent.isStopped = true;
            StopMoveAnims();
            return;
        }
        // Multiplayer host: go for the closest player
        if (TargetPicker != null && Time.time >= nextTargetPick)
        {
            nextTargetPick = Time.time + 0.5f;
            Transform picked = TargetPicker(transform.position);
            if (picked != null) player = picked;
        }

        // No player, or the one we had was turned off (e.g. the extra copy from the menu): look for the real one
        if (player == null || !player.gameObject.activeInHierarchy)
        {
            FindPlayer();
            Wander();
            UpdateMoveAnim();
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        if (isChasing && Vector3.Distance(transform.position, homePosition) > maxChaseDistance) GiveUp();

        isWaiting = false;
        bool wantsToFight = Time.time >= ignorePlayerUntil &&
                            (distanceToPlayer <= chaseRange || (isChasing && distanceToPlayer <= loseSightRange));

        if (!wantsToFight)
        {
            LeaveFight();
            Wander();
        }
        else if (IsArcher) ArcherFight(distanceToPlayer);
        else
        {
            JoinFight();
            if (!HasToken && closeSwapDistance > 0f && distanceToPlayer <= closeSwapDistance) TakeTokenFromAttacker(false);
            TryTakeToken();

            if (HasToken)
            {
                ResetSteering();
                if (distanceToPlayer <= attackRange) AttackPlayer();
                else ChasePlayer();
            }
            else WaitAroundPlayer(distanceToPlayer);
        }

        UpdateMoveAnim();
    }

    void JoinFight()
    {
        if (!engaged.Contains(this)) engaged.Add(this);
    }

    void LeaveFight()
    {
        Disengage();
        ResetSteering();
    }

    void Disengage()
    {
        engaged.Remove(this);
        ReleaseToken();
    }

    void ResetSteering()
    {
        agent.updateRotation = true;
        agent.stoppingDistance = defaultStoppingDistance;
    }

    void ReleaseToken()
    {
        if (attackers.Remove(this)) slotFreeAt = Time.time + nextAttackerDelay;
    }

    // A free slot goes to the closest waiting enemy
    void TryTakeToken()
    {
        engaged.RemoveAll(e => e == null || e.isDead);
        attackers.RemoveAll(e => e == null || e.isDead);
        if (slotFreeAt > Time.time + nextAttackerDelay + 1f) slotFreeAt = 0f; // old value from a previous Play
        if (HasToken || attackers.Count >= maxAttackers || Time.time < slotFreeAt) return;

        EnemyAI_01 closest = null;
        float best = float.MaxValue;
        foreach (EnemyAI_01 e in engaged)
        {
            if (e.HasToken || e.player == null) continue;
            float d = Vector3.Distance(e.transform.position, e.player.position);
            if (d < best) { best = d; closest = e; }
        }
        if (closest == this) attackers.Add(this);
    }

    // Becomes the attacker right away; the current attacker goes back to waiting
    void TakeTokenFromAttacker(bool force)
    {
        if (HasToken) return;
        if (lastSwapTime > Time.time) lastSwapTime = -999f; // old value from a previous Play
        if (!force && Time.time < lastSwapTime + swapCooldown) return;
        attackers.RemoveAll(e => e == null || e.isDead);
        if (attackers.Count >= maxAttackers && attackers.Count > 0) attackers.RemoveAt(0);
        attackers.Add(this);
        lastSwapTime = Time.time;
    }

    // Waiting enemies keep their distance, face the player and slowly circle around
    void WaitAroundPlayer(float distanceToPlayer)
    {
        isChasing = true;
        hasWanderTarget = false;
        agent.isStopped = false;
        bool farAway = distanceToPlayer > waitRadius + 3f;
        agent.speed = farAway ? chaseSpeed : circleSpeed; // catch up if far, walk if close
        isWaiting = !farAway;
        agent.stoppingDistance = 0.3f;
        agent.updateRotation = farAway;
        if (!agent.updateRotation) FaceTarget();

        if (Time.time >= nextStrafeFlip)
        {
            nextStrafeFlip = Time.time + Random.Range(2f, 5f);
            if (Random.value < 0.4f) strafeDirection = -strafeDirection;
        }

        if (Time.time < nextCircleUpdate) return;
        nextCircleUpdate = Time.time + 0.25f;

        Vector3 fromPlayer = transform.position - player.position;
        fromPlayer.y = 0f;
        if (fromPlayer.sqrMagnitude < 0.01f) fromPlayer = -player.forward;
        fromPlayer.Normalize();

        // Slowly move around the circle
        Vector3 around = Quaternion.Euler(0f, strafeDirection * 15f, 0f) * fromPlayer;
        Vector3 target = player.position + around * waitRadius;

        // Don't bunch up with the other waiting enemies
        foreach (EnemyAI_01 other in engaged)
        {
            if (other == null || other == this || other.isDead) continue;
            Vector3 away = transform.position - other.transform.position;
            away.y = 0f;
            float d = away.magnitude;
            if (d < 2f && d > 0.01f) target += away.normalized * (2f - d);
        }

        MoveToNavPoint(target);
    }

    // Sets the destination to the nearest NavMesh point (within 2m); false if none
    bool MoveToNavPoint(Vector3 target)
    {
        if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 2f, NavMesh.AllAreas)) return false;
        agent.SetDestination(hit.position);
        return true;
    }

    void OnEnable()
    {
        nextPathTime = Time.time + Random.value * 0.2f; // enemies do not all path-find in the same frame
        if (!All.Contains(this)) All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
        Disengage();
    }

    // Path-finding is the most expensive thing an enemy does: update the chase target 5 times a second, not every frame
    void SetChaseDestination(Vector3 destination)
    {
        if (Time.time < nextPathTime) return;
        nextPathTime = Time.time + 0.2f;
        agent.SetDestination(destination);
    }

    void UpdateMoveAnim()
    {
        bool moving = agent.velocity.sqrMagnitude > 0.05f;

        // Circling sideways around the player: strafe left/right (if the Animator has those bools)
        Vector3 local = transform.InverseTransformDirection(agent.velocity);
        bool sideways = isWaiting && moving && Mathf.Abs(local.x) > 0.15f && Mathf.Abs(local.x) >= Mathf.Abs(local.z) * 0.7f;
        bool canStrafe = animParams.Contains("isStrafingLeft") && animParams.Contains("isStrafingRight");
        bool strafing = sideways && canStrafe;
        SetStrafeAnim(strafing && local.x < 0f, strafing && local.x > 0f);

        SetChasingAnim(moving && isChasing && !isWaiting);
        if (anim != null && animParams.Contains("isWalking")) anim.SetBool("isWalking", moving && (!isChasing || isWaiting) && !strafing);
    }

    void SetStrafeAnim(bool left, bool right)
    {
        if (anim == null) return;
        if (animParams.Contains("isStrafingLeft")) anim.SetBool("isStrafingLeft", left);
        if (animParams.Contains("isStrafingRight")) anim.SetBool("isStrafingRight", right);
    }

    void SetChasingAnim(bool chasing)
    {
        if (anim != null && animParams.Contains("isChasing")) anim.SetBool("isChasing", chasing);
    }

    void StopMoveAnims()
    {
        SetChasingAnim(false);
        SetStrafeAnim(false, false);
    }

    void GiveUp()
    {
        LeaveFight();
        isChasing = false;
        ignorePlayerUntil = Time.time + giveUpTime;
        agent.isStopped = false;
        agent.speed = wanderSpeed;
        agent.SetDestination(homePosition);
        hasWanderTarget = true;
        Debug.Log(name + " gave up the chase.");
    }

    // The player died fighting a boss: every boss heals to full and goes back home (host / single player)
    public static void ResetBossesAfterPlayerDeath()
    {
        foreach (EnemyAI_01 e in All)
            if (e != null && e.isBoss && !e.isDead && !e.isNetworkMirror) e.ResetAfterPlayerDeath();
    }

    void ResetAfterPlayerDeath()
    {
        currentHealth = maxHealth;
        if (agent != null && agent.isOnNavMesh) GiveUp(); // stops fighting and walks back to where it started
        else { isChasing = false; ignorePlayerUntil = Time.time + giveUpTime; }
        Debug.Log(name + " healed to full: the player died.");
    }

    // ---------- Patrol (RoamingPatrolSpawner): walks in a group to a point; fights when it sees a player, then goes on ----------

    private bool patrolling;
    private Vector3 patrolDestination;
    private float nextPatrolPath;
    public bool IsPatrolling => patrolling;
    public bool FinishedPatrol { get; private set; }

    public void StartPatrol(Vector3 destination)
    {
        patrolling = true;
        FinishedPatrol = false;
        patrolDestination = destination;
        hasWanderTarget = false;
        nextPatrolPath = 0f;
    }

    void Patrol()
    {
        agent.isStopped = false;
        agent.speed = wanderSpeed;
        homePosition = transform.position; // if it starts a fight, it gives up relative to where the fight began
        if (Time.time >= nextPatrolPath)
        {
            nextPatrolPath = Time.time + 0.5f;
            if (!MoveToNavPoint(patrolDestination)) agent.SetDestination(patrolDestination);
        }
        Vector3 left = patrolDestination - transform.position;
        left.y = 0f;
        if (left.sqrMagnitude < 2.5f * 2.5f)
        {
            patrolling = false;
            FinishedPatrol = true;
            nextWanderTime = Time.time + Random.Range(wanderWaitMin, wanderWaitMax);
        }
    }

    void Wander()
    {
        if (isChasing)
        {
            isChasing = false;
            hasWanderTarget = false;
            nextWanderTime = Time.time + Random.Range(wanderWaitMin, wanderWaitMax);
        }

        if (patrolling)
        {
            Patrol();
            return;
        }

        agent.isStopped = false;
        agent.speed = wanderSpeed;

        if (hasWanderTarget)
        {
            if (agent.pathPending || agent.remainingDistance > agent.stoppingDistance + 0.2f) return;
            hasWanderTarget = false;
            nextWanderTime = Time.time + Random.Range(wanderWaitMin, wanderWaitMax);
            return;
        }
        if (Time.time < nextWanderTime) return;

        Vector2 random = Random.insideUnitCircle * wanderRadius;
        if (MoveToNavPoint(homePosition + new Vector3(random.x, 0f, random.y))) hasWanderTarget = true;
        else nextWanderTime = Time.time + 0.5f;
    }

    void PlayTrigger(string trigger)
    {
        if (anim != null && animParams.Contains(trigger)) anim.SetTrigger(trigger);
    }

    // Only called while player == null
    void FindPlayer() => PlayerRef.Find(ref player);

    void ChasePlayer()
    {
        isChasing = true;
        hasWanderTarget = false;
        agent.speed = chaseSpeed;
        agent.isStopped = false;
        SetChaseDestination(player.position);
    }

    // ---------- Archer ----------

    void ArcherFight(float distanceToPlayer)
    {
        isChasing = true;
        hasWanderTarget = false;

        // Shooting: stand still and keep looking at the player
        if (Time.time < standStillUntil)
        {
            StandStill();
            FaceTarget();
            return;
        }

        agent.isStopped = false;
        if (Time.time >= nextSightCheck)
        {
            nextSightCheck = Time.time + 0.3f;
            canSeePlayer = HasLineOfSight();
        }

        if (distanceToPlayer > shootRange || !canSeePlayer)
        {
            // Get closer (or find a spot where the player can be seen)
            agent.updateRotation = true;
            agent.stoppingDistance = 0.5f;
            agent.speed = chaseSpeed;
            SetChaseDestination(player.position);
            return;
        }

        agent.updateRotation = false;
        FaceTarget();

        if (distanceToPlayer < tooCloseDistance)
        {
            // Too close: step back while still looking at the player
            isWaiting = true; // walk animation, not run
            agent.speed = circleSpeed * 1.5f;
            agent.stoppingDistance = 0.2f;
            Vector3 away = transform.position - player.position;
            away.y = 0f;
            MoveToNavPoint(transform.position + away.normalized * 3f);
        }
        else
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        if (Time.time >= lastShotTime + shootCooldown && Time.time >= lastAttackTime + 0.2f)
        {
            lastShotTime = Time.time;
            lastAttackTime = Time.time;
            standStillUntil = Time.time + drawTime + shootStandTime;
            StandStill();
            PlayTrigger(shootTrigger);
            SoundManager.Play(Sfx.BowDraw, transform.position + Vector3.up * 1.5f);
            StartCoroutine(ShootAfterDraw());
        }
    }

    void StandStill()
    {
        agent.isStopped = true;
        agent.updateRotation = false;
        if (agent.hasPath) agent.ResetPath();
        agent.velocity = Vector3.zero;
        SetStrafeAnim(false, false);
    }

    bool HasLineOfSight()
    {
        Vector3 from = ArrowStart();
        Vector3 dir = PlayerChest() - from;
        int count = Physics.RaycastNonAlloc(from, dir.normalized, sightHits, dir.magnitude, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = sightHits[i];
            if (hit.collider.transform.IsChildOf(transform)) continue;   // own body
            if (hit.collider.transform.IsChildOf(player)) continue;      // the player
            if (hit.collider.GetComponentInParent<EnemyAI_01>() != null) continue; // other enemies don't block
            return false; // a wall, rock or tree is in the way
        }
        return true;
    }

    Vector3 PlayerChest()
    {
        Collider c = player.GetComponent<Collider>();
        return c != null ? c.bounds.center + Vector3.up * c.bounds.extents.y * 0.3f : player.position + Vector3.up * 1.3f;
    }

    Vector3 ArrowStart() => arrowSpawnPoint != null ? arrowSpawnPoint.position : transform.position + Vector3.up * 1.4f + transform.forward * 0.5f;

    IEnumerator ShootAfterDraw()
    {
        yield return new WaitForSeconds(drawTime);
        if (isDead || player == null || Time.time < stunUntil) yield break; // hit while drawing: no shot

        Vector3 start = ArrowStart();
        Vector3 target = PlayerChest();

        // Aim a bit higher so gravity brings the arrow down onto the player
        float flatDistance = Vector3.Distance(new Vector3(start.x, 0f, start.z), new Vector3(target.x, 0f, target.z));
        float flightTime = flatDistance / Mathf.Max(1f, arrowSpeed);
        target.y += 0.5f * Mathf.Abs(Physics.gravity.y) * flightTime * flightTime;

        Vector3 direction = (target - start).normalized;
        direction = Quaternion.Euler(Random.Range(-aimError, aimError), Random.Range(-aimError, aimError), 0f) * direction;

        GameObject arrow = arrowPrefab != null ? Instantiate(arrowPrefab) : Arrow.CreateDefault();
        arrow.transform.position = start + direction * 0.3f;
        if (arrowPrefab == null) arrow.transform.localScale *= arrowScale;
        if (!arrow.TryGetComponent(out Arrow arrowScript)) arrowScript = arrow.AddComponent<Arrow>();
        arrowScript.Launch(direction * arrowSpeed, arrowDamage, transform);
        SoundManager.Play(Sfx.ArrowRelease, start);
    }

    void AttackPlayer()
    {
        isChasing = true;
        agent.isStopped = true;
        FaceTarget();
        if (Time.time < lastAttackTime + attackCooldown) return;

        lastAttackTime = Time.time;
        armorDamage = 0f; // it got to attack: the damage counter starts again

        // Pick the attack: boss special > 2nd attack > normal (a variant is only used if the Animator has its trigger)
        string trigger = "AttackTrigger";
        float windup = attackWindup, damage = attackDamage, radiusScale = 1f;
        if (isBoss && animParams.Contains("SpecialAttackTrigger") && Time.time >= lastSpecialTime + specialCooldown && Random.value < specialChance)
        {
            trigger = "SpecialAttackTrigger";
            windup = specialWindup;
            damage = attackDamage * specialDamageMultiplier;
            radiusScale = specialRadiusMultiplier;
            lastSpecialTime = Time.time;
        }
        else if (animParams.Contains("Attack2Trigger") && Random.value < attack2Chance)
        {
            trigger = "Attack2Trigger";
            windup = attack2Windup;
            damage = attackDamage * attack2DamageMultiplier;
        }

        PlayTrigger(trigger);
        SoundManager.Play(Sfx.EnemyAttack, transform.position + Vector3.up, windup * 0.6f);
        StartCoroutine(DealDamageAfterWindup(windup, damage, radiusScale));
    }

    IEnumerator DealDamageAfterWindup(float windup, float damage, float radiusScale)
    {
        yield return new WaitForSeconds(windup);
        if (isDead || player == null || Time.time < stunUntil) yield break;
        if (!PlayerInHitZone(radiusScale)) yield break;
        if (player.TryGetComponent(out PlayerStats playerStats)) playerStats.TakeDamage(damage, transform.position, isBoss ? Sfx.BossHit : Sfx.EnemyHit);
        else if (player.TryGetComponent(out RemotePlayerAvatar remotePlayer)) remotePlayer.ApplyDamage(damage, transform.position);
    }

    Vector3 HitCenter() => transform.position + transform.forward * attackDistance + Vector3.up * attackHeight;

    bool PlayerInHitZone(float radiusScale = 1f)
    {
        Vector3 center = HitCenter();
        Collider playerCollider = player.GetComponent<Collider>();
        Vector3 closest = playerCollider != null ? playerCollider.ClosestPoint(center) : player.position + Vector3.up * attackHeight;
        return Vector3.Distance(center, closest) <= attackRadius * radiusScale;
    }

    void FaceTarget()
    {
        Vector3 direction = player.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 5f);
    }

    // Hit by the player's sword: damage + a small push away from the player
    public void TakeDamage(float damageAmount, Vector3 attackerPosition, float knockbackDistance)
    {
        if (isDead) return;
        if (isNetworkMirror)
        {
            MirrorHitFeedback(attackerPosition);
            MirrorHit?.Invoke(this, damageAmount, attackerPosition, knockbackDistance);
            return;
        }
        bloodFrom = attackerPosition;
        TakeDamage(damageAmount);
        if (isDead || IsArmored || knockbackDistance <= 0f || agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Vector3 away = transform.position - attackerPosition;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f) away = -transform.forward;
        StopCoroutine(nameof(Knockback));
        StartCoroutine(Knockback(away.normalized * knockbackDistance));
    }

    // Slides back quickly at first, then slows down
    IEnumerator Knockback(Vector3 push)
    {
        const float duration = 0.18f;
        float t = 0f;
        while (t < duration && !isDead && agent != null && agent.enabled && agent.isOnNavMesh)
        {
            float dt = Time.deltaTime;
            t += dt;
            float k = 2f * (1f - Mathf.Clamp01(t / duration)); // integrates to 1 over the duration
            agent.Move(push * (k * dt / duration));
            yield return null;
        }
    }

    public void TakeDamage(float damageAmount)
    {
        if (isDead) return;
        if (isNetworkMirror)
        {
            Vector3 from = bloodFrom ?? transform.position + transform.forward;
            bloodFrom = null;
            MirrorHitFeedback(from);
            MirrorHit?.Invoke(this, damageAmount, from, 0f);
            return;
        }
        currentHealth -= damageAmount;
        LastDamagedTime = Time.time;
        Damaged?.Invoke(this, bloodFrom ?? transform.position + transform.forward);
        isChasing = true;

        // Blood flies away from whoever hit us
        Vector3 center = BodyCenter();
        Vector3 away = bloodFrom.HasValue ? center - bloodFrom.Value : -transform.forward;
        away.y = 0f;
        BloodEffects.Spawn(center, away.normalized + Vector3.up * 0.35f);
        SoundManager.Play(currentHealth <= 0 ? Sfx.EnemyDeath : Sfx.EnemyHurt, center);
        bloodFrom = null;
        Debug.Log($"Enemy hit! Damage: {damageAmount} | Health left: {currentHealth}");

        StopCoroutine(nameof(HitFlash));
        StartCoroutine(nameof(HitFlash));

        if (currentHealth <= 0)
        {
            // Sword kill: small chance of a finisher (the enemy waits for it, then dies)
            if (!noFinisher && PlayerFinisher.TryStart(this)) StopForDeath();
            else Die();
            return;
        }

        // Hit armor: too much damage in a row without the enemy attacking = it shrugs the hits off for a while
        if (hitArmor && (isBoss || !hitArmorOnlyBoss))
        {
            if (Time.time - armorLastHit > armorForgetTime) armorDamage = 0f;
            armorLastHit = Time.time;
            armorDamage += damageAmount;
            if (armorDamage >= armorDamageThreshold)
            {
                armorUntil = Time.time + armorDuration;
                armorDamage = 0f;
            }
        }
        if (IsArmored)
        {
            stunUntil = 0f; // cancels a flinch that was already playing
            return;
        }

        stunUntil = Time.time + hitStunTime;
        PlayTrigger("HitTrigger");

        // You attacked a waiting enemy: it takes the place of the current attacker
        if (hitEnemyJoinsFight && !IsArcher && !HasToken && player != null)
        {
            JoinFight();
            TakeTokenFromAttacker(true);
        }
    }

    private Vector3? bloodFrom;

    Vector3 BodyCenter()
    {
        Collider c = GetComponent<Collider>();
        return c != null && c.enabled ? c.bounds.center + Vector3.up * c.bounds.extents.y * 0.3f : transform.position + Vector3.up * 1.2f;
    }

    IEnumerator HitFlash()
    {
        SetColor(hitColor);
        yield return new WaitForSeconds(hitFlashTime);
        if (!isDead) RestoreColors();
    }

    void Die()
    {
        StopForDeath();
        FinishDeath(true);
    }

    // ---------- Finisher ----------

    private bool finisherDeathDone;

    // Plays the enemy's finisher animation (if the Animator has that state). Returns false if it doesn't
    public bool PlayFinisherReaction(string state, float fade)
    {
        if (anim == null || string.IsNullOrEmpty(state) || !anim.HasState(0, Animator.StringToHash(state))) return false;
        anim.CrossFadeInFixedTime(state, fade, 0);
        return true;
    }

    // Called by PlayerFinisher when the finisher ends
    public void CompleteFinisherDeath(bool playDeathAnimation)
    {
        if (finisherDeathDone) return;
        finisherDeathDone = true;
        FinishDeath(playDeathAnimation);
    }

    void FinishDeath(bool playDeathAnimation)
    {
        if (playDeathAnimation) PlayTrigger("DieTrigger");
        DropLoot();
        WorldCleanup.Add(gameObject, WorldCleanup.Kind.EnemyCorpse);
    }

    // Stops everything (AI, NavMesh, colliders) - the enemy counts as dead from here
    void StopForDeath()
    {
        isDead = true;
        Disengage(); // the next enemy comes in after nextAttackerDelay
        Debug.Log("Enemy died!");

        StopAllCoroutines();
        agent.isStopped = true;
        agent.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

        RestoreColors();
        StopMoveAnims();
    }

    void DropLoot()
    {
        var drops = new List<PlayerItemData>();
        foreach (LootEntry entry in lootTable)
        {
            if (entry == null || entry.item == null || Random.value > entry.dropChance) continue;
            int amount = entry.RollAmount();
            for (int i = 0; i < amount; i++) drops.Add(entry.item);
        }

        // Lucky Find backpack perk: 35% chance of one extra item from this enemy's loot table
        if (PlayerEquipmentManager.PerkActive(BackpackPerk.LuckyFind) && Random.value <= 0.35f)
        {
            List<LootEntry> valid = lootTable.FindAll(e => e != null && e.item != null);
            if (valid.Count > 0) drops.Add(valid[Random.Range(0, valid.Count)].item);
        }
        if (drops.Count == 0) return;

        GameObject prefab = lootBagPrefab != null ? lootBagPrefab
            : PlayerInventoryManager.Instance != null ? PlayerInventoryManager.Instance.lootBagPrefab : null;
        LootBag.Spawn(prefab, transform.position + transform.forward * 0.6f, drops);
    }

    void SetColor(Color color)
    {
        foreach (Renderer r in renderers) r.material.color = color;
    }

    void RestoreColors()
    {
        for (int i = 0; i < renderers.Length; i++) renderers[i].material.color = originalColors[i];
    }

    // ---------- Multiplayer ----------

    public bool NetInCombat => isChasing && !isDead;

    // Damage sent by another player (no finisher: that player is on another computer)
    public void NetworkDamage(float amount, Vector3 attackerPosition, float knockback)
    {
        noFinisher = true;
        try { TakeDamage(amount, attackerPosition, knockback); }
        finally { noFinisher = false; }
    }

    // Copy on a client: blood, sound and flash when someone hits it
    public void MirrorHitFeedback(Vector3 from)
    {
        if (isDead || renderers == null) return;
        Vector3 center = BodyCenter();
        Vector3 away = center - from;
        away.y = 0f;
        BloodEffects.Spawn(center, away.normalized + Vector3.up * 0.35f);
        SoundManager.Play(Sfx.EnemyHurt, center);
        StopCoroutine(nameof(HitFlash));
        StartCoroutine(nameof(HitFlash));
    }

    // Copy on a client: the host tells how much health is left (for the boss bar)
    public void SetMirrorHealth(float fraction)
    {
        if (!isNetworkMirror || isDead) return;
        float before = currentHealth;
        currentHealth = Mathf.Clamp01(fraction) * maxHealth;
        if (currentHealth < before) LastDamagedTime = Time.time;
    }

    public void MirrorSetCombat(bool inCombat)
    {
        if (!isDead) isChasing = inCombat;
    }

    // Copy on a client: the host's enemy died
    public void MirrorDie(bool dropLoot)
    {
        if (isDead) return;
        isDead = true;
        isChasing = false;
        StopAllCoroutines();
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        if (renderers != null)
        {
            RestoreColors();
            SoundManager.Play(Sfx.EnemyDeath, BodyCenter());
        }
        if (dropLoot) DropLoot();
    }

    void OnDrawGizmosSelected()
    {
        Vector3 home = Application.isPlaying ? homePosition : transform.position;
        DrawSphere(Color.yellow, transform.position, chaseRange);
        DrawSphere(Color.magenta, home, maxChaseDistance);
        DrawSphere(Color.green, home, wanderRadius);
        DrawSphere(new Color(1f, 0.5f, 0f), transform.position, loseSightRange);
        DrawSphere(new Color(1f, 0.4f, 0.4f, 0.35f), transform.position, attackRange); // attack start distance
        DrawSphere(Color.red, HitCenter(), attackRadius); // where the attack actually hits
    }

    static void DrawSphere(Color color, Vector3 center, float radius)
    {
        Gizmos.color = color;
        Gizmos.DrawWireSphere(center, radius);
    }
}
