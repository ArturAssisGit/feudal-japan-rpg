using TMPro;
using UnityEngine;

// A short message in the middle of the screen that fades away (creates itself). CenterMessage.Show("text").
public class CenterMessage : MonoBehaviour
{
    public static CenterMessage Instance { get; private set; }

    [Tooltip("Seconds the message stays fully visible")] public float showSeconds = 2.5f;
    [Tooltip("Seconds it takes to fade away")] public float fadeSeconds = 0.6f;
    [Tooltip("Text size")] public float fontSize = 30f;
    [Tooltip("Move it up (+) or down (-) from the exact centre, in pixels")] public float yOffset = 0f;

    private TextMeshProUGUI text;
    private float until;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    public static void Show(string message, float seconds = -1f)
    {
        if (Instance == null) WorldUtil.CreatePersistent<CenterMessage>("CenterMessage");
        Instance.Display(message, seconds);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
    }

    void Build()
    {
        Transform canvas = UIKit.CreateCanvas("CenterMessageCanvas", transform, 200).transform;
        text = UIKit.CreateText("Message", canvas, "", fontSize, UIStyle.Text, LootWindow.GameFont);
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        RectTransform rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1100f, 160f);
        rt.anchoredPosition = new Vector2(0f, yOffset);
        text.alpha = 0f;
    }

    void Display(string message, float seconds)
    {
        if (text == null) Build();
        text.fontSize = fontSize;
        text.rectTransform.anchoredPosition = new Vector2(0f, yOffset);
        text.text = message ?? "";
        text.alpha = 1f;
        until = Time.unscaledTime + (seconds > 0f ? seconds : showSeconds);
    }

    void Update()
    {
        if (text == null || text.alpha <= 0f) return;
        if (Time.unscaledTime > until)
            text.alpha = Mathf.MoveTowards(text.alpha, 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, fadeSeconds));
    }
}
