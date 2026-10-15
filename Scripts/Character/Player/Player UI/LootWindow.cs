using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Creates itself at game start. To customise the look, add it to a scene object and fill the Style fields.
public class LootWindow : MonoBehaviour
{
    public static LootWindow Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.windowOpen;

    [Header("Controls")]
    public KeyCode interactKey = KeyCode.E;
    public KeyCode closeKey = KeyCode.Escape;
    public KeyCode altCloseKey = KeyCode.Tab;

    [Header("Loot Prompt")]
    public Vector2 promptSize = new Vector2(130f, 34f);
    public float promptFontSize = 18f;
    [Tooltip("Position from the screen center")] public Vector2 promptPosition = new Vector2(0f, -150f);
    [Tooltip("Off = only the text is shown (no dark box behind [E] Loot / [E] Gather)")] public bool promptBackground = false;

    [Header("Interaction texts ([E] Loot / Gather / Trade...)")]
    [Tooltip("Where those texts appear, from the CENTER of the screen (X right, Y up; negative Y = lower). They stack downwards from here")]
    public Vector2 promptsFromCenter = new Vector2(0f, -190f);
    [Tooltip("Old setting (top-left corner), not used anymore")] [HideInInspector] public Vector2 cornerOffset = new Vector2(28f, 24f);
    [Tooltip("Size of those texts")] public float cornerFontSize = 16f;

    // ---------- UI skin helpers (used by every code-made window) ----------

    static Sprite Pick(Sprite a, Sprite b) => a != null ? a : b;

    static void SetSprite(Image image, Sprite sprite)
    {
        image.sprite = sprite;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
    }

    // Rounded 9-sliced sprite made in code (corner radius in pixels) - used when no sprite is assigned
    private static readonly Dictionary<int, Sprite> roundedCache = new Dictionary<int, Sprite>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRounded() => roundedCache.Clear();

    public static Sprite RoundedSprite(int radius)
    {
        if (roundedCache.TryGetValue(radius, out Sprite cached) && cached != null) return cached;
        int size = radius * 2 + 2;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.hideFlags = HideFlags.HideAndDontSave;
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float dx = Mathf.Max(radius - px, 0f, px - (size - radius));
                float dy = Mathf.Max(radius - py, 0f, py - (size - radius));
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        sprite.hideFlags = HideFlags.HideAndDontSave;
        roundedCache[radius] = sprite;
        return sprite;
    }

    static Color Solid(Color c) { c.a = 1f; return c; }

    // Window background: own sprite, else Panel Sprite, else a solid rounded brown panel; adds the frame on top
    public static void SkinWindow(Image image, Sprite specific)
    {
        if (image == null || Instance == null) return;
        Sprite sprite = Pick(specific, Instance.panelSprite);
        if (sprite != null)
        {
            SetSprite(image, sprite);
            image.color = Instance.windowSpriteTint;
        }
        else
        {
            SetSprite(image, RoundedSprite(26));
            image.color = Solid(Instance.panelColor);
        }
        AddBorder(image.rectTransform, Instance.windowBorderSprite, Instance.windowBorderColor, Instance.windowBorderPadding);
    }

    // Background behind scrolling lists (solid, rounded)
    public static void SkinList(Image image)
    {
        if (image == null || Instance == null) return;
        if (Instance.listBackgroundSprite != null)
        {
            SetSprite(image, Instance.listBackgroundSprite);
            image.color = Instance.windowSpriteTint;
        }
        else
        {
            SetSprite(image, RoundedSprite(16));
            image.color = Solid(Instance.listBackgroundColor);
        }
    }

