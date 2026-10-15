using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Creates itself at game start (LootWindow style). Near a HorseStable: [E] Stable.
// Left: the horses of the stable. Right: a big card of the selected horse (picture, description, stats) with one button: Buy / Choose.
// Esc / Tab / E = close.
public class StableWindow : MonoBehaviour
{
    public static StableWindow Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;

    [Header("Controls")]
    public KeyCode interactKey = KeyCode.E;

    private Transform player;
    private bool open;
    private HorseStable stable;
    private int selected;

    // UI
    private TMP_FontAsset font;
    private Sprite rowSprite;
    private Color textColor, detailColor, titleColor;
    private TextMeshProUGUI promptText, toastText;
    private GameObject panel;
    private TextMeshProUGUI titleText, coinsText, messageText;
    private RectTransform listRoot;
    private Image iconImage;
    private TextMeshProUGUI nameText, statusText, descText, priceText, buttonText;
    private Image coinImage, buttonImage;
    private Button actionButton;
    private GameObject detailRoot;
    private readonly Image[] barFills = new Image[3];
    private readonly TextMeshProUGUI[] barValues = new TextMeshProUGUI[3];
    private float messageUntil, toastUntil;
    private readonly List<GameObject> rows = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<StableWindow>("StableWindow");
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start() => BuildUI();

    // A short message in the corner (used when you press H without a horse)
    public static void Toast(string text)
    {
        if (Instance == null || Instance.toastText == null) return;
        Instance.toastText.text = text;
        Instance.toastUntil = Time.unscaledTime + 3f;
    }

    void Update()
    {
        if (panel == null) return;
        if (messageText.text.Length > 0 && Time.unscaledTime > messageUntil) messageText.text = "";
        if (toastText.text.Length > 0 && Time.unscaledTime > toastUntil) toastText.text = "";

        if (!FindPlayer())
        {
            promptText.gameObject.SetActive(false);
            return;
        }

        if (open)
        {
            bool tooFar = stable == null || WorldUtil.FlatDistance(player.position, stable.transform.position) > stable.interactRange + 1.5f;
            if (tooFar || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(interactKey)) Close();
            return;
        }

        HorseStable nearby = FindStableInRange();
        promptText.gameObject.SetActive(nearby != null);
        if (nearby == null) return;

        promptText.text = $"[{interactKey}] {nearby.stableName}";
        if (Input.GetKeyDown(interactKey)) Open(nearby);
    }

    HorseStable FindStableInRange()
    {
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if ((gear != null && gear.IsOpen) || PlacementSystem.IsPlacing || RespawnSystem.IsDeathScreenOpen || HorseMount.PlayerMounted) return null;

        // Loot bags use E too: they win
        if (LootBag.AnyInRange(player.position)) return null;

        return WorldUtil.Nearest(HorseStable.All, player.position, n => n.interactRange);
    }

    // ---------- Open / close ----------

    void Open(HorseStable npc)
    {
        stable = npc;
        open = true;
        npc.FaceTowards(player.position);

        selected = 0;
        string active = HorseOwnership.Active;
        for (int i = 0; i < npc.horses.Count; i++)
            if (npc.horses[i] != null && npc.horses[i].Key == active) { selected = i; break; }

        promptText.gameObject.SetActive(false);
        panel.SetActive(true);
        messageText.text = "";
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Rebuild();
    }

