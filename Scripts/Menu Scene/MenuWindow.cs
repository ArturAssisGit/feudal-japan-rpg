using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Base of the code-made menu windows (Settings, Load...), used in the main menu and in the pause menu.
// Same brown + gold look as the other windows. ESC closes the window that is open (and only that one).
public abstract class MenuWindow : MonoBehaviour
{
    // The window open right now (only one at a time)
    public static MenuWindow Current { get; private set; }
    public static bool AnyOpen => Current != null && Current.Showing;
    // True in the frame where a window used the ESC key (so the pause menu doesn't close too)
    public static bool EscapeUsedThisFrame => escapeFrame == Time.frameCount;
    private static int escapeFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Current = null;
        escapeFrame = -1;
    }

    protected GameObject canvas;
    protected RectTransform window;
    private Action onClosed;

    public bool Showing => canvas != null && canvas.activeSelf;

    protected abstract Vector2 WindowSize { get; }
    protected abstract void Build();
    protected virtual void OnShow() { }
    // ESC: closes the window (override to go back one step first)
    protected virtual void OnEscape() => Hide();

    // Finds the window, or creates it (it stays between scenes)
    protected static T Get<T>(ref T cached) where T : MenuWindow
    {
        if (cached == null) cached = WorldUtil.CreatePersistent<T>(typeof(T).Name);
        return cached;
    }

    protected void Show(Action closed)
    {
        if (AnyOpen && Current != this) Current.Hide(false);
        if (canvas == null)
        {
            canvas = UIKit.CreateCanvas(GetType().Name + "Canvas", transform, 195, true); // above the pause menu (190)
            GameObject dim = UIKit.CreateImage("Dim", canvas.transform, UIStyle.Dim);
            UIKit.SetRect(dim.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            window = UIStyle.CreateWindow(canvas.transform, "Window", WindowSize);
            Build();
        }
        EnsureEventSystem();
        onClosed = closed;
        canvas.SetActive(true);
        Current = this;
        OnShow();
    }

    // callBack = false: closes without running what the opener asked (e.g. when a save is being loaded)
    public void Hide(bool callBack = true)
    {
        if (canvas != null) canvas.SetActive(false);
        if (Current == this) Current = null;
        Action closed = onClosed;
        onClosed = null;
        if (callBack) closed?.Invoke();
    }

    protected virtual void Update()
    {
        if (Current != this || !Showing) return;
        if (Input.GetKeyDown(KeyCode.Escape) && !EscapeUsedThisFrame)
        {
            escapeFrame = Time.frameCount;
            OnEscape();
        }
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(es);
    }

    // ---------- Building blocks ----------

    // The "[ESC] Back" hint at the bottom left of the window
    protected void EscapeHint(string text = "[ESC]  Back")
    {
        TextMeshProUGUI hint = UIKit.CreateText("EscHint", window, text, 22f, UIStyle.Tan, UIStyle.Font, TextAlignmentOptions.BottomLeft);
        UIKit.SetRect(hint.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(40f, 22f), new Vector2(-40f, 60f));
    }

    protected static TextMeshProUGUI Words(Transform parent, string text, float size, Color color, float width = -1f,
        TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, float height = -1f)
    {
        TextMeshProUGUI t = UIKit.CreateText("Text", parent, text, size, color, UIStyle.Font, align, TextOverflowModes.Ellipsis);
        LayoutElement le = t.gameObject.AddComponent<LayoutElement>();
        if (width > 0f) le.preferredWidth = width;
        else le.flexibleWidth = 1f;
        if (height > 0f) le.preferredHeight = height;
        return t;
    }

    // A button that is only its word (light colour, lights up gold on hover), like the main menu
    protected static Button WordButton(Transform parent, string label, float fontSize, UnityAction onClick, float width = -1f, Color? normal = null)
    {
        GameObject go = UIKit.CreateImage("Word_" + label, parent, new Color(0f, 0f, 0f, 0f));
        Button button = go.AddComponent<Button>();
        if (onClick != null) button.onClick.AddListener(onClick);
        TextMeshProUGUI t = UIKit.CreateText("Text", go.transform, label, fontSize, UIStyle.Cream, UIStyle.Font);
        UIKit.SetRect(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = fontSize * 1.7f;
        if (width > 0f) le.preferredWidth = width;
        else le.flexibleWidth = 1f;
        go.AddComponent<MenuTextButton>().Setup(normal ?? UIStyle.Cream, UIStyle.Gold, 1.06f, 3f, 0.35f);
        return button;
    }

    protected static void SetWordColor(Button button, Color normal)
    {
        MenuTextButton word = button.GetComponent<MenuTextButton>();
        if (word != null) word.Setup(normal, UIStyle.Gold, 1.06f, 3f, 0.35f);
    }

    protected static void SetLabel(Button button, string label)
    {
        TMP_Text t = button.GetComponentInChildren<TMP_Text>(true);
        if (t != null) t.text = label;
    }

    // Label on the left, a choice in the middle with < and > to change it
    protected static void ChoiceRow(Transform parent, string label, string[] options, int index, Action<int> onChange)
    {
        RectTransform row = UIStyle.Row(parent, 50f, 10f);
        Words(row, label, 26f, UIStyle.Cream);
        int current = Mathf.Clamp(index, 0, Mathf.Max(0, options.Length - 1));
        TextMeshProUGUI value = null;
        void Step(int d)
        {
            if (options.Length == 0) return;
            current = (current + d + options.Length) % options.Length;
            value.text = options[current];
            onChange(current);
        }
        WordButton(row, "<", 30f, () => Step(-1), 50f);
        value = Words(row, options.Length > 0 ? options[current] : "-", 26f, UIStyle.Gold, 300f, TextAlignmentOptions.Center);
        WordButton(row, ">", 30f, () => Step(1), 50f);
    }

    protected static void ToggleRow(Transform parent, string label, bool on, Action<bool> onChange) =>
        ChoiceRow(parent, label, new[] { "Off", "On" }, on ? 1 : 0, i => onChange(i == 1));

    // Label on the left, a slider, and the value on the right
    protected static void SliderRow(Transform parent, string label, float min, float max, float value, Func<float, string> format, Action<float> onChange)
    {
        RectTransform row = UIStyle.Row(parent, 50f, 18f);
        Words(row, label, 26f, UIStyle.Cream, 380f);
        TextMeshProUGUI valueText = null;
        CreateSlider(row, min, max, value, v => { valueText.text = format(v); onChange(v); });
        valueText = Words(row, format(value), 24f, UIStyle.Gold, 110f, TextAlignmentOptions.MidlineRight);
    }

    protected static Slider CreateSlider(Transform parent, float min, float max, float value, UnityAction<float> onChange)
    {
        GameObject root = new GameObject("Slider", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        LayoutElement le = root.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.preferredHeight = 26f;

        GameObject bg = UIKit.CreateImage("Background", root.transform, new Color(0.06f, 0.045f, 0.03f, 1f), LootWindow.RoundedSprite(6));
        UIKit.SetRect(bg.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -5f), new Vector2(0f, 5f));
        Outline outline = bg.AddComponent<Outline>();
        Color line = UIStyle.Gold;
        line.a = 0.35f;
        outline.effectColor = line;
        outline.effectDistance = new Vector2(1f, 1f);

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(root.transform, false);
        UIKit.SetRect(fillArea.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -5f), new Vector2(0f, 5f));
        Color fillColor = UIStyle.Gold;
        fillColor.a = 0.8f;
        GameObject fill = UIKit.CreateImage("Fill", fillArea.transform, fillColor, LootWindow.RoundedSprite(6));
        fill.GetComponent<Image>().raycastTarget = false;
        UIKit.SetRect(fill.GetComponent<RectTransform>(), Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);

        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(root.transform, false);
        UIKit.SetRect(handleArea.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f));
        GameObject handle = UIKit.CreateImage("Handle", handleArea.transform, UIStyle.Cream, LootWindow.RoundedSprite(10));
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        UIKit.SetRect(handleRect, Vector2.zero, new Vector2(0f, 1f), new Vector2(-10f, 0f), new Vector2(10f, 0f));

        Slider slider = root.AddComponent<Slider>();
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handleRect;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener(onChange);
        return slider;
    }

    // A list that scrolls with the mouse wheel. Put the rows in the returned content
    protected static RectTransform ScrollList(Transform parent, float spacing)
    {
        GameObject view = new GameObject("Scroll", typeof(RectTransform), typeof(RectMask2D), typeof(ScrollRect));
        view.transform.SetParent(parent, false);
        LayoutElement le = view.AddComponent<LayoutElement>();
        le.flexibleHeight = 1f;
        le.flexibleWidth = 1f;
        Image hit = view.AddComponent<Image>(); // so the wheel works between the rows too
        hit.color = new Color(0f, 0f, 0f, 0f);

        GameObject content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(view.transform, false);
        RectTransform rect = content.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        VerticalLayoutGroup v = UIStyle.Vertical(content, 0, spacing);
        v.padding = new RectOffset(4, 16, 4, 4);
        ContentSizeFitter fit = content.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = view.GetComponent<ScrollRect>();
        scroll.content = rect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 35f;
        scroll.viewport = view.GetComponent<RectTransform>();
        return rect;
    }

    protected static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--) Destroy(parent.GetChild(i).gameObject);
    }
}
