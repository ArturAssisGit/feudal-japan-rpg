using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Auto-created. Shows "[E] Chop" near a GatherNode; hold E to gather (same look as the loot prompt).
public class GatherSystem : MonoBehaviour
{
    public static GatherSystem Instance { get; private set; }
    public static bool IsGathering => Instance != null && Instance.gathering;

    [Header("Controls")]
    public KeyCode gatherKey = KeyCode.E;

    [Header("Animation")]
    [Tooltip("Bool parameter set on the player's Animator while gathering (only used if it exists)")] public string animatorBool = "isGathering";

    [Header("Prompt")]
    public Vector2 promptSize = new Vector2(150f, 40f);
    public Color barColor = new Color(0.95f, 0.82f, 0.55f);

    private Transform player;
    private Animator anim;
    private bool animHasBool;
    private float nextScan;

    private GatherNode target;
    private bool gathering;
    private float progress;

    private GameObject promptObject;
    private TextMeshProUGUI promptText, messageText;
    private string shownAction;
    private Image barFill;
    private GameObject barObject;
    private float messageUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<GatherSystem>("GatherSystem");
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start() => BuildUI();

    void Update()
    {
        if (promptObject == null) return;
        if (messageText.text.Length > 0 && Time.time > messageUntil) messageText.text = "";

        if (!FindPlayer())
        {
            promptObject.SetActive(false);
            return;
        }

        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.2f;
            foreach (GatherNode node in GatherNode.All) if (node != null) node.TickRespawn();
            if (!gathering) target = FindNodeInRange();
        }

        bool menuOpen = PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.IsOpen;
        bool blocked = menuOpen || LootWindow.IsOpen || ScreenFade.IsActive || LootBagInRange();

        if (gathering)
        {
            if (target == null || target.IsEmpty || blocked || !Input.GetKey(gatherKey) || MovementPressed() || !InRange(target)) StopGathering();
            else
            {
                progress += Time.deltaTime;
                if (progress >= target.gatherTime) Complete();
            }
        }
        else if (target != null && !blocked && InRange(target) && Input.GetKeyDown(gatherKey) && !PickupInRange()) StartGathering();

