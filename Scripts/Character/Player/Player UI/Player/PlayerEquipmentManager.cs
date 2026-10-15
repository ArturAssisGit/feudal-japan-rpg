using System.Collections.Generic;
using UnityEngine;

public class PlayerEquipmentManager : MonoBehaviour
{
    // The manager of the player you control. Players can survive scene loads (DontDestroyOnLoad), so an old, switched-off
    // or destroyed manager is never used: the newest one wins, and a broken one is replaced by the active one.
    private static PlayerEquipmentManager instance;
    public static PlayerEquipmentManager Instance
    {
        get
        {
            if ((instance == null || !instance.isActiveAndEnabled) && Time.unscaledTime >= nextSearch)
            {
                PlayerEquipmentManager best = FindBest();
                if (best != null) instance = best;
                else nextSearch = Time.unscaledTime + 0.5f; // none in this scene (menu): don't search every frame
            }
            return instance;
        }
        set => instance = value;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; nextSearch = 0f; }
    private static float nextSearch;

    static PlayerEquipmentManager FindBest()
    {
        PlayerEquipmentManager[] all = FindObjectsByType<PlayerEquipmentManager>();
        if (all.Length == 0) return null;
        GameObject player = PlayerRef.Object;
        if (player != null)
            foreach (PlayerEquipmentManager m in all)
                if (m.transform.IsChildOf(player.transform) || (m.rightHandWeaponHolder != null && m.rightHandWeaponHolder.IsChildOf(player.transform))) return m;
        foreach (PlayerEquipmentManager m in all) if (m.weaponSlotUI != null) return m;
        return all[0];
    }

    [Header("UI Panels")]
    public GameObject equipmentPanel;
    public GameObject backpackGrid;
    [Tooltip("Camera that films the character for the inventory. It only renders while the inventory is open (empty = found automatically)")]
    public Camera inventoryCamera;

    [Header("Equip Points")]
    public Transform rightHandWeaponHolder;
    public Transform sheathHolder;

    [Header("UI Gear Slots")]
    public PlayerItemSlot weaponSlotUI;

    [Header("Armor System")]
    public PlayerItemSlot chestSlotUI;
    public SkinnedMeshRenderer nakedBodyMesh;

    [Header("Helmet System")]
    public PlayerItemSlot helmetSlotUI;
    public Transform headHolder;

    [Header("Bow System")]
    public PlayerItemSlot bowSlotUI;
    public Transform leftHandBowHolder;
    public Transform bowSheathHolder;

    [Header("Backpack System")]
    public PlayerItemSlot backpackSlotUI;
    public Transform backHolder;
    [Tooltip("Drops the equipped backpack on the ground (pick it up again with E)")] public KeyCode dropBackpackKey = KeyCode.R;

    [Header("Lantern System")]
    [Tooltip("UI slot for the lantern (empty = finds the object called 'LanternItemSlot')")] public PlayerItemSlot lanternSlotUI;
    [Tooltip("Point on the waist where the lantern hangs (empty = the hips bone)")] public Transform lanternHolder;

    [Header("Stats Readout")]
    [Tooltip("Tiny text in the corner of the armor slot with Attack and Defense (empty = created automatically)")] public TMPro.TextMeshProUGUI statsText;
    public float statsFontSize = 14f;
    [Tooltip("Moves the Attack/Defense text, in pixels, from its place on the armor slot (X right, Y up). Change it with the inventory open to see it move")] public Vector2 statsOffset = Vector2.zero;

    private GameObject currentLanternObject;
    private Light lanternLight;      // small glow on the lantern
    private bool lanternLightCreated;
    private Light lanternAreaLight;  // main light that follows the player and lights the area
    private float lanternLit = -1f;

    private GameObject currentChestObject, currentHelmetObject, currentBackpackObject, currentBowObject;
    private PlayerItemData currentBowData;
    private GameObject currentWeaponObject;
    private PlayerItemData currentWeaponData;
    private bool isOpen;

