using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class PlayerItemSlot : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [Header("Slot Data")]
    public PlayerItemData itemData;
    [Tooltip("How many of this item are in the slot (consumables and utilities stack)")] public int amount;

    [Header("UI References")]
    public Image iconImage;
    [Tooltip("Text that shows the amount (empty = created automatically in the bottom-right corner)")] public TextMeshProUGUI amountText;
    public float amountFontSize = 18f;

    void Start()
    {
        // Drops need something on the slot that the mouse can hit, even when the slot is empty
        if (GetComponent<Graphic>() == null)
        {
            Image hit = gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
        }
        UpdateSlotUI();
    }

    public bool IsEmpty => itemData == null;

    public void UpdateSlotUI()
    {
        if (itemData == null) amount = 0;
        else if (amount < 1) amount = 1;

        if (iconImage != null)
        {
            bool hasIcon = itemData != null && itemData.itemIcon != null;
            iconImage.sprite = hasIcon ? itemData.itemIcon : null;
            iconImage.enabled = hasIcon;
        }

        bool showAmount = itemData != null && amount > 1;
        if (showAmount && amountText == null) CreateAmountText();
        if (amountText != null)
        {
            amountText.gameObject.SetActive(showAmount);
            if (showAmount) amountText.text = amount.ToString();
        }
    }

    public void SetItem(PlayerItemData newItemData) => SetItem(newItemData, 1);

    public void SetItem(PlayerItemData newItemData, int newAmount)
    {
        itemData = newItemData;
        amount = newItemData != null ? Mathf.Max(1, newAmount) : 0;
        UpdateSlotUI();
    }

    public void ClearSlot() => SetItem(null);

    // How many more of this item fit in this slot
    public int SpaceFor(PlayerItemData item)
    {
        if (item == null) return 0;
        if (itemData == null) return item.MaxStack;
        return itemData != item || !item.IsStackable ? 0 : Mathf.Max(0, item.MaxStack - amount);
    }

    public void AddAmount(int count)
    {
        amount += count;
        UpdateSlotUI();
    }

    // Removes one item from the stack (clears the slot when it was the last one)
    public void RemoveOne()
    {
        if (itemData == null) return;
        if (amount > 1) AddAmount(-1);
        else ClearSlot();
    }

    void CreateAmountText()
    {
        GameObject obj = new GameObject("Amount", typeof(RectTransform));
        obj.transform.SetParent(transform, false);
        amountText = obj.AddComponent<TextMeshProUGUI>();
        if (LootWindow.GameFont != null) amountText.font = LootWindow.GameFont;
        amountText.fontSize = amountFontSize;
        amountText.fontStyle = FontStyles.Bold;
        amountText.alignment = TextAlignmentOptions.BottomRight;
        amountText.color = Color.white;
        amountText.raycastTarget = false;
        amountText.textWrappingMode = TextWrappingModes.NoWrap;

        RectTransform rect = amountText.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(2f, 1f);
        rect.offsetMax = new Vector2(-4f, -2f);
        obj.transform.SetAsLastSibling();
    }

    // ---------- Drag and drop (inventory and backpack slots only) ----------

    private static PlayerItemSlot dragSource;
    private static GameObject dragGhost;
    private static RectTransform dragGhostRect, dragCanvasRect;
    private static Canvas dragCanvas;

    // The slot under the mouse (quick slots: hover a food and press 3/4/5)
    public static PlayerItemSlot Hovered { get; private set; }

    // Consumables need a second click to be used (so food isn't eaten by accident)
    private static PlayerItemSlot confirmSlot;
    private static float confirmUntil;
    private const float ConfirmTime = 1.2f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetDragStatics() { dragSource = null; dragGhost = null; dragGhostRect = dragCanvasRect = null; dragCanvas = null; Hovered = null; confirmSlot = null; }

    bool CanDrag => itemData != null && PlayerInventoryManager.Instance != null && PlayerInventoryManager.Instance.IsStorageSlot(this);

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !CanDrag || iconImage == null || iconImage.sprite == null) return;

        dragCanvas = GetComponentInParent<Canvas>();
        if (dragCanvas != null) dragCanvas = dragCanvas.rootCanvas;
        if (dragCanvas == null) return;

        dragSource = this;
        ItemTooltip.Hide();
        iconImage.color = new Color(iconImage.color.r, iconImage.color.g, iconImage.color.b, 0.35f);

        dragCanvasRect = dragCanvas.transform as RectTransform;
        dragGhost = new GameObject("Drag Icon", typeof(RectTransform), typeof(CanvasGroup));
        dragGhost.transform.SetParent(dragCanvas.transform, false);
        dragGhost.transform.SetAsLastSibling();
        dragGhost.GetComponent<CanvasGroup>().blocksRaycasts = false; // so the slot under the mouse still receives the drop
        Image ghostImage = dragGhost.AddComponent<Image>();
        ghostImage.sprite = iconImage.sprite;
        ghostImage.preserveAspect = true;
        ghostImage.raycastTarget = false;
        dragGhostRect = ghostImage.rectTransform;
        dragGhostRect.anchorMin = dragGhostRect.anchorMax = dragGhostRect.pivot = new Vector2(0.5f, 0.5f);
        Vector2 size = iconImage.rectTransform.rect.size;
        dragGhostRect.sizeDelta = size * 1.05f;
        MoveGhost(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragSource == this) MoveGhost(eventData);
    }

    static void MoveGhost(PointerEventData eventData)
    {
        if (dragGhostRect == null || dragCanvasRect == null) return;
        Camera cam = dragCanvas != null && dragCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? dragCanvas.worldCamera : null;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(dragCanvasRect, eventData.position, cam, out Vector2 local))
            dragGhostRect.anchoredPosition = local;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (dragSource == this) EndDrag();
    }

    static void EndDrag()
    {
        if (dragSource != null && dragSource.iconImage != null)
        {
            Color c = dragSource.iconImage.color;
            dragSource.iconImage.color = new Color(c.r, c.g, c.b, 1f);
        }
        if (dragGhost != null) Destroy(dragGhost);
        dragSource = null;
        dragGhost = null;
        dragGhostRect = null;
    }

    // Something was dropped on this slot: same stackable item = merge, otherwise the two slots swap places
    public void OnDrop(PointerEventData eventData)
    {
        PlayerItemSlot from = dragSource;
        if (from == null || from == this || from.itemData == null) return;
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (inventory == null || !inventory.IsStorageSlot(this) || !inventory.IsStorageSlot(from)) return;

        if (itemData == from.itemData && itemData.IsStackable)
        {
            int move = Mathf.Min(SpaceFor(from.itemData), from.amount);
            if (move > 0)
            {
                AddAmount(move);
                if (move >= from.amount) from.ClearSlot();
                else from.AddAmount(-move);
                ItemTooltip.Show(itemData);
                return;
            }
        }

        PlayerItemData otherItem = itemData;
        int otherAmount = amount;
        SetItem(from.itemData, from.amount);
        from.SetItem(otherItem, otherAmount);
        ItemTooltip.Show(itemData);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (itemData == null || eventData.dragging) return;

        switch (eventData.button)
        {
            case PointerEventData.InputButton.Left:
                PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
                if (gear == null) { Debug.LogWarning("No PlayerEquipmentManager found - can't use " + itemData.itemName); break; }
                if (itemData.type == ItemType.Consumable && !gear.IsGearSlot(this))
                {
                    if (confirmSlot != this || Time.unscaledTime > confirmUntil)
                    {
                        confirmSlot = this;
                        confirmUntil = Time.unscaledTime + ConfirmTime;
                        QuickSlots.Hint($"Click again to use {ItemUpgrades.DisplayName(itemData)}");
                        break;
                    }
                    confirmSlot = null;
                    QuickSlots.Hint("");
                }
                gear.EquipItem(this);
                break;
            case PointerEventData.InputButton.Right:
                // Right click drops the whole stack, Shift + right click only one
                PlayerInventoryManager.Instance.DropItem(this, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                break;
        }

        // The slot's item may have changed (equipped/dropped): update the tooltip
        ItemTooltip.Show(itemData);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        Hovered = this;
        transform.localScale = Vector3.one * 1.05f;
        ItemTooltip.Show(itemData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (Hovered == this) Hovered = null;
        transform.localScale = Vector3.one;
        ItemTooltip.Hide();
    }

    void OnDisable()
    {
        if (dragSource == this) EndDrag(); // the inventory was closed in the middle of a drag
        if (Hovered == this) Hovered = null;
        ItemTooltip.Hide();
    }
}
