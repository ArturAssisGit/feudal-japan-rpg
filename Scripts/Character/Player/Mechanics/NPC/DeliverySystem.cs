using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Creates itself at game start (ShopWindow style). Talk to a DeliveryNpc that gives packages, take the package to the NPC it names, get paid.
// One delivery at a time. Keys: the NPC's key (E) to talk / accept / deliver, Esc or Tab to close the dialog.
public class DeliverySystem : MonoBehaviour
{
    public static DeliverySystem Instance { get; private set; }
    public static bool IsDialogOpen => Instance != null && Instance.dialog != Dialog.None;

    // Where the current delivery goes (null = no delivery). Used by the map.
    public static Vector3? TargetPosition
    {
        get
        {
            if (Instance == null || !Instance.job.active) return null;
            DeliveryNpc target = Instance.TargetNpc();
            return target != null ? target.transform.position : (Vector3?)null;
        }
    }

    public static bool IsTarget(DeliveryNpc npc) => Instance != null && Instance.job.active && npc != null && npc.SaveKey == Instance.job.targetKey;

    [Header("Controls")]
    [Tooltip("Seconds without the package before the delivery is cancelled (it was dropped, sold or lost)")] public float lostPackageGrace = 1f;
    [Tooltip("Show the delivery line (where to go and how far) at the top of the screen")] public bool showHud = true;
    [Tooltip("A dark transparent box behind the delivery line. Off = only the text (with a dark outline so it can still be read)")] public bool hudBackground = false;

    private enum Dialog { None, Offer, Active, Thanks, NoJob }

    private class Job
    {
        public bool active;
        public string giverKey, targetKey, cargo;
        public int reward;
        public PlayerItemData packageItem, coinItem;
    }

    private Job job = new Job();
    private int completed;

    private Transform player;
    private float nextJobCheck, graceUntil; private bool packageSeen; private int missingChecks;
    private Dialog dialog = Dialog.None;
    private DeliveryNpc dialogNpc;

    // The offer being shown
    private DeliveryNpc offerTarget;
    private int offerReward;
    private string offerCargo;
    private string thanksReward;

