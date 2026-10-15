using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Creates itself at game start (LootWindow style). Near a ShopNpc: [E] Trade. Left: merchant stock, right: your items.
// Left click twice = buy, right click twice = sell. Esc / Tab / E = close.
public class ShopWindow : MonoBehaviour
{
    public static ShopWindow Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;

    [Header("Controls")]
    public KeyCode interactKey = KeyCode.E;
    [Tooltip("Seconds you have to click a second time to confirm")] public float confirmTime = 2.5f;

    private Transform player;
    private bool open;
    private ShopNpc merchant;

    private PlayerItemData pendingItem;
    private bool pendingBuy;
    private float pendingUntil;

    // UI
    private TMP_FontAsset font;
    private Sprite rowSprite;
    private Color textColor, detailColor, titleColor;
    private TextMeshProUGUI promptText;
    private string tradePrompt;
    private GameObject panel;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI coinsText;
    private TextMeshProUGUI messageText;
    private RectTransform merchantList;
    private RectTransform playerList;
    private float messageUntil;
    private readonly List<GameObject> rows = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<ShopWindow>("ShopWindow");
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

    void Update()
    {
        if (panel == null) return;
        if (messageText.text.Length > 0 && Time.unscaledTime > messageUntil) messageText.text = "";
        if (pendingItem != null && Time.unscaledTime > pendingUntil)
        {
            pendingItem = null;
            Rebuild();
        }

        if (!FindPlayer())
        {
            promptText.gameObject.SetActive(false);
            return;
        }

        if (open)
        {
            bool tooFar = merchant == null || WorldUtil.FlatDistance(player.position, merchant.transform.position) > merchant.interactRange + 1.5f;
            if (tooFar || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(interactKey)) Close();
            return;
        }

        ShopNpc nearby = FindMerchantInRange();
        promptText.gameObject.SetActive(nearby != null);
        if (nearby == null) return;

        if (tradePrompt == null) tradePrompt = $"[{interactKey}] Trade"; // built once, not every frame
        promptText.text = tradePrompt;
        if (Input.GetKeyDown(interactKey)) Open(nearby);
    }

    ShopNpc FindMerchantInRange()
    {
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if ((gear != null && gear.IsOpen) || PlacementSystem.IsPlacing || RespawnSystem.IsDeathScreenOpen) return null;

        // Loot bags use E too: they win
        if (LootBag.AnyInRange(player.position)) return null;

        return WorldUtil.Nearest(ShopNpc.All, player.position, n => n.interactRange);
    }

    // ---------- Open / close ----------

    void Open(ShopNpc npc)
    {
        merchant = npc;
        open = true;
        pendingItem = null;
        merchant.FaceTowards(player.position);

        promptText.gameObject.SetActive(false);
        panel.SetActive(true);
        messageText.text = "";
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (merchant.coinItem == null) ShowMessage("This merchant has no Coin Item set");
        Rebuild();
    }

    void Close()
    {
        open = false;
        merchant = null;
        pendingItem = null;
        panel.SetActive(false);
        ItemTooltip.Hide();

        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (gear == null || !gear.IsInventoryOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // ---------- Buying / selling ----------

    void OnMerchantRowClicked(PlayerItemData item, PointerEventData.InputButton button)
    {
        if (button == PointerEventData.InputButton.Left && Confirm(item, true)) Buy(item);
    }

    void OnPlayerRowClicked(PlayerItemData item, PointerEventData.InputButton button)
    {
        if (button != PointerEventData.InputButton.Right) return;
        if (merchant.SellPrice(item) <= 0) ShowMessage("The merchant doesn't want that");
        else if (Confirm(item, false)) Sell(item);
    }

    // First click marks the item, second click (in time) confirms
    bool Confirm(PlayerItemData item, bool buying)
    {
        if (pendingItem == item && pendingBuy == buying && Time.unscaledTime <= pendingUntil)
        {
            pendingItem = null;
            return true;
        }
        pendingItem = item;
        pendingBuy = buying;
        pendingUntil = Time.unscaledTime + confirmTime;
        Rebuild();
        return false;
    }

    void Buy(PlayerItemData item)
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        ShopStock entry = merchant.FindStock(item);
        if (inventory == null || entry == null || merchant.coinItem == null) return;

        int price = merchant.BuyPrice(item);
        if (inventory.CountItem(merchant.coinItem) < price) ShowMessage("Not enough coins");
        else
        {
            inventory.RemoveItem(merchant.coinItem, price);
            if (!inventory.AddItem(item))
            {
                inventory.AddItem(merchant.coinItem, price); // give the money back
                ShowMessage("Inventory full!");
            }
            else
            {
                merchant.TakeOne(entry);
                ShowMessage($"Bought {NameOf(item)} for {price}");
            }
        }
        Rebuild();
    }

    void Sell(PlayerItemData item)
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (inventory == null || merchant.coinItem == null) return;

        int price = merchant.SellPrice(item);
        if (inventory.RemoveItem(item, 1) == 0) return;

        int added = inventory.AddItem(merchant.coinItem, price);
        if (added < price && inventory.playerTransform != null)
            LootBag.DropFromPlayer(inventory.playerTransform, merchant.coinItem, inventory.lootBagPrefab, inventory.bagMergeRadius, price - added);

        merchant.AddSoldItem(item);
        ShowMessage($"Sold {NameOf(item)} for {price}");
        Rebuild();
    }