    // A frame image drawn on top of 'target' (ignored by layout groups)
    public static void AddBorder(RectTransform target, Sprite border, Color color, float padding)
    {
        if (target == null || border == null || target.Find("Border") != null) return;
        GameObject go = new GameObject("Border", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(target, false);
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        Image image = go.GetComponent<Image>();
        SetSprite(image, border);
        image.color = color;
        image.raycastTarget = false;
        SetRect(go.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(-padding, -padding), new Vector2(padding, padding));
        go.transform.SetAsLastSibling();
    }

    // Puts an interaction text in the middle of the screen, a bit low. line 0 = prompt, line 1 = message under it
    // (the name stayed "Corner" so every script that calls it keeps working)
    public static void PlaceInCorner(RectTransform rect, TextMeshProUGUI text, int line = 0, float width = 800f)
    {
        Vector2 offset = PromptsOffset;
        float size = Instance != null ? Instance.cornerFontSize : 16f;
        float lineHeight = size * 1.6f;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(offset.x, offset.y - line * lineHeight);
        rect.sizeDelta = new Vector2(width, lineHeight);
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Top;
        RegisterCornerText(rect, text, line);
    }

    static Vector2 PromptsOffset => Instance != null ? Instance.promptsFromCenter : new Vector2(0f, -190f);

    // ---------- Corner texts: every corner text is remembered and the visible ones are stacked each frame ----------

    class CornerText
    {
        public RectTransform rect;
        public TextMeshProUGUI text;
        public int line;
        public int order;
    }

    private static readonly List<CornerText> cornerTexts = new List<CornerText>();
    private static int cornerOrder;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCornerTexts()
    {
        cornerTexts.Clear();
        cornerOrder = 0;
    }

    static void RegisterCornerText(RectTransform rect, TextMeshProUGUI text, int line)
    {
        foreach (CornerText c in cornerTexts)
        {
            if (c.rect != rect) continue;
            c.text = text;
            c.line = line;
            return;
        }
        cornerTexts.Add(new CornerText { rect = rect, text = text, line = line, order = cornerOrder++ });
    }

    static bool IsShowing(CornerText c)
    {
        if (c.rect == null || c.text == null || !c.rect.gameObject.activeInHierarchy || !c.text.enabled) return false;
        if (string.IsNullOrWhiteSpace(c.text.text) || c.text.alpha < 0.01f) return false;
        CanvasGroup group = c.rect.GetComponentInParent<CanvasGroup>();
        return group == null || group.alpha > 0.01f;
    }

    static void ArrangeCornerTexts()
    {
        cornerTexts.RemoveAll(c => c.rect == null);
        if (cornerTexts.Count == 0) return;

        var showing = cornerTexts.FindAll(IsShowing);
        showing.Sort((a, b) => a.line != b.line ? a.line.CompareTo(b.line) : a.order.CompareTo(b.order));

        Vector2 offset = PromptsOffset;
        float size = Instance != null ? Instance.cornerFontSize : 16f;
        float gap = size * 0.35f, y = offset.y;
        foreach (CornerText c in showing)
        {
            c.rect.anchoredPosition = new Vector2(offset.x, y);
            y -= Mathf.Max(c.rect.sizeDelta.y, c.text.preferredHeight) + gap;
        }
    }

    void LateUpdate() => ArrangeCornerTexts();

    [Header("Style (optional)")]
    [Tooltip("TextMeshPro Font Asset (empty = default font)")] public TMP_FontAsset font;
    [Tooltip("Background image of the window (9-sliced if it has borders)")] public Sprite panelSprite;
    public Color panelColor = new Color(0.17f, 0.12f, 0.075f, 1f);
    [Tooltip("Background image of each item row")] public Sprite rowSprite;
    [Tooltip("Background image of the [E] Loot prompt")] public Sprite promptSprite;

    [Header("UI Skin (used by ALL code-made windows: loot, shop, crafting, tooltip, map, death screen)")]
    [Tooltip("Tint of the window images when a sprite is set (white = original colors)")] public Color windowSpriteTint = Color.white;
    [Tooltip("Frame drawn ON TOP of every window (a 9-sliced border with a transparent middle)")] public Sprite windowBorderSprite;
    public Color windowBorderColor = Color.white;
    [Tooltip("How far the frame sticks out of the window (pixels)")] public float windowBorderPadding = 0f;
    [Tooltip("Background of the merchant window (empty = Panel Sprite)")] public Sprite shopWindowSprite;
    [Tooltip("Background of the crafting / bonfire window (empty = Panel Sprite)")] public Sprite craftingWindowSprite;
    [Tooltip("Background of the item tooltip (empty = Panel Sprite)")] public Sprite tooltipSprite;
    [Tooltip("Background behind the scrolling lists (shop, crafting)")] public Sprite listBackgroundSprite;
    public Color listBackgroundColor = new Color(0.115f, 0.08f, 0.05f, 1f);
    [Tooltip("Image behind the big map (e.g. old paper)")] public Sprite mapBackgroundSprite;
    [Tooltip("Frame on top of the big map (9-sliced border)")] public Sprite mapFrameSprite;
    [Tooltip("Space between the map picture and its background/frame (pixels)")] public float mapFramePadding = 40f;
    [Tooltip("Full-screen image of the death screen (empty = dark overlay)")] public Sprite deathScreenSprite;
    public Color titleColor = new Color(0.95f, 0.82f, 0.55f);
    public Color textColor = Color.white;
    public Color detailColor = new Color(0.78f, 0.72f, 0.6f);

    private Transform player;
    private bool windowOpen;
    private LootBag currentBag;

    private GameObject promptObject;
    private TextMeshProUGUI promptText;
    private string openPrompt, lootPrompt;
    private GameObject panel;
    private RectTransform listRoot;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI messageText;
    private float messageUntil;
    private readonly List<GameObject> rows = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<LootWindow>("LootWindow");
    }

