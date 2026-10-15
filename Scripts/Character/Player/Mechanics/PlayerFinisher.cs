using System.Collections;
using UnityEngine;

// Finisher: small chance on a sword kill; both play finisher anims with slow motion, player invulnerable until it ends.
// Needs Base Layer states "Finisher" (player) and optionally "Finisher React" (enemy); no transitions. Self-creates at start.
public class PlayerFinisher : MonoBehaviour
{
    public static PlayerFinisher Instance { get; private set; }

    // True while a finisher is playing: the player can't take damage, move or attack
    public static bool IsActive { get; private set; }

    // Set by PlayerCombat while a sword hit is being dealt (finishers only happen on sword kills)
    public static bool MeleeHit;

    [Header("Chance")]
    [Tooltip("Chance of a finisher when a sword hit kills an enemy. 1 = always, 0.2 = 20%, 0.002 = 0.2%")]
    [Range(0f, 1f)] public float chance = 0.2f;

    [Header("Animations (state names in the Animators)")]
    [Tooltip("State in the PLAYER Animator (Base Layer)")] public string playerState = "Finisher";
    [Tooltip("State in the ENEMY Animator (Base Layer). Empty or missing = the enemy just plays its death at the end")] public string enemyState = "Finisher React";
    [Tooltip("Seconds of blending into the finisher animations")] public float crossFade = 0.1f;

    [Header("Timing")]
    [Tooltip("Length of the finisher in seconds (0 = use the length of the player's animation)")] public float duration = 0f;
    [Tooltip("When the enemy dies, as a part of the finisher (0.6 = at 60%). Only used when the enemy has no react animation")]
    [Range(0f, 1f)] public float killMoment = 0.6f;

    [Header("Placement")]
    [Tooltip("Distance between the player and the enemy during the finisher (meters)")] public float enemyDistance = 1.3f;

    [Header("Blood")]
    [Tooltip("A big gush of blood from the enemy during the finisher")] public bool finisherBlood = true;
    [Tooltip("When the blood gushes out, as a part of the finisher (0.45 = at 45%, the moment of the cut)")] [Range(0f, 1f)] public float bloodMoment = 0.45f;
    [Tooltip("Height on the enemy where the blood comes out (meters from the feet)")] public float bloodHeight = 1.35f;

    [Header("Slow motion")]
    public bool slowMotion = true;
    [Tooltip("Game speed during the slow motion (0.4 = 40%)")] [Range(0.05f, 1f)] public float slowMotionScale = 0.4f;
    [Tooltip("When the slow motion starts, as a part of the finisher")] [Range(0f, 1f)] public float slowMotionStart = 0.45f;
    [Tooltip("Real seconds the slow motion lasts")] public float slowMotionTime = 0.5f;