    public bool HasWeaponEquipped => currentWeaponObject != null;
    public bool HasBowEquipped => currentBowObject != null;
    public bool HasBackpackEquipped => backpackSlotUI != null && backpackSlotUI.itemData != null;
    public bool HasBackpackPerk(BackpackPerk perk) => HasBackpackEquipped && backpackSlotUI.itemData.backpackPerks != null && backpackSlotUI.itemData.backpackPerks.Contains(perk);
    public static bool PerkActive(BackpackPerk perk) => Instance != null && Instance.HasBackpackPerk(perk);
    // Multiply any money the player gains by this
    public float MoneyMultiplier => HasBackpackPerk(BackpackPerk.DoubleMoney) ? 2f : 1f;
    public float CurrentBowDamage => currentBowData != null ? currentBowData.weaponDamage : 0f;
    public Transform BowTransform => currentBowObject != null ? currentBowObject.transform : null;
    public GameObject CurrentBackpackObject => currentBackpackObject;
    public bool IsOpen => isOpen || LootWindow.IsOpen || RespawnSystem.IsDeathScreenOpen || ShopWindow.IsOpen || StableWindow.IsOpen || CraftingWindow.IsOpen || SleepSystem.IsSleeping || MultiplayerSession.PanelOpen || PauseMenu.IsOpen;
    public bool IsInventoryOpen => isOpen;
    public float CurrentWeaponDamage => currentWeaponData != null ? currentWeaponData.weaponDamage : 0f;
    public float TotalDefense => SlotDefense(chestSlotUI) + SlotDefense(helmetSlotUI);

    // Oni Stone perks: true when any equipped item is possessed by this demon
    public bool HasSpirit(OniSpirit spirit) => spirit != OniSpirit.None &&
        (SlotSpirit(weaponSlotUI) == spirit || SlotSpirit(bowSlotUI) == spirit || SlotSpirit(chestSlotUI) == spirit || SlotSpirit(helmetSlotUI) == spirit);

    static OniSpirit SlotSpirit(PlayerItemSlot slot) => slot != null && slot.itemData != null ? slot.itemData.oniSpirit : OniSpirit.None;

    float SlotDefense(PlayerItemSlot slot) => slot != null && slot.itemData != null ? slot.itemData.armorDefense : 0f;

    void Awake() => instance = this; // the newest (the one of the scene just loaded) wins

    void OnEnable() { if (instance == null || !instance.isActiveAndEnabled) instance = this; }

    void OnDestroy() { if (instance == this) instance = null; }

    void Start()
    {
        isOpen = false;
        if (equipmentPanel != null) equipmentPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        FindInventoryCamera();
        UpdateInventoryCamera();
        FindLanternSlot();
    }

    // ---------- Lantern ----------

    void FindLanternSlot()
    {
        if (lanternSlotUI != null) return;
        foreach (PlayerItemSlot slot in FindObjectsByType<PlayerItemSlot>(FindObjectsInactive.Include))
            if (slot.name == "LanternItemSlot") { lanternSlotUI = slot; return; }
    }

    // The waist point you created, or the hips bone of the player
    Transform LanternHolder()
    {
        if (lanternHolder != null) return lanternHolder;
        GameObject player = PlayerRef.Object;
        if (player == null) return null;
        foreach (Transform t in player.GetComponentsInChildren<Transform>(true))
            if (t.name.EndsWith("Hips")) return t;
        return player.transform;
    }

    void EquipLantern(PlayerItemSlot sourceSlot, PlayerItemData itemData)
    {
        FindLanternSlot();
        if (lanternSlotUI == null)
        {
            Debug.LogWarning("No Lantern Slot UI assigned in PlayerEquipmentManager (or no object called 'LanternItemSlot')!");
            return;
        }

        EquipAttachment(sourceSlot, itemData, lanternSlotUI, LanternHolder(), ref currentLanternObject);
        lanternLight = currentLanternObject != null ? SetupLanternLight(currentLanternObject, itemData, out lanternLightCreated) : null;
        CreateLanternAreaLight();
        lanternLit = -1f;
    }

