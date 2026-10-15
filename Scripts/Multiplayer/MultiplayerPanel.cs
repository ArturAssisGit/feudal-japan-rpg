using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The multiplayer window (F8) and the join-code banner, in the same brown/gold style as the rest of the UI.
// Added by MultiplayerSession; it only shows things - all the logic stays in MultiplayerSession.
public class MultiplayerPanel : MonoBehaviour
{
    private GameObject canvas, window;
    private RectTransform content;
    private TextMeshProUGUI banner, notice, statusText, playersText;
    private TMP_InputField nameField, codeField;
    private bool wasOpen;
    private MultiplayerSession.Mode builtMode = (MultiplayerSession.Mode)(-1);
    private bool builtInWorld;
    private string codeKeep = "";
    private string shownStatus, shownBanner, shownNotice;

    void Awake()
    {
        canvas = UIKit.CreateCanvas("MultiplayerCanvas", transform, 192, true);

        banner = UIKit.CreateText("CodeBanner", canvas.transform, "", 36f, UIStyle.Gold, UIStyle.Font);
        PlaceTop(banner.rectTransform, 22f, 50f);
        Outline(banner);
        notice = UIKit.CreateText("Notice", canvas.transform, "", 24f, UIStyle.Cream, UIStyle.Font);
        PlaceTop(notice.rectTransform, 76f, 36f);
        Outline(notice);

        RectTransform win = UIStyle.CreateWindow(canvas.transform, "MultiplayerWindow", new Vector2(640f, 640f));
        window = win.gameObject;
        window.SetActive(false);
    }

    static void PlaceTop(RectTransform rect, float y, float height)
    {
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(1000f, height);
        rect.anchoredPosition = new Vector2(0f, -y);
    }

    static void Outline(TextMeshProUGUI t)
    {
        t.outlineWidth = 0.25f;
        t.outlineColor = new Color32(0, 0, 0, 255);
    }

    void Update()
    {
        MultiplayerSession s = MultiplayerSession.Instance;
        if (s == null) return;

        // Banner + notice
        string b = "";
        if (MultiplayerSession.IsHost && (Time.unscaledTime < s.ShowCodeUntil || MultiplayerSession.PanelOpen))
            b = $"Code: {MultiplayerSession.JoinCode}   ({PlayerCount()}/{s.MaxPlayers} players)";
        if (b != shownBanner) { shownBanner = b; banner.text = b; }
        string n = Time.unscaledTime < s.NoticeUntil ? s.Notice : "";
        if (n != shownNotice) { shownNotice = n; notice.text = n; }

        bool open = MultiplayerSession.PanelOpen;
        if (open != wasOpen)
        {
            wasOpen = open;
            window.SetActive(open);
            if (open) { EnsureEventSystem(); builtMode = (MultiplayerSession.Mode)(-1); }
        }
        if (!open) return;

        bool inWorld = MultiplayerSession.InWorldNow;
        if (MultiplayerSession.CurrentMode != builtMode || inWorld != builtInWorld) Rebuild(s, inWorld);

        if (statusText != null && statusText.text != MultiplayerSession.Status) statusText.text = MultiplayerSession.Status;
        if (playersText != null) playersText.text = $"Players: {PlayerCount()}/{s.MaxPlayers}";
    }

    static int PlayerCount() => MultiplayerSync.Instance != null ? MultiplayerSync.Instance.RemotePlayerCount + 1 : 1;

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(es);
    }

    void Rebuild(MultiplayerSession s, bool inWorld)
    {
        if (codeField != null) codeKeep = codeField.text;
        if (content != null) Destroy(content.gameObject);
        builtMode = MultiplayerSession.CurrentMode;
        builtInWorld = inWorld;
        nameField = codeField = null;
        playersText = null;

        content = UIStyle.Group(window.transform, "Content", 36, 14f);
        UIStyle.Label(content, "Multiplayer", 46f, UIStyle.Gold, 64f, TextAlignmentOptions.Center, FontStyles.Bold);
        UIStyle.Divider(content);

        // name
        RectTransform nameRow = UIStyle.Row(content, 52f, 12f);
        TextMeshProUGUI nameLabel = UIStyle.Label(nameRow, "Your name", 24f, UIStyle.Tan, 52f, TextAlignmentOptions.MidlineLeft);
        nameLabel.GetComponent<LayoutElement>().preferredWidth = 160f;
        nameField = UIStyle.CreateInput(nameRow, "Name", 52f, 24);
        nameField.text = MultiplayerSession.PlayerName;
        nameField.onValueChanged.AddListener(v => { if (MultiplayerSession.Instance != null) MultiplayerSession.Instance.SetNameInput(v); });
        UIStyle.Spacer(content, 6f);

        switch (builtMode)
        {
            case MultiplayerSession.Mode.Offline:
                UIStyle.CreateButton(content, inWorld ? "Host (open this game to friends)" : "Host (continue my save)", 66f, s.Host, 26f);
                if (!inWorld) UIStyle.CreateButton(content, "Host a new game", 56f, s.HostNewGame, 24f);
                UIStyle.Spacer(content, 10f);
                UIStyle.Label(content, "Join a friend - write their code:", 24f, UIStyle.Tan, 34f, TextAlignmentOptions.MidlineLeft);
                RectTransform joinRow = UIStyle.Row(content, 58f, 12f);
                codeField = UIStyle.CreateInput(joinRow, "CODE", 58f, 12, 30f);
                codeField.onValidateInput = (text, index, ch) => char.ToUpperInvariant(ch);
                codeField.text = codeKeep;
                codeField.onSubmit.AddListener(v => s.Join(v));
                UIStyle.CreateButton(joinRow, "Join", 58f, () => s.Join(codeField.text), 26f, 150f);
                break;

            case MultiplayerSession.Mode.Starting:
                UIStyle.Spacer(content, 20f);
                UIStyle.Label(content, "Please wait...", 28f, UIStyle.Cream, 50f);
                break;

            case MultiplayerSession.Mode.Host:
                UIStyle.Label(content, "Your code (send it to your friend):", 24f, UIStyle.Tan, 34f, TextAlignmentOptions.MidlineLeft);
                RectTransform codeRow = UIStyle.Row(content, 70f, 12f);
                TextMeshProUGUI code = UIStyle.Label(codeRow, MultiplayerSession.JoinCode, 44f, UIStyle.Gold, 70f, TextAlignmentOptions.Center, FontStyles.Bold);
                code.GetComponent<LayoutElement>().flexibleWidth = 1f;
                UIStyle.CreateButton(codeRow, "Copy", 58f, () => GUIUtility.systemCopyBuffer = MultiplayerSession.JoinCode, 24f, 150f);
                playersText = UIStyle.Label(content, "", 24f, UIStyle.Cream, 34f, TextAlignmentOptions.MidlineLeft);
                UIStyle.Spacer(content, 6f);
                UIStyle.CreateButton(content, "Close the session", 60f, s.Leave, 26f);
                break;

            case MultiplayerSession.Mode.Client:
                UIStyle.Spacer(content, 10f);
                UIStyle.Label(content, "Playing in a friend's game (" + MultiplayerSession.JoinCode + ")", 26f, UIStyle.Cream, 70f);
                UIStyle.CreateButton(content, "Leave", 60f, s.Leave, 26f);
                break;
        }

        UIStyle.FlexSpacer(content);
        statusText = UIStyle.Label(content, MultiplayerSession.Status, 22f, UIStyle.Cream, 56f);
        UIStyle.Label(content, $"[{s.PanelKey}] / [Esc] close", 20f, UIStyle.Tan, 28f);
    }
}