    private Transform player;
    private Animator playerAnim;
    private PlayerCombat combat;
    private bool warnedMissingState;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        IsActive = false;
        MeleeHit = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null || FindAnyObjectByType<PlayerFinisher>() != null) return;
        WorldUtil.CreatePersistent<PlayerFinisher>("PlayerFinisher");
    }

    void Awake()
    {
        // Never destroy the object this is on (it can be the player!). The one on the player (with your settings)
        // wins over the automatic one; any other duplicate only removes itself.
        if (Instance != null && Instance != this)
        {
            if (Instance.gameObject.name == "PlayerFinisher" && Instance.gameObject != gameObject) Destroy(Instance.gameObject);
            else { Destroy(this); return; }
        }
        Instance = this;
    }

    void OnDisable()
    {
        if (IsActive) Time.timeScale = 1f;
        IsActive = false;
    }

    // Called by the enemy when a hit kills it. Returns true when a finisher starts (the enemy then waits for it)
    public static bool TryStart(EnemyAI_01 enemy)
    {
        if (!MeleeHit || IsActive || Instance == null || enemy == null || !Instance.isActiveAndEnabled) return false;
        if (Random.value >= Instance.chance) return false;
        if (!Instance.FindPlayer() || !Instance.HasState(Instance.playerAnim, Instance.playerState)) return false;

        Instance.StartCoroutine(Instance.Run(enemy));
        return true;
    }

    IEnumerator Run(EnemyAI_01 enemy)
    {
        IsActive = true;
        // Wait one frame so the enemy has fully stopped (AI and NavMesh off) before moving it
        yield return null;
        if (enemy == null || player == null || playerAnim == null)
        {
            if (enemy != null) enemy.CompleteFinisherDeath(true);
            IsActive = false;
            yield break;
        }

        // Face each other, the enemy right in front of the player
        Vector3 toEnemy = enemy.transform.position - player.position;
        toEnemy.y = 0f;
        if (toEnemy.sqrMagnitude < 0.01f) toEnemy = player.forward;
        toEnemy.Normalize();
        player.rotation = Quaternion.LookRotation(toEnemy);
        Vector3 enemyPos = player.position + toEnemy * enemyDistance;
        enemyPos.y = enemy.transform.position.y;
        enemy.transform.SetPositionAndRotation(enemyPos, Quaternion.LookRotation(-toEnemy));

        // Clear queued attacks/blocks so nothing interrupts the finisher
        foreach (AnimatorControllerParameter p in playerAnim.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger) playerAnim.ResetTrigger(p.name);
        playerAnim.SetBool("isBlocking", false);

        playerAnim.CrossFadeInFixedTime(playerState, crossFade, 0);
        bool enemyReacts = enemy.PlayFinisherReaction(enemyState, crossFade);

        // Length: from the setting, or from the player's animation
        yield return null;
        float length = duration;
        if (length <= 0f)
        {
            AnimatorStateInfo next = playerAnim.GetNextAnimatorStateInfo(0);
            AnimatorStateInfo current = playerAnim.GetCurrentAnimatorStateInfo(0);
            if (next.IsName(playerState)) length = next.length;
            else if (current.IsName(playerState)) length = current.length;
            if (length <= 0.1f) length = 2f;
        }

        float elapsed = Time.deltaTime;
        bool slowDone = !slowMotion, enemyDead = false, bloodDone = !finisherBlood;
        while (elapsed < length)
        {
            float part = elapsed / length;
            if (!slowDone && part >= slowMotionStart)
            {
                slowDone = true;
                StartCoroutine(SlowMotion());
            }
            if (!bloodDone && part >= bloodMoment)
            {
                bloodDone = true;
                if (enemy != null && player != null)
                {
                    // Out of the wound, away from the player and a bit upwards
                    Vector3 away = enemy.transform.position - player.position;
                    away.y = 0f;
                    Vector3 dir = (away.normalized + Vector3.up * 0.8f).normalized;
                    BloodEffects.SpawnGush(enemy.transform.position + Vector3.up * bloodHeight, dir, enemy.transform);
                }
            }
            if (!enemyDead && !enemyReacts && part >= killMoment)
            {
                enemyDead = true;
                if (enemy != null) enemy.CompleteFinisherDeath(true);
            }
            yield return null;
            elapsed += Time.deltaTime;
        }

        if (!enemyDead && enemy != null) enemy.CompleteFinisherDeath(!enemyReacts);

        // Back to the normal (armed) idle
        if (playerAnim != null)
            playerAnim.CrossFadeInFixedTime(combat != null ? combat.IdleStateName : "Blend Tree", 0.25f, 0);

        IsActive = false;
    }

    IEnumerator SlowMotion()
    {
        Time.timeScale = slowMotionScale;
        yield return new WaitForSecondsRealtime(slowMotionTime);
        if (IsActive || Time.timeScale == slowMotionScale) Time.timeScale = 1f;
    }

    bool HasState(Animator anim, string state)
    {
        if (anim == null || string.IsNullOrEmpty(state)) return false;
        if (anim.HasState(0, Animator.StringToHash(state))) return true;
        if (!warnedMissingState)
        {
            warnedMissingState = true;
            Debug.LogWarning($"Finisher: the player Animator has no state called '{playerState}' on the Base Layer. Add it with the finisher animation.");
        }
        return false;
    }

    bool FindPlayer()
    {
        if (player != null && player.gameObject.activeInHierarchy && playerAnim != null) return true;
        if (!PlayerRef.Find(ref player)) return false;
        combat = PlayerRef.Get<PlayerCombat>();
        playerAnim = PlayerRef.Animator;
        return playerAnim != null;
    }
}
