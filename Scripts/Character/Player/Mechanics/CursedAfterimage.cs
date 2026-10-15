using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Oni Stone curse: while cursed (and in combat), the player leaves a blurry grey ghost when drawing, starting/stopping running or dodging.
// Self-creates at start; add it to a scene object (e.g. World Managers) to tweak values in the Inspector.
public class CursedAfterimage : MonoBehaviour
{
    public static CursedAfterimage Instance { get; private set; }

    [Header("When it happens")]
    [Tooltip("On = only in combat mode (sword or bow drawn). Off = also while exploring")] public bool onlyInCombat = true;
    [Tooltip("When you draw the sword or the bow")] public bool onDrawWeapon = true;
    public bool onStartRunning = true;
    public bool onStopRunning = true;
    public bool onDodge = true;
    [Tooltip("Keep leaving ghosts the whole time you run (a trail)")] public bool whileRunning = false;
    [Tooltip("Seconds between ghosts of the running trail")] public float runningInterval = 0.25f;
    [Tooltip("Minimum seconds between two ghosts (so it doesn't happen all the time)")] public float cooldown = 0.6f;

    [Header("Color")]
    [Tooltip("Color of the ghost from when it appears (left) to when it disappears (right). The alpha is the transparency")]
    public Gradient colorOverLife = DefaultGradient();
    [Tooltip("Makes the colors brighter (>1) or darker (<1)")] [Range(0f, 3f)] public float brightness = 1f;
    [Tooltip("On = the ghost glows (adds light). Off = normal see-through ghost")] public bool glow = false;

    [Header("Blur")]
    [Tooltip("How many copies make up one ghost. More copies = more blur")] [Range(1, 12)] public int blurCopies = 4;
    [Tooltip("How far apart the copies are, in random directions (meters)")] [Range(0f, 0.5f)] public float blurSpread = 0.07f;
    [Tooltip("Stretches the copies behind the direction you are moving, like motion blur (meters)")] [Range(0f, 1.5f)] public float motionBlur = 0.25f;
    [Tooltip("Transparency of the extra copies compared to the main one")] [Range(0f, 1f)] public float copyStrength = 0.5f;
    [Tooltip("The copies tremble while fading (0 = still)")] [Range(0f, 0.2f)] public float shake = 0.02f;

    [Header("Movement")]
    [Tooltip("Seconds until the ghost disappears")] [Range(0.1f, 3f)] public float fadeTime = 0.6f;
    [Tooltip("How far the ghost drifts backwards while fading (meters)")] public float driftDistance = 0.35f;
    [Tooltip("How far the ghost rises while fading (meters)")] public float riseHeight = 0.05f;
    [Tooltip("Size of the ghost when it disappears (1 = same size)")] [Range(0.5f, 2f)] public float growTo = 1.06f;

    private Transform player;
    private PlayerMovement movement;
    private PlayerCombat combat;
    private float nextGhostTime, nextTrailTime;
    private bool initialized, wasRunning, wasArmed, wasDodging;
    private Vector3 lastPlayerPosition, playerVelocity;
    private Material material;
    private bool materialGlow;
    private MaterialPropertyBlock block;
    private readonly List<Ghost> ghosts = new List<Ghost>();

    class Ghost
    {
        public GameObject root;
        public Vector3 origin;
        public Vector3 drift;
        public float startTime;
        public readonly List<Mesh> ownedMeshes = new List<Mesh>();
        public readonly List<Transform> copies = new List<Transform>();
        public readonly List<Vector3> copyOffsets = new List<Vector3>();
        public readonly List<Renderer> renderers = new List<Renderer>();
        public readonly List<float> strengths = new List<float>();
    }

    struct Part
    {
        public Mesh mesh;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }

