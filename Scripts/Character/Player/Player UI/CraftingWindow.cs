using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Creates itself at game start (LootWindow style). Near a CraftingStation: [E] Cook, click a recipe to make it. Esc / Tab / E = close.
// "Upgrade Table" stations show Item + Stone slots and a Craft button instead (Unmei, Kake and Oni stones).
public class CraftingWindow : MonoBehaviour
{
    public static CraftingWindow Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;

    public KeyCode interactKey = KeyCode.E;

    private Transform player;
    private bool open;
    private CraftingStation station;

    private TMP_FontAsset font;
    private Sprite rowSprite;
    private Color textColor, detailColor, titleColor;
    private TextMeshProUGUI promptText;
    private CraftingStation promptFor;
    private GameObject panel;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI messageText;
    private RectTransform list;
    private float messageUntil;
    private readonly List<GameObject> rows = new List<GameObject>();
    private RectTransform viewportRect;
    private TextMeshProUGUI footerText;

    // Upgrade table (item + stone)
    private GameObject upgradeArea;
    private Image itemSlotIcon, stoneSlotIcon, craftButtonImage;
    private ShopRow craftButtonRow;
    private TextMeshProUGUI itemSlotLabel, stoneSlotLabel, previewText, craftButtonText;
    private PlayerItemSlot selectedSlot;
    private PlayerItemData selectedItem;
    private PlayerItemData selectedStone;
    private bool UpgradeMode => station != null && station.upgradeTable;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<CraftingWindow>("CraftingWindow");
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

        if (!FindPlayer())
        {
            promptText.gameObject.SetActive(false);
            return;
        }

        if (open)
        {
            bool tooFar = station == null || WorldUtil.FlatDistance(player.position, station.transform.position) > station.interactRange + 1.5f;
            if (tooFar || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(interactKey)) Close();
            return;
        }

        CraftingStation nearby = FindStationInRange();
        promptText.gameObject.SetActive(nearby != null);
        if (nearby == null) return;

