using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class TitleScreenManager : MonoBehaviour
{
    [Header("Main Menu (optional)")]
    [Tooltip("The Continue button - it is greyed out when there is no save")] public Button continueButton;
    [Tooltip("Old settings panel of the scene (not used anymore: Settings opens the game's Settings window). Hidden at start")] public GameObject settingsPanel;
    [Tooltip("The panel with Continue / New Game / Settings / Exit. Hidden when the game starts, the Play button shows it (empty = found by the name TitleScreenMainMenu)")]
    public GameObject mainMenuPanel;
    [Header("Buttons (only the word)")]
    [Tooltip("The buttons lose their background and outline: only the word stays")] public bool wordOnlyButtons = true;
    [Tooltip("Colour of the words")] public Color buttonTextColor = new Color(0.92f, 0.88f, 0.8f);
    [Tooltip("Colour of the word under the mouse")] public Color buttonHoverColor = new Color(1f, 0.86f, 0.45f);
    [Tooltip("The word under the mouse grows to this size (1 = no growth)")] public float hoverScale = 1.08f;
    [Tooltip("The letters of the word under the mouse open up by this much")] public float hoverLetterSpacing = 6f;
    [Tooltip("How visible a disabled word is (Continue without a save). 0 = invisible, 1 = like the others")] [Range(0f, 1f)] public float disabledAlpha = 0.35f;

    [Header("Press any button")]
    [Tooltip("The first screen says 'Press any button' instead of 'Play'. Any key (or a click) opens Continue / New Game / Settings")] public bool showPressAnyButton = false;
    [Tooltip("The text shown")] public string pressAnyText = "Press any button";
    [Tooltip("The Play button (empty = found by its name or text 'Play')")] public Button playButton;

    [Tooltip("Font for all the texts of the menu (title and buttons). Empty = keep the fonts as they are")] public TMP_FontAsset menuFont;

#if UNITY_EDITOR
    void OnValidate()
    {
        // The game's font, filled in automatically
        if (menuFont == null)
            menuFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Art/Fonts/Almendra-Regular SDF.asset");
    }
#endif

    private float nextRefresh, pressAnyReadyAt;
    private bool playDone;

    // True while the main menu is open (other scripts use it instead of searching the scene)
    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => IsOpen = false;

    void Awake() => LootWindow.SetDefaultFont(menuFont); // code-made windows (deliveries, loot, gather...) use it too in the build

    void OnEnable() => IsOpen = true;
    void OnDisable() => IsOpen = false;

    void Start()
    {
        // Found by name if not set in the Inspector
        if (mainMenuPanel == null) mainMenuPanel = FindChild(transform, "TitleScreenMainMenu");
        if (continueButton == null)
        {
            GameObject c = FindChild(transform, "ContinueButton");
            if (c != null) continueButton = c.GetComponent<Button>();
        }

        // Same font as the rest of the game
        if (menuFont != null)
            foreach (TMP_Text t in GetComponentsInChildren<TMP_Text>(true)) t.font = menuFont;

        if (wordOnlyButtons)
            foreach (Button b in GetComponentsInChildren<Button>(true))
            {
                if (settingsPanel != null && b.transform.IsChildOf(settingsPanel.transform)) continue;
                MenuTextButton word = b.GetComponent<MenuTextButton>();
                if (word == null) word = b.gameObject.AddComponent<MenuTextButton>();
                word.Setup(buttonTextColor, buttonHoverColor, hoverScale, hoverLetterSpacing, disabledAlpha);
            }

        if (showPressAnyButton) SetupPressAny();
        if (settingsPanel != null) settingsPanel.SetActive(false);

        // The game always starts on the title screen (PROJECT + Play)
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        RefreshContinueButton();
    }

    // The Play button becomes the "Press any button" text
    void SetupPressAny()
    {
        if (playButton == null)
            foreach (Button b in GetComponentsInChildren<Button>(true))
            {
                if (mainMenuPanel != null && b.transform.IsChildOf(mainMenuPanel.transform)) continue;
                if (settingsPanel != null && b.transform.IsChildOf(settingsPanel.transform)) continue;
                TMP_Text label = b.GetComponentInChildren<TMP_Text>(true);
                if (b.name.ToLower().Contains("play") || (label != null && label.text.Trim().ToLower() == "play")) { playButton = b; break; }
            }
        if (playButton == null) { Debug.LogWarning("TitleScreenManager: Press Any Button needs the Play button (assign Play Button, or name it 'Play').", this); return; }

        TMP_Text text = playButton.GetComponentInChildren<TMP_Text>(true);
        if (text != null) text.text = pressAnyText;
        MenuTextButton word = playButton.GetComponent<MenuTextButton>();
        if (word == null) word = playButton.gameObject.AddComponent<MenuTextButton>();
        word.Setup(buttonTextColor, buttonHoverColor, hoverScale, hoverLetterSpacing, disabledAlpha);
        word.pulse = true;
        pressAnyReadyAt = Time.unscaledTime + 0.4f;
    }

    static GameObject FindChild(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t.gameObject;
        return null;
    }

    public void StartNetworkHost()
    {
        NetworkManager.Singleton.StartHost();
        RefreshContinueButton();
    }

    // ---------- Buttons (OnClick) ----------

    public void StartNewGame() { if (WorldSaveGameManager.instance != null) WorldSaveGameManager.instance.StartNewGame(); }

    // Asks: continue from the last save, or choose a save
    public void ContinueGame() { if (WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.HasSave) SaveLoadWindow.OpenContinue(); }

    // Opens the list of saves straight away (for a Load button, if you add one)
    public void OpenLoad() => SaveLoadWindow.OpenLoad();

    public void OpenSettings() => SettingsWindow.Open();

    public void CloseSettings() { if (SettingsWindow.Current is SettingsWindow w) w.Hide(); }

    public void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ---------- Continue is only clickable when there is a save ----------

    void Update()
    {
        // The menu always needs a free mouse (the player scripts lock it for the camera)
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;

        if (showPressAnyButton && playButton != null && !playDone)
        {
            if (mainMenuPanel != null && mainMenuPanel.activeInHierarchy) playDone = true; // opened with a click on the word
            // A left click is handled by the button itself (so it doesn't run twice)
            else if (Time.unscaledTime > pressAnyReadyAt && Input.anyKeyDown && !Input.GetMouseButtonDown(0) && playButton.gameObject.activeInHierarchy)
            {
                playDone = true;
                playButton.onClick.Invoke();
            }
        }

        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.5f;
        RefreshContinueButton();
    }

    public void RefreshContinueButton()
    {
        if (continueButton == null) return;
        continueButton.interactable = WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.HasSave;
    }
}
