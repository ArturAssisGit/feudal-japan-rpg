using UnityEngine;
using UnityEngine.AI;
using static WorldUtil;

// A fox: it wanders (Idle <-> Walk). When the player gets close it runs away (Run). When it is killed it plays Death
// and stays on the ground for a while. Put it on the fox prefab (the root) and drag the 4 clips from the fox FBX.
// No Animator Controller needed (the model only needs its Animator + Avatar).
// If the scene has a NavMesh the fox uses it (goes around trees and rocks). If not, it walks straight on the terrain.
[DisallowMultipleComponent]
public class FoxAI : MonoBehaviour, IAnimalTarget
{
    [Header("Animation clips (from the fox FBX)")]
    public AnimationClip idleClip;
    public AnimationClip walkClip;
    public AnimationClip runClip;
    public AnimationClip deathClip;
    [Tooltip("Seconds to blend from one animation to the next")] public float blendSeconds = 0.2f;
    public float walkAnimSpeed = 1f;
    public float runAnimSpeed = 1f;

    [Header("Health")]
    public float maxHealth = 20f;
    [Tooltip("Layer the fox is put on so the sword and arrows can hit it (the layer in Player Combat > Enemy Layer). Empty = leave it as it is")] public string hitLayerName = "Enemy";

    [Header("Wandering")]
    public Vector2 idleSeconds = new Vector2(2f, 7f);
    public Vector2 walkSeconds = new Vector2(2f, 6f);
    public float walkSpeed = 1.3f;
    [Tooltip("How far from where it appeared the fox walks (meters)")] public float wanderRadius = 10f;
    [Tooltip("Raise or lower the model so the feet touch the ground (only without NavMesh)")] public float groundOffset = 0f;

    [Header("Running away")]
    [Tooltip("The fox runs when the player gets this close (meters)")] public float scareDistance = 10f;
    [Tooltip("Multiplier of that distance when the player is on the horse")] public float mountedScareMultiplier = 1.5f;
    public float runSpeed = 6f;
    [Tooltip("Each time it picks a new spot this far away from the player (meters)")] public float fleeDistance = 18f;
    [Tooltip("It stops running when the player is farther than this (meters)...")] public float calmDistance = 35f;
    [Tooltip("...for this many seconds")] public float calmSeconds = 3f;
    [Tooltip("Turn speed without NavMesh (degrees per second)")] public float turnSpeed = 360f;

    enum Mode { Idle, Walk, Flee, Dead }
    enum Anim { Idle = 0, Walk = 1, Run = 2, Death = 3 }

    private Mode mode = Mode.Idle;
    private Transform player;
    private Vector3 home, moveTarget, threat;
    private bool haveThreat;
    private float modeTimer, repathTimer, calmTimer, health;
    private NavMeshAgent agent;
    private ClipBlender blender;
    private readonly float[] speeds = new float[4];

    public bool IsDead => mode == Mode.Dead;

    void Start()
    {
        health = maxHealth;
        home = transform.position;
        SetupHitbox();
        SetupAgent();
        blender = ClipBlender.Create(this, new[] { "Idle", "Walk", "Run", "Death" }, new[] { idleClip, walkClip, runClip, deathClip },
            new[] { true, true, true, false }, blendSeconds);
        EnterIdle();
        if (!AgentActive) SnapToGround();
    }

    void OnDestroy() => blender?.Destroy();

    bool AgentActive => agent != null && agent.enabled && agent.isOnNavMesh;

