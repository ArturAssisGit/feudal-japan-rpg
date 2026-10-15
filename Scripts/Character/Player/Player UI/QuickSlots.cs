using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Quick slots for food and drink (creates itself).
//   Put an item in a slot: open the inventory, put the mouse over a consumable and press 3, 4 or 5.
//   Use it in game: press the same key (eats/drinks one from your bags).
//   Clear a slot: with the inventory open, put the mouse over an empty slot and press the key.
public class QuickSlots : MonoBehaviour
{
    public static QuickSlots Instance { get; private set; }

    [Header("Keys (one per quick slot)")]
    public KeyCode[] keys = { KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5 };

    [Header("Look")]
    public float slotSize = 44f;
    public float spacing = 8f;
    [Tooltip("On = a column just above the health bar (found by itself). Off = at Fallback Position")] public bool aboveHealthBar = true;
    [Tooltip("Moves the column from the top-left corner of the health bar (X right, Y up, pixels at 1080p)")] public Vector2 offsetFromHealthBar = new Vector2(0f, 16f);
    [Tooltip("Bottom-left corner of the column when there is no health bar (pixels from the bottom-left of the screen)")] public Vector2 fallbackPosition = new Vector2(28f, 110f);

    [Header("When they show (discreet)")]
    [Tooltip("Shown for a moment when you draw the sword or bow (then they fade out, even in combat)")] public bool showWhenArmed = true;
    [Tooltip("Shown while the inventory is open (to fill them)")] public bool showWithInventory = true;
    [Tooltip("Seconds they stay visible after using or filling a slot")] public float showSeconds = 2.5f;
    [Tooltip("Seconds of the fade in / fade out")] public float fadeSeconds = 0.5f;
    [Tooltip("How see-through they are when visible (1 = solid)")] [Range(0.2f, 1f)] public float visibleAlpha = 0.85f;