    // The light that really lights the world: follows the player from above so the body isn't burned by it
    void CreateLanternAreaLight()
    {
        RemoveLanternAreaLight();
        GameObject player = PlayerRef.Object;
        if (player == null) return;
        GameObject go = new GameObject("Lantern Area Light");
        go.transform.SetParent(player.transform, false);
        lanternAreaLight = go.AddComponent<Light>();
        SetupPointLight(lanternAreaLight);
    }

    // 360-degree point light without shadows, like the bonfire
    static void SetupPointLight(Light light)
    {
        light.type = LightType.Point;
        light.shadows = LightShadows.None;
        light.renderMode = LightRenderMode.ForcePixel;
    }

    void RemoveLanternAreaLight()
    {
        if (lanternAreaLight != null) Destroy(lanternAreaLight.gameObject);
        lanternAreaLight = null;
    }

    // Uses the Light inside the lantern prefab if it has one, otherwise creates one
    static Light SetupLanternLight(GameObject lantern, PlayerItemData data, out bool created)
    {
        Light light = lantern.GetComponentInChildren<Light>(true);
        created = light == null;
        if (light == null)
        {
            GameObject go = new GameObject("Lantern Light");
            go.transform.SetParent(lantern.transform, false);
            go.transform.localPosition = data.lanternLightOffset;
            light = go.AddComponent<Light>();
        }
        SetupPointLight(light);
        return light;
    }

    // Lit at night (or always), with a small flicker
    void UpdateLantern()
    {
        if ((lanternLight == null && lanternAreaLight == null) || lanternSlotUI == null || lanternSlotUI.itemData == null) return;
        PlayerItemData data = lanternSlotUI.itemData;

        float target = data.lanternOnlyAtNight ? DayNightCycle.NightAmount() : 1f;
        lanternLit = lanternLit < 0f ? target : Mathf.MoveTowards(lanternLit, target, Time.deltaTime * 0.5f);
        float noise = Mathf.PerlinNoise(Time.time * 8f, 0.37f);
        float flame = lanternLit * (1f - data.lanternFlicker * 0.5f + data.lanternFlicker * noise);

        // Main light: above the player, lights the whole area
        if (lanternAreaLight != null)
        {
            Transform player = lanternAreaLight.transform.parent;
            if (player != null) lanternAreaLight.transform.position = player.position + player.rotation * data.lanternAreaLightOffset;
            lanternAreaLight.enabled = flame > 0.01f;
            lanternAreaLight.color = data.lanternLightColor;
            lanternAreaLight.range = data.lanternRange;
            lanternAreaLight.intensity = data.lanternIntensity * flame;
        }

        // Small glow on the lantern itself (just above it in world space)
        if (lanternLight != null)
        {
            if (lanternLightCreated && currentLanternObject != null)
                lanternLight.transform.position = currentLanternObject.transform.position + data.lanternLightOffset;
            lanternLight.enabled = flame > 0.01f && data.lanternGlowIntensity > 0f;
            lanternLight.color = data.lanternLightColor;
            lanternLight.range = data.lanternGlowRange;
            lanternLight.intensity = data.lanternGlowIntensity * flame;
        }
    }

    // The camera that renders into the RawImage shown in the inventory
    void FindInventoryCamera()
    {
        if (inventoryCamera != null) return;
        foreach (UnityEngine.UI.RawImage image in FindObjectsByType<UnityEngine.UI.RawImage>(FindObjectsInactive.Include))
        {
            if (!(image.texture is RenderTexture texture)) continue;
            foreach (Camera cam in FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                if (cam.targetTexture != texture) continue;
                inventoryCamera = cam;
                return;
            }
        }
    }

    // Rendering the scene twice is expensive: only while the inventory is open
    void UpdateInventoryCamera() { if (inventoryCamera != null) inventoryCamera.enabled = isOpen; }