    // Layer for the hits + a collider if the prefab has none
    void SetupHitbox()
    {
        int layer = string.IsNullOrEmpty(hitLayerName) ? -1 : LayerMask.NameToLayer(hitLayerName);
        if (layer >= 0) foreach (Transform t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

        if (GetComponentInChildren<Collider>() != null) return;
        SkinnedMeshRenderer mesh = GetComponentInChildren<SkinnedMeshRenderer>();
        if (mesh == null) return;
        BoxCollider box = mesh.gameObject.AddComponent<BoxCollider>();
        box.center = mesh.localBounds.center;
        box.size = mesh.localBounds.size;
        if (layer >= 0) mesh.gameObject.layer = layer;
    }

    void SetupAgent()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent == null) agent = gameObject.AddComponent<NavMeshAgent>();
        agent.radius = 0.3f;
        agent.height = 0.6f;
        agent.angularSpeed = 540f;
        agent.acceleration = 14f;
        agent.stoppingDistance = 0.15f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
        agent.enabled = false;
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 4f, NavMesh.AllAreas))
        {
            transform.position = hit.position;
            agent.enabled = true;
            agent.Warp(hit.position);
        }
    }

    // ---------- Update ----------

    void Update()
    {
        float dt = Time.deltaTime;

        if (mode == Mode.Dead)
        {
            blender.Update(dt, speeds);
            return;
        }

        bool havePlayer = PlayerRef.Find(ref player);
        if (havePlayer) { threat = player.position; haveThreat = true; }
        float distance = havePlayer ? Flat(player.position - transform.position).magnitude : float.MaxValue;

        if (mode != Mode.Flee && havePlayer)
        {
            float scare = scareDistance * (HorseMount.PlayerMounted ? mountedScareMultiplier : 1f);
            if (distance < scare) StartFlee();
        }

        switch (mode)
        {
            case Mode.Idle: UpdateIdle(dt); break;
            case Mode.Walk: UpdateWalk(dt); break;
            case Mode.Flee: UpdateFlee(dt, distance); break;
        }

        speeds[(int)Anim.Walk] = walkAnimSpeed;
        speeds[(int)Anim.Run] = runAnimSpeed;
        speeds[(int)Anim.Idle] = 1f;
        speeds[(int)Anim.Death] = 1f;
        blender.Update(dt, speeds);
    }

    // ---------- Wander ----------

    void EnterIdle()
    {
        mode = Mode.Idle;
        modeTimer = Random.Range(Mathf.Min(idleSeconds.x, idleSeconds.y), Mathf.Max(idleSeconds.x, idleSeconds.y));
        if (AgentActive) agent.ResetPath();
        blender?.Play((int)Anim.Idle);
    }

    void EnterWalk()
    {
        mode = Mode.Walk;
        modeTimer = Random.Range(Mathf.Min(walkSeconds.x, walkSeconds.y), Mathf.Max(walkSeconds.x, walkSeconds.y));
        Vector2 circle = Random.insideUnitCircle * wanderRadius;
        moveTarget = home + new Vector3(circle.x, 0f, circle.y);
        GoTo(moveTarget, walkSpeed);
        blender.Play((int)Anim.Walk);
    }

    void UpdateIdle(float dt)
    {
        modeTimer -= dt;
        if (modeTimer <= 0f) EnterWalk();
    }

    void UpdateWalk(float dt)
    {
        modeTimer -= dt;
        if (modeTimer <= 0f || Arrived()) { EnterIdle(); return; }
        Step(dt, walkSpeed);
    }

    // ---------- Run away ----------

    void StartFlee()
    {
        if (mode == Mode.Flee || mode == Mode.Dead) return;
        mode = Mode.Flee;
        repathTimer = 0f;
        calmTimer = 0f;
        blender.Play((int)Anim.Run);
    }

    void UpdateFlee(float dt, float distance)
    {
        repathTimer -= dt;
        if (repathTimer <= 0f || Arrived())
        {
            repathTimer = 0.5f;
            Vector3 away = haveThreat ? transform.position - threat : transform.forward;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            away = Quaternion.Euler(0f, Random.Range(-30f, 30f), 0f) * away.normalized;
            moveTarget = transform.position + away * fleeDistance;
            GoTo(moveTarget, runSpeed);
        }
        Step(dt, runSpeed);

        if (distance > calmDistance)
        {
            calmTimer += dt;
            if (calmTimer >= calmSeconds) { home = transform.position; EnterIdle(); }
        }
        else calmTimer = 0f;
    }

    // ---------- Movement (NavMesh when there is one, straight on the terrain when not) ----------

    void GoTo(Vector3 target, float speed)
    {
        if (!AgentActive) return;
        agent.speed = speed;
        if (NavMesh.SamplePosition(target, out NavMeshHit hit, 6f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
    }

    bool Arrived()
    {
        if (AgentActive) return !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f;
        return Flat(moveTarget - transform.position).sqrMagnitude < 0.25f;
    }

    void Step(float dt, float speed)
    {
        if (AgentActive) return; // the agent moves by itself
        Vector3 to = Flat(moveTarget - transform.position);
        if (to.sqrMagnitude < 0.01f) return;
        Quaternion want = Quaternion.LookRotation(to.normalized);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * dt);
        if (Vector3.Angle(transform.forward, to) < 70f)
        {
            Vector3 pos = transform.position + transform.forward * speed * dt;
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

    // ---------- Damage ----------

    // Called by the sword (PlayerCombat) and by arrows
    public void Hit(float damage, Vector3 attackerPosition, float knockback)
    {
        if (mode == Mode.Dead) return;
        health -= damage;
        threat = attackerPosition;
        haveThreat = true;
        if (health <= 0f) Die();
        else StartFlee(); // hurt: runs
    }

    [ContextMenu("Kill now")]
    void Die()
    {
        if (mode == Mode.Dead) return;
        mode = Mode.Dead;
        WorldCleanup.Add(gameObject, WorldCleanup.Kind.AnimalCorpse);
        if (agent != null) agent.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false; // the body can't be hit again
        blender.Play((int)Anim.Death, true);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f);
        Gizmos.DrawWireSphere(transform.position, scareDistance);
        Gizmos.color = new Color(0.3f, 0.8f, 1f);
        Gizmos.DrawWireSphere(Application.isPlaying ? home : transform.position, wanderRadius);
    }
}
