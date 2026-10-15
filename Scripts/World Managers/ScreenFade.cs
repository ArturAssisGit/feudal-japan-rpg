using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Full-screen black overlay (creates itself). HoldBlack() = screen goes black at once, FadeIn(seconds) = fades back to the game.
// Optionally the sound fades with it (AudioListener.volume: music, rain, effects, everything).
public class ScreenFade : MonoBehaviour
{
    private static ScreenFade instance;
    // True while the screen is black or fading in: the player can't do anything yet
    public static bool IsActive => instance != null && (instance.holding || instance.fading || instance.fadingOut);
    // True once the screen is fully black (after HoldBlack, or at the end of FadeOut)
    public static bool IsBlack => instance != null && instance.holding && instance.alpha >= 1f;
    private Image image;
    private float alpha, duration = 1f, audioLevel = 1f;
    private bool fading, holding, fadeAudio, fadingOut;
    private const float AudioOutSeconds = 0.8f;

    // Text on the black screen (intro). IntroShowing stays true until the player presses the skip key.
    public static bool IntroShowing => instance != null && instance.introActive;
    private bool introActive;
    private KeyCode introKey = KeyCode.Space;
    private float introTime;
    private TextMeshProUGUI introText, introHint;
    private const float IntroTextFade = 2f, IntroHintDelay = 1.5f, IntroMinTime = 0.4f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; AudioListener.volume = 1f; }

    static ScreenFade Get()
    {
        if (instance != null) return instance;
        GameObject go = new GameObject("ScreenFade");
        DontDestroyOnLoad(go);
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000; // above everything
        GameObject img = new GameObject("Black");
        img.transform.SetParent(go.transform, false);
        Image image = img.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = false; // never blocks the mouse
        RectTransform rt = image.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        instance = go.AddComponent<ScreenFade>();
        instance.image = image;
        return instance;
    }

    // Black right now. With fadeSound the sound fades out quickly too.
    public static void HoldBlack(bool fadeSound = true)
    {
        ScreenFade f = Get();
        f.fading = false;
        f.fadingOut = false;
        f.holding = true;
        f.fadeAudio = fadeSound;
        f.alpha = 1f;
        f.Apply();
    }

    // Shows a text on the black screen until the key is pressed (call after HoldBlack). Check IntroShowing to know when it ended.
    public static void ShowIntro(string text, string hint, KeyCode skipKey, float fontSize = 30f)
    {
        ScreenFade f = Get();
        f.introKey = skipKey;
        f.introTime = 0f;
        f.introActive = true;
        f.holding = true;
        f.BuildIntro(text, hint, fontSize);
    }

    // Fade from black to the game over this many seconds. With fadeSound the sound comes in with it.
    public static void FadeIn(float seconds, bool fadeSound = true)
    {
        ScreenFade f = Get();
        f.duration = Mathf.Max(0.05f, seconds);
        f.alpha = 1f;
        f.holding = false;
        f.fadingOut = false;
        f.fading = true;
        f.fadeAudio = fadeSound;
        f.Apply();
    }

    // From the game to black over this many seconds, then it stays black (call FadeIn after)
    public static void FadeOut(float seconds, bool fadeSound = false)
    {
        ScreenFade f = Get();
        f.duration = Mathf.Max(0.05f, seconds);
        f.fading = false;
        f.holding = false;
        f.fadingOut = true;
        f.fadeAudio = fadeSound;
        f.audioLevel = 1f;
        f.Apply();
    }

    public static void Clear()
    {
        if (instance == null) return;
        instance.fading = false;
        instance.fadingOut = false;
        instance.holding = false;
        instance.alpha = 0f;
        instance.audioLevel = 1f;
        instance.EndIntro();
        instance.Apply();
        AudioListener.volume = GameSettings.Master;
    }

    void Update()
    {
        if (!fading && !holding && !fadingOut) return;

        if (fadingOut)
        {
            alpha = Mathf.MoveTowards(alpha, 1f, Time.unscaledDeltaTime / duration);
            if (fadeAudio) AudioListener.volume = (1f - alpha) * GameSettings.Master;
            if (alpha >= 1f) { fadingOut = false; holding = true; audioLevel = fadeAudio ? 0f : 1f; }
            Apply();
            return;
        }

        if (holding)
        {
            if (fadeAudio)
            {
                audioLevel = Mathf.MoveTowards(audioLevel, 0f, Time.unscaledDeltaTime / AudioOutSeconds);
                AudioListener.volume = audioLevel * GameSettings.Master;
            }
            if (introActive) UpdateIntro();
            return;
        }

        alpha -= Time.unscaledDeltaTime / duration;
        if (alpha <= 0f)
        {
            alpha = 0f;
            fading = false;
            audioLevel = 1f;
            AudioListener.volume = GameSettings.Master;
        }
        else if (fadeAudio)
        {
            float k = 1f - alpha;
            audioLevel = k * k; // gentle start: the ear hears volume on a curve
            AudioListener.volume = audioLevel * GameSettings.Master;
        }
        Apply();
    }

    void BuildIntro(string text, string hint, float fontSize)
    {
        if (introText != null) Destroy(introText.gameObject);
        if (introHint != null) Destroy(introHint.gameObject);
        Color white = new Color(1f, 1f, 1f, 0f);

        introText = UIKit.CreateText("IntroText", transform, text, fontSize, white, LootWindow.GameFont);
        introText.alignment = TextAlignmentOptions.Center;
        introText.textWrappingMode = TextWrappingModes.Normal;
        introText.raycastTarget = false;
        RectTransform r = introText.rectTransform;
        r.anchorMin = new Vector2(0.2f, 0.25f); r.anchorMax = new Vector2(0.8f, 0.75f);
        r.offsetMin = r.offsetMax = Vector2.zero;

        introHint = UIKit.CreateText("IntroHint", transform, hint, Mathf.Max(14f, fontSize * 0.55f), white, LootWindow.GameFont);
        introHint.alignment = TextAlignmentOptions.Center;
        introHint.raycastTarget = false;
        RectTransform h = introHint.rectTransform;
        h.anchorMin = new Vector2(0.2f, 0.06f); h.anchorMax = new Vector2(0.8f, 0.16f);
        h.offsetMin = h.offsetMax = Vector2.zero;
    }

    void UpdateIntro()
    {
        introTime += Time.unscaledDeltaTime;
        if (introText != null) introText.alpha = Mathf.Clamp01(introTime / IntroTextFade);
        if (introHint != null) introHint.alpha = Mathf.Clamp01((introTime - IntroHintDelay) / 1f) * (0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 3f));
        if (introTime > IntroMinTime && Input.GetKeyDown(introKey)) EndIntro();
    }

    void EndIntro()
    {
        introActive = false;
        if (introText != null) Destroy(introText.gameObject);
        if (introHint != null) Destroy(introHint.gameObject);
        introText = introHint = null;
    }

    void OnDestroy()
    {
        if (instance == this) AudioListener.volume = GameSettings.Master;
    }

    void Apply()
    {
        if (image != null) image.color = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, alpha));
    }
}