    static Gradient DefaultGradient()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(0.6f, 0.6f, 0.65f), 0f), new GradientColorKey(new Color(0.25f, 0.25f, 0.28f), 1f) },
            new[] { new GradientAlphaKey(0.45f, 0f), new GradientAlphaKey(0.3f, 0.4f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null || FindAnyObjectByType<CursedAfterimage>() != null) return;
        WorldUtil.CreatePersistent<CursedAfterimage>("CursedAfterimage");
    }

    void Awake()
    {
        // Never destroy the object this is on (it can be the player!). The one on the player (with your settings)
        // wins over the automatic one; any other duplicate only removes itself.
        if (Instance != null && Instance != this)
        {
            if (Instance.gameObject.name == "CursedAfterimage" && Instance.gameObject != gameObject) Destroy(Instance.gameObject);
            else { Destroy(this); return; }
        }
        Instance = this;
        block = new MaterialPropertyBlock();
        if (colorOverLife == null) colorOverLife = DefaultGradient();
    }

    void Update()
    {
        UpdateGhosts();
        if (!FindPlayer()) return;

        // How the player is moving (for the motion blur)
        if (Time.deltaTime > 0f)
        {
            Vector3 delta = player.position - lastPlayerPosition;
            delta.y = 0f;
            playerVelocity = Vector3.Lerp(playerVelocity, delta / Time.deltaTime, 0.3f);
        }
        lastPlayerPosition = player.position;

        bool running = movement != null && movement.enabled && movement.IsRunning;
        bool armed = combat != null && combat.IsArmed;
        bool dodging = PlayerDodge.IsDodging;
        if (!initialized)
        {
            SetPrevious(running, armed, dodging);
            initialized = true;
            return;
        }

        bool trigger = (onDrawWeapon && armed && !wasArmed)
                    || (onStartRunning && running && !wasRunning)
                    || (onStopRunning && !running && wasRunning)
                    || (onDodge && dodging && !wasDodging);
        bool trail = whileRunning && running && Time.time >= nextTrailTime;
        SetPrevious(running, armed, dodging);
        if (!((!onlyInCombat || armed) && IsCursed())) return;

        if (trigger && Time.time >= nextGhostTime)
        {
            nextGhostTime = Time.time + cooldown;
            nextTrailTime = Time.time + runningInterval;
            SpawnGhost();
        }
        else if (trail)
        {
            nextTrailTime = Time.time + Mathf.Max(0.05f, runningInterval);
            SpawnGhost();
        }
    }

    void SetPrevious(bool running, bool armed, bool dodging)
    {
        wasRunning = running;
        wasArmed = armed;
        wasDodging = dodging;
    }

    // True when any equipped item has an Oni Stone demon
    public static bool IsCursed()
    {
        foreach (OniSpirit spirit in System.Enum.GetValues(typeof(OniSpirit)))
            if (spirit != OniSpirit.None && ItemUpgrades.Active(spirit)) return true;
        return false;
    }

    // ---------- Ghost ----------

    // Can also be called from other scripts for a one-off ghost
    public void SpawnGhost()
    {
        if (player == null) return;
        if (material == null || materialGlow != glow) CreateMaterial();

        // A snapshot of everything the player is wearing right now
        var parts = new List<Part>();
        var ghost = new Ghost();
        foreach (Renderer r in player.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (r is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null) continue;
                Mesh baked = new Mesh();
                skinned.BakeMesh(baked, true);
                ghost.ownedMeshes.Add(baked);
                parts.Add(new Part { mesh = baked, position = skinned.transform.position, rotation = skinned.transform.rotation, scale = Vector3.one });
            }
            else if (r is MeshRenderer)
            {
                MeshFilter filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                parts.Add(new Part { mesh = filter.sharedMesh, position = r.transform.position, rotation = r.transform.rotation, scale = r.transform.lossyScale });
            }
        }
        if (parts.Count == 0)
        {
            foreach (Mesh m in ghost.ownedMeshes) Destroy(m);
            return;
        }

        ghost.root = new GameObject("CursedGhost");
        ghost.origin = player.position;
        ghost.root.transform.position = ghost.origin;
        ghost.startTime = Time.time;

        // Drifts backwards (away from where the player is going)
        Vector3 back = playerVelocity.sqrMagnitude > 0.25f ? -playerVelocity.normalized : -player.forward;
        back.y = 0f;
        back.Normalize();
        ghost.drift = back * driftDistance + Vector3.up * riseHeight;

        int copies = Mathf.Max(1, blurCopies);
        for (int copy = 0; copy < copies; copy++)
        {
            // Copy 0 is the sharp one; the others are spread out and stretched behind the movement
            Vector3 offset = Vector3.zero;
            if (copy > 0)
            {
                float along = copies > 1 ? (float)copy / (copies - 1) : 0f;
                offset = back * motionBlur * along + Vector3.Scale(Random.insideUnitSphere, new Vector3(1f, 0.4f, 1f)) * blurSpread;
            }
            float strength = copy == 0 ? 1f : copyStrength * (1f - 0.5f * copy / copies);

            Transform copyRoot = new GameObject("Copy" + copy).transform;
            copyRoot.SetParent(ghost.root.transform, false);
            copyRoot.localPosition = offset;
            ghost.copies.Add(copyRoot);
            ghost.copyOffsets.Add(offset);

            foreach (Part part in parts)
            {
                GameObject piece = new GameObject("Part", typeof(MeshFilter), typeof(MeshRenderer));
                piece.transform.SetParent(copyRoot, true);
                piece.transform.SetPositionAndRotation(part.position + offset, part.rotation);
                piece.transform.localScale = part.scale;
                piece.GetComponent<MeshFilter>().sharedMesh = part.mesh;

                MeshRenderer mr = piece.GetComponent<MeshRenderer>();
                Material[] materials = new Material[Mathf.Max(1, part.mesh.subMeshCount)];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                mr.sharedMaterials = materials;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

                ghost.renderers.Add(mr);
                ghost.strengths.Add(strength);
            }
        }

        ghosts.Add(ghost);
        UpdateGhost(ghost, 0f);
    }

    void UpdateGhosts()
    {
        for (int i = ghosts.Count - 1; i >= 0; i--)
        {
            Ghost ghost = ghosts[i];
            float t = fadeTime > 0f ? (Time.time - ghost.startTime) / fadeTime : 1f;
            if (t >= 1f || ghost.root == null)
            {
                DestroyGhost(ghost);
                ghosts.RemoveAt(i);
                continue;
            }
            UpdateGhost(ghost, t);
        }
    }

    void UpdateGhost(Ghost ghost, float t)
    {
        float ease = 1f - (1f - t) * (1f - t);
        ghost.root.transform.position = ghost.origin + ghost.drift * ease;
        ghost.root.transform.localScale = Vector3.one * Mathf.Lerp(1f, growTo, ease);

        // Trembling copies
        for (int c = 0; c < ghost.copies.Count; c++)
        {
            if (ghost.copies[c] == null) continue;
            Vector3 jitter = c == 0 || shake <= 0f ? Vector3.zero : Random.insideUnitSphere * shake * (1f - t);
            ghost.copies[c].localPosition = ghost.copyOffsets[c] + jitter;
        }

        Color baseColor = colorOverLife != null ? colorOverLife.Evaluate(t) : new Color(0.55f, 0.55f, 0.6f, 0.4f * (1f - t));
        for (int i = 0; i < ghost.renderers.Count; i++)
        {
            Renderer r = ghost.renderers[i];
            if (r == null) continue;
            Color c = new Color(baseColor.r * brightness, baseColor.g * brightness, baseColor.b * brightness, baseColor.a * ghost.strengths[i]);
            block.SetColor("_BaseColor", c); // URP
            block.SetColor("_Color", c);     // fallback shader
            r.SetPropertyBlock(block);
        }
    }

    void DestroyGhost(Ghost ghost)
    {
        if (ghost.root != null) Destroy(ghost.root);
        foreach (Mesh mesh in ghost.ownedMeshes) if (mesh != null) Destroy(mesh);
    }

    void OnDestroy()
    {
        foreach (Ghost ghost in ghosts) DestroyGhost(ghost);
        ghosts.Clear();
        if (material != null) Destroy(material);
    }

    // Transparent unlit material (URP): normal see-through, or additive when Glow is on
    void CreateMaterial()
    {
        if (material != null) Destroy(material);
        materialGlow = glow;

        // Same look in the editor and in builds. The player's meshes have no useful vertex colors: ignore them
        material = ParticleMaterials.Create("CursedGhost", null, glow, 0f, false, false, false);

    }

    bool FindPlayer()
    {
        if (player != null && player.gameObject.activeInHierarchy) return true;
        GameObject found = PlayerRef.Object;
        if (found == null) return false;
        player = found.transform;
        movement = found.GetComponentInChildren<PlayerMovement>();
        combat = found.GetComponentInChildren<PlayerCombat>();
        lastPlayerPosition = player.position;
        playerVelocity = Vector3.zero;
        initialized = false;
        return true;
    }
}
