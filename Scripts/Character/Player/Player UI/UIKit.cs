using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shared helpers for the UI that is built from code (windows, prompts, messages, death/sleep screens)
public static class UIKit
{
    // Screen-space canvas scaled for 1920x1080 (with a GraphicRaycaster when it has clickable things)
    public static GameObject CreateCanvas(string name, Transform parent, int sortingOrder, bool clickable = false)
    {
        GameObject go = clickable
            ? new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
            : new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
        if (parent != null) go.transform.SetParent(parent, false);
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return go;
    }

    // Image (9-sliced when the sprite has borders)
    public static GameObject CreateImage(string name, Transform parent, Color color, Sprite sprite = null)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        }
        else if (name == "Row" || name == "ActionButton" || name.StartsWith("Button") || name.StartsWith("Input"))
        {
            // rows and buttons without their own sprite get rounded corners
            image.sprite = LootWindow.RoundedSprite(12);
            image.type = Image.Type.Sliced;
        }
        return go;
    }

    // Text that ignores clicks and stays on one line
    public static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color,
        TMP_FontAsset font = null, TextAlignmentOptions alignment = TextAlignmentOptions.Center, TextOverflowModes? overflow = null)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        if (overflow.HasValue) tmp.overflowMode = overflow.Value;
        return tmp;
    }

    public static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    // One of the small texts near the center-bottom of the screen ("[E] Loot", "Inventory is full"...),
    // in the game's font and colour, stacked with the others by LootWindow. 'line' = which row (0 = first)
    public static TextMeshProUGUI CornerText(string name, Transform parent, int line = 0, bool hidden = false)
    {
        TextMeshProUGUI text = CreateText(name, parent, "", 18f, UIStyle.Text, LootWindow.GameFont);
        LootWindow.PlaceInCorner(text.rectTransform, text, line);
        if (hidden) text.gameObject.SetActive(false);
        return text;
    }
}
