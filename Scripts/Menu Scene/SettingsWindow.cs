using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Settings window (creates itself): Controls / Gameplay / Video / Audio tabs. ESC goes back.
// Opened from the main menu and from the pause menu: SettingsWindow.Open(whatToDoWhenClosed)
public class SettingsWindow : MenuWindow
{
    private static SettingsWindow instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetInstance() => instance = null;

    public static void Open(Action onClosed = null) => Get(ref instance).Show(onClosed);

    protected override Vector2 WindowSize => new Vector2(1180f, 800f);

    private static readonly string[] TabNames = { "Controls", "Gameplay", "Video", "Audio" };
    private readonly List<Button> tabs = new List<Button>();
    private RectTransform page;
    private int currentTab;

    protected override void Build()
    {
        RectTransform root = UIStyle.Group(window, "Settings", 40, 12f);
        UIStyle.Label(root, "Settings", 48f, UIStyle.Gold, 66f, TextAlignmentOptions.Center, FontStyles.Bold);

        RectTransform tabRow = UIStyle.Row(root, 56f, 30f);
        for (int i = 0; i < TabNames.Length; i++)
        {
            int index = i;
            tabs.Add(WordButton(tabRow, TabNames[i].ToUpperInvariant(), 30f, () => ShowTab(index), 220f));
        }
        UIStyle.Divider(root);
        UIStyle.Spacer(root, 6f);

        page = ScrollList(root, 6f);
        UIStyle.Spacer(root, 40f); // room for the ESC hint
        EscapeHint();
    }

    protected override void OnShow() => ShowTab(currentTab);

    void ShowTab(int index)
    {
        currentTab = index;
        for (int i = 0; i < tabs.Count; i++) SetWordColor(tabs[i], i == index ? UIStyle.Gold : UIStyle.Cream);
        ClearChildren(page);
        switch (index)
        {
            case 0: BuildControls(); break;
            case 1: BuildGameplay(); break;
            case 2: BuildVideo(); break;
            default: BuildAudio(); break;
        }
        page.anchoredPosition = Vector2.zero; // back to the top
    }

    // ---------- Tabs ----------