    // ---------- Attack / Defense numbers (small, in the armor slot) ----------

    void CreateStatsText()
    {
        if (statsText != null || chestSlotUI == null) return;
        GameObject go = new GameObject("StatsText", typeof(RectTransform));
        go.transform.SetParent(chestSlotUI.transform, false);
        statsText = go.AddComponent<TMPro.TextMeshProUGUI>();
        if (LootWindow.GameFont != null) statsText.font = LootWindow.GameFont;
        statsText.fontSize = statsFontSize;
        statsText.fontStyle = TMPro.FontStyles.Bold;
        statsText.alignment = TMPro.TextAlignmentOptions.BottomLeft;
        statsText.color = Color.white;
        statsText.raycastTarget = false;
        statsText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        statsText.overflowMode = TMPro.TextOverflowModes.Overflow;
        RectTransform rect = statsText.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(3f, 2f);
        rect.offsetMax = new Vector2(-3f, -2f);
    }

    void ApplyStatsOffset()
    {
        if (statsText == null) return;
        RectTransform rect = statsText.rectTransform;
        rect.offsetMin = new Vector2(3f, 2f) + statsOffset;
        rect.offsetMax = new Vector2(-3f, -2f) + statsOffset;
        statsText.fontSize = statsFontSize;
    }

    void UpdateStatsText()
    {
        CreateStatsText();
        if (statsText == null) return;
        ApplyStatsOffset();
        float attack = CurrentWeaponDamage > 0f ? CurrentWeaponDamage : CurrentBowDamage;
        statsText.text = $"ATK {Mathf.RoundToInt(attack)}\nDEF {Mathf.RoundToInt(TotalDefense)}";
    }

    void Update()
    {
        if (isOpen) UpdateStatsText();
        UpdateLantern();
        if (Input.GetKeyDown(KeyCode.I) && !LootWindow.IsOpen && !RespawnSystem.IsDeathScreenOpen && !ShopWindow.IsOpen && !StableWindow.IsOpen && !CraftingWindow.IsOpen && !SleepSystem.IsSleeping && !MultiplayerSession.PanelOpen && !PauseMenu.IsOpen && !ScreenFade.IsActive) ToggleEquipment();
        if (Input.GetKeyDown(dropBackpackKey) && !IsOpen && HasBackpackEquipped && !ScreenFade.IsActive) DropBackpack();
    }

    public void ToggleEquipment()
    {
        isOpen = !isOpen;
        if (equipmentPanel != null) equipmentPanel.SetActive(isOpen);
        UpdateInventoryCamera();
        Cursor.lockState = isOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = isOpen;
    }

    public bool IsGearSlot(PlayerItemSlot slot) => slot != null && (slot == weaponSlotUI || slot == chestSlotUI || slot == helmetSlotUI || slot == backpackSlotUI || slot == bowSlotUI || (slot == lanternSlotUI && slot != null));
    public bool IsBackpackSlot(PlayerItemSlot slot) => slot != null && slot == backpackSlotUI;

    public void EquipItem(PlayerItemSlot sourceSlot)
    {
        if (sourceSlot == null || sourceSlot.itemData == null) return;

        PlayerItemData itemData = sourceSlot.itemData;
        if (IsGearSlot(sourceSlot))
        {
            Unequip(sourceSlot, itemData);
            return;
        }

        // Upgrade stones are only used at a crafting table (even if their Type was left on Weapon)
        if (itemData.IsUpgradeStone || itemData.type == ItemType.Stone) return;

        switch (itemData.type)
        {
            case ItemType.Weapon: EquipWeapon(sourceSlot, itemData); PlayEquipSound(); break;
            case ItemType.Chest: EquipChest(sourceSlot, itemData); PlayEquipSound(); break;
            case ItemType.Helmet: EquipAttachment(sourceSlot, itemData, helmetSlotUI, headHolder, ref currentHelmetObject); PlayEquipSound(); break;
            case ItemType.Bow: EquipBow(sourceSlot, itemData); PlayEquipSound(); break;
            case ItemType.Backpack:
                // (old saves may still have a backpack in the inventory) it goes on your back, the one you wear disappears
                sourceSlot.RemoveOne();
                EquipNewBackpack(itemData);
                break;
            case ItemType.Lantern: EquipLantern(sourceSlot, itemData); PlayEquipSound(); break;
            case ItemType.Consumable: Consume(sourceSlot, itemData); break;
            case ItemType.Place:
                if (isOpen) ToggleEquipment(); // close the inventory to see where it goes
                PlacementSystem.Begin(sourceSlot, itemData);
                break;
        }
    }

