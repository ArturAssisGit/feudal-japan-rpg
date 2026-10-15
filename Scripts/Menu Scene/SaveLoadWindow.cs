using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Load window (creates itself): every save, newest first. Click one to load it, "Delete" to remove it.
// Also the small "Continue" question of the main menu: last save, or choose one.
public class SaveLoadWindow : MenuWindow
{
    private static SaveLoadWindow instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetInstance() => instance = null;

    // The list of saves. From the game (pause menu) it asks before loading, because what isn't saved is lost
    public static void OpenLoad(Action onClosed = null) => Get(ref instance).ShowPage(false, onClosed);

    // Main menu Continue: "Last save" or "Choose a save"
    public static void OpenContinue(Action onClosed = null) => Get(ref instance).ShowPage(true, onClosed);

    protected override Vector2 WindowSize => new Vector2(1000f, 760f);

    private RectTransform root;
    private bool continuePage;
    private string confirmPath; // the save waiting for "Yes / No" (load or delete)
    private bool confirmDelete;

    protected override void Build()
    {
        root = UIStyle.Group(window, "Page", 40, 12f);
        EscapeHint();
    }

    void ShowPage(bool asContinue, Action onClosed)
    {
        continuePage = asContinue;
        confirmPath = null;
        Show(onClosed);
    }

    protected override void OnShow() => Rebuild();

    // ESC: first cancels a "Yes / No", then the list goes back to the Continue question, then closes
    protected override void OnEscape()
    {
        if (confirmPath != null) { confirmPath = null; Rebuild(); return; }
        Hide();
    }

    static bool InGame => !TitleScreenManager.IsOpen;

    void Rebuild()
    {
        ClearChildren(root);
        if (continuePage) BuildContinue();
        else BuildList();
    }

    // ---------- Continue ----------

    void BuildContinue()
    {
        WorldSaveGameManager w = WorldSaveGameManager.instance;
        List<WorldSaveGameManager.SaveInfo> saves = w != null ? w.ListSaves() : new List<WorldSaveGameManager.SaveInfo>();

        UIStyle.Label(root, "Continue", 48f, UIStyle.Gold, 66f, TextAlignmentOptions.Center, FontStyles.Bold);
        UIStyle.Divider(root);
        UIStyle.FlexSpacer(root);

        if (saves.Count == 0)
        {
            UIStyle.Label(root, "There are no saves yet", 28f, UIStyle.Tan, 50f);
        }
        else
        {
            WorldSaveGameManager.SaveInfo last = saves[0];
            WordButton(root, "Last save", 40f, () => Load(last.path));
            UIStyle.Label(root, Describe(last), 22f, UIStyle.Tan, 34f);
            UIStyle.Spacer(root, 30f);
            WordButton(root, "Choose a save", 40f, () => { continuePage = false; Rebuild(); });
            UIStyle.Label(root, saves.Count == 1 ? "1 save" : saves.Count + " saves", 22f, UIStyle.Tan, 34f);
        }
        UIStyle.FlexSpacer(root);
        UIStyle.Spacer(root, 40f);
    }

    // ---------- List ----------

    void BuildList()
    {
        WorldSaveGameManager w = WorldSaveGameManager.instance;
        List<WorldSaveGameManager.SaveInfo> saves = w != null ? w.ListSaves() : new List<WorldSaveGameManager.SaveInfo>();

        UIStyle.Label(root, "Load", 48f, UIStyle.Gold, 66f, TextAlignmentOptions.Center, FontStyles.Bold);
        UIStyle.Divider(root);
        UIStyle.Spacer(root, 6f);

        if (saves.Count == 0)
        {
            UIStyle.FlexSpacer(root);
            UIStyle.Label(root, "There are no saves yet", 28f, UIStyle.Tan, 50f);
            UIStyle.FlexSpacer(root);
        }
        else
        {
            RectTransform list = ScrollList(root, 8f);
            foreach (WorldSaveGameManager.SaveInfo save in saves) SaveRow(list, save);
        }
        UIStyle.Spacer(root, 40f);
    }

    void SaveRow(Transform list, WorldSaveGameManager.SaveInfo save)
    {
        GameObject box = UIKit.CreateImage("Save", list, new Color(0.22f, 0.16f, 0.10f, 0.55f), LootWindow.RoundedSprite(12));
        LayoutElement le = box.AddComponent<LayoutElement>();
        le.preferredHeight = 78f;
        HorizontalLayoutGroup h = box.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(22, 18, 8, 8);
        h.spacing = 14f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        if (confirmPath == save.path)
        {
            string question = confirmDelete ? "Delete this save?" : "Load? What isn't saved will be lost";
            Words(box.transform, question, 26f, UIStyle.Gold);
            WordButton(box.transform, "Yes", 28f, () =>
            {
                string path = confirmPath;
                bool delete = confirmDelete;
                confirmPath = null;
                if (delete) { WorldSaveGameManager.instance?.DeleteSave(path); Rebuild(); }
                else Load(path);
            }, 90f);
            WordButton(box.transform, "No", 28f, () => { confirmPath = null; Rebuild(); }, 90f);
            return;
        }

        // The whole row is a button: click = load
        Button load = box.AddComponent<Button>();
        load.targetGraphic = box.GetComponent<Image>();
        ColorBlock cb = load.colors; // x2: normal = as it is, highlighted = twice as bright
        cb.normalColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        cb.highlightedColor = Color.white;
        cb.selectedColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        cb.pressedColor = new Color(0.35f, 0.35f, 0.35f, 0.5f);
        cb.colorMultiplier = 2f;
        cb.fadeDuration = 0.08f;
        load.colors = cb;
        load.onClick.AddListener(() =>
        {
            if (InGame) { confirmPath = save.path; confirmDelete = false; Rebuild(); }
            else Load(save.path);
        });

        Words(box.transform, save.KindName, 30f, save.kind == WorldSaveGameManager.SaveKind.Auto ? UIStyle.Cream : UIStyle.Gold, 230f);
        Words(box.transform, Describe(save), 24f, UIStyle.Tan);
        WordButton(box.transform, "Delete", 24f, () => { confirmPath = save.path; confirmDelete = true; Rebuild(); }, 110f);
    }

    static string Describe(WorldSaveGameManager.SaveInfo save)
    {
        string text = save.time.ToString("d MMM yyyy,  HH:mm", CultureInfo.InvariantCulture);
        if (save.hour >= 0f)
        {
            int minutes = Mathf.FloorToInt(Mathf.Repeat(save.hour, 24f) * 60f);
            text += $"     ·     In game {minutes / 60:00}:{minutes % 60:00}";
        }
        return text;
    }

    // ---------- Loading ----------

    void Load(string path)
    {
        WorldSaveGameManager w = WorldSaveGameManager.instance;
        if (w == null || w.IsLoading) return;
        Hide(false); // the pause menu (if any) must not come back
        if (PauseMenu.IsOpen && PauseMenu.Instance != null) PauseMenu.Instance.Close();
        w.LoadSave(path);
    }
}
