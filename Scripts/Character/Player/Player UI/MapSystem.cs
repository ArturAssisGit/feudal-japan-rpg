using UnityEngine;
using UnityEngine.UI;

// Creates itself at game start. Takes a top-down "photo" of the terrain and shows it as a minimap (top-right, off by default)
// and a big map with M (close with M or Esc). Parts you haven't been to yet are dark; walking around reveals them (saved per game).
// White dot = player, orange dot = respawn tent, yellow = where your delivery goes, blue dots = delivery NPCs.
public class MapSystem : MonoBehaviour
{
    public static MapSystem Instance { get; private set; }
    public static bool IsMapOpen => Instance != null && Instance.bigMapOpen;

    [Header("Controls")]
    public KeyCode mapKey = KeyCode.M;

    [Header("Map photo")]
    [Tooltip("Resolution of the map picture")] public int mapResolution = 1024;
    [Tooltip("Height of the map camera above the terrain")] public float cameraHeight = 400f;

    [Header("Map look")]
    [Tooltip("Color the map picture is tinted with (white = no tint). A warm color gives an old-map feel")] public Color mapTint = new Color(1f, 0.93f, 0.82f);

    [Header("Minimap")]
    public bool showMinimap = false;
    [Tooltip("Size of the minimap on screen")] public float minimapSize = 230f;
    [Tooltip("How many meters of the world the minimap shows")] public float minimapWorldSize = 80f;
    [Tooltip("Distance from the top-right corner")] public Vector2 minimapMargin = new Vector2(30f, 30f);

    [Header("Big map")]
    [Tooltip("Size of the big map on screen")] public float bigMapSize = 900f;

    [Header("Icons")]
    public Color playerColor = Color.white;
    public Color tentColor = new Color(1f, 0.5f, 0.15f);
    [Tooltip("The NPC you are delivering a package to")] public Color deliveryColor = new Color(1f, 0.85f, 0.1f);
    [Tooltip("Other delivery NPCs (small dots on the big map)")] public Color deliveryNpcColor = new Color(0.45f, 0.75f, 1f);
    public float playerIconSize = 4f;
    [Tooltip("Backpacks you dropped on the ground")] public Color backpackColor = new Color(0.35f, 0.95f, 0.45f);
    [Tooltip("On the minimap, a backpack that is out of view sticks to the edge, pointing where it is")] public bool backpackOnMinimapEdge = true;

    [Header("Explored areas (fog of war)")]
    [Tooltip("Off = the whole map is visible from the start")] public bool fogOfWar = true;
    [Tooltip("Color over the parts you haven't explored yet. Alpha = how dark (1 = black)")] public Color fogColor = new Color(0f, 0f, 0f, 0.99f);
    [Tooltip("Meters around you that get revealed while you walk")] public float revealRadius = 40f;
    [Tooltip("Soft edge of the revealed area (0 = hard edge, 1 = very soft)")] [Range(0f, 1f)] public float revealSoftness = 0.5f;
    [Tooltip("Detail of the explored areas (cells per side). Changing it later forgets what was explored in old saves")] public int fogResolution = 256;

    private Texture2D fogTexture;
    private byte[] explored;
    private Color32[] fogPixels;
    private RawImage minimapFog, bigMapFog;
    private float nextReveal;

    private RenderTexture mapTexture;
    private Vector3 mapMin;      // world X/Z of the bottom-left corner of the picture
    private float mapWorldSize;  // world meters covered by the picture (square)
    private bool hasMap;

    private Transform player;
    private bool bigMapOpen;

    private RectTransform minimapRoot;
    private RawImage minimapImage;
    private RectTransform minimapArrow;
    private RectTransform minimapTent;
    private RectTransform minimapDelivery;
    private Sprite dotSprite;
    private readonly System.Collections.Generic.List<RectTransform> deliveryDots = new System.Collections.Generic.List<RectTransform>();
    private readonly System.Collections.Generic.List<RectTransform> minimapBackpacks = new System.Collections.Generic.List<RectTransform>();
    private readonly System.Collections.Generic.List<RectTransform> bigMapBackpacks = new System.Collections.Generic.List<RectTransform>();

