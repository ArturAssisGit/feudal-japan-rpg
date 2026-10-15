using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Small box that follows the mouse and shows what an item is. Creates itself when first needed.
public class ItemTooltip : MonoBehaviour
{
    private static ItemTooltip instance;

    private RectTransform canvasRect;
    private RectTransform panel;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI bodyText;

    public static void Show(PlayerItemData item)
    {
        if (item == null)
        {
            Hide();
            return;
        }
        Get().Display(item);
    }

    public static void Hide()
    {
        if (instance != null && instance.panel != null) instance.panel.gameObject.SetActive(false);
    }

    static ItemTooltip Get()
    {
        if (instance == null) instance = WorldUtil.CreatePersistent<ItemTooltip>("ItemTooltip");
        return instance;
    }

    void Awake()
    {
        instance = this;
        BuildUI();
    }

    private bool skinned;

    void Display(PlayerItemData item)
    {
        if (!skinned && LootWindow.Instance != null)
        {
            LootWindow.SkinWindow(panel.GetComponent<Image>(), LootWindow.Instance.tooltipSprite);
            skinned = true;
        }

        titleText.text = string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName;

        string body = LootWindow.Describe(item).Replace("  ·  ", "\n");
        if (!string.IsNullOrEmpty(item.itemDescription)) body += "\n\n<i>" + item.itemDescription + "</i>";
        bodyText.text = body;

        TMP_FontAsset font = LootWindow.GameFont;
        if (font != null)
        {
            titleText.font = font;
            bodyText.font = font;
        }

        panel.gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        FollowMouse();
    }

    void Update()
    {
        if (!panel.gameObject.activeSelf) return;

        bool menuOpen = PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.IsOpen;
        if (menuOpen) FollowMouse();
        else Hide();
    }

    void FollowMouse()
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Input.mousePosition, null, out Vector2 mouse);

        // Open to the left/down of the mouse when there's no room on the right/bottom
        Vector2 half = canvasRect.rect.size * 0.5f;
        bool flipX = mouse.x + panel.rect.width + 24f > half.x;
        bool flipY = mouse.y - panel.rect.height - 24f < -half.y;
        panel.pivot = new Vector2(flipX ? 1f : 0f, flipY ? 0f : 1f);
        panel.anchoredPosition = mouse + new Vector2(flipX ? -18f : 18f, flipY ? 18f : -18f);
    }

    void BuildUI()
    {
        GameObject canvasObject = UIKit.CreateCanvas("TooltipCanvas", transform, 100);
        canvasRect = canvasObject.GetComponent<RectTransform>();

        GameObject panelObject = new GameObject("Tooltip", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(canvasObject.transform, false);
        Image background = panelObject.GetComponent<Image>();
        background.color = new Color(0.09f, 0.075f, 0.06f, 0.95f);
        background.raycastTarget = false;
        panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(300f, 0f);

        VerticalLayoutGroup layout = panelObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 12);
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        panelObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        titleText = CreateText("Title", panelObject.transform, 24, new Color(0.95f, 0.82f, 0.55f));
        bodyText = CreateText("Body", panelObject.transform, 18, new Color(0.85f, 0.8f, 0.7f));

        panelObject.SetActive(false);
    }

    static TextMeshProUGUI CreateText(string name, Transform parent, float size, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.color = color;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        return tmp;
    }
}