    // ---------- Lists ----------

    void Rebuild()
    {
        if (!open || merchant == null) return;
        foreach (GameObject row in rows) Destroy(row);
        rows.Clear();

        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        int coins = inventory != null && merchant.coinItem != null ? inventory.CountItem(merchant.coinItem) : 0;
        titleText.text = merchant.merchantName;
        coinsText.text = $"Coins: {coins}";

        foreach (ShopStock entry in merchant.stock)
        {
            if (entry == null || entry.item == null || entry.amount == 0) continue;
            PlayerItemData item = entry.item;
            int price = merchant.BuyPrice(item);
            bool pending = pendingItem == item && pendingBuy;
            string label = NameOf(item) + (entry.amount > 0 ? $"  x{entry.amount}" : "");
            string detail = pending ? "<color=#FFD066>Click again to buy</color>" : LootWindow.Describe(item);
            Color priceColor = coins >= price ? textColor : new Color(1f, 0.45f, 0.4f);
            rows.Add(CreateRow(merchantList, item, label, detail, price.ToString(), priceColor, pending, OnMerchantRowClicked));
        }

        if (inventory == null) return;
        foreach (PlayerItemData item in inventory.DistinctItems())
        {
            if (item == merchant.coinItem) continue;
            int count = inventory.CountItem(item);
            int price = merchant.SellPrice(item);
            bool pending = pendingItem == item && !pendingBuy;
            string label = NameOf(item) + (count > 1 ? $"  x{count}" : "");
            string detail = pending ? "<color=#FFD066>Right click again to sell</color>" : LootWindow.Describe(item);
            rows.Add(CreateRow(playerList, item, label, detail, price > 0 ? price.ToString() : "-", textColor, pending, OnPlayerRowClicked));
        }
    }

    GameObject CreateRow(RectTransform parent, PlayerItemData item, string label, string detail, string price, Color priceColor, bool highlighted,
                         Action<PlayerItemData, PointerEventData.InputButton> onClick)
    {
        Color normal = rowSprite != null ? Color.white : new Color(1f, 1f, 1f, 0.06f);
        Color hover = rowSprite != null ? new Color(1f, 0.85f, 0.6f) : new Color(0.85f, 0.65f, 0.3f, 0.35f);
        Color marked = new Color(0.85f, 0.65f, 0.3f, 0.55f);

        GameObject row = CreateImage("Row", parent, highlighted ? marked : normal, rowSprite);
        row.AddComponent<LayoutElement>().preferredHeight = 62f;
        row.AddComponent<ShopRow>().Setup(row.GetComponent<Image>(), highlighted ? marked : normal, hover, item, onClick);

        GameObject icon = CreateImage("Icon", row.transform, item.itemIcon != null ? Color.white : new Color(1f, 1f, 1f, 0.15f), item.itemIcon);
        Image iconImage = icon.GetComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        RectTransform iconRect = icon.GetComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(36f, 0f);
        iconRect.sizeDelta = new Vector2(50f, 50f);

        TextMeshProUGUI nameText = CreateText("Name", row.transform, label, 24f, TextAlignmentOptions.BottomLeft, textColor);
        UIKit.SetRect(nameText.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(72f, 0f), new Vector2(-90f, -4f));

        TextMeshProUGUI detailText = CreateText("Detail", row.transform, detail, 18f, TextAlignmentOptions.TopLeft, detailColor);
        UIKit.SetRect(detailText.rectTransform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(72f, 4f), new Vector2(-90f, 0f));

        TextMeshProUGUI priceText = CreateText("Price", row.transform, price, 24f, TextAlignmentOptions.Right, priceColor);
        UIKit.SetRect(priceText.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-84f, 0f), new Vector2(-40f, 0f));