    // UI
    private TMP_FontAsset font;
    private Color textColor, detailColor, titleColor;
    private TextMeshProUGUI promptText, titleText, bodyText, footerText, messageText, toastText, hudText;
    private GameObject panel, hudBox;
    private float messageUntil, toastUntil;
    private int promptLine = -1;
    private DeliveryNpc promptNpc;
    private int promptKind;
    private string promptString;
    private int hudDistance = -1, hudDirection;
    private DeliveryNpc hudTarget;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<DeliverySystem>("DeliverySystem");
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    // A new scene (New Game, Continue, back to the menu): the old delivery is gone. A loaded save puts its own delivery back afterwards.
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        job = new Job();
        completed = 0;
        CloseDialog();
    }

    void Start() => BuildUI();

    // ---------- Save / load ----------

    public static DeliveryState Capture()
    {
        var state = new DeliveryState();
        if (Instance == null) return state;
        state.completed = Instance.completed;
        Job j = Instance.job;
        if (!j.active) return state;
        state.active = true;
        state.giverKey = j.giverKey;
        state.targetKey = j.targetKey;
        state.cargo = j.cargo;
        state.reward = j.reward;
        return state;
    }

    public static void Restore(DeliveryState state)
    {
        if (Instance == null) return;
        Instance.job = new Job();
        Instance.completed = state != null ? state.completed : 0;
        Instance.CloseDialog();
        if (state == null || !state.active) return;

        DeliveryNpc giver = FindByKey(state.giverKey);
        DeliveryNpc target = FindByKey(state.targetKey);
        PlayerItemData package = giver != null ? giver.packageItem : null;
        PlayerItemData coin = giver != null ? giver.coinItem : null;
        if (package == null || coin == null)
            foreach (DeliveryNpc npc in DeliveryNpc.All)
            {
                if (npc == null) continue;
                if (package == null) package = npc.packageItem;
                if (coin == null) coin = npc.coinItem;
            }
        if (target == null || package == null || coin == null) return; // the NPCs of that delivery are not in this scene any more

        Instance.job = new Job
        {
            active = true, giverKey = state.giverKey, targetKey = state.targetKey, cargo = state.cargo,
            reward = state.reward, packageItem = package, coinItem = coin
        };
        Instance.packageSeen = false; Instance.missingChecks = 0; Instance.graceUntil = Time.time + 3f; // the inventory has just been filled: don't judge it yet
    }

    static DeliveryNpc FindByKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        foreach (DeliveryNpc npc in DeliveryNpc.All) if (npc != null && npc.SaveKey == key) return npc;
        return null;
    }

    DeliveryNpc TargetNpc() => job.active ? FindByKey(job.targetKey) : null;

    // ---------- Update ----------

    void Update()
    {
        if (panel == null) return;
        if (messageText.text.Length > 0 && Time.unscaledTime > messageUntil) messageText.text = "";
        if (toastText.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastText.gameObject.SetActive(false);

        if (!PlayerRef.Find(ref player))
        {
            promptText.gameObject.SetActive(false);
            hudBox.SetActive(false);
            return;
        }

        CheckJob();
        UpdateHud();

        if (dialog != Dialog.None)
        {
            UpdateDialog();
            return;
        }

        int kind;
        DeliveryNpc nearby = FindNpcInRange(out kind);
        promptText.gameObject.SetActive(nearby != null);
        if (nearby == null) return;

        SetPrompt(nearby, kind);
        if (Input.GetKeyDown(nearby.TalkKey) && !ScreenFade.IsActive) Talk(nearby, kind);
    }

    // The package disappeared from the inventory (dropped, sold, lost when dying): the delivery is over
    void CheckJob()
    {
        if (!job.active || Time.time < nextJobCheck) return;
        nextJobCheck = Time.time + 0.5f;

        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (inventory == null || Time.time < graceUntil) return;

        // A missing target NPC (far away / disabled) is not a reason to cancel; only a lost package is.
        int count = inventory.CountItem(job.packageItem);
        if (count > 0) { packageSeen = true; return; }
        if (!packageSeen && ++missingChecks < 6) return; // never seen yet: give the inventory a few more checks
        Debug.Log("[Delivery] Package missing: item=" + (job.packageItem != null ? job.packageItem.name : "null") + " count=" + count + " seen=" + packageSeen);
        CancelJob("You lost the package. The delivery is over.");
    }

    void CancelJob(string message)
    {
        job = new Job();
        Toast(message);
    }

    // kind 1 = deliver here, kind 2 = ask for a job (or see the current one)
    DeliveryNpc FindNpcInRange(out int kind)
    {
        kind = 0;
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if ((gear != null && gear.IsOpen) || PlacementSystem.IsPlacing || RespawnSystem.IsDeathScreenOpen || ShopWindow.IsOpen || LootWindow.IsOpen || MapSystem.IsMapOpen) return null;

        // Loot bags use E too: they win
        if (LootBag.AnyInRange(player.position)) return null;

        DeliveryNpc best = null;
        float bestDistance = float.MaxValue;
        foreach (DeliveryNpc npc in DeliveryNpc.All)
        {
            if (npc == null) continue;
            float d = WorldUtil.FlatDistance(player.position, npc.transform.position);
            if (d > npc.interactRange || d >= bestDistance) continue;

            int k = 0;
            if (job.active && npc.receivesPackages && npc.SaveKey == job.targetKey) k = 1;
            else if (npc.givesPackages) k = 2;
            if (k == 0) continue;

            bestDistance = d;
            best = npc;
            kind = k;
        }
        return best;
    }

    void SetPrompt(DeliveryNpc npc, int kind)
    {
        if (npc != promptNpc || kind != promptKind || promptString == null)
        {
            promptNpc = npc;
            promptKind = kind;
            promptString = kind == 1 ? $"[{npc.TalkKey}] Deliver package to {npc.npcName}" : $"[{npc.TalkKey}] Talk to {npc.npcName}"; // built when it changes, not every frame
        }
        promptText.text = promptString;

        // A merchant's own "[E] Trade" is on line 0: this one goes under it
        int line = npc.IsMerchant ? 1 : 0;
        if (line != promptLine)
        {
            promptLine = line;
            LootWindow.PlaceInCorner(promptText.rectTransform, promptText, line);
        }
    }

    // ---------- Talking ----------

    void Talk(DeliveryNpc npc, int kind)
    {
        dialogNpc = npc;
        npc.FaceTowards(player.position);

        if (kind == 1)
        {
            Deliver(npc);
            return;
        }

        if (job.active)
        {
            dialog = Dialog.Active;
            DeliveryNpc target = TargetNpc();
            Show(npc.npcName, Fill(npc.reminderText, target, job.reward, job.cargo), $"[{npc.TalkKey}] Abandon the delivery     [Esc] Close");
            return;
        }

        if (!MakeOffer(npc))
        {
            dialog = Dialog.NoJob;
            Show(npc.npcName, npc.noJobText, "[Esc] Close");
            return;
        }
        dialog = Dialog.Offer;
        Show(npc.npcName, Fill(npc.offerText, offerTarget, offerReward, offerCargo), $"[{npc.TalkKey}] Accept     [Esc] Decline");
    }

    // Picks where the package goes and how much it pays
    bool MakeOffer(DeliveryNpc giver)
    {
        if (giver.packageItem == null || giver.coinItem == null)
        {
            Debug.LogWarning($"{giver.name}: DeliveryNpc needs the Package Item and the Coin Item.");
            return false;
        }

        var candidates = new List<DeliveryNpc>();
        DeliveryNpc farthest = null;
        float farthestDistance = 0f;
        foreach (DeliveryNpc npc in DeliveryNpc.All)
        {
            if (npc == null || npc == giver || !npc.receivesPackages) continue;
            float d = WorldUtil.FlatDistance(giver.transform.position, npc.transform.position);
            if (d > farthestDistance) { farthestDistance = d; farthest = npc; }
            if (d < giver.minDistance || (giver.maxDistance > 0f && d > giver.maxDistance)) continue;
            candidates.Add(npc);
        }

        // Nobody in the wanted range: use the farthest one (as long as it is not right next door)
        if (candidates.Count == 0 && farthest != null && farthestDistance >= 10f) candidates.Add(farthest);
        if (candidates.Count == 0) return false;

        offerTarget = candidates[Random.Range(0, candidates.Count)];
        float distance = WorldUtil.FlatDistance(giver.transform.position, offerTarget.transform.position);
        float reward = (giver.baseReward + giver.rewardPerMeter * distance) * Random.Range(1f - giver.rewardVariation, 1f + giver.rewardVariation);
        offerReward = Mathf.Max(1, Mathf.RoundToInt(reward));
        offerCargo = giver.cargoNames != null && giver.cargoNames.Length > 0 ? giver.cargoNames[Random.Range(0, giver.cargoNames.Length)] : "a package";
        return true;
    }

    void UpdateDialog()
    {
        bool tooFar = dialogNpc == null || WorldUtil.FlatDistance(player.position, dialogNpc.transform.position) > dialogNpc.interactRange + 1.5f;
        if (tooFar || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab))
        {
            CloseDialog();
            return;
        }
        if (!Input.GetKeyDown(dialogNpc.TalkKey)) return;

        switch (dialog)
        {
            case Dialog.Offer: Accept(); break;
            case Dialog.Active: Abandon(); break;
            default: CloseDialog(); break;
        }
    }

    void Accept()
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (inventory == null || offerTarget == null || dialogNpc == null) { CloseDialog(); return; }

        if (!inventory.AddItem(dialogNpc.packageItem))
        {
            ShowMessage("Your inventory is full!");
            return;
        }

        job = new Job
        {
            active = true, giverKey = dialogNpc.SaveKey, targetKey = offerTarget.SaveKey, cargo = offerCargo,
            reward = offerReward, packageItem = dialogNpc.packageItem, coinItem = dialogNpc.coinItem
        };
        graceUntil = Time.time + lostPackageGrace; packageSeen = false; missingChecks = 0;
        Toast($"Take {offerCargo} to {offerTarget.npcName}");
        CloseDialog();
    }

    void Abandon()
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (inventory != null && job.packageItem != null) inventory.RemoveItem(job.packageItem, 1);
        job = new Job();
        Toast("You gave up the delivery.");
        CloseDialog();
    }

    void Deliver(DeliveryNpc npc)
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (inventory == null || !job.active) return;

        if (inventory.CountItem(job.packageItem) <= 0)
        {
            Toast("You don't have the package.");
            return;
        }

        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        int pay = Mathf.RoundToInt(job.reward * (gear != null ? gear.MoneyMultiplier : 1f)); // the Double Money backpack perk counts
        int added = inventory.AddItem(job.coinItem, pay);
        if (added < pay)
        {
            inventory.RemoveItem(job.coinItem, added); // not enough room for the coins: undo, and try again after making room
            Toast("Make some room in your inventory first.");
            return;
        }

        inventory.RemoveItem(job.packageItem, 1);
        completed++;
        string text = Fill(npc.thanksText, npc, pay, job.cargo);
        job = new Job();

        dialog = Dialog.Thanks;
        dialogNpc = npc;
        Show(npc.npcName, text, $"[{npc.TalkKey}] Close");
    }

    string Fill(string template, DeliveryNpc target, int reward, string cargo)
    {
        float distance = target != null && dialogNpc != null ? WorldUtil.FlatDistance(player.position, target.transform.position) : 0f;
        return template
            .Replace("{cargo}", cargo ?? "a package")
            .Replace("{target}", target != null ? target.npcName : "someone")
            .Replace("{distance}", Mathf.RoundToInt(distance / 5f) * 5 + "")
            .Replace("{reward}", reward.ToString())
            .Replace("{npc}", dialogNpc != null ? dialogNpc.npcName : "")
            .Replace("{done}", completed.ToString());
    }

    // ---------- Show / hide ----------

    void Show(string title, string body, string footer)
    {
        promptText.gameObject.SetActive(false);
        titleText.text = title;
        bodyText.text = body;
        footerText.text = footer;
        messageText.text = "";
        panel.SetActive(true);
    }

    void CloseDialog()
    {
        dialog = Dialog.None;
        dialogNpc = null;
        if (panel != null) panel.SetActive(false);
    }

    void ShowMessage(string text)
    {
        messageText.text = text;
        messageUntil = Time.unscaledTime + 2.5f;
    }

    void Toast(string text)
    {
        toastText.text = text;
        toastText.gameObject.SetActive(true);
        toastUntil = Time.unscaledTime + 4f;
    }

    // "Delivery: a small crate to Hana - 230 m NE" at the top of the screen (the text is only rebuilt when the distance or direction changes)
    void UpdateHud()
    {
        DeliveryNpc target = showHud && job.active ? TargetNpc() : null;
        hudBox.SetActive(target != null);
        if (target == null)
        {
            hudDistance = -1;
            return;
        }

        Vector3 offset = target.transform.position - player.position;
        offset.y = 0f;
        int distance = Mathf.RoundToInt(offset.magnitude / 5f) * 5;
        int direction = CompassIndex(offset);
        if (distance == hudDistance && direction == hudDirection && target == hudTarget) return;

        hudDistance = distance;
        hudDirection = direction;
        hudTarget = target;
        hudText.text = $"Delivery: {job.cargo} to {target.npcName}  -  {distance} m {CompassNames[direction]}";
    }

    static readonly string[] CompassNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    // North is +Z (the same as the map)
    static int CompassIndex(Vector3 direction)
    {
        float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        return Mathf.RoundToInt(((angle % 360f) + 360f) % 360f / 45f) % 8;
    }

    // ---------- UI ----------

    void BuildUI()
    {
        LootWindow style = LootWindow.Instance;
        font = LootWindow.GameFont;
        textColor = UIStyle.Text;
        detailColor = UIStyle.Tan;
        titleColor = UIStyle.Gold;
        Sprite panelSprite = UIStyle.PanelSprite;
        Color panelColor = UIStyle.PanelColor;

        GameObject canvasObject = UIKit.CreateCanvas("DeliveryCanvas", transform, 58);

        // "[E] Talk to ..." (text only)
        promptText = UIKit.CreateText("DeliveryPrompt", canvasObject.transform, "", style != null ? style.promptFontSize : 18f, textColor, font, TextAlignmentOptions.Center);
        promptText.gameObject.SetActive(false);

        // Dialog window (bottom center)
        panel = UIKit.CreateImage("DeliveryPanel", canvasObject.transform, panelColor, panelSprite);
        LootWindow.SkinWindow(panel.GetComponent<Image>(), style != null ? style.shopWindowSprite : null);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 110f);
        panelRect.sizeDelta = new Vector2(860f, 300f);

        titleText = UIKit.CreateText("Title", panel.transform, "", 34f, titleColor, font, TextAlignmentOptions.Left);
        UIKit.SetRect(titleText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(30f, -66f), new Vector2(-30f, -16f));

        bodyText = UIKit.CreateText("Body", panel.transform, "", 27f, textColor, font, TextAlignmentOptions.TopLeft);
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        UIKit.SetRect(bodyText.rectTransform, Vector2.zero, Vector2.one, new Vector2(30f, 100f), new Vector2(-30f, -76f));

        messageText = UIKit.CreateText("Message", panel.transform, "", 22f, new Color(1f, 0.8f, 0.45f), font, TextAlignmentOptions.Center);
        UIKit.SetRect(messageText.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(30f, 58f), new Vector2(-30f, 92f));

        footerText = UIKit.CreateText("Footer", panel.transform, "", 22f, new Color(0.78f, 0.74f, 0.66f), font, TextAlignmentOptions.Center);
        UIKit.SetRect(footerText.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(30f, 16f), new Vector2(-30f, 52f));
        panel.SetActive(false);

        // Message (top center, a few seconds)
        toastText = UIKit.CreateText("DeliveryToast", canvasObject.transform, "", 30f, titleColor, font, TextAlignmentOptions.Center);
        RectTransform toastRect = toastText.rectTransform;
        toastRect.anchorMin = toastRect.anchorMax = toastRect.pivot = new Vector2(0.5f, 1f);
        toastRect.anchoredPosition = new Vector2(0f, -110f);
        toastRect.sizeDelta = new Vector2(1200f, 50f);
        toastText.gameObject.SetActive(false);

        // Delivery line (top center)
        hudBox = UIKit.CreateImage("DeliveryHud", canvasObject.transform, new Color(0f, 0f, 0f, hudBackground ? 0.45f : 0f));
        hudBox.GetComponent<Image>().raycastTarget = false;
        RectTransform hudRect = hudBox.GetComponent<RectTransform>();
        hudRect.anchorMin = hudRect.anchorMax = hudRect.pivot = new Vector2(0.5f, 1f);
        hudRect.anchoredPosition = new Vector2(0f, -40f);
        hudRect.sizeDelta = new Vector2(760f, 44f);
        hudText = UIKit.CreateText("HudText", hudBox.transform, "", 22f, titleColor, font, TextAlignmentOptions.Center);
        if (!hudBackground)
        {
            // No box: a thin dark outline keeps the text readable on any background
            hudText.outlineWidth = 0.25f;
            hudText.outlineColor = new Color32(0, 0, 0, 255);
        }
        UIKit.SetRect(hudText.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f));
        hudBox.SetActive(false);
    }
}