        bool showPrompt = target != null && !blocked && InRange(target);
        promptObject.SetActive(showPrompt);
        if (showPrompt)
        {
            if (shownAction != target.actionName)
            {
                shownAction = target.actionName;
                promptText.text = $"[{gatherKey}] {shownAction}"; // only when the action word changes
            }
            barObject.SetActive(gathering);
            barFill.fillAmount = gathering ? Mathf.Clamp01(progress / Mathf.Max(0.01f, target.gatherTime)) : 0f;
        }
    }

    // ---------- Gathering ----------

    void StartGathering()
    {
        gathering = true;
        progress = 0f;

        Vector3 look = target.transform.position - player.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.01f) player.rotation = Quaternion.LookRotation(look);

        SetAnim(true);
        GatherAnimation.StartLoop();
    }

    void StopGathering()
    {
        gathering = false;
        progress = 0f;
        SetAnim(false);
        GatherAnimation.Stop();
    }

    void Complete()
    {
        GatherNode node = target;
        StopGathering();

        List<KeyValuePair<PlayerItemData, int>> results = node.Harvest();
        if (results.Count == 0)
        {
            ShowMessage("Found nothing");
            return;
        }

        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        var parts = new List<string>();
        bool dropped = false;

        foreach (var result in results)
        {
            int added = inventory != null ? inventory.AddItem(result.Key, result.Value) : 0;
            if (added < result.Value)
            {
                LootBag.DropFromPlayer(player, result.Key, inventory != null ? inventory.lootBagPrefab : null, inventory != null ? inventory.bagMergeRadius : 2.5f, result.Value - added);
                dropped = true;
            }
            parts.Add($"+{result.Value} {(string.IsNullOrEmpty(result.Key.itemName) ? result.Key.name : result.Key.itemName)}");
        }

        string message = string.Join("   ", parts);
        if (dropped) message += "\n<size=80%>Inventory full - dropped on the ground</size>";
        ShowMessage(message);
    }

    GatherNode FindNodeInRange()
    {
        return WorldUtil.Nearest(GatherNode.All, player.position, n => n.gatherRange, n => !n.IsEmpty && n.isActiveAndEnabled);
    }

    bool InRange(GatherNode node) => node != null && WorldUtil.FlatDistance(player.position, node.transform.position) <= node.gatherRange + 0.3f;

    // Loot bags have priority over gathering (they use E too)
    bool LootBagInRange() => LootBag.AnyInRange(player.position);

    // Items and backpacks on the ground also use E: picking them up wins
    bool PickupInRange()
    {
        foreach (ItemPickup pickup in FindObjectsByType<ItemPickup>())
            if (pickup.itemData != null && WorldUtil.FlatDistance(player.position, pickup.transform.position) <= pickup.pickupRange) return true;
        foreach (DroppedBackpack backpack in FindObjectsByType<DroppedBackpack>())
            if (WorldUtil.FlatDistance(player.position, backpack.transform.position) <= backpack.pickupRange) return true;
        return false;
    }

    static bool MovementPressed() =>
        Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f || Input.GetKeyDown(KeyCode.Space);

    void SetAnim(bool value)
    {
        if (anim != null && animHasBool) anim.SetBool(animatorBool, value);
    }

    bool FindPlayer()
    {
        if (player != null && player.gameObject.activeInHierarchy) return true; // (a player from the menu may have been turned off)
        GameObject found = PlayerRef.Object;
        if (found == null) return false;
        player = found.transform;
        anim = found.GetComponentInChildren<Animator>();
        animHasBool = false;
        if (anim != null)
            foreach (AnimatorControllerParameter p in anim.parameters)
                if (p.name == animatorBool && p.type == AnimatorControllerParameterType.Bool) animHasBool = true;
        return true;
    }

    void ShowMessage(string text)
    {
        messageText.text = text;
        messageUntil = Time.time + 2.5f;
    }

    // ---------- UI ----------

    void BuildUI()
    {
        LootWindow style = LootWindow.Instance;
        TMP_FontAsset font = LootWindow.GameFont;
        Sprite promptSprite = style != null ? style.promptSprite : null;
        Color textColor = UIStyle.Text;

        Transform canvas = UIKit.CreateCanvas("GatherCanvas", transform, 49).transform;

        // "[E] Chop" prompt
        promptObject = UIKit.CreateImage("Prompt", canvas, promptSprite != null ? Color.white : new Color(0f, 0f, 0f, 0.6f), promptSprite);
        Image promptImage = promptObject.GetComponent<Image>();
        promptImage.raycastTarget = false;
        RectTransform promptRect = promptObject.GetComponent<RectTransform>();
        promptImage.enabled = style != null && style.promptBackground;

        promptText = UIKit.CreateText("Text", promptObject.transform, "[E] Gather", 18f, textColor, font); // size set by PlaceInCorner
        UIKit.SetRect(promptText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        LootWindow.PlaceInCorner(promptRect, promptText, 0, 300f);
        promptRect.sizeDelta += new Vector2(0f, 10f); // room for the progress bar

        // Progress bar under the text
        barObject = UIKit.CreateImage("Bar", promptObject.transform, new Color(0f, 0f, 0f, 0.5f));
        barObject.GetComponent<Image>().raycastTarget = false;
        UIKit.SetRect(barObject.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-70f, 2f), new Vector2(70f, 7f)); // centered under the text

        GameObject fill = UIKit.CreateImage("Fill", barObject.transform, barColor);
        barFill = fill.GetComponent<Image>();
        barFill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        barFill.type = Image.Type.Filled;
        barFill.fillMethod = Image.FillMethod.Horizontal;
        barFill.raycastTarget = false;
        UIKit.SetRect(fill.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        barObject.SetActive(false);

        promptObject.SetActive(false);

        // "+2 Wood Stick" message above the prompt
        messageText = UIKit.CreateText("GatherMessage", canvas, "", 22f, textColor, font); // size set by PlaceInCorner
        messageText.textWrappingMode = TextWrappingModes.Normal;
        LootWindow.PlaceInCorner(messageText.rectTransform, messageText, 1);
        messageText.rectTransform.sizeDelta = new Vector2(800f, 60f);
    }
}