        Sprite coinIcon = merchant.coinItem != null ? merchant.coinItem.itemIcon : null;
        if (coinIcon != null)
        {
            GameObject coin = CreateImage("Coin", row.transform, Color.white, coinIcon);
            Image coinImage = coin.GetComponent<Image>();
            coinImage.preserveAspect = true;
            coinImage.raycastTarget = false;
            RectTransform coinRect = coin.GetComponent<RectTransform>();
            coinRect.anchorMin = coinRect.anchorMax = new Vector2(1f, 0.5f);
            coinRect.anchoredPosition = new Vector2(-20f, 0f);
            coinRect.sizeDelta = new Vector2(28f, 28f);
        }
        return row;
    }

    static string NameOf(PlayerItemData item) => string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName;

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

        GameObject canvasObject = UIKit.CreateCanvas("ShopCanvas", transform, 60, true);

        // "[E] Trade" (text only)
        promptText = UIKit.CornerText("TradePrompt", canvasObject.transform, 0, true);

        // Window
        panel = CreateImage("ShopPanel", canvasObject.transform, panelColor, panelSprite);
        LootWindow.SkinWindow(panel.GetComponent<Image>(), style != null ? style.shopWindowSprite : null);
        panel.GetComponent<RectTransform>().sizeDelta = new Vector2(1180f, 700f);

        titleText = CreateText("Title", panel.transform, "Merchant", 34f, TextAlignmentOptions.Left, titleColor);
        UIKit.SetRect(titleText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -60f), new Vector2(-24f, -12f));

        coinsText = CreateText("Coins", panel.transform, "Coins: 0", 26f, TextAlignmentOptions.Right, titleColor);
        UIKit.SetRect(coinsText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -60f), new Vector2(-24f, -12f));

        merchantList = CreateColumn(panel.transform, "For sale", new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(24f, 80f), new Vector2(-12f, -70f));
        playerList = CreateColumn(panel.transform, "Your items", new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(12f, 80f), new Vector2(-24f, -70f));

        messageText = CreateText("Message", panel.transform, "", 22f, TextAlignmentOptions.Center, new Color(1f, 0.8f, 0.45f));
        UIKit.SetRect(messageText.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(24f, 42f), new Vector2(-24f, 72f));

        TextMeshProUGUI footer = CreateText("Footer", panel.transform, "Left click x2: Buy     Right click x2: Sell     [Esc] Close", 19f, TextAlignmentOptions.Center, new Color(0.7f, 0.68f, 0.62f));
        UIKit.SetRect(footer.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(24f, 10f), new Vector2(-24f, 40f));

        panel.SetActive(false);
    }

    // A titled, scrollable list
    RectTransform CreateColumn(Transform parent, string title, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject column = new GameObject(title, typeof(RectTransform));
        column.transform.SetParent(parent, false);
        UIKit.SetRect(column.GetComponent<RectTransform>(), anchorMin, anchorMax, offsetMin, offsetMax);

        TextMeshProUGUI header = CreateText("Header", column.transform, title, 24f, TextAlignmentOptions.Left, detailColor);
        UIKit.SetRect(header.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -34f), Vector2.zero);

        GameObject viewport = CreateImage("Viewport", column.transform, new Color(0f, 0f, 0f, 0.15f));
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

    static GameObject CreateImage(string name, Transform parent, Color color, Sprite sprite = null) => UIKit.CreateImage(name, parent, color, sprite);

    TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, TextAlignmentOptions alignment, Color color)
        => UIKit.CreateText(name, parent, text, size, color, font, alignment, TextOverflowModes.Overflow);
}

// One clickable row in the shop (left AND right click, hover color and the item tooltip)
public class ShopRow : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    private Image background;
    private Color normal, hover;
    private PlayerItemData item;
    private Action<PlayerItemData, PointerEventData.InputButton> onClick;

    public void Setup(Image image, Color normalColor, Color hoverColor, PlayerItemData rowItem, Action<PlayerItemData, PointerEventData.InputButton> clicked)
    {
        background = image;
        normal = normalColor;
        hover = hoverColor;
        item = rowItem;
        onClick = clicked;
    }

    private bool hovered;

    // Change the colors later (e.g. the Craft button turns orange when it can be used)
    public void SetColors(Color normalColor, Color hoverColor)
    {
        normal = normalColor;
        hover = hoverColor;
        if (background != null) background.color = hovered ? hover : normal;
    }

    public void OnPointerClick(PointerEventData eventData) => onClick?.Invoke(item, eventData.button);

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        if (background != null) background.color = hover;
        ItemTooltip.Show(item);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        if (background != null) background.color = normal;
        ItemTooltip.Hide();
    }

    void OnDisable() => ItemTooltip.Hide();
}