    void BuildControls()
    {
        SliderRow(page, "Mouse sensitivity", 0.2f, 3f, GameSettings.MouseSensitivity, v => v.ToString("0.0") + "x", v => GameSettings.MouseSensitivity = v);
        ToggleRow(page, "Invert vertical look", GameSettings.InvertY, v => GameSettings.InvertY = v);

        UIStyle.Spacer(page, 10f);
        UIStyle.Label(page, "Keys", 30f, UIStyle.Gold, 44f, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        foreach (KeyValuePair<string, string> key in KeyList())
        {
            RectTransform row = UIStyle.Row(page, 38f, 10f);
            Words(row, key.Key, 24f, UIStyle.Cream);
            Words(row, key.Value, 24f, UIStyle.Gold, 360f, TextAlignmentOptions.MidlineRight);
        }
    }

    void BuildGameplay()
    {
        ToggleRow(page, "Camera shake", GameSettings.CameraShake, v => GameSettings.CameraShake = v);
        ToggleRow(page, "Black bars in Scenic View", GameSettings.ScenicBars, v => GameSettings.ScenicBars = v);

        int[] minutes = { 0, 2, 5, 10, 15, 30 };
        string[] names = { "Off", "Every 2 min", "Every 5 min", "Every 10 min", "Every 15 min", "Every 30 min" };
        int now = GameSettings.AutosaveMinutes;
        if (now < 0) now = WorldSaveGameManager.instance != null ? Mathf.RoundToInt(WorldSaveGameManager.instance.autosaveMinutes) : 2;
        int index = Array.IndexOf(minutes, now);
        if (index < 0) index = 1;
        ChoiceRow(page, "Autosave", names, index, i => GameSettings.AutosaveMinutes = minutes[i]);
    }

    void BuildVideo()
    {
        // Screen size and mode
        List<Vector2Int> sizes = GameSettings.Resolutions();
        string[] sizeNames = new string[sizes.Count];
        for (int i = 0; i < sizes.Count; i++) sizeNames[i] = sizes[i].x + " x " + sizes[i].y;
        int sizeIndex = Mathf.Max(0, sizes.IndexOf(new Vector2Int(Screen.width, Screen.height)));
        int modeIndex = Mathf.Max(0, Array.IndexOf(GameSettings.DisplayModes, Screen.fullScreenMode));
        ChoiceRow(page, "Resolution", sizeNames, sizeIndex, i => { sizeIndex = i; GameSettings.SetScreen(sizes[sizeIndex], GameSettings.DisplayModes[modeIndex]); });
        ChoiceRow(page, "Display mode", GameSettings.DisplayModeNames, modeIndex, i => { modeIndex = i; GameSettings.SetScreen(sizes[sizeIndex], GameSettings.DisplayModes[modeIndex]); });

        ChoiceRow(page, "Graphics quality", QualitySettings.names, GameSettings.Quality, i => GameSettings.Quality = i);
        ToggleRow(page, "VSync", GameSettings.VSync, v => GameSettings.VSync = v);

        int[] fps = { 30, 60, 120, 144, 0 };
        string[] fpsNames = { "30", "60", "120", "144", "Unlimited" };
        int fpsIndex = Array.IndexOf(fps, GameSettings.FpsLimit);
        ChoiceRow(page, "Frame rate limit", fpsNames, fpsIndex < 0 ? fps.Length - 1 : fpsIndex, i => GameSettings.FpsLimit = fps[i]);

        float fov = GameSettings.Fov > 0f ? GameSettings.Fov : (Camera.main != null ? Camera.main.fieldOfView : 60f);
        SliderRow(page, "Field of view", 50f, 90f, fov, v => Mathf.RoundToInt(v).ToString(), v => GameSettings.Fov = Mathf.Round(v));
    }

    void BuildAudio()
    {
        SliderRow(page, "Master volume", 0f, 1f, GameSettings.Master, Percent, v => GameSettings.Master = v);
        SliderRow(page, "Music", 0f, 1f, GameSettings.Music, Percent, v => GameSettings.Music = v);
        SliderRow(page, "Ambience and weather", 0f, 1f, GameSettings.Ambience, Percent, v => GameSettings.Ambience = v);
        SliderRow(page, "Effects", 0f, 1f, GameSettings.Effects, Percent, v => GameSettings.Effects = v);
    }

    static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";

    // ---------- Keys (read from the game's scripts when they exist, otherwise their usual key) ----------

    static List<KeyValuePair<string, string>> KeyList()
    {
        var list = new List<KeyValuePair<string, string>>();
        void Add(string action, string key) => list.Add(new KeyValuePair<string, string>(action, key));

        Add("Move", "W  A  S  D");
        Add("Run", "Left Shift");
        Add("Jump", "Space");
        Add("Slide", "C");
        Add("Dodge", Key<PlayerDodge>(c => c.dodgeKey, KeyCode.LeftAlt));
        Add("Draw / put away sword", Key<PlayerCombat>(c => c.swordKey, KeyCode.Alpha1));
        Add("Draw / put away bow", Key<PlayerCombat>(c => c.bowDrawKey, KeyCode.Alpha2));
        Add("Attack", "Left Mouse");
        Add("Second attack / aim the bow", "Right Mouse");
        Add("Strong attack", "F");
        Add("Block", Key<PlayerCombat>(c => c.blockKey, KeyCode.Q));
        Add("Interact / loot / talk", "E");
        Add("Pick up a placed object", Key<PlacedObjectPickup>(c => c.pickupKey, KeyCode.T));
        Add("Inventory", "I");
        Add("Map", Key<MapSystem>(c => c.mapKey, KeyCode.M));
        Add("Drop the backpack", Key<PlayerEquipmentManager>(c => c.dropBackpackKey, KeyCode.R));
        Add("Sit", Key<SitAnimation>(c => c.sitKey, KeyCode.B));
        Add("Sleep", Key<SleepSystem>(c => c.sleepKey, KeyCode.Z));
        Add("Call your horse", Key<HorseMount>(c => c.callKey, KeyCode.H));
        Add("Mount / dismount", Key<HorseMount>(c => c.mountKey, KeyCode.E));
        Add("Scenic view", Key<CameraController>(c => c.scenicViewKey, KeyCode.CapsLock));
        Add("Quick save", WorldSaveGameManager.instance != null ? Pretty(WorldSaveGameManager.instance.quickSaveKey) : "F5");
        Add("Pause", PauseMenu.Instance != null ? Pretty(PauseMenu.Instance.pauseKey) : "P");
        Add("Multiplayer", Key<MultiplayerSession>(c => c.panelKey, KeyCode.F8));
        return list;
    }

    static string Key<T>(Func<T, KeyCode> read, KeyCode fallback) where T : UnityEngine.Object
    {
        T found = FindAnyObjectByType<T>();
        return Pretty(found != null ? read(found) : fallback);
    }

    static string Pretty(KeyCode key)
    {
        if (key == KeyCode.None) return "-";
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)(key - KeyCode.Alpha0)).ToString();
        if (key == KeyCode.Mouse0) return "Left Mouse";
        if (key == KeyCode.Mouse1) return "Right Mouse";
        if (key == KeyCode.Mouse2) return "Middle Mouse";
        string s = key.ToString();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString(); // LeftShift -> Left Shift, CapsLock -> Caps Lock
    }
}