    void Close()
    {
        open = false;
        stable = null;
        panel.SetActive(false);

        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (gear == null || !gear.IsInventoryOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // ---------- Buying / choosing ----------

    void OnAction()
    {
        if (!open || stable == null || selected < 0 || selected >= stable.horses.Count) return;
        HorseOffer offer = stable.horses[selected];
        if (offer == null) return;

        string key = offer.Key;
        if (string.IsNullOrEmpty(key) || offer.prefab == null)
        {
            ShowMessage("This horse is not set up (it needs a Prefab)");
            return;
        }

        if (HorseOwnership.IsOwned(key))
        {
            if (HorseOwnership.Active != key) UseHorse(offer, false);
        }
        else
        {
            int price = stable.PriceOf(offer);
            if (price > 0)
            {
                PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
                if (inventory == null || stable.coinItem == null)
                {
                    ShowMessage("This stable has no Coin Item set");
                    return;
                }
                if (inventory.CountItem(stable.coinItem) < price)
                {
                    ShowMessage("Not enough coins");
                    return;
                }
                inventory.RemoveItem(stable.coinItem, price);
            }
            HorseOwnership.Grant(key);
            UseHorse(offer, true);
        }
        Rebuild();
    }

    void UseHorse(HorseOffer offer, bool bought)
    {
        HorseOwnership.Select(offer.Key);

        // The horse that was waiting in the world goes away: the next H brings the chosen one
        HorseMount old = HorseMount.Main;
        if (old != null && !old.IsRidden && old.ownershipKey != offer.Key)
        {
            old.gameObject.SetActive(false);
            Destroy(old.gameObject);
        }

        ShowMessage(bought
            ? $"You bought {offer.DisplayName}! Press H to call it"
            : $"{offer.DisplayName} is now your horse. Press H to call it");
    }

    // ---------- Lists ----------

    void Rebuild()
    {
        if (!open || stable == null) return;
        foreach (GameObject row in rows) Destroy(row);
        rows.Clear();

        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        int coins = inventory != null && stable.coinItem != null ? inventory.CountItem(stable.coinItem) : 0;
        titleText.text = stable.stableName;
        coinsText.text = $"Coins: {coins}";

        string active = HorseOwnership.Active;
        for (int i = 0; i < stable.horses.Count; i++)
        {
            HorseOffer offer = stable.horses[i];
            if (offer == null) continue;
            int index = i;
            bool owned = HorseOwnership.IsOwned(offer.Key);
            int price = stable.PriceOf(offer);

            string tag;
            Color tagColor = textColor;
            if (owned && offer.Key == active) { tag = "Your horse"; tagColor = titleColor; }
            else if (owned) tag = "Owned";
            else
            {
                tag = price > 0 ? price.ToString() : "Free";
                if (price > coins) tagColor = new Color(1f, 0.45f, 0.4f);
            }
            rows.Add(CreateRow(offer, tag, tagColor, i == selected, () => { selected = index; Rebuild(); }));
        }

        RefreshDetail(coins);
    }

    GameObject CreateRow(HorseOffer offer, string tag, Color tagColor, bool highlighted, Action onClick)
    {
        Color normal = rowSprite != null ? Color.white : new Color(1f, 1f, 1f, 0.06f);
        Color hover = rowSprite != null ? new Color(1f, 0.85f, 0.6f) : new Color(0.85f, 0.65f, 0.3f, 0.35f);
        Color marked = new Color(0.85f, 0.65f, 0.3f, 0.55f);

        GameObject row = UIKit.CreateImage("Row", listRoot, highlighted ? marked : normal, rowSprite);
        row.AddComponent<LayoutElement>().preferredHeight = 64f;
        row.AddComponent<StableRow>().Setup(row.GetComponent<Image>(), highlighted ? marked : normal, hover, onClick);

        Sprite icon = offer.item != null ? offer.item.itemIcon : null;
        GameObject iconObject = UIKit.CreateImage("Icon", row.transform, icon != null ? Color.white : new Color(1f, 1f, 1f, 0.15f), icon);
        Image iconImg = iconObject.GetComponent<Image>();
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(36f, 0f);
        iconRect.sizeDelta = new Vector2(50f, 50f);

        TextMeshProUGUI nameLabel = Text("Name", row.transform, offer.DisplayName, 24f, TextAlignmentOptions.Left, textColor);
        UIKit.SetRect(nameLabel.rectTransform, Vector2.zero, Vector2.one, new Vector2(72f, 0f), new Vector2(-150f, 0f));

        TextMeshProUGUI tagLabel = Text("Tag", row.transform, tag, 22f, TextAlignmentOptions.Right, tagColor);
        UIKit.SetRect(tagLabel.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-150f, 0f), new Vector2(-16f, 0f));
        return row;
    }

    // The big card of the selected horse
    void RefreshDetail(int coins)
    {
        HorseOffer offer = selected >= 0 && selected < stable.horses.Count ? stable.horses[selected] : null;
        detailRoot.SetActive(offer != null);
        if (offer == null)
        {
            ShowMessage("This stable has no horses for sale yet");
            return;
        }

        string key = offer.Key;
        bool owned = HorseOwnership.IsOwned(key);
        bool isActive = owned && HorseOwnership.Active == key;
        int price = stable.PriceOf(offer);

        Sprite icon = offer.item != null ? offer.item.itemIcon : null;
        iconImage.sprite = icon;
        iconImage.color = icon != null ? Color.white : new Color(1f, 1f, 1f, 0.12f);

        nameText.text = offer.DisplayName;
        statusText.text = isActive ? "Your horse" : owned ? "Owned" : "For sale";
        statusText.color = isActive ? titleColor : detailColor;
        descText.text = !string.IsNullOrEmpty(offer.description) ? offer.description : offer.item != null ? offer.item.itemDescription : "";

        int[] values = { offer.speed, offer.stamina, offer.agility };
        for (int i = 0; i < 3; i++)
        {
            barFills[i].rectTransform.anchorMax = new Vector2(Mathf.Clamp01(values[i] / 10f), 1f);
            barValues[i].text = $"{values[i]}/10";
        }

        bool showPrice = !owned;
        priceText.gameObject.SetActive(showPrice);
        Sprite coinIcon = stable.coinItem != null ? stable.coinItem.itemIcon : null;
        coinImage.gameObject.SetActive(showPrice && coinIcon != null);
        coinImage.sprite = coinIcon;
        priceText.text = price > 0 ? price.ToString() : "Free";
        priceText.color = price > coins ? new Color(1f, 0.45f, 0.4f) : textColor;

        actionButton.interactable = !isActive;
        buttonText.text = isActive ? "Your current horse" : owned ? "Choose this horse" : price > 0 ? $"Buy for {price}" : "Take this horse";
    }

    void ShowMessage(string text)
    {
        messageText.text = text;
        messageUntil = Time.unscaledTime + 2.5f;
    }

    bool FindPlayer() => PlayerRef.Find(ref player);

    // ---------- UI ----------

    void BuildUI()
    {
        LootWindow style = LootWindow.Instance;
        font = LootWindow.GameFont;
        rowSprite = style != null ? style.rowSprite : null;
        textColor = UIStyle.Text;
        detailColor = UIStyle.Tan;
        titleColor = UIStyle.Gold;
        Sprite panelSprite = UIStyle.PanelSprite;
        Color panelColor = UIStyle.PanelColor;

        GameObject canvasObject = UIKit.CreateCanvas("StableCanvas", transform, 60, true);
        Transform canvas = canvasObject.transform;

        // "[E] Stable" (text only)
        promptText = UIKit.CornerText("StablePrompt", canvas, 0, true);
        toastText = UIKit.CornerText("StableToast", canvas, 1);

        // Window
        panel = UIKit.CreateImage("StablePanel", canvas, panelColor, panelSprite);
        LootWindow.SkinWindow(panel.GetComponent<Image>(), null);
        panel.GetComponent<RectTransform>().sizeDelta = new Vector2(1180f, 700f);

        titleText = Text("Title", panel.transform, "Stable", 34f, TextAlignmentOptions.Left, titleColor);
        UIKit.SetRect(titleText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -60f), new Vector2(-24f, -12f));

        coinsText = Text("Coins", panel.transform, "Coins: 0", 26f, TextAlignmentOptions.Right, titleColor);
        UIKit.SetRect(coinsText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -60f), new Vector2(-24f, -12f));