        if (promptFor != nearby)
        {
            promptFor = nearby;
            promptText.text = $"[{interactKey}] {nearby.actionName}"; // only when the station changes
        }
        if (Input.GetKeyDown(interactKey)) Open(nearby);
    }

    CraftingStation FindStationInRange()
    {
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if ((gear != null && gear.IsOpen) || PlacementSystem.IsPlacing || RespawnSystem.IsDeathScreenOpen) return null;

        // Loot bags use E too: they win
        if (LootBag.AnyInRange(player.position)) return null;

        return WorldUtil.Nearest(CraftingStation.All, player.position, s => s.interactRange);
    }

    // ---------- Open / close ----------

    void Open(CraftingStation s)
    {
        station = s;
        open = true;
        promptText.gameObject.SetActive(false);
        panel.SetActive(true);
        messageText.text = "";
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        ClearItemSelection();
        selectedStone = null;
        bool upgrade = UpgradeMode;
        upgradeArea.SetActive(upgrade);
        panel.GetComponent<RectTransform>().sizeDelta = new Vector2(640f, upgrade ? 780f : 620f);
        viewportRect.offsetMax = new Vector2(-24f, upgrade ? -300f : -70f);
        footerText.text = upgrade ? "Click an item and a stone, then Craft     [Esc] Close" : "Click: Make     [Esc] Close";

        if (!upgrade && station.recipes.Count == 0) ShowMessage("Nothing can be made here yet");
        Rebuild();
    }

    void Close()
    {
        open = false;
        station = null;
        panel.SetActive(false);
        ItemTooltip.Hide();

        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (gear == null || !gear.IsInventoryOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // ---------- Crafting ----------

    bool HasIngredients(CraftingRecipe recipe)
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (inventory == null) return false;
        foreach (RecipeIngredient ingredient in recipe.ingredients)
            if (ingredient != null && ingredient.item != null && inventory.CountItem(ingredient.item) < ingredient.amount) return false;
        return true;
    }

    void Craft(CraftingRecipe recipe)
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (inventory == null || recipe == null || recipe.result == null) return;

        if (!HasIngredients(recipe))
        {
            ShowMessage("Missing ingredients");
            return;
        }

        foreach (RecipeIngredient ingredient in recipe.ingredients)
            if (ingredient != null && ingredient.item != null) inventory.RemoveItem(ingredient.item, ingredient.amount);

        int added = inventory.AddItem(recipe.result, recipe.resultAmount);
        if (added < recipe.resultAmount && inventory.playerTransform != null)
            LootBag.DropFromPlayer(inventory.playerTransform, recipe.result, inventory.lootBagPrefab, inventory.bagMergeRadius, recipe.resultAmount - added);

        ShowMessage(added < recipe.resultAmount ? $"Made {recipe.DisplayName} (inventory full - dropped on the ground)" : $"Made {recipe.DisplayName}");
        Rebuild();
    }

    // ---------- List ----------

    void Rebuild()
    {
        if (!open || station == null) return;
        foreach (GameObject row in rows) Destroy(row);
        rows.Clear();
        titleText.text = station.stationName;
        if (UpgradeMode)
        {
            RebuildUpgrade();
            return;
        }

        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        foreach (CraftingRecipe recipe in station.recipes)
        {
            if (recipe == null || recipe.result == null) continue;

            var parts = new List<string>();
            foreach (RecipeIngredient ingredient in recipe.ingredients)
            {
                if (ingredient == null || ingredient.item == null) continue;
                int have = inventory != null ? inventory.CountItem(ingredient.item) : 0;
                string itemName = string.IsNullOrEmpty(ingredient.item.itemName) ? ingredient.item.name : ingredient.item.itemName;
                string color = have >= ingredient.amount ? "#9BE08A" : "#FF7A6E";
                parts.Add($"<color={color}>{itemName} {have}/{ingredient.amount}</color>");
            }

            string label = recipe.DisplayName + (recipe.resultAmount > 1 ? $"  x{recipe.resultAmount}" : "");
            rows.Add(CreateRow(recipe, label, string.Join("   ", parts), HasIngredients(recipe)));
        }
    }

    GameObject CreateRow(CraftingRecipe recipe, string label, string detail, bool canMake) =>
        CreateItemRow(recipe.result, label, detail, canMake, () => Craft(recipe));

    GameObject CreateItemRow(PlayerItemData rowItem, string label, string detail, bool canMake, System.Action onClick)
    {
        Color normal = rowSprite != null ? Color.white : new Color(1f, 1f, 1f, canMake ? 0.08f : 0.03f);
        Color hover = rowSprite != null ? new Color(1f, 0.85f, 0.6f) : new Color(0.85f, 0.65f, 0.3f, 0.35f);

        GameObject row = CreateImage("Row", list, normal, rowSprite);
        row.AddComponent<LayoutElement>().preferredHeight = 66f;
        row.AddComponent<ShopRow>().Setup(row.GetComponent<Image>(), normal, hover, rowItem, (item, button) =>
        {
            if (button == PointerEventData.InputButton.Left) onClick?.Invoke();
        });

        GameObject icon = CreateImage("Icon", row.transform, rowItem.itemIcon != null ? Color.white : new Color(1f, 1f, 1f, 0.15f), rowItem.itemIcon);
        Image iconImage = icon.GetComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        if (!canMake) iconImage.color = new Color(1f, 1f, 1f, 0.45f);
        RectTransform iconRect = icon.GetComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(38f, 0f);
        iconRect.sizeDelta = new Vector2(54f, 54f);

        TextMeshProUGUI nameText = CreateText("Name", row.transform, label, 26f, TextAlignmentOptions.BottomLeft, canMake ? textColor : detailColor);
        UIKit.SetRect(nameText.rectTransform, new Vector2(0f, 0.5f), Vector2.one, new Vector2(78f, 0f), new Vector2(-12f, -4f));

        TextMeshProUGUI detailText = CreateText("Ingredients", row.transform, detail, 19f, TextAlignmentOptions.TopLeft, detailColor);
        UIKit.SetRect(detailText.rectTransform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(78f, 4f), new Vector2(-12f, 0f));
        return row;
    }

    void ShowMessage(string text, float seconds = 2.5f)
    {
        messageText.text = text;
        messageUntil = Time.unscaledTime + seconds;
    }

    // ---------- Upgrade table: item + stone ----------

    void RebuildUpgrade()
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;

        // The selection may be gone (dropped, sold, used...)
        if (selectedSlot != null && (selectedSlot.itemData != selectedItem || !ItemUpgrades.CanUpgrade(selectedItem))) ClearItemSelection();
        if (selectedStone != null && (inventory == null || inventory.CountItem(selectedStone) < 1)) selectedStone = null;

        SetSlotBox(itemSlotIcon, itemSlotLabel, selectedItem, "Item");
        SetSlotBox(stoneSlotIcon, stoneSlotLabel, selectedStone, "Stone");

        bool ready = selectedItem != null && selectedStone != null;
        Color craftNormal = ready ? new Color(0.85f, 0.55f, 0.25f, 1f) : new Color(1f, 1f, 1f, 0.12f);
        Color craftHover = ready ? new Color(1f, 0.7f, 0.35f, 1f) : new Color(1f, 1f, 1f, 0.2f);
        if (craftButtonRow != null) craftButtonRow.SetColors(craftNormal, craftHover);
        else craftButtonImage.color = craftNormal;
        craftButtonText.color = ready ? Color.white : detailColor;
        previewText.text = ready ? UpgradePreview() : selectedItem == null ? "Pick a weapon, bow or armor below" : "Pick an upgrade stone below";

        if (inventory == null) return;

        // Weapons, bows and armor in the bags (equipped items must be unequipped first)
        rows.Add(CreateHeader("Equipment  <size=16>(unequip an item to upgrade it)</size>"));
        bool any = false;
        foreach (PlayerItemSlot slot in inventory.UsableSlots())
        {
            PlayerItemData item = slot.itemData;
            if (item == null) continue;
            bool canUpgrade = ItemUpgrades.CanUpgrade(item);
            if (!canUpgrade && !item.IsUpgraded) continue;

            PlayerItemSlot captured = slot;
            string label = (captured == selectedSlot ? "<color=#FFD27A>> </color>" : "") + ItemUpgrades.DisplayName(item);
            string detail = canUpgrade ? LootWindow.Describe(item) : "Already upgraded (one stone per item)";
            rows.Add(CreateItemRow(item, label, detail, canUpgrade, canUpgrade ? () =>
            {
                selectedSlot = captured;
                selectedItem = captured.itemData;
                Rebuild();
            } : (System.Action)null));
            any = true;
        }
        if (!any) rows.Add(CreateHeader("<color=#FF7A6E>No weapons, bows or armor in your bags</color>"));

        // Upgrade stones
        rows.Add(CreateHeader("Stones"));
        any = false;
        foreach (PlayerItemData item in inventory.DistinctItems())
        {
            if (item == null || !item.IsUpgradeStone) continue;
            PlayerItemData stone = item;
            string label = (stone == selectedStone ? "<color=#FFD27A>> </color>" : "") + ItemUpgrades.DisplayName(stone) + $"  x{inventory.CountItem(stone)}";
            rows.Add(CreateItemRow(stone, label, ItemUpgrades.StoneEffect(stone.upgradeStone), true, () =>
            {
                selectedStone = stone;
                Rebuild();
            }));
            any = true;
        }
        if (!any) rows.Add(CreateHeader("<color=#FF7A6E>No upgrade stones</color>"));
    }

    string UpgradePreview()
    {
        string itemName = ItemUpgrades.DisplayName(selectedItem);
        return selectedStone.upgradeStone switch
        {
            UpgradeStone.Unmei => $"<color=#9BE08A>{itemName}: stats +{ItemUpgrades.UnmeiBonus * 100f:0}% (always works)</color>",
            UpgradeStone.Kake => $"<color=#FF7A6E>50%: {itemName} doubles its stats.  50%: it is destroyed!</color>",
            UpgradeStone.Oni => $"<color=#FF9A7A>A random demon will possess {itemName} and give it a perk</color>",
            _ => ""
        };
    }

    void CraftUpgrade()
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (inventory == null) return;
        if (selectedItem == null || selectedStone == null)
        {
            ShowMessage("Put an item and a stone first");
            return;
        }
        if (selectedSlot == null || selectedSlot.itemData != selectedItem || !ItemUpgrades.CanUpgrade(selectedItem))
        {
            ShowMessage("That item can't be upgraded");
            ClearItemSelection();
            Rebuild();
            return;
        }
        if (inventory.CountItem(selectedStone) < 1)
        {
            ShowMessage("You don't have that stone anymore");
            selectedStone = null;
            Rebuild();
            return;
        }

        inventory.RemoveItem(selectedStone, 1);
        PlayerItemData result = ItemUpgrades.Apply(selectedItem, selectedStone.upgradeStone, out string message);
        if (result == null) selectedSlot.ClearSlot(); // Kake Stone failed: the item is destroyed
        else selectedSlot.SetItem(result, 1);

        Vector3 soundPosition = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        SoundManager.Play(result == null ? Sfx.Block : Sfx.Equip, soundPosition);
        ShowMessage(message, 5f);

        ClearItemSelection();
        if (inventory.CountItem(selectedStone) < 1) selectedStone = null;
        ItemTooltip.Hide();
        Rebuild();
    }

    void ClearItemSelection()
    {
        selectedSlot = null;
        selectedItem = null;
    }

    static void SetSlotBox(Image icon, TextMeshProUGUI label, PlayerItemData item, string emptyLabel)
    {
        bool hasIcon = item != null && item.itemIcon != null;
        icon.sprite = hasIcon ? item.itemIcon : null;
        icon.enabled = hasIcon;
        label.text = item != null ? ItemUpgrades.DisplayName(item) : emptyLabel;
    }

    GameObject CreateHeader(string text)
    {
        TextMeshProUGUI header = CreateText("Header", list, text, 21f, TextAlignmentOptions.BottomLeft, titleColor);
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
        return header.gameObject;
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

        GameObject canvasObject = UIKit.CreateCanvas("CraftingCanvas", transform, 60, true);

        // "[E] Cook" (text only)
        promptText = UIKit.CornerText("CraftPrompt", canvasObject.transform, 0, true);

        panel = CreateImage("CraftingPanel", canvasObject.transform, panelColor, panelSprite);
        LootWindow.SkinWindow(panel.GetComponent<Image>(), style != null ? style.craftingWindowSprite : null);
        panel.GetComponent<RectTransform>().sizeDelta = new Vector2(640f, 620f);

        titleText = CreateText("Title", panel.transform, "Bonfire", 34f, TextAlignmentOptions.Left, titleColor);
        UIKit.SetRect(titleText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -60f), new Vector2(-24f, -12f));

        // Scrollable recipe list
        GameObject viewport = CreateImage("Viewport", panel.transform, new Color(0f, 0f, 0f, 0.15f));
        LootWindow.SkinList(viewport.GetComponent<Image>());
        viewport.AddComponent<RectMask2D>();
        viewportRect = viewport.GetComponent<RectTransform>();
        UIKit.SetRect(viewportRect, Vector2.zero, Vector2.one, new Vector2(24f, 80f), new Vector2(-24f, -70f));

        GameObject content = new GameObject("Recipes", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        list = content.GetComponent<RectTransform>();
        list.anchorMin = new Vector2(0f, 1f);
        list.anchorMax = Vector2.one;
        list.pivot = new Vector2(0.5f, 1f);
        list.offsetMin = list.offsetMax = Vector2.zero;
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
        scroll.content = list;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        messageText = CreateText("Message", panel.transform, "", 22f, TextAlignmentOptions.Center, new Color(1f, 0.8f, 0.45f));
        messageText.textWrappingMode = TextWrappingModes.Normal;
        messageText.enableAutoSizing = true;
        messageText.fontSizeMin = 14f;
        messageText.fontSizeMax = 22f;
        UIKit.SetRect(messageText.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(24f, 42f), new Vector2(-24f, 72f));

        footerText = CreateText("Footer", panel.transform, "Click: Make     [Esc] Close", 19f, TextAlignmentOptions.Center, new Color(0.7f, 0.68f, 0.62f));
        UIKit.SetRect(footerText.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(24f, 10f), new Vector2(-24f, 40f));

        BuildUpgradeArea();

        panel.SetActive(false);
    }

    // Item slot + Stone slot + Craft button, shown only at upgrade tables
    void BuildUpgradeArea()
    {
        upgradeArea = new GameObject("UpgradeArea", typeof(RectTransform));
        upgradeArea.transform.SetParent(panel.transform, false);
        UIKit.SetRect(upgradeArea.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -290f), new Vector2(-24f, -70f));

        itemSlotIcon = CreateSlotBox("ItemSlot", new Vector2(85f, 25f), "Item", out itemSlotLabel, () => { ClearItemSelection(); Rebuild(); });
        TextMeshProUGUI plus = CreateText("Plus", upgradeArea.transform, "+", 46f, TextAlignmentOptions.Center, titleColor);
        RectTransform plusRect = plus.rectTransform;
        plusRect.anchorMin = plusRect.anchorMax = new Vector2(0f, 0.5f);
        plusRect.anchoredPosition = new Vector2(185f, 25f);
        plusRect.sizeDelta = new Vector2(60f, 60f);
        stoneSlotIcon = CreateSlotBox("StoneSlot", new Vector2(285f, 25f), "Stone", out stoneSlotLabel, () => { selectedStone = null; Rebuild(); });

        // Craft button
        GameObject button = CreateImage("CraftButton", upgradeArea.transform, new Color(1f, 1f, 1f, 0.12f));
        craftButtonImage = button.GetComponent<Image>();
        RectTransform buttonRect = button.GetComponent<RectTransform>();
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(-100f, 25f);
        buttonRect.sizeDelta = new Vector2(170f, 70f);
        craftButtonRow = button.AddComponent<ShopRow>();
        craftButtonRow.Setup(craftButtonImage, craftButtonImage.color, new Color(1f, 1f, 1f, 0.2f), null, (item, mouse) =>
        {
            if (mouse == PointerEventData.InputButton.Left) CraftUpgrade();
        });
        craftButtonText = CreateText("Label", button.transform, "Craft", 30f, TextAlignmentOptions.Center, Color.white);
        UIKit.SetRect(craftButtonText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // What the stone will do
        previewText = CreateText("Preview", upgradeArea.transform, "", 19f, TextAlignmentOptions.Center, detailColor);
        previewText.textWrappingMode = TextWrappingModes.Normal;
        UIKit.SetRect(previewText.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 48f));

        upgradeArea.SetActive(false);
    }

    Image CreateSlotBox(string name, Vector2 position, string label, out TextMeshProUGUI labelText, System.Action onClick)
    {
        Color normal = rowSprite != null ? Color.white : new Color(1f, 1f, 1f, 0.08f);
        Color hover = rowSprite != null ? new Color(1f, 0.85f, 0.6f) : new Color(0.85f, 0.65f, 0.3f, 0.35f);
        GameObject box = CreateImage(name, upgradeArea.transform, normal, rowSprite);
        RectTransform boxRect = box.GetComponent<RectTransform>();
        boxRect.anchorMin = boxRect.anchorMax = new Vector2(0f, 0.5f);
        boxRect.anchoredPosition = position;
        boxRect.sizeDelta = new Vector2(110f, 110f);
        box.AddComponent<ShopRow>().Setup(box.GetComponent<Image>(), normal, hover, null, (item, mouse) =>
        {
            if (mouse == PointerEventData.InputButton.Left) onClick?.Invoke(); // click to empty the slot
        });

        GameObject icon = CreateImage("Icon", box.transform, Color.white);
        Image iconImage = icon.GetComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        iconImage.enabled = false;
        UIKit.SetRect(icon.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -10f));

        labelText = CreateText(name + "Label", upgradeArea.transform, label, 18f, TextAlignmentOptions.Center, detailColor);
        labelText.enableAutoSizing = true;
        labelText.fontSizeMin = 12f;
        labelText.fontSizeMax = 18f;
        RectTransform labelRect = labelText.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0f, 0.5f);
        labelRect.anchoredPosition = new Vector2(position.x, position.y - 72f);
        labelRect.sizeDelta = new Vector2(170f, 28f);
        return iconImage;
    }

    static GameObject CreateImage(string name, Transform parent, Color color, Sprite sprite = null) => UIKit.CreateImage(name, parent, color, sprite);

    TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, TextAlignmentOptions alignment, Color color)
        => UIKit.CreateText(name, parent, text, size, color, font, alignment, TextOverflowModes.Overflow);
}