    // Quick slots: eats/drinks one of this item from the bags. False when there is none left
    public bool ConsumeFromInventory(PlayerItemData item)
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (item == null || inventory == null) return false;
        foreach (PlayerItemSlot slot in inventory.UsableSlots())
        {
            if (slot.itemData != item) continue;
            Consume(slot, item);
            return true;
        }
        return false;
    }

    // Eats/drinks the item: applies its effects and removes it from the slot
    void Consume(PlayerItemSlot slot, PlayerItemData itemData)
    {
        playerStats = PlayerStatsNow;
        if (playerStats == null)
        {
            Debug.LogWarning("No PlayerStats found to consume " + itemData.itemName);
            return;
        }

        if (itemData.foodRestore > 0f) playerStats.Eat(itemData.foodRestore);
        if (itemData.waterRestore > 0f) playerStats.Drink(itemData.waterRestore);
        if (itemData.healthRestore > 0f) playerStats.Heal(itemData.healthRestore);

        slot.RemoveOne();
        bool isDrink = itemData.waterRestore > itemData.foodRestore && itemData.waterRestore > itemData.healthRestore;
        SoundManager.Play(isDrink ? Sfx.Drink : Sfx.UseItem, SoundPosition());
    }

    private PlayerStats playerStats;
    private PlayerStats PlayerStatsNow
    {
        get
        {
            if (playerStats == null || !playerStats.isActiveAndEnabled) playerStats = PlayerRef.Stats;
            return playerStats;
        }
    }

    void PlayEquipSound() => SoundManager.Play(Sfx.Equip, SoundPosition());

    // UI sounds play at the camera so they are always at full volume
    Vector3 SoundPosition() => Camera.main != null ? Camera.main.transform.position : transform.position;

    void RefreshBackpackGrid() { if (PlayerInventoryManager.Instance != null) PlayerInventoryManager.Instance.RefreshBackpackGrid(); }

    void Unequip(PlayerItemSlot slot, PlayerItemData itemData)
    {
        if (slot == backpackSlotUI)
        {
            // The backpack never goes into the inventory: it can only be put on the ground
            CenterMessage.Show($"The backpack can't go in the inventory. Press {dropBackpackKey} to put it on the ground");
            return;
        }
        if (!PlayerInventoryManager.Instance.AddItem(itemData))
        {
            Debug.Log("Inventory is full! Cannot unequip right now.");
            return;
        }
        ClearGearSlot(slot);
        PlayEquipSound();
    }

    // Empties an equipment slot and removes the item from the character (unequip or drop)
    public void ClearGearSlot(PlayerItemSlot slot)
    {
        if (slot == null || slot.itemData == null) return;
        PlayerItemData itemData = slot.itemData;
        slot.ClearSlot();

        string label = null;
        if (slot == weaponSlotUI)
        {
            ClearObject(ref currentWeaponObject);
            currentWeaponData = null;
            label = "weapon";
        }
        else if (slot == chestSlotUI)
        {
            ClearObject(ref currentChestObject);
            if (nakedBodyMesh != null) nakedBodyMesh.gameObject.SetActive(true);
            label = "armor";
        }
        else if (slot == helmetSlotUI)
        {
            ClearObject(ref currentHelmetObject);
            label = "helmet";
        }
        else if (slot == bowSlotUI)
        {
            ClearObject(ref currentBowObject);
            currentBowData = null;
            label = "bow";
        }
        else if (slot == backpackSlotUI)
        {
            ClearObject(ref currentBackpackObject);
            RefreshBackpackGrid();
            label = "backpack";
        }
        else if (slot == lanternSlotUI)
        {
            ClearObject(ref currentLanternObject);
            lanternLight = null;
            RemoveLanternAreaLight();
            label = "lantern";
        }
        if (label != null) Debug.Log("Unequipped " + label + ": " + itemData.itemName);
    }

    void EquipWeapon(PlayerItemSlot sourceSlot, PlayerItemData itemData)
    {
        sourceSlot.SetItem(weaponSlotUI.itemData);
        weaponSlotUI.SetItem(itemData);

        // Was the old sword in the hand (combat mode)? Then the new one goes to the hand too
        bool wasInHand = currentWeaponObject != null && rightHandWeaponHolder != null && currentWeaponObject.transform.parent == rightHandWeaponHolder;
        if (!wasInHand)
        {
            PlayerCombat combat = PlayerCombatOfPlayer();
            wasInHand = combat != null && combat.IsArmed && !combat.IsBowDrawn;
        }

        ClearObject(ref currentWeaponObject);
        currentWeaponData = null;
        if (itemData.itemPrefab == null || rightHandWeaponHolder == null) return;

        currentWeaponObject = Instantiate(itemData.itemPrefab, rightHandWeaponHolder);
        currentWeaponData = itemData;

        if (currentWeaponObject.TryGetComponent(out Rigidbody rb)) rb.isKinematic = true;
        foreach (Collider col in currentWeaponObject.GetComponents<Collider>()) col.enabled = false;
        foreach (ItemPickup p in currentWeaponObject.GetComponentsInChildren<ItemPickup>()) Destroy(p);

        if (wasInHand) DrawWeapon();
        else SheathWeapon();
        Debug.Log("Equipped weapon: " + itemData.itemName);
    }

    static PlayerCombat PlayerCombatOfPlayer() => PlayerRef.Get<PlayerCombat>();

    void EquipBow(PlayerItemSlot sourceSlot, PlayerItemData itemData)
    {
        if (bowSlotUI == null)
        {
            Debug.LogWarning("No Bow Slot UI assigned in PlayerEquipmentManager!");
            return;
        }

        sourceSlot.SetItem(bowSlotUI.itemData);
        bowSlotUI.SetItem(itemData);

        ClearObject(ref currentBowObject);
        currentBowData = null;
        Transform holder = bowSheathHolder != null ? bowSheathHolder : leftHandBowHolder;
        if (itemData.itemPrefab == null || holder == null) return;

        currentBowObject = Instantiate(itemData.itemPrefab, holder);
        currentBowData = itemData;
        StripPhysics(currentBowObject);

        SheathBow();
        Debug.Log("Equipped bow: " + itemData.itemName);
    }

    public void DrawBow() => PlaceHeldItem(currentBowObject, currentBowData, leftHandBowHolder, false);
    public void SheathBow() => PlaceHeldItem(currentBowObject, currentBowData, bowSheathHolder, true);

    void EquipChest(PlayerItemSlot sourceSlot, PlayerItemData itemData)
    {
        sourceSlot.SetItem(chestSlotUI.itemData);
        chestSlotUI.SetItem(itemData);

        if (itemData.itemPrefab == null || !EnsureBodyMesh()) { ClearObject(ref currentChestObject); return; }
        ClearObject(ref currentChestObject);

        currentChestObject = SpawnSkinned(itemData.itemPrefab);
        nakedBodyMesh.gameObject.SetActive(false);
        Debug.Log("Equipped armor: " + itemData.itemName);
    }

    void EquipAttachment(PlayerItemSlot sourceSlot, PlayerItemData itemData, PlayerItemSlot gearSlot, Transform holder, ref GameObject currentObject)
    {
        if (gearSlot == null)
        {
            Debug.LogWarning($"No UI slot assigned for {itemData.type} in PlayerEquipmentManager!");
            return;
        }

        sourceSlot.SetItem(gearSlot.itemData);
        gearSlot.SetItem(itemData);

        ClearObject(ref currentObject);
        currentObject = SpawnAttachment(itemData, holder);
        if (currentObject != null) Debug.Log($"Equipped {itemData.type}: {itemData.itemName}");
    }

    GameObject SpawnAttachment(PlayerItemData itemData, Transform holder)
    {
        if (itemData.itemPrefab == null) return null;

        bool isSkinned = itemData.itemPrefab.GetComponentInChildren<SkinnedMeshRenderer>() != null;
        if (isSkinned && EnsureBodyMesh()) return SpawnSkinned(itemData.itemPrefab);

        if (holder == null)
        {
            Debug.LogWarning($"{itemData.type} needs a holder Transform assigned in PlayerEquipmentManager!");
            return null;
        }

        GameObject obj = Instantiate(itemData.itemPrefab, holder);
        StripPhysics(obj);
        PlaceHeldItem(obj, itemData, holder, false);
        return obj;
    }

    // ---------- Backpack drop / pick up (Outward style) ----------

    public void DropBackpack()
    {
        if (!HasBackpackEquipped) return;

        PlayerItemData backpack = backpackSlotUI.itemData;
        List<PlayerItemData> contents = PlayerInventoryManager.Instance != null ? PlayerInventoryManager.Instance.TakeBackpackContents() : new List<PlayerItemData>();
        // Keep the same size it has on the back
        Vector3 worldScale = currentBackpackObject != null ? currentBackpackObject.transform.lossyScale : Vector3.zero;
        backpackSlotUI.ClearSlot();
        ClearObject(ref currentBackpackObject);
        RefreshBackpackGrid();

        GameObject player = PlayerRef.Object;
        Transform p = player != null ? player.transform : transform;
        DroppedBackpack dropped = DroppedBackpack.Spawn(backpack, p.position - p.forward * 0.6f, p.rotation, worldScale);
        dropped.contents = contents;
        Debug.Log($"Dropped backpack: {backpack.itemName} ({contents.Count} items inside)");
    }

    // Equips a backpack you just got (bought, looted, crafted, found...). The one you wear is replaced and disappears;
    // the items inside it stay (every backpack has the same backpack slots)
    public bool EquipNewBackpack(PlayerItemData backpack)
    {
        if (backpack == null || backpackSlotUI == null) return false;
        PlayerItemData old = backpackSlotUI.itemData;
        ClearObject(ref currentBackpackObject);
        backpackSlotUI.SetItem(backpack);
        currentBackpackObject = SpawnAttachment(backpack, backHolder);
        RefreshBackpackGrid();
        PlayEquipSound();
        Debug.Log(old != null ? $"Backpack replaced: {old.itemName} -> {backpack.itemName}" : "Equipped backpack: " + backpack.itemName);
        return true;
    }

    // A backpack from the ground (one you dropped with R). Wearing another one? That one is left on the ground in its place
    public bool PickUpBackpack(PlayerItemData backpack, List<PlayerItemData> contents = null)
    {
        if (backpack == null || backpackSlotUI == null) return false;
        if (HasBackpackEquipped) DropBackpack();

        backpackSlotUI.SetItem(backpack);
        currentBackpackObject = SpawnAttachment(backpack, backHolder);
        RefreshBackpackGrid();
        if (PlayerInventoryManager.Instance != null) PlayerInventoryManager.Instance.RestoreBackpackContents(contents);
        Debug.Log("Picked up backpack: " + backpack.itemName);
        return true;
    }

    // Finds the player's body mesh automatically if it isn't assigned in the Inspector
    bool EnsureBodyMesh()
    {
        if (nakedBodyMesh != null) return true;

        GameObject player = PlayerRef.Object;
        if (player != null)
        {
            foreach (SkinnedMeshRenderer mesh in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (IsPartOf(mesh, currentChestObject) || IsPartOf(mesh, currentHelmetObject) || IsPartOf(mesh, currentBackpackObject)) continue;
                nakedBodyMesh = mesh;
                break;
            }
        }

        if (nakedBodyMesh == null) Debug.LogWarning("Naked Body Mesh is not assigned in PlayerEquipmentManager and the player's body mesh could not be found!");
        return nakedBodyMesh != null;
    }

    static bool IsPartOf(Component c, GameObject root) => root != null && c.transform.IsChildOf(root.transform);

    GameObject SpawnSkinned(GameObject prefab)
    {
        Transform body = nakedBodyMesh.transform;
        GameObject obj = Instantiate(prefab, body.parent);
        StripPhysics(obj);
        obj.transform.SetLocalPositionAndRotation(body.localPosition, body.localRotation);
        obj.transform.localScale = body.localScale;

        foreach (SkinnedMeshRenderer mesh in obj.GetComponentsInChildren<SkinnedMeshRenderer>()) RebindBones(mesh, obj.transform);
        return obj;
    }

    void ClearObject(ref GameObject obj)
    {
        if (obj != null) Destroy(obj);
        obj = null;
    }

    void StripPhysics(GameObject obj)
    {
        foreach (Collider c in obj.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (Rigidbody r in obj.GetComponentsInChildren<Rigidbody>()) Destroy(r);
        foreach (ItemPickup p in obj.GetComponentsInChildren<ItemPickup>()) Destroy(p);
    }

    void RebindBones(SkinnedMeshRenderer armorMesh, Transform armorRoot)
    {
        var boneMap = new Dictionary<string, Transform>();
        foreach (Transform bone in nakedBodyMesh.transform.root.GetComponentsInChildren<Transform>(true))
            if (!bone.IsChildOf(armorRoot) && !boneMap.ContainsKey(bone.name)) boneMap.Add(bone.name, bone);

        Transform[] oldBones = armorMesh.bones;
        Transform[] newBones = new Transform[oldBones.Length];
        for (int i = 0; i < oldBones.Length; i++) newBones[i] = FindMatchingBone(oldBones[i], boneMap);

        armorMesh.bones = newBones;
        armorMesh.rootBone = nakedBodyMesh.rootBone;
    }

    // Bones the player doesn't have (e.g. extra fingers) follow the closest parent bone the player does have
    static Transform FindMatchingBone(Transform bone, Dictionary<string, Transform> boneMap)
    {
        for (Transform b = bone; b != null; b = b.parent)
            if (boneMap.TryGetValue(b.name, out Transform match)) return match;
        return bone;
    }

    public void DrawWeapon() => PlaceHeldItem(currentWeaponObject, currentWeaponData, rightHandWeaponHolder, false);
    public void SheathWeapon() => PlaceHeldItem(currentWeaponObject, currentWeaponData, sheathHolder, true);

    // Moves an item to a holder with its equip or sheath offsets, then applies its equip scale
    void PlaceHeldItem(GameObject obj, PlayerItemData data, Transform holder, bool sheathed)
    {
        if (obj == null || data == null || holder == null) return;
        ApplyOffsets(obj.transform, data.itemPrefab, holder,
            sheathed ? data.sheathPositionOffset : data.equipPositionOffset,
            sheathed ? data.sheathRotationOffset : data.equipRotationOffset);
        if (data.equipScale != Vector3.zero) obj.transform.localScale = data.equipScale;
    }

    private void ApplyOffsets(Transform obj, GameObject prefab, Transform newParent, Vector3 posOffset, Vector3 rotOffset)
    {
        obj.SetParent(newParent, false);
        obj.SetLocalPositionAndRotation(posOffset, Quaternion.Euler(rotOffset));

        Vector3 parentScale = newParent.lossyScale;
        Vector3 prefabScale = prefab.transform.localScale;
        obj.localScale = new Vector3(prefabScale.x / parentScale.x, prefabScale.y / parentScale.y, prefabScale.z / parentScale.z);
    }
}
