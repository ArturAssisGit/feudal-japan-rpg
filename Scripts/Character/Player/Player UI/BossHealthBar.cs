using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Boss name + health bar at the top center of the screen (creates itself).
// Only for enemies with "Is Boss" ticked, and only while you fight them. Normal enemies never show a bar.
// The name comes from the enemy's "Boss Name" field (empty = the object's name).
public class BossHealthBar : MonoBehaviour
{
    public static BossHealthBar Instance { get; private set; }

    [Header("Position and size (pixels at 1080p)")]
    [Tooltip("Distance from the top of the screen")] public float topOffset = 36f;
    public float barWidth = 640f;
    public float barHeight = 12f;
    public float nameFontSize = 28f;

    [Header("When it shows")]
    [Tooltip("The boss must be closer than this to the player (meters)")] public float showDistance = 35f;
    [Tooltip("Seconds it stays after the boss stops fighting / dies")] public float lingerSeconds = 2.5f;
    [Tooltip("Seconds of the fade in / fade out")] public float fadeSeconds = 0.5f;

    [Header("Colors")]
    public Color fillColor = new Color(0.62f, 0.12f, 0.1f, 1f);
    [Tooltip("The part that was just lost (shrinks after a moment)")] public Color damageColor = new Color(0.95f, 0.8f, 0.5f, 1f);
    public Color backgroundColor = new Color(0.08f, 0.06f, 0.045f, 1f);

    private GameObject canvas;
    private CanvasGroup group;
    private TextMeshProUGUI nameText;
    private RectTransform fill, chip;
    private EnemyAI_01 boss;
    private Transform player;
    private float nextSearch, alpha, shown = 1f, chipValue = 1f, chipWaitUntil, visibleUntil;
    private bool inMenu;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<BossHealthBar>("BossHealthBar");
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Update()
    {
        if (canvas == null) Build();

        if (Time.unscaledTime >= nextSearch)
        {
            nextSearch = Time.unscaledTime + 0.25f;
            inMenu = TitleScreenManager.IsOpen;
            PlayerRef.Find(ref player);
            EnemyAI_01 found = FindFightingBoss();
            if (found != null && found != boss)
            {
                boss = found;
                nameText.text = boss.DisplayName;
                shown = chipValue = boss.HealthFraction;
            }
            if (found != null) visibleUntil = Time.unscaledTime + lingerSeconds;
        }

        bool wanted = !inMenu && !RespawnSystem.IsDeathScreenOpen && boss != null && Time.unscaledTime < visibleUntil;
        alpha = Mathf.MoveTowards(alpha, wanted ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, fadeSeconds));
        group.alpha = alpha;
        if (alpha <= 0f && !wanted) { boss = null; return; }
        if (boss == null) return;

        // The red part follows the health quickly; the light part waits a moment, then catches up
        float target = boss.IsDead ? 0f : boss.HealthFraction;
        if (target < shown - 0.001f) chipWaitUntil = Time.unscaledTime + 0.6f;
        shown = Mathf.MoveTowards(shown, target, Time.unscaledDeltaTime * 2.5f);
        if (target > chipValue) chipValue = target;
        else if (Time.unscaledTime >= chipWaitUntil) chipValue = Mathf.MoveTowards(chipValue, shown, Time.unscaledDeltaTime * 0.8f);

        SetWidth(fill, shown);
        SetWidth(chip, chipValue);
    }

    // The closest living boss that is fighting (or was hit a moment ago) near the player
    EnemyAI_01 FindFightingBoss()
    {
        EnemyAI_01 best = null;
        float bestDistance = showDistance * showDistance;
        foreach (EnemyAI_01 e in EnemyAI_01.All)
        {
            if (e == null || !e.isBoss || e.IsDead || !e.gameObject.activeInHierarchy) continue;
            bool fighting = e.IsInCombat || Time.time - e.LastDamagedTime < 5f;
            if (!fighting) continue;
            float d = player != null ? (e.transform.position - player.position).sqrMagnitude : 0f;
            if (d > bestDistance) continue;
            bestDistance = d;
            best = e;
        }
        return best;
    }

    static void SetWidth(RectTransform rect, float fraction)
    {
        bool visible = fraction > 0.005f; // an empty bar would draw with a negative width
        if (rect.gameObject.activeSelf != visible) rect.gameObject.SetActive(visible);
        Vector2 max = rect.anchorMax;
        max.x = Mathf.Clamp01(fraction);
        rect.anchorMax = max;
    }

    void Build()
    {
        canvas = UIKit.CreateCanvas("BossHealthCanvas", transform, 45);

        GameObject root = new GameObject("BossBar", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);
        group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = rootRect.anchorMax = rootRect.pivot = new Vector2(0.5f, 1f);
        rootRect.anchoredPosition = new Vector2(0f, -topOffset);
        rootRect.sizeDelta = new Vector2(barWidth, nameFontSize * 1.4f + barHeight + 6f);

        // Name
        nameText = UIKit.CreateText("Name", root.transform, "", nameFontSize, UIStyle.Gold, LootWindow.GameFont, TextAlignmentOptions.Bottom);
        RectTransform nameRect = nameText.rectTransform;
        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.pivot = new Vector2(0.5f, 1f);
        nameRect.sizeDelta = new Vector2(0f, nameFontSize * 1.4f);
        nameRect.anchoredPosition = Vector2.zero;
        nameText.outlineWidth = 0.2f;
        nameText.outlineColor = new Color32(0, 0, 0, 255);

        // Bar: background, the "just lost" part, then the health
        Sprite rounded = LootWindow.RoundedSprite(4);
        GameObject background = UIKit.CreateImage("Background", root.transform, backgroundColor, rounded);
        background.GetComponent<Image>().raycastTarget = false;
        RectTransform bg = background.GetComponent<RectTransform>();
        bg.anchorMin = new Vector2(0f, 0f);
        bg.anchorMax = new Vector2(1f, 0f);
        bg.pivot = new Vector2(0.5f, 0f);
        bg.sizeDelta = new Vector2(0f, barHeight);
        bg.anchoredPosition = Vector2.zero;
        Outline outline = background.AddComponent<Outline>();
        Color gold = UIStyle.Gold;
        outline.effectColor = new Color(gold.r, gold.g, gold.b, 0.45f);
        outline.effectDistance = new Vector2(1f, 1f);

        chip = Bar("Lost", background.transform, damageColor, rounded);
        fill = Bar("Health", background.transform, fillColor, rounded);
    }

    static RectTransform Bar(string name, Transform parent, Color color, Sprite sprite)
    {
        GameObject go = UIKit.CreateImage(name, parent, color, sprite);
        go.GetComponent<Image>().raycastTarget = false;
        RectTransform rect = go.GetComponent<RectTransform>();
        UIKit.SetRect(rect, Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
        return rect;
    }
}