    private RectTransform bigMapRoot;
    private GameObject bigMapWindow; // background + map + frame
    private RectTransform bigMapArrow;
    private RectTransform bigMapTent;
    private RectTransform bigMapDelivery;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<MapSystem>("MapSystem");
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this); // only this component (never the object it is on, e.g. the Player)
            return;
        }
        Instance = this;
        // Put on the Player prefab by mistake: it must live on its own object that survives scene changes
        if (transform.parent != null || gameObject.name != "MapSystem") Debug.LogWarning("MapSystem creates itself: remove it from this object (e.g. the Player prefab).", this);
    }

    void Start()
    {
        if (Instance != this) return;
        BuildUI();
        FindPlayer(); // so the player can be hidden in the photo
        TakeMapPhoto();
        // Coming from the menu: take the photo again once the world scene is loaded
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void Update()
    {
        if (Instance != this || minimapRoot == null) return;
        if (!FindPlayer())
        {
            minimapRoot.gameObject.SetActive(false);
            return;
        }

        bool otherMenuOpen = (PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.IsInventoryOpen) || LootWindow.IsOpen || RespawnSystem.IsDeathScreenOpen;
        if (Input.GetKeyDown(mapKey) && !otherMenuOpen && !ScreenFade.IsActive) SetBigMap(!bigMapOpen);
        else if (bigMapOpen && (Input.GetKeyDown(KeyCode.Escape) || otherMenuOpen)) SetBigMap(false);

        minimapRoot.gameObject.SetActive(showMinimap && hasMap && !bigMapOpen);
        if (!hasMap) return;

        Vector2 playerUV = WorldToUV(player.position);
        UpdateFog(playerUV);

        // Minimap: shows the part of the picture around the player
        if (minimapRoot.gameObject.activeSelf)
        {
            float viewUV = minimapWorldSize / mapWorldSize;
            minimapImage.uvRect = new Rect(playerUV.x - viewUV * 0.5f, playerUV.y - viewUV * 0.5f, viewUV, viewUV);
            minimapFog.uvRect = minimapImage.uvRect;
            PlaceIcon(minimapTent, TentPosition(), (uv) => (uv - playerUV) / viewUV * minimapSize, minimapSize * 0.5f);
            PlaceIcon(minimapDelivery, DeliverySystem.TargetPosition, (uv) => (uv - playerUV) / viewUV * minimapSize, minimapSize * 0.5f);
            UpdateBackpackIcons(minimapBackpacks, minimapRoot, 12f, (uv) => (uv - playerUV) / viewUV * minimapSize, minimapSize * 0.5f - 7f, backpackOnMinimapEdge);
        }
        else UpdateBackpackIcons(minimapBackpacks, minimapRoot, 12f, null, 0f, false);

        // Big map: the whole picture
        if (bigMapOpen)
        {
            bigMapArrow.anchoredPosition = (playerUV - new Vector2(0.5f, 0.5f)) * bigMapSize;
            PlaceIcon(bigMapTent, TentPosition(), (uv) => (uv - new Vector2(0.5f, 0.5f)) * bigMapSize, bigMapSize * 0.5f);
            PlaceIcon(bigMapDelivery, DeliverySystem.TargetPosition, (uv) => (uv - new Vector2(0.5f, 0.5f)) * bigMapSize, bigMapSize * 0.5f);
            UpdateDeliveryDots();
            UpdateBackpackIcons(bigMapBackpacks, bigMapRoot, 18f, (uv) => (uv - new Vector2(0.5f, 0.5f)) * bigMapSize, bigMapSize * 0.5f, false);
        }
    }

    // A green dot (with a dark ring) for every backpack lying on the ground. uvToPosition null = hide them all
    void UpdateBackpackIcons(System.Collections.Generic.List<RectTransform> icons, RectTransform parent, float size, System.Func<Vector2, Vector2> uvToPosition, float halfSize, bool stickToEdge)
    {
        int used = 0;
        if (uvToPosition != null)
        {
            foreach (DroppedBackpack bag in DroppedBackpack.All)
            {
                if (bag == null) continue;
                if (used >= icons.Count)
                {
                    RectTransform ring = CreateImage("BackpackIcon", parent, new Color(0.05f, 0.08f, 0.04f, 0.9f), dotSprite).GetComponent<RectTransform>();
                    ring.sizeDelta = Vector2.one * (size + 5f);
                    RectTransform dot = CreateImage("Dot", ring, backpackColor, dotSprite).GetComponent<RectTransform>();
                    dot.sizeDelta = Vector2.one * size;
                    icons.Add(ring);
                }
                RectTransform icon = icons[used++];
                Vector2 position = uvToPosition(WorldToUV(bag.transform.position));
                bool inside = Mathf.Abs(position.x) <= halfSize && Mathf.Abs(position.y) <= halfSize;
                if (!inside && stickToEdge)
                {
                    // Out of view: put it on the edge, in its direction
                    float scale = halfSize / Mathf.Max(Mathf.Abs(position.x), Mathf.Abs(position.y));
                    position *= scale;
                    inside = true;
                }
                icon.gameObject.SetActive(inside);
                icon.anchoredPosition = position;
                icon.SetAsLastSibling();
            }
        }
        for (int i = used; i < icons.Count; i++) icons[i].gameObject.SetActive(false);
    }

    void SetBigMap(bool open)
    {
        if (open) TakeMapPhoto(); // refresh, so the map is up to date
        bigMapOpen = open;
        bigMapWindow.SetActive(open);
    }

    Vector3? TentPosition() => TentSpawn.Selected != null ? TentSpawn.Selected.transform.position : (Vector3?)null;

    void PlaceIcon(RectTransform icon, Vector3? world, System.Func<Vector2, Vector2> uvToPosition, float halfSize)
    {
        if (world == null)
        {
            icon.gameObject.SetActive(false);
            return;
        }
        Vector2 position = uvToPosition(WorldToUV(world.Value));
        bool inside = Mathf.Abs(position.x) <= halfSize && Mathf.Abs(position.y) <= halfSize;
        icon.gameObject.SetActive(inside);
        icon.anchoredPosition = position;
    }

    // Small dots for the delivery NPCs (the one you deliver to has its own bigger yellow icon)
    void UpdateDeliveryDots()
    {
        int used = 0;
        foreach (DeliveryNpc npc in DeliveryNpc.All)
        {
            if (npc == null || !npc.showOnMap || DeliverySystem.IsTarget(npc)) continue;
            if (used >= deliveryDots.Count)
            {
                RectTransform dot = CreateImage("DeliveryNpcDot", bigMapRoot, deliveryNpcColor, dotSprite).GetComponent<RectTransform>();
                dot.sizeDelta = Vector2.one * 11f;
                deliveryDots.Add(dot);
            }
            PlaceIcon(deliveryDots[used], npc.transform.position, (uv) => (uv - new Vector2(0.5f, 0.5f)) * bigMapSize, bigMapSize * 0.5f);
            used++;
        }
        for (int i = used; i < deliveryDots.Count; i++) deliveryDots[i].gameObject.SetActive(false);
    }

    // ---------- Explored areas ----------

    // New game: everything is dark again
    public static void ResetExplored() { if (Instance != null) Instance.ClearExplored(); }

    // For the save file ("resolution:base64")
    public static string ExportExplored()
    {
        if (Instance == null || Instance.explored == null) return null;
        return Instance.fogResolution + ":" + System.Convert.ToBase64String(Instance.explored);
    }

    // Loading a save (null = nothing explored yet)
    public static void ImportExplored(string saved) { if (Instance != null) Instance.LoadExplored(saved); }

    void EnsureFog()
    {
        fogResolution = Mathf.Clamp(fogResolution, 32, 1024);
        int count = fogResolution * fogResolution;
        if (explored != null && explored.Length == count && fogTexture != null && fogTexture.width == fogResolution) return;

        if (explored == null || explored.Length != count) explored = new byte[count];
        fogPixels = new Color32[count];
        if (fogTexture != null) Destroy(fogTexture);
        fogTexture = new Texture2D(fogResolution, fogResolution, TextureFormat.RGBA32, false)
        {
            name = "MapExplored",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        if (minimapFog != null) minimapFog.texture = fogTexture;
        if (bigMapFog != null) bigMapFog.texture = fogTexture;
        RebuildFogTexture();
    }

    void ClearExplored()
    {
        EnsureFog();
        System.Array.Clear(explored, 0, explored.Length);
        RebuildFogTexture();
    }

    void LoadExplored(string saved)
    {
        EnsureFog();
        System.Array.Clear(explored, 0, explored.Length);
        if (!string.IsNullOrEmpty(saved))
        {
            int colon = saved.IndexOf(':');
            if (colon > 0 && int.TryParse(saved.Substring(0, colon), out int res) && res == fogResolution)
            {
                try
                {
                    byte[] data = System.Convert.FromBase64String(saved.Substring(colon + 1));
                    if (data.Length == explored.Length) System.Array.Copy(data, explored, data.Length);
                }
                catch (System.FormatException) { Debug.LogWarning("MapSystem: the explored map in the save is damaged, starting dark."); }
            }
        }
        RebuildFogTexture();
    }

    void UpdateFog(Vector2 playerUV)
    {
        bool on = fogOfWar;
        if (minimapFog != null && minimapFog.enabled != on) minimapFog.enabled = on;
        if (bigMapFog != null && bigMapFog.enabled != on) bigMapFog.enabled = on;
        if (!on) return;

        EnsureFog();
        if (Time.unscaledTime < nextReveal) return;
        nextReveal = Time.unscaledTime + 0.2f;
        if (Reveal(playerUV)) RebuildFogTexture();
    }

    // Marks the cells around the player as explored. True when something new was revealed
    bool Reveal(Vector2 uv)
    {
        if (mapWorldSize <= 0f || uv.x < -0.1f || uv.y < -0.1f || uv.x > 1.1f || uv.y > 1.1f) return false;
        int res = fogResolution;
        float radius = Mathf.Max(1f, revealRadius / mapWorldSize * res); // in cells
        float hard = 1f - Mathf.Clamp01(revealSoftness);
        Vector2 center = uv * res;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(center.x - radius)), x1 = Mathf.Min(res - 1, Mathf.CeilToInt(center.x + radius));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(center.y - radius)), y1 = Mathf.Min(res - 1, Mathf.CeilToInt(center.y + radius));
        bool changed = false;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / radius;
                if (d >= 1f) continue;
                float value = d <= hard ? 1f : 1f - (d - hard) / Mathf.Max(0.001f, 1f - hard);
                byte v = (byte)Mathf.RoundToInt(Mathf.SmoothStep(0f, 1f, value) * 255f);
                int i = y * res + x;
                if (v > explored[i]) { explored[i] = v; changed = true; }
            }
        return changed;
    }

    void RebuildFogTexture()
    {
        if (fogTexture == null || fogPixels == null || explored == null) return;
        byte r = (byte)(fogColor.r * 255f), g = (byte)(fogColor.g * 255f), b = (byte)(fogColor.b * 255f);
        float a = fogColor.a * 255f;
        for (int i = 0; i < explored.Length; i++)
            fogPixels[i] = new Color32(r, g, b, (byte)(a * (1f - explored[i] / 255f)));
        fogTexture.SetPixels32(fogPixels);
        fogTexture.Apply(false);
    }

    Vector2 WorldToUV(Vector3 world) => new Vector2((world.x - mapMin.x) / mapWorldSize, (world.z - mapMin.z) / mapWorldSize);

    // ---------- Map photo ----------

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Invoke(nameof(TakeMapPhotoQuiet), 0.5f);

    void OnDestroy() => UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

    void TakeMapPhotoQuiet()
    {
        foreach (Terrain t in Terrain.activeTerrains) if (t != null) { TakeMapPhoto(); return; }
    }

    // Renders the terrain from straight above into a texture (without fog and without the player)
    public void TakeMapPhoto()
    {
        if (!GetWorldBounds(out Bounds world)) return;

        mapWorldSize = Mathf.Max(world.size.x, world.size.z);
        mapMin = new Vector3(world.center.x - mapWorldSize * 0.5f, 0f, world.center.z - mapWorldSize * 0.5f);

        if (mapTexture == null)
        {
            mapTexture = new RenderTexture(mapResolution, mapResolution, 24) { name = "MapTexture", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            minimapImage.texture = mapTexture;
            bigMapRoot.GetComponent<RawImage>().texture = mapTexture;
        }

        GameObject cameraObject = new GameObject("MapCamera");
        Camera mapCamera = cameraObject.AddComponent<Camera>();
        mapCamera.orthographic = true;
        mapCamera.orthographicSize = mapWorldSize * 0.5f;
        mapCamera.aspect = 1f;
        mapCamera.transform.SetPositionAndRotation(new Vector3(world.center.x, world.max.y + cameraHeight, world.center.z), Quaternion.Euler(90f, 0f, 0f));
        mapCamera.nearClipPlane = 1f;
        mapCamera.farClipPlane = cameraHeight + world.size.y + 100f;
        mapCamera.clearFlags = CameraClearFlags.SolidColor;
        mapCamera.backgroundColor = new Color(0.12f, 0.1f, 0.08f);
        mapCamera.targetTexture = mapTexture;
        mapCamera.enabled = false;

        // Hide the player and the fog just for the photo
        Renderer[] playerRenderers = player != null ? player.GetComponentsInChildren<Renderer>() : new Renderer[0];
        foreach (Renderer r in playerRenderers) r.enabled = false;
        bool fog = RenderSettings.fog;
        RenderSettings.fog = false;

        mapCamera.Render();

        RenderSettings.fog = fog;
        foreach (Renderer r in playerRenderers) r.enabled = true;
        mapCamera.targetTexture = null;
        Destroy(cameraObject);
        hasMap = true;
        ApplyMapLook();
    }

    void ApplyMapLook()
    {
        if (minimapImage != null) minimapImage.color = mapTint;
        if (bigMapRoot != null) bigMapRoot.GetComponent<RawImage>().color = mapTint;
    }

    static bool GetWorldBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            if (terrain == null || terrain.terrainData == null) continue;
            Bounds b = new Bounds(terrain.transform.position + terrain.terrainData.size * 0.5f, terrain.terrainData.size);
            if (!found) bounds = b;
            else bounds.Encapsulate(b);
            found = true;
        }
        if (!found) Debug.LogWarning("MapSystem: no Terrain in the scene, so there is no map.");
        return found;
    }

    bool FindPlayer() => PlayerRef.Find(ref player);

    // ---------- UI ----------

    void BuildUI()
    {
        GameObject canvasObject = UIKit.CreateCanvas("MapCanvas", transform, 40);
        Sprite dot = CreateDotSprite();
        dotSprite = dot;

        // Minimap (top-right corner)
        GameObject frame = CreateImage("Minimap", canvasObject.transform, new Color(0f, 0f, 0f, 0.6f), null);
        minimapRoot = frame.GetComponent<RectTransform>();
        minimapRoot.anchorMin = minimapRoot.anchorMax = minimapRoot.pivot = Vector2.one;
        minimapRoot.anchoredPosition = -minimapMargin;
        minimapRoot.sizeDelta = Vector2.one * (minimapSize + 6f);
        frame.AddComponent<RectMask2D>();

        GameObject mapObject = new GameObject("MinimapImage", typeof(RectTransform), typeof(RawImage));
        mapObject.transform.SetParent(frame.transform, false);
        minimapImage = mapObject.GetComponent<RawImage>();
        minimapImage.raycastTarget = false;
        mapObject.GetComponent<RectTransform>().sizeDelta = Vector2.one * minimapSize;
        minimapFog = CreateFogImage("MinimapExplored", frame.transform, minimapSize);

        minimapTent = CreateImage("TentIcon", frame.transform, tentColor, dot).GetComponent<RectTransform>();
        minimapTent.sizeDelta = Vector2.one * 14f;
        minimapDelivery = CreateImage("DeliveryIcon", frame.transform, deliveryColor, dot).GetComponent<RectTransform>();
        minimapDelivery.sizeDelta = Vector2.one * 14f;
        minimapArrow = CreateImage("PlayerDot", frame.transform, playerColor, dot).GetComponent<RectTransform>();
        minimapArrow.sizeDelta = Vector2.one * playerIconSize;

        // Big map (center of the screen): optional background image, the map, optional frame on top
        LootWindow style = LootWindow.Instance;
        float pad = style != null ? style.mapFramePadding : 40f;
        bigMapWindow = new GameObject("BigMapWindow", typeof(RectTransform));
        bigMapWindow.transform.SetParent(canvasObject.transform, false);
        bigMapWindow.GetComponent<RectTransform>().sizeDelta = Vector2.one * bigMapSize;

        if (style != null && style.mapBackgroundSprite != null)
        {
            // (UIKit.CreateImage 9-slices it when the sprite has borders)
            CreateImage("MapBackground", bigMapWindow.transform, style.windowSpriteTint, style.mapBackgroundSprite).GetComponent<RectTransform>().sizeDelta = Vector2.one * (bigMapSize + pad * 2f);
        }

        GameObject big = new GameObject("BigMap", typeof(RectTransform), typeof(RawImage));
        big.transform.SetParent(bigMapWindow.transform, false);
        bigMapRoot = big.GetComponent<RectTransform>();
        bigMapRoot.sizeDelta = Vector2.one * bigMapSize;
        big.GetComponent<RawImage>().raycastTarget = false;
        bigMapFog = CreateFogImage("BigMapExplored", big.transform, bigMapSize);

        bigMapTent = CreateImage("TentIcon", big.transform, tentColor, dot).GetComponent<RectTransform>();
        bigMapTent.sizeDelta = Vector2.one * 18f;
        bigMapDelivery = CreateImage("DeliveryIcon", big.transform, deliveryColor, dot).GetComponent<RectTransform>();
        bigMapDelivery.sizeDelta = Vector2.one * 20f;
        bigMapArrow = CreateImage("PlayerDot", big.transform, playerColor, dot).GetComponent<RectTransform>();
        bigMapArrow.sizeDelta = Vector2.one * (playerIconSize + 6f);

        if (style != null && style.mapFrameSprite != null)
            LootWindow.AddBorder(bigMapWindow.GetComponent<RectTransform>(), style.mapFrameSprite, style.windowBorderColor, pad);
        bigMapWindow.SetActive(false);
    }

    // Dark layer over the map, cleared where you have been
    RawImage CreateFogImage(string name, Transform parent, float size)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<RectTransform>().sizeDelta = Vector2.one * size;
        RawImage image = obj.GetComponent<RawImage>();
        image.raycastTarget = false;
        image.color = Color.white;
        EnsureFog();
        image.texture = fogTexture;
        return image;
    }

    // Image that ignores clicks
    static GameObject CreateImage(string name, Transform parent, Color color, Sprite sprite)
    {
        GameObject obj = UIKit.CreateImage(name, parent, color, sprite);
        obj.GetComponent<Image>().raycastTarget = false;
        return obj;
    }

    static Sprite CreateDotSprite()
    {
        const int size = 32;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16f, 16f));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(15f - d)));
            }
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