    // The game's font even when this window was made by code (the menu and the save manager tell it which one it is)
    public static TMP_FontAsset DefaultFont;
    public static TMP_FontAsset GameFont => Instance != null && Instance.font != null ? Instance.font : DefaultFont;

    public static void SetDefaultFont(TMP_FontAsset f)
    {
        if (f == null) return;
        DefaultFont = f;
        if (Instance != null && Instance.font == null) Instance.font = f;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (font == null) font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Art/Fonts/Almendra-Regular SDF.asset");
    }
#endif

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (font == null) font = DefaultFont;
        else if (DefaultFont == null) DefaultFont = font;
        BuildUI();
    }

    void Update()
    {
        // No EventSystem = no clickable UI: create one if the scene has none
        if (EventSystem.current == null && FindAnyObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Debug.Log("LootWindow: the scene had no EventSystem, so one was created (needed to click the UI).");
        }

        if (!FindPlayer())
        {
            promptObject.SetActive(false);
            if (windowOpen) Close();
            return;
        }

        if (windowOpen)
        {
            bool tooFar = currentBag == null || WorldUtil.FlatDistance(player.position, currentBag.transform.position) > currentBag.lootRange + 1f;
            if (tooFar || currentBag.IsEmpty || Input.GetKeyDown(closeKey) || Input.GetKeyDown(altCloseKey))
            {
                Close();
                return;
            }
            if (Input.GetKeyDown(interactKey)) TakeAll();
            if (messageText.text.Length > 0 && Time.time > messageUntil) messageText.text = "";
            return;
        }

        bool inventoryOpen = PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.IsInventoryOpen;
        LootBag nearby = inventoryOpen ? null : FindBagInRange();
        promptObject.SetActive(nearby != null);
        if (nearby != null)
        {
            ApplyPromptLayout();
            if (openPrompt == null) { openPrompt = $"[{interactKey}] Open"; lootPrompt = $"[{interactKey}] Loot"; } // built once, not every frame
            promptText.text = nearby.IsLocked ? nearby.chest.lockedText : nearby.IsChest ? openPrompt : lootPrompt;
        }

        if (nearby != null && !nearby.IsLocked && Input.GetKeyDown(interactKey) && !ScreenFade.IsActive) Open(nearby);
    }

    // Lets you tweak the prompt size live in the Inspector (during Play)
    void ApplyPromptLayout()
    {
        PlaceInCorner(promptObject.GetComponent<RectTransform>(), promptText, 0);
        promptObject.GetComponent<Image>().enabled = promptBackground;
    }

    bool FindPlayer() => PlayerRef.Find(ref player);

    LootBag FindBagInRange()
    {
        return WorldUtil.Nearest(LootBag.All, player.position, b => b.lootRange, b => !b.IsEmpty);
    }

    // ---------- Open / close ----------

    void Open(LootBag bag)
    {
        if (bag != null && bag.IsChest) GatherAnimation.PlayOnce(); // chests only: no animation for enemy bodies / loot bags
        currentBag = bag;
        windowOpen = true;
        promptObject.SetActive(false);
        panel.SetActive(true);
        messageText.text = "";

        if (FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Rebuild();
    }

    void Close()
    {
        windowOpen = false;
        currentBag = null;
        panel.SetActive(false);

        bool inventoryOpen = PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.IsInventoryOpen;
        if (!inventoryOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void TakeOne(PlayerItemData item)
    {
        if (currentBag == null) return;
        if (!currentBag.TakeItem(item)) ShowMessage("Inventory full!");
        CloseOrRebuild();
    }

    void TakeAll()
    {
        if (currentBag == null) return;
        int total = currentBag.items.Count;
        if (currentBag.TakeAll() < total) ShowMessage("Inventory full!");
        CloseOrRebuild();
    }

    void CloseOrRebuild()
    {
        if (currentBag.IsEmpty) Close();
        else Rebuild();
    }

    void ShowMessage(string text)
    {
        messageText.text = text;
        messageUntil = Time.time + 2f;
    }

    // ---------- UI ----------

    void Rebuild()
    {
        foreach (GameObject row in rows) Destroy(row);
        rows.Clear();

        List<PlayerItemData> distinct = currentBag.DistinctItems();
        titleText.text = $"Loot ({distinct.Count})";
        foreach (PlayerItemData item in distinct) rows.Add(CreateRow(item, currentBag.CountOf(item)));
    }

    GameObject CreateRow(PlayerItemData item, int count)
    {
        GameObject row = CreateImage("Row", listRoot, Color.white, rowSprite);
        row.AddComponent<LayoutElement>().preferredHeight = 66f;

        Button button = row.AddComponent<Button>();
        button.targetGraphic = row.GetComponent<Image>();
        ColorBlock colors = button.colors;
        bool hasSprite = rowSprite != null;
        colors.normalColor = hasSprite ? Color.white : new Color(1f, 1f, 1f, 0.06f);
        colors.highlightedColor = hasSprite ? new Color(1f, 0.85f, 0.6f) : new Color(0.85f, 0.65f, 0.3f, 0.35f);
        colors.selectedColor = colors.normalColor;
        colors.pressedColor = hasSprite ? new Color(0.8f, 0.65f, 0.45f) : new Color(0.85f, 0.65f, 0.3f, 0.6f);
        button.colors = colors;
        button.onClick.AddListener(() => TakeOne(item));

        GameObject icon = CreateImage("Icon", row.transform, item.itemIcon != null ? Color.white : new Color(1f, 1f, 1f, 0.15f));
        Image iconImage = icon.GetComponent<Image>();
        iconImage.sprite = item.itemIcon;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        RectTransform iconRect = icon.GetComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(38f, 0f);
        iconRect.sizeDelta = new Vector2(54f, 54f);

        string itemName = string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName;
        if (count > 1) itemName += $"  x{count}";
        TextMeshProUGUI nameText = CreateText("Name", row.transform, itemName, 26, TextAlignmentOptions.BottomLeft, textColor);
        SetRect(nameText.rectTransform, new Vector2(0f, 0.5f), Vector2.one, new Vector2(78f, 0f), new Vector2(-12f, -4f));
        nameText.overflowMode = TextOverflowModes.Overflow; // with tall fonts Ellipsis hid the whole name

        TextMeshProUGUI detailText = CreateText("Detail", row.transform, Describe(item), 19, TextAlignmentOptions.TopLeft, detailColor);
        SetRect(detailText.rectTransform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(78f, 4f), new Vector2(-12f, 0f));
        return row;
    }

    public static string Describe(PlayerItemData item)
    {
        string type = item.type switch
        {
            ItemType.Weapon => "Weapon",
            ItemType.Helmet => "Helmet",
            ItemType.Chest => "Armor",
            ItemType.Backpack => "Backpack",
            ItemType.Consumable => "Consumable",
            ItemType.Bow => "Bow",
            ItemType.Utilities => "Utility",
            ItemType.Place => "Placeable",
            ItemType.Stone => "Stone",
            ItemType.Lantern => "Lantern",
            _ => item.type.ToString()
        };

        if (item.weaponDamage > 0f) type += $"  ·  Damage {item.weaponDamage:0}";
        if (item.armorDefense > 0f) type += $"  ·  Defense {item.armorDefense:0}";
        if (item.type == ItemType.Backpack && item.backpackPerks != null)
            foreach (BackpackPerk perk in item.backpackPerks) type += "  ·  " + PerkName(perk);
        if (item.IsUpgraded) type += "  ·  " + ItemUpgrades.UpgradeLabel(item);
        if (item.IsUpgradeStone) type += "  ·  " + ItemUpgrades.StoneEffect(item.upgradeStone);
        if (item.type == ItemType.Consumable)
        {
            if (item.foodRestore > 0f) type += $"  ·  +{item.foodRestore:0} Food";
            if (item.waterRestore > 0f) type += $"  ·  +{item.waterRestore:0} Water";
            if (item.healthRestore > 0f) type += $"  ·  +{item.healthRestore:0} Health";
        }
        return type;
    }

    public static string PerkName(BackpackPerk perk) => perk switch
    {
        BackpackPerk.NoSpeedPenalty => "No speed penalty",
        BackpackPerk.HalfSprintStamina => "Half sprint stamina",
        BackpackPerk.DoubleMoney => "Double money",
        BackpackPerk.Quiver => "+25% arrow damage",
        BackpackPerk.FreeBlock => "Half block stamina",
        BackpackPerk.LuckyFind => "Lucky find",
        BackpackPerk.QuickDraw => "Quick draw",
        BackpackPerk.HeavyHitter => "+30% strong attack",
        _ => perk.ToString()
    };

    void BuildUI()
    {
        GameObject canvasObject = UIKit.CreateCanvas("LootCanvas", transform, 50, true);

        // "[E] Loot" prompt
        promptObject = CreateImage("Prompt", canvasObject.transform, promptSprite != null ? Color.white : new Color(0f, 0f, 0f, 0.6f), promptSprite);
        promptObject.GetComponent<Image>().raycastTarget = false;
        RectTransform promptRect = promptObject.GetComponent<RectTransform>();
        promptRect.anchoredPosition = promptPosition;
        promptRect.sizeDelta = promptSize;
        promptText = CreateText("Text", promptObject.transform, "[E] Loot", promptFontSize, TextAlignmentOptions.Center, textColor);
        SetRect(promptText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        promptObject.GetComponent<Image>().enabled = promptBackground;
        promptObject.SetActive(false);

        // Loot window
        panel = CreateImage("LootPanel", canvasObject.transform, panelColor, panelSprite);
        SkinWindow(panel.GetComponent<Image>(), null);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.anchoredPosition = new Vector2(160f, 0f);
        panelRect.sizeDelta = new Vector2(480f, 0f);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 14, 14);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        titleText = CreateText("Title", panel.transform, "Loot", 32, TextAlignmentOptions.Left, titleColor);
        titleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;

        GameObject list = new GameObject("Items", typeof(RectTransform));
        list.transform.SetParent(panel.transform, false);
        listRoot = list.GetComponent<RectTransform>();
        VerticalLayoutGroup listLayout = list.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 6f;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;

        messageText = CreateText("Message", panel.transform, "", 22, TextAlignmentOptions.Center, new Color(1f, 0.45f, 0.4f));
        messageText.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        TextMeshProUGUI footer = CreateText("Footer", panel.transform, "Click: Take     [E] Take All     [Esc] Close", 19, TextAlignmentOptions.Center, new Color(0.7f, 0.68f, 0.62f));
        footer.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        panel.SetActive(false);
    }

    static GameObject CreateImage(string name, Transform parent, Color color, Sprite sprite = null) => UIKit.CreateImage(name, parent, color, sprite);

    TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, TextAlignmentOptions alignment, Color color)
        => UIKit.CreateText(name, parent, text, size, color, font, alignment, TextOverflowModes.Ellipsis);

    static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        => UIKit.SetRect(rect, anchorMin, anchorMax, offsetMin, offsetMax);
}
