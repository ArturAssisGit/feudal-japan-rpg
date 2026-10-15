using System.Collections.Generic;
using UnityEngine;

// Leaves lying on the ground that react to whoever moves through them.
// Walking pushes them a little, running more, a dodge throws them up in the air; they flutter and fall back down.
// Enemies kick them too. Put this on an empty object where you want leaves (e.g. under a tree) and set the Radius.
// Several piles can be placed around the map. Needs nothing else (no colliders, no rigidbodies: cheap).
public class LeafPile : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Pictures of leaves (empty = uses the ones from Falling Leaves, or a simple default leaf)")] public Texture2D[] leafTextures;
    [Tooltip("Make the picture's background (corner color) transparent, for pictures without transparency")] public bool removeBackground = true;
    [Tooltip("How different from the background a pixel must be to stay")] [Range(0.02f, 0.5f)] public float backgroundTolerance = 0.15f;
    [Tooltip("Size of a leaf in meters (random between X and Y)")] public Vector2 leafSize = new Vector2(0.1f, 0.18f);
    [Tooltip("Leaves get a random color between these two (white = the picture's own color)")] public Color colorA = Color.white;
    public Color colorB = new Color(0.8f, 0.62f, 0.4f);

    [Header("Pile")]
    [Tooltip("How many leaves")] [Range(1, 1500)] public int count = 250;
    [Tooltip("Radius of the area covered (meters)")] public float radius = 4f;
    [Tooltip("More leaves in the middle (0 = spread evenly, 1 = mostly in the middle)")] [Range(0f, 1f)] public float clumping = 0.4f;
    [Tooltip("What the leaves lie on")] public LayerMask groundLayers = ~0;

    [Header("Reacting to movement")]
    [Tooltip("How close (meters) someone must pass to move a leaf")] public float kickRadius = 0.9f;
    [Tooltip("Slower than this (m/s) moves nothing")] public float minSpeed = 0.6f;
    [Tooltip("How much the leaves are pushed along with the walker")] public float push = 0.55f;
    [Tooltip("How high walking / running throws them (m/s up, at full run)")] public float lift = 1.6f;
    [Tooltip("A dodge (or slide) throws them this many times harder, and higher")] public float dodgeMultiplier = 2.6f;
    [Tooltip("Enemies also kick the leaves")] public bool enemiesKick = true;

    [Header("In the air")]
    [Tooltip("Pull down (m/s²) - leaves are light")] public float gravity = 4f;
    [Tooltip("Fastest a leaf falls (m/s) - they float down")] public float maxFallSpeed = 1.3f;
    [Tooltip("Air braking sideways (higher = stop sooner)")] public float drag = 2.2f;
    [Tooltip("Swinging side to side while falling")] public float flutter = 1.2f;
    [Tooltip("Spinning speed in the air (degrees per second)")] public float spin = 420f;

    [Header("Performance")]
    [Tooltip("The pile only reacts while the camera is closer than this (meters)")] public float activeDistance = 60f;

    private struct Leaf
    {
        public Vector3 position, velocity, euler, spinSpeed;
        public float groundY, size, phase, lastGroundCheckX, lastGroundCheckZ;
        public bool grounded;
        public Color32 color;
    }

    private struct Mover
    {
        public Transform t;
        public Vector3 lastPosition, velocity;
        public bool isLocalPlayer;
    }

    private Leaf[] leaves;
    private ParticleSystem system;
    private ParticleSystem.Particle[] particles;
    private readonly List<Mover> movers = new List<Mover>();
    private float nextMoverSearch;

    void Start()
    {
        BuildSystem();
        Scatter();
    }

    // ---------- Setup ----------

    void Scatter()
    {
        leaves = new Leaf[count];
        particles = new ParticleSystem.Particle[count];
        for (int i = 0; i < count; i++)
        {
            // Random point in the circle, denser in the middle when clumping > 0
            float r = radius * Mathf.Pow(Random.value, 0.5f + clumping * 0.8f);
            float a = Random.value * Mathf.PI * 2f;
            Vector3 p = transform.position + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            float ground = GroundHeight(p, transform.position.y + 3f);
            p.y = ground + 0.02f + Random.value * 0.015f;

            leaves[i] = new Leaf
            {
                position = p,
                groundY = ground,
                grounded = true,
                euler = new Vector3(90f, 0f, Random.Range(0f, 360f)), // lying flat
                size = Random.Range(leafSize.x, leafSize.y),
                phase = Random.value * 10f,
                lastGroundCheckX = p.x,
                lastGroundCheckZ = p.z,
                color = Color.Lerp(colorA, colorB, Random.value)
            };
        }
        WriteParticles();
    }

    float GroundHeight(Vector3 at, float fromY)
    {
        Vector3 origin = new Vector3(at.x, fromY + 2f, at.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 12f, groundLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue, y = at.y;
        foreach (RaycastHit h in hits)
        {
            Collider c = h.collider;
            if (c == null || c.attachedRigidbody != null || c is CharacterController) continue;
            if (c.GetComponentInParent<CharacterController>() != null || c.GetComponentInParent<Animator>() != null) continue; // not on people / animals
            if (h.distance < best) { best = h.distance; y = h.point.y; }
        }
        if (best == float.MaxValue && WorldUtil.TerrainHeight(at, out float terrainY)) y = terrainY;
        return y;
    }

    void BuildSystem()
    {
        GameObject go = new GameObject("Leaves");
        go.transform.SetParent(transform, false);
        system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = system.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(1, count);
        main.startRotation3D = true;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        var emission = system.emission;
        emission.enabled = false;
        var shape = system.shape;
        shape.enabled = false;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = ParticleMaterials.Quad();
        renderer.alignment = ParticleSystemRenderSpace.World;
        renderer.material = ParticleMaterials.Create("Leaf Pile", PickTexture(), false, 0.4f, true, true, true);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        system.Play();
    }

    Texture2D PickTexture()
    {
        Texture2D[] pictures = leafTextures;
        bool clearBackground = removeBackground;
        if ((pictures == null || pictures.Length == 0) && FallingLeaves.Instance != null)
        {
            pictures = FallingLeaves.Instance.leafTextures;
            clearBackground = FallingLeaves.Instance.removeBackground;
        }
        if (pictures != null)
            foreach (Texture2D t in pictures)
                if (t != null) return clearBackground ? ParticleMaterials.RemoveBackground(t, backgroundTolerance) : t;
        return DefaultLeaf();
    }

    // ---------- Every frame ----------

    void LateUpdate()
    {
        if (leaves == null) return;
        Camera cam = Camera.main;
        if (cam != null && (cam.transform.position - transform.position).sqrMagnitude > (activeDistance + radius) * (activeDistance + radius)) return;

        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (dt <= 0f) return;
        UpdateMovers(dt);
        Kick();
        Simulate(dt);
        WriteParticles();
    }

    void UpdateMovers(float dt)
    {
        if (Time.time >= nextMoverSearch)
        {
            nextMoverSearch = Time.time + 1f;
            var found = new List<Transform>();
            foreach (GameObject p in GameObject.FindGameObjectsWithTag("Player")) found.Add(p.transform);
            if (enemiesKick)
                foreach (EnemyAI_01 e in EnemyAI_01.All)
                    if (e != null && (e.transform.position - transform.position).sqrMagnitude < (radius + 15f) * (radius + 15f)) found.Add(e.transform);

            // Keep the old entries (their last position) for the ones still here
            var next = new List<Mover>();
            foreach (Transform t in found)
            {
                int old = movers.FindIndex(m => m.t == t);
                next.Add(old >= 0 ? movers[old] : new Mover { t = t, lastPosition = t.position, isLocalPlayer = t.GetComponent<PlayerDodge>() != null && t.GetComponent<PlayerDodge>().enabled });
            }
            movers.Clear();
            movers.AddRange(next);
        }

        for (int i = movers.Count - 1; i >= 0; i--)
        {
            Mover m = movers[i];
            if (m.t == null) { movers.RemoveAt(i); continue; }
            Vector3 v = (m.t.position - m.lastPosition) / dt;
            if (v.sqrMagnitude > 900f) v = Vector3.zero; // teleport / respawn
            m.velocity = Vector3.Lerp(m.velocity, v, 0.5f);
            m.lastPosition = m.t.position;
            movers[i] = m;
        }
    }

    void Kick()
    {
        float r2 = kickRadius * kickRadius;
        foreach (Mover m in movers)
        {
            Vector3 flatVel = new Vector3(m.velocity.x, 0f, m.velocity.z);
            float speed = flatVel.magnitude;
            bool dodge = m.isLocalPlayer && PlayerDodge.IsDodging;
            if (speed < minSpeed && !dodge) continue;
            Vector3 center = m.t.position;
            if ((new Vector3(center.x - transform.position.x, 0f, center.z - transform.position.z)).sqrMagnitude > (radius + 3f) * (radius + 3f)) continue;

            float strength = Mathf.Clamp01(speed / 6f) * (dodge ? dodgeMultiplier : 1f);
            float reach = dodge ? 1.5f : 1f;
            for (int i = 0; i < leaves.Length; i++)
            {
                Vector3 d = leaves[i].position - center;
                d.y = 0f;
                float dist2 = d.sqrMagnitude;
                if (dist2 > r2 * reach * reach || leaves[i].position.y > center.y + 1.2f) continue;
                float near = 1f - Mathf.Sqrt(dist2) / (kickRadius * reach);
                Vector3 outward = dist2 > 0.0001f ? d.normalized : Random.insideUnitSphere;
                outward.y = 0f;

                Vector3 impulse = flatVel * push * near + outward * (0.6f + speed * 0.25f) * near * (dodge ? 1.8f : 1f);
                impulse.y = lift * strength * near * Random.Range(0.6f, 1.3f);
                if (impulse.y < 0.15f && !dodge) impulse.y = 0f; // a slow walk just slides them

                Leaf l = leaves[i];
                l.velocity += impulse * Time.deltaTime * 12f; // spread over a few frames while passing
                if (l.velocity.y > 0.2f || impulse.sqrMagnitude > 0.5f)
                {
                    l.grounded = false;
                    l.spinSpeed = Random.onUnitSphere * spin * Random.Range(0.4f, 1f);
                }
                leaves[i] = l;
            }
        }
    }

    void Simulate(float dt)
    {
        float t = Time.time;
        for (int i = 0; i < leaves.Length; i++)
        {
            Leaf l = leaves[i];
            if (l.grounded && l.velocity.sqrMagnitude < 0.0004f) continue;

            if (l.grounded)
            {
                // Sliding on the ground: strong friction, stays flat
                l.velocity *= Mathf.Exp(-8f * dt);
                l.velocity.y = 0f;
                l.position += l.velocity * dt;
                RefreshGround(ref l);
                l.position.y = l.groundY + 0.02f;
                l.euler.z += l.velocity.magnitude * 90f * dt;
                leaves[i] = l;
                continue;
            }

            // In the air: light gravity, capped fall speed, sideways braking, flutter
            l.velocity.y -= gravity * dt;
            if (l.velocity.y < -maxFallSpeed) l.velocity.y = -maxFallSpeed;
            float brake = Mathf.Exp(-drag * dt);
            l.velocity.x *= brake;
            l.velocity.z *= brake;
            if (l.velocity.y < 0f)
            {
                float s = Mathf.Sin(t * 3.1f + l.phase) * flutter;
                l.position += new Vector3(s, 0f, Mathf.Cos(t * 2.3f + l.phase * 1.7f) * flutter * 0.6f) * dt;
            }
            l.position += l.velocity * dt;
            l.euler += l.spinSpeed * dt;
            l.spinSpeed *= Mathf.Exp(-0.6f * dt);

            RefreshGround(ref l);
            if (l.position.y <= l.groundY + 0.02f && l.velocity.y <= 0f)
            {
                // Landed: lie flat again (keep its own turn)
                l.position.y = l.groundY + 0.02f + Random.value * 0.01f;
                l.velocity = new Vector3(l.velocity.x, 0f, l.velocity.z) * 0.3f;
                l.grounded = true;
                l.euler = new Vector3(90f, 0f, l.euler.z);
                l.spinSpeed = Vector3.zero;
            }
            leaves[i] = l;
        }
    }

    // Ground height under the leaf (rechecked only after it moved a bit)
    void RefreshGround(ref Leaf l)
    {
        float dx = l.position.x - l.lastGroundCheckX, dz = l.position.z - l.lastGroundCheckZ;
        if (dx * dx + dz * dz < 0.09f) return;
        l.groundY = GroundHeight(l.position, Mathf.Max(l.position.y, l.groundY));
        l.lastGroundCheckX = l.position.x;
        l.lastGroundCheckZ = l.position.z;
    }

    void WriteParticles()
    {
        for (int i = 0; i < leaves.Length; i++)
        {
            Leaf l = leaves[i];
            particles[i].position = l.position;
            particles[i].velocity = Vector3.zero;
            particles[i].rotation3D = l.euler;
            particles[i].startSize = l.size;
            particles[i].startColor = l.color;
            particles[i].startLifetime = 1e6f;
            particles[i].remainingLifetime = 1e6f;
        }
        system.SetParticles(particles, leaves.Length);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.9f, 0.6f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }

    // ---------- Mesh and textures ----------

    static Texture2D DefaultLeaf()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Default Pile Leaf", wrapMode = TextureWrapMode.Clamp };
        Color leaf = new Color(0.55f, 0.36f, 0.16f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n;
                bool inside = Mathf.Abs(u) < Mathf.Sin(v * Mathf.PI) * 0.55f;
                bool vein = Mathf.Abs(u) < 0.04f && v > 0.05f && v < 0.92f;
                Color c = vein ? leaf * 0.7f : leaf * (0.85f + 0.3f * v);
                c.a = inside ? 1f : 0f;
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return tex;
    }
}
