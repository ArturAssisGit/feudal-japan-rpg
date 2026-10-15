using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

// Auto-created. Left click a "Place" item in the inventory -> a see-through preview follows your view.
// Left click = place, right click (or Esc) = cancel, mouse wheel = rotate.
public class PlacementSystem : MonoBehaviour
{
    public static PlacementSystem Instance { get; private set; }
    public static bool IsPlacing => Instance != null && Instance.ghost != null;

    [Header("Placement")]
    [Tooltip("How far from the player you can place things (meters)")] public float maxDistance = 6f;
    [Tooltip("Distance used when you look at the sky")] public float defaultDistance = 3f;
    [Tooltip("Degrees per mouse wheel step")] public float rotateStep = 15f;
    [Tooltip("Steepest ground you can place on (degrees)")] public float maxSlope = 35f;

    [Header("Preview colors")]
    public Color validColor = new Color(0.35f, 1f, 0.45f, 0.4f);
    public Color invalidColor = new Color(1f, 0.3f, 0.25f, 0.4f);

    private PlayerItemSlot sourceSlot;
    private PlayerItemData placingItem;
    private GameObject ghost;
    private Renderer[] ghostRenderers;
    private Material validMaterial, invalidMaterial;
    private bool canPlace;
    private float yaw;
    private int startFrame;