        listRoot = CreateColumn(panel.transform, "Horses", new Vector2(0f, 0f), new Vector2(0.34f, 1f), new Vector2(24f, 80f), new Vector2(-12f, -70f));
        BuildDetail();

        messageText = Text("Message", panel.transform, "", 22f, TextAlignmentOptions.Center, new Color(1f, 0.8f, 0.45f));
        UIKit.SetRect(messageText.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(24f, 42f), new Vector2(-24f, 72f));

        TextMeshProUGUI footer = Text("Footer", panel.transform, "Click a horse to look at it     [Esc] Close", 19f, TextAlignmentOptions.Center, new Color(0.7f, 0.68f, 0.62f));
        UIKit.SetRect(footer.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(24f, 10f), new Vector2(-24f, 40f));

        panel.SetActive(false);
    }

    // Top-left anchored placement inside the detail card (x to the right, y down as a negative number)
    static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    void BuildDetail()
    {
        detailRoot = new GameObject("Detail", typeof(RectTransform));
        detailRoot.transform.SetParent(panel.transform, false);
        UIKit.SetRect(detailRoot.GetComponent<RectTransform>(), new Vector2(0.36f, 0f), Vector2.one, new Vector2(0f, 80f), new Vector2(-24f, -70f));
        Transform detail = detailRoot.transform;

        // Picture
        GameObject frame = UIKit.CreateImage("PictureFrame", detail, new Color(0f, 0f, 0f, 0.3f));
        LootWindow.SkinList(frame.GetComponent<Image>());
        frame.GetComponent<Image>().raycastTarget = false;
        Place(frame.GetComponent<RectTransform>(), 0f, 0f, 230f, 230f);
        GameObject picture = UIKit.CreateImage("Picture", frame.transform, Color.white);
        iconImage = picture.GetComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        UIKit.SetRect(picture.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(14f, 14f), new Vector2(-14f, -14f));

        // Name, status, description
        nameText = Text("Name", detail, "", 38f, TextAlignmentOptions.Left, titleColor);
        Place(nameText.rectTransform, 252f, 0f, 470f, 56f);
        statusText = Text("Status", detail, "", 22f, TextAlignmentOptions.Left, detailColor);
        Place(statusText.rectTransform, 252f, -58f, 470f, 32f);
        descText = Text("Description", detail, "", 20f, TextAlignmentOptions.TopLeft, detailColor);
        descText.textWrappingMode = TextWrappingModes.Normal;
        descText.overflowMode = TextOverflowModes.Ellipsis;
        Place(descText.rectTransform, 252f, -98f, 470f, 134f);

        // Stats
        TextMeshProUGUI header = Text("StatsHeader", detail, "Qualities", 22f, TextAlignmentOptions.Left, detailColor);
        Place(header.rectTransform, 0f, -250f, 300f, 30f);
        string[] labels = { "Speed", "Stamina", "Agility" };
        for (int i = 0; i < 3; i++)
        {
            float y = -288f - i * 44f;
            TextMeshProUGUI label = Text("Label" + i, detail, labels[i], 24f, TextAlignmentOptions.MidlineLeft, textColor);
            Place(label.rectTransform, 0f, y, 160f, 34f);

            GameObject barBackground = UIKit.CreateImage("Bar" + i, detail, new Color(0f, 0f, 0f, 0.4f));
            barBackground.GetComponent<Image>().raycastTarget = false;
            Place(barBackground.GetComponent<RectTransform>(), 172f, y - 9f, 430f, 16f);
            GameObject fill = UIKit.CreateImage("Fill", barBackground.transform, new Color(0.85f, 0.65f, 0.3f));
            fill.GetComponent<Image>().raycastTarget = false;
            barFills[i] = fill.GetComponent<Image>();
            UIKit.SetRect(fill.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(1f, 1f), new Vector2(-1f, -1f));

            barValues[i] = Text("Value" + i, detail, "", 22f, TextAlignmentOptions.MidlineRight, textColor);
            Place(barValues[i].rectTransform, 622f, y, 100f, 34f);
        }

        // Price
        GameObject coin = UIKit.CreateImage("Coin", detail, Color.white);
        coinImage = coin.GetComponent<Image>();
        coinImage.preserveAspect = true;
        coinImage.raycastTarget = false;
        Place(coin.GetComponent<RectTransform>(), 0f, -434f, 34f, 34f);
        priceText = Text("Price", detail, "", 30f, TextAlignmentOptions.MidlineLeft, textColor);
        Place(priceText.rectTransform, 44f, -432f, 300f, 38f);

        // The one button
        GameObject button = UIKit.CreateImage("ActionButton", detail, new Color(0.9f, 0.68f, 0.3f));
        buttonImage = button.GetComponent<Image>();
        Place(button.GetComponent<RectTransform>(), 0f, -482f, 360f, 62f);
        actionButton = button.AddComponent<Button>();
        actionButton.targetGraphic = buttonImage;
        ColorBlock colors = actionButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.93f, 0.78f);
        colors.selectedColor = Color.white;
        colors.pressedColor = new Color(0.78f, 0.68f, 0.5f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.7f);
        actionButton.colors = colors;
        actionButton.navigation = new Navigation { mode = Navigation.Mode.None };
        actionButton.onClick.AddListener(OnAction);
        buttonText = Text("Label", button.transform, "", 26f, TextAlignmentOptions.Center, new Color(0.13f, 0.08f, 0.03f));
        UIKit.SetRect(buttonText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    // A titled, scrollable list
    RectTransform CreateColumn(Transform parent, string title, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject column = new GameObject(title, typeof(RectTransform));
        column.transform.SetParent(parent, false);
        UIKit.SetRect(column.GetComponent<RectTransform>(), anchorMin, anchorMax, offsetMin, offsetMax);

        TextMeshProUGUI header = Text("Header", column.transform, title, 24f, TextAlignmentOptions.Left, detailColor);
        UIKit.SetRect(header.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -34f), Vector2.zero);

        GameObject viewport = UIKit.CreateImage("Viewport", column.transform, new Color(0f, 0f, 0f, 0.15f));
        LootWindow.SkinList(viewport.GetComponent<Image>());
        viewport.AddComponent<RectMask2D>();
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        UIKit.SetRect(viewportRect, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -40f));

        GameObject content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = Vector2.one;
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = contentRect.offsetMax = Vector2.zero;
        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 6, 6);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewport.AddComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        return contentRect;
    }

    TextMeshProUGUI Text(string name, Transform parent, string text, float size, TextAlignmentOptions alignment, Color color)
        => UIKit.CreateText(name, parent, text, size, color, font, alignment, TextOverflowModes.Overflow);
}

// One clickable row of the stable list
public class StableRow : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    private Image background;
    private Color normal, hover;
    private Action onClick;

    public void Setup(Image image, Color normalColor, Color hoverColor, Action clicked)
    {
        background = image;
        normal = normalColor;
        hover = hoverColor;
        onClick = clicked;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) onClick?.Invoke();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (background != null) background.color = hover;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (background != null) background.color = normal;
    }
}
