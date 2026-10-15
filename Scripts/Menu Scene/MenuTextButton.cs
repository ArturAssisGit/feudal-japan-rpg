using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Turns a button into just its word: no background, no outline. The word has one colour, and on hover it
// lights up, grows a little and the letters open up. A disabled button (Continue without a save) is the same word, dimmer.
// Added automatically by the TitleScreenManager to every button of the main menu.
public class MenuTextButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Color normal = new Color(0.92f, 0.88f, 0.8f), hover = new Color(1f, 0.86f, 0.45f);
    private float scale = 1.08f, spacing = 6f, disabledAlpha = 0.35f;
    private const float Speed = 10f;
    // The word slowly fades in and out (used by "Press any button")
    public bool pulse;

    private Button button;
    private TMP_Text text;
    private bool over;
    private float t, baseSpacing;
    private Vector3 baseScale = Vector3.one;

    public void Setup(Color normalColor, Color hoverColor, float hoverScale, float hoverSpacing, float disabledAlpha)
    {
        normal = normalColor; hover = hoverColor; scale = hoverScale; spacing = hoverSpacing; this.disabledAlpha = disabledAlpha;

        button = GetComponent<Button>();
        text = GetComponentInChildren<TMP_Text>(true);
        if (button != null) button.transition = Selectable.Transition.None; // the colours are made here

        // No visible background or outline (the button still receives the mouse)
        foreach (Graphic g in GetComponentsInChildren<Graphic>(true))
        {
            if (g is TMP_Text) continue;
            Color c = g.color;
            c.a = 0f;
            g.color = c;
            g.raycastTarget = g.gameObject == gameObject;
        }
        foreach (Shadow s in GetComponentsInChildren<Shadow>(true)) Destroy(s); // Outline is a Shadow too

        if (text != null)
        {
            baseScale = text.transform.localScale;
            baseSpacing = text.characterSpacing;
        }
        Apply();
    }

    void Update()
    {
        bool usable = button == null || button.interactable;
        float target = over && usable ? 1f : 0f;
        t = Mathf.MoveTowards(t, target, Time.unscaledDeltaTime * Speed);
        Apply();
    }

    void Apply()
    {
        if (text == null) return;
        bool usable = button == null || button.interactable;
        Color c = Color.Lerp(normal, hover, t);
        if (!usable) c.a *= disabledAlpha;
        if (pulse) c.a *= 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 2.2f);
        text.color = c;
        text.transform.localScale = baseScale * Mathf.Lerp(1f, scale, t);
        text.characterSpacing = Mathf.Lerp(baseSpacing, baseSpacing + spacing, t);
    }

    public void OnPointerEnter(PointerEventData e) => over = true;
    public void OnPointerExit(PointerEventData e) => over = false;

    void OnDisable()
    {
        over = false;
        t = 0f;
        if (text != null) Apply();
    }
}