    private Transform player;
    private Camera cam;
    private TextMeshProUGUI hintText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<PlacementSystem>("PlacementSystem");
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        validMaterial = CreateGhostMaterial(validColor);
        invalidMaterial = CreateGhostMaterial(invalidColor);
    }

    void Start() => BuildUI();

    // Called when you left click a Place item in the inventory
    public static void Begin(PlayerItemSlot slot, PlayerItemData item)
    {
        if (Instance == null || slot == null || item == null) return;
        if (item.itemPrefab == null)
        {
            Debug.LogWarning($"{item.itemName} has no Item Prefab, so it can't be placed.");
            return;
        }
        Instance.StartPlacing(slot, item);
    }

    void StartPlacing(PlayerItemSlot slot, PlayerItemData item)
    {
        Cancel();

        player = PlayerRef.Transform;
        if (player == null) return;
        cam = Camera.main;

        sourceSlot = slot;
        placingItem = item;
        yaw = player.eulerAngles.y + 180f; // facing the player
        startFrame = Time.frameCount;

        ghost = BuildGhost(item.itemPrefab);
        ghostRenderers = ghost.GetComponentsInChildren<Renderer>();
        if (hintText != null) hintText.gameObject.SetActive(true);
    }

    void Update()
    {
        if (ghost == null) return;

        bool menuOpen = PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.IsOpen;
        if (menuOpen || player == null || sourceSlot == null || sourceSlot.itemData != placingItem)
        {
            Cancel();
            return;
        }

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f) yaw += Mathf.Sign(scroll) * rotateStep;

        UpdateGhostPosition();

        if (Time.frameCount == startFrame) return; // ignore the click that opened the preview
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) Cancel();
        else if (Input.GetMouseButtonDown(0) && canPlace) Place();
    }

    void UpdateGhostPosition()
    {
        Vector3 flatForward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : player.forward;
        Vector3 target = player.position + flatForward * defaultDistance;

        // Where the camera is looking (only if it's close enough to the player)
        if (cam != null && RaycastIgnoringPlayer(new Ray(cam.transform.position, cam.transform.forward), 100f, out RaycastHit lookHit))
        {
            Vector3 offset = lookHit.point - player.position;
            offset.y = 0f;
            target = offset.magnitude <= maxDistance ? lookHit.point : player.position + offset.normalized * maxDistance;
        }

        bool onGround = RaycastIgnoringPlayer(new Ray(target + Vector3.up * 5f, Vector3.down), 20f, out RaycastHit groundHit);
        if (onGround) target = groundHit.point;

        ghost.transform.SetPositionAndRotation(target, Quaternion.Euler(0f, yaw, 0f) * placingItem.itemPrefab.transform.rotation);

        // Sit the bottom of the model on the ground, wherever its pivot is
        Bounds bounds = GetBounds();
        ghost.transform.position += Vector3.up * (target.y - bounds.min.y);
        bounds = GetBounds();

        canPlace = onGround && Vector3.Angle(groundHit.normal, Vector3.up) <= maxSlope && !Blocked(bounds);

        Material material = canPlace ? validMaterial : invalidMaterial;
        foreach (Renderer r in ghostRenderers)
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            r.sharedMaterials = mats;
        }
    }

    // True when something solid (not the ground) is in the way
    bool Blocked(Bounds bounds)
    {
        const float groundMargin = 0.3f;
        float height = Mathf.Max(0.1f, bounds.size.y - groundMargin);
        Vector3 center = new Vector3(bounds.center.x, bounds.min.y + groundMargin + height * 0.5f, bounds.center.z);
        Vector3 half = new Vector3(bounds.extents.x * 0.9f, height * 0.5f, bounds.extents.z * 0.9f);

        foreach (Collider c in Physics.OverlapBox(center, half, Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            if (!(c is TerrainCollider)) return true;
        return false;
    }

    bool RaycastIgnoringPlayer(Ray ray, float distance, out RaycastHit result)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, distance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(player)) continue;
            result = hit;
            return true;
        }
        result = default;
        return false;
    }

    Bounds GetBounds()
    {
        if (ghostRenderers.Length == 0) return new Bounds(ghost.transform.position, Vector3.one * 0.5f);
        Bounds bounds = ghostRenderers[0].bounds;
        foreach (Renderer r in ghostRenderers) bounds.Encapsulate(r.bounds);
        return bounds;
    }

    void Place()
    {
        GameObject placed = Instantiate(placingItem.itemPrefab, ghost.transform.position, ghost.transform.rotation);
        placed.name = placingItem.itemPrefab.name;
        placed.AddComponent<PlacedObject>().itemId = ItemDatabase.IdOf(placingItem); // so it is saved
        Debug.Log("Placed: " + placingItem.itemName);

        sourceSlot.RemoveOne();
        Cancel();
    }

    public void Cancel()
    {
        if (ghost != null) Destroy(ghost);
        ghost = null;
        ghostRenderers = null;
        sourceSlot = null;
        placingItem = null;
        if (hintText != null) hintText.gameObject.SetActive(false);
    }

    // ---------- Preview model ----------

    // Copies only the meshes of the prefab (no colliders or scripts), so the preview can't interact with anything
    GameObject BuildGhost(GameObject prefab)
    {
        GameObject root = new GameObject(prefab.name + " (Preview)");
        root.transform.localScale = prefab.transform.localScale;

        // With LODs, only the most detailed level is used
        HashSet<Renderer> allowed = null;
        LODGroup lodGroup = prefab.GetComponentInChildren<LODGroup>(true);
        if (lodGroup != null && lodGroup.GetLODs().Length > 0) allowed = new HashSet<Renderer>(lodGroup.GetLODs()[0].renderers);

        Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
        foreach (Renderer source in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (allowed != null && !allowed.Contains(source)) continue;

            Mesh mesh = null;
            if (source is MeshRenderer && source.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;
            else if (source is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
            if (mesh == null) continue;

            Matrix4x4 local = toRoot * source.transform.localToWorldMatrix;
            GameObject part = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = local.GetColumn(3);
            part.transform.localRotation = local.rotation;
            part.transform.localScale = local.lossyScale;
            part.GetComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Material[] mats = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < mats.Length; i++) mats[i] = validMaterial;
            renderer.sharedMaterials = mats;
        }
        return root;
    }

    static Material CreateGhostMaterial(Color color)
    {
        Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (urpUnlit != null)
        {
            Material m = new Material(urpUnlit);
            m.SetFloat("_Surface", 1f); // transparent
            m.SetFloat("_Blend", 0f);   // alpha
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetColor("_BaseColor", color);
            return m;
        }

        Material fallback = new Material(Shader.Find("Sprites/Default"));
        fallback.color = color;
        return fallback;
    }

    // ---------- Hint text ----------

    void BuildUI()
    {
        Transform canvas = UIKit.CreateCanvas("PlacementCanvas", transform, 48).transform;
        hintText = UIKit.CornerText("PlacementHint", canvas, 0, true);
        hintText.text = "[Left Click] Place     [Right Click] Cancel     [Mouse Wheel] Rotate";
    }
}