    private PlayerItemData[] items;
    private CanvasGroup group;
    private float alpha, visibleUntil;
    private PlayerCombat combat;
    private bool wasArmed;
    private GameObject canvas, row;
    private Image[] icons, backgrounds;
    private TextMeshProUGUI[] counts;
    private float[] flashUntil;
    private TextMeshProUGUI hintText;
    private float hintUntil;
    private bool inMenu;
    private float nextMenuCheck;
    private RectTransform rowRect, hintRect;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<QuickSlots>("QuickSlots");
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        items = new PlayerItemData[keys.Length];
        flashUntil = new float[keys.Length];
        Load();
    }

    // Short message above the quick slots (also used by the inventory: "Click again to use...")
    public static void Hint(string text, float seconds = 1.5f)
    {
        if (Instance == null) return;
        if (Instance.canvas == null) Instance.Build();
        Instance.hintText.text = text ?? "";
        Instance.hintUntil = string.IsNullOrEmpty(text) ? 0f : Time.unscaledTime + seconds;
    }

    void Update()
    {
        if (canvas == null) Build();

        if (Time.unscaledTime >= nextMenuCheck)
        {
            nextMenuCheck = Time.unscaledTime + 0.5f;
            GameObject player = PlayerRef.Object;
            inMenu = TitleScreenManager.IsOpen || player == null;
            if (player != null && (combat == null || !combat.isActiveAndEnabled)) combat = player.GetComponentInChildren<PlayerCombat>();
            PlaceColumn(player);
        }

        // Hidden in the menu, on the death/sleep screens and behind other windows (shown with the inventory, to fill the slots)
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        bool show = !inMenu && !RespawnSystem.IsDeathScreenOpen && !SleepSystem.IsSleeping && !PauseMenu.IsOpen &&
                    (gear == null || !gear.IsOpen || gear.IsInventoryOpen);
        if (row.activeSelf != show) row.SetActive(show);
        hintText.alpha = Mathf.Clamp01((hintUntil - Time.unscaledTime) * 3f);
        if (!show) { alpha = 0f; group.alpha = 0f; return; }

        // Clean UI: they only appear for a moment (drawing the weapon, using a slot) and fade out again, even in combat.
        // With the inventory open they stay, so they can be filled
        bool armed = combat != null && combat.IsArmed;
        if (showWhenArmed && armed && !wasArmed) visibleUntil = Time.unscaledTime + showSeconds;
        wasArmed = armed;
        bool inventory = showWithInventory && gear != null && gear.IsInventoryOpen;
        bool wanted = inventory || Time.unscaledTime < visibleUntil;
        alpha = Mathf.MoveTowards(alpha, wanted ? visibleAlpha : 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, fadeSeconds));
        group.alpha = alpha;

        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] == KeyCode.None || !Input.GetKeyDown(keys[i]) || ScreenFade.IsActive) continue;
            if (gear != null && gear.IsInventoryOpen && PlayerItemSlot.Hovered != null) Assign(i, PlayerItemSlot.Hovered.itemData);
            else if (gear != null && !gear.IsOpen && !PlacementSystem.IsPlacing && !PauseMenu.IsOpen) Use(i);
        }

        Refresh();
    }

    void Assign(int index, PlayerItemData item)
    {
        if (item != null && item.type != ItemType.Consumable)
        {
            Hint("Only food and drinks can go in a quick slot");
            return;
        }
        if (item != null)
            for (int i = 0; i < items.Length; i++)
                if (items[i] == item) items[i] = null; // one slot per item
        items[index] = item;
        flashUntil[index] = Time.unscaledTime + 0.25f;
        visibleUntil = Time.unscaledTime + showSeconds;
        Hint(item != null ? $"{ItemUpgrades.DisplayName(item)} -> [{KeyName(keys[index])}]" : $"[{KeyName(keys[index])}] cleared");
        Save();
    }

    void Use(int index)
    {
        PlayerItemData item = items[index];
        visibleUntil = Time.unscaledTime + showSeconds; // pressing the key shows them for a moment
        if (item == null) return;
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        PlayerStats stats = PlayerStatsNow();
        if (stats != null && stats.IsDead) return;
        if (gear == null || !gear.ConsumeFromInventory(item))
        {
            Hint($"No {ItemUpgrades.DisplayName(item)} left");
            return;
        }
        flashUntil[index] = Time.unscaledTime + 0.2f;
    }

    static PlayerStats PlayerStatsNow() => PlayerRef.Get<PlayerStats>();

    // ---------- Look ----------

    void Refresh()
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        for (int i = 0; i < items.Length; i++)
        {
            PlayerItemData item = items[i];
            int count = item != null && inventory != null ? inventory.CountItem(item) : 0;
            icons[i].enabled = item != null && item.itemIcon != null;
            if (icons[i].enabled)
            {
                icons[i].sprite = item.itemIcon;
                icons[i].color = count > 0 ? Color.white : new Color(1f, 1f, 1f, 0.3f);
            }
            counts[i].text = item != null ? count.ToString() : "";
            bool flash = Time.unscaledTime < flashUntil[i];
            backgrounds[i].color = flash ? new Color(0.45f, 0.32f, 0.16f, 1f) : new Color(0.13f, 0.095f, 0.06f, 1f);
        }
    }

    void Build()
    {
        canvas = UIKit.CreateCanvas("QuickSlotsCanvas", transform, 120); // above the inventory, so the hint can be read

        row = new GameObject("Row", typeof(RectTransform));
        row.transform.SetParent(canvas.transform, false);
        group = row.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        // A vertical column in the bottom-left corner (key 3 on top)
        rowRect = row.GetComponent<RectTransform>();
        float height = keys.Length * slotSize + (keys.Length - 1) * spacing;
        rowRect.anchorMin = rowRect.anchorMax = rowRect.pivot = Vector2.zero;
        rowRect.sizeDelta = new Vector2(slotSize, height);
        rowRect.anchoredPosition = fallbackPosition;

        icons = new Image[keys.Length];
        backgrounds = new Image[keys.Length];
        counts = new TextMeshProUGUI[keys.Length];
        Sprite rounded = LootWindow.RoundedSprite(7);
        Color gold = UIStyle.Gold;

        for (int i = 0; i < keys.Length; i++)
        {
            GameObject slot = UIKit.CreateImage("QuickSlot" + i, row.transform, new Color(0.13f, 0.095f, 0.06f, 1f), rounded);
            Image bg = slot.GetComponent<Image>();
            bg.raycastTarget = false;
            Outline outline = slot.AddComponent<Outline>();
            outline.effectColor = new Color(gold.r, gold.g, gold.b, 0.35f);
            outline.effectDistance = new Vector2(1f, 1f);
            RectTransform r = slot.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
            r.sizeDelta = new Vector2(slotSize, slotSize);
            r.anchoredPosition = new Vector2(0f, -i * (slotSize + spacing));
            backgrounds[i] = bg;

            GameObject icon = UIKit.CreateImage("Icon", slot.transform, Color.white);
            Image iconImage = icon.GetComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            UIKit.SetRect(iconImage.rectTransform, Vector2.zero, Vector2.one, new Vector2(5f, 5f), new Vector2(-5f, -5f));
            icons[i] = iconImage;

            TextMeshProUGUI key = UIKit.CreateText("Key", slot.transform, KeyName(keys[i]), 12f, gold, LootWindow.GameFont, TMPro.TextAlignmentOptions.TopLeft);
            UIKit.SetRect(key.rectTransform, Vector2.zero, Vector2.one, new Vector2(4f, 1f), new Vector2(-3f, -2f));
            key.fontStyle = FontStyles.Bold;

            TextMeshProUGUI count = UIKit.CreateText("Count", slot.transform, "", 12f, Color.white, LootWindow.GameFont, TMPro.TextAlignmentOptions.BottomRight);
            UIKit.SetRect(count.rectTransform, Vector2.zero, Vector2.one, new Vector2(3f, 2f), new Vector2(-4f, -1f));
            count.fontStyle = FontStyles.Bold;
            counts[i] = count;
        }

        hintText = UIKit.CreateText("Hint", canvas.transform, "", 18f, UIStyle.Cream, LootWindow.GameFont);
        hintText.alignment = TextAlignmentOptions.TopLeft;
        hintRect = hintText.rectTransform;
        hintRect.anchorMin = hintRect.anchorMax = Vector2.zero;
        hintRect.pivot = new Vector2(0f, 1f);
        hintRect.sizeDelta = new Vector2(700f, 30f);
        hintText.outlineWidth = 0.2f;
        hintText.outlineColor = new Color32(0, 0, 0, 255);
        hintText.alpha = 0f;
        PlaceColumn(PlayerRef.Object);
    }

    // Puts the column right above the health bar (works with any resolution); the hint goes to its right
    void PlaceColumn(GameObject player)
    {
        if (rowRect == null) return;
        Vector2 position = fallbackPosition;
        RectTransform bar = aboveHealthBar ? HealthBar(player) : null;
        RectTransform canvasRect = canvas.transform as RectTransform;
        if (bar != null && canvasRect != null)
        {
            Vector3[] corners = new Vector3[4];
            bar.GetWorldCorners(corners); // 1 = top-left
            Canvas barCanvas = bar.GetComponentInParent<Canvas>();
            Camera cam = barCanvas != null && barCanvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? barCanvas.rootCanvas.worldCamera : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, corners[1]);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local))
                position = local - canvasRect.rect.min + offsetFromHealthBar;
        }
        rowRect.anchoredPosition = position;
        if (hintRect != null) hintRect.anchoredPosition = position + new Vector2(slotSize + 12f, rowRect.sizeDelta.y);
    }

    // The health bar: the frame around PlayerStats' Health Bar Fill (or the fill itself)
    static RectTransform HealthBar(GameObject player)
    {
        PlayerStats stats = player != null ? player.GetComponentInChildren<PlayerStats>() : null;
        if (stats == null) stats = FindAnyObjectByType<PlayerStats>();
        if (stats == null || stats.healthBarFill == null || !stats.healthBarFill.gameObject.activeInHierarchy) return null;
        RectTransform fill = stats.healthBarFill.rectTransform;
        RectTransform parent = fill.parent as RectTransform;
        return parent != null && parent.GetComponent<Canvas>() == null ? parent : fill;
    }

    static string KeyName(KeyCode key)
    {
        string s = key.ToString();
        return s.StartsWith("Alpha") ? s.Substring(5) : s;
    }

    // ---------- Remembered between sessions ----------

    void Save()
    {
        for (int i = 0; i < items.Length; i++) PlayerPrefs.SetString("QuickSlot" + i, ItemDatabase.IdOf(items[i]) ?? "");
        PlayerPrefs.Save();
    }

    void Load()
    {
        for (int i = 0; i < items.Length; i++)
        {
            string id = PlayerPrefs.GetString("QuickSlot" + i, "");
            items[i] = string.IsNullOrEmpty(id) ? null : ItemDatabase.Find(id);
        }
    }
}
