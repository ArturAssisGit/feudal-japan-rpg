using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Save/load (player, stats, items, gear, hour, placed objects + respawn tent); New Game / Continue / Load / autosave / quick save.
// Every save is its own file in the 'Saves' folder (like The Witcher): F5 and the pause menu's Save always make a new one,
// autosaves keep only the last few. The Load window lists them all.
public class WorldSaveGameManager : MonoBehaviour
{
    public static WorldSaveGameManager instance;

    [Header("World Scene")]
    [Tooltip("Name of the game scene. It must be in File > Build Profiles > Scene List. Empty = use the index below")]
    public string worldSceneName = "Scene_Final_Map";
    [SerializeField] int worldSceneIndex = 1;
    [Tooltip("Used only if, after loading the world, there is no player at all: it is created at PlayerSpawnPoint")]
    public GameObject playerPrefab;

    [Header("New Game")]
    [Tooltip("Weapon the player starts a NEW game with, already equipped (empty = none)")] public PlayerItemData startingWeapon;
    [Tooltip("Hat / helmet the player starts a NEW game with, already equipped (empty = none)")] public PlayerItemData startingHelmet;

    [Header("Saving")]
    [Tooltip("The old single save file: moved into the Saves folder the first time the game runs")] public string saveFileName = "savegame.json";
    [Tooltip("Saves automatically every X minutes while playing (0 = off). The player can change it in Settings > Gameplay")] public float autosaveMinutes = 2f;
    [Tooltip("How many autosaves are kept (the oldest are deleted). Quick saves (F5) and saves from the pause menu are always kept")] [Min(1)] public int autosavesKept = 3;
    [Tooltip("Save the game when you close it")] public bool saveOnQuit = true;
    public KeyCode quickSaveKey = KeyCode.F5;
    [Tooltip("Shows 'Game saved' in the corner")] public bool showSaveMessage = true;
    [Tooltip("Font of the 'Game saved' message (empty = the font set in LootWindow)")] public TMP_FontAsset messageFont;
    public float messageFontSize = 30f;

    public enum SaveKind { Auto, Quick, Manual }

    // One save in the list of the Load window
    public class SaveInfo
    {
        public string path;
        public SaveKind kind;
        public DateTime time;
        public float hour = -1f;
        public string KindName => kind == SaveKind.Quick ? "Quick save" : kind == SaveKind.Manual ? "Manual save" : "Autosave";
    }

    public string SaveFolder => Path.Combine(Application.persistentDataPath, "Saves");
    public bool HasSave => LatestSavePath() != null;
    // Minutes between autosaves: the Settings value, or the Inspector one
    float AutosaveMinutes => GameSettings.AutosaveMinutes >= 0 ? GameSettings.AutosaveMinutes : autosaveMinutes;
    public bool IsLoading { get; private set; }
    // True once the world was loaded through New Game / Continue (not when you press Play straight in the map scene)
    public bool StartedFromMenu { get; private set; }

    private float nextAutosave;
    private float messageUntil;
    private TextMeshProUGUI messageText;

#if UNITY_EDITOR
    void OnValidate()
    {
        if (messageFont == null)
            messageFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Art/Fonts/Almendra-Regular SDF.asset");
        if (playerPrefab == null)
            playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Data/Prefebs/Player/Player_Prefeb.prefab");
    }
#endif

    // Makes sure there is always one, even when you press Play directly in the world scene
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null || FindAnyObjectByType<WorldSaveGameManager>() != null) return;
        new GameObject("World Save Game Manager").AddComponent<WorldSaveGameManager>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        LootWindow.SetDefaultFont(messageFont);
        if (transform.parent != null) transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        MoveOldSave();
    }

    void Start() => ResetAutosaveTimer();

    void ResetAutosaveTimer() => nextAutosave = Time.unscaledTime + Mathf.Max(0.5f, AutosaveMinutes) * 60f;

    // ---------- Menu buttons ----------

    [Header("New game")]
    [Tooltip("Seconds of fade in from black when a new game starts (0 = no fade)")] public float newGameFadeSeconds = 10f;
    [Tooltip("The sound (music, rain, everything) fades in during the same seconds")] public bool newGameFadeSound = true;
    [Tooltip("Text shown on a black screen before the fade in (empty = no intro). Use a new line for paragraphs")] [TextArea(3, 10)] public string newGameIntroText = "";
    [Tooltip("Key that skips the intro text and starts the fade in")] public KeyCode newGameIntroSkipKey = KeyCode.Space;
    [Tooltip("Small text under the intro")] public string newGameIntroHint = "Press SPACE to skip";
    [Tooltip("Size of the intro text")] public float newGameIntroFontSize = 30f;
    [Tooltip("A new game starts with the player sitting (W/A/S/D or B to stand up). Needs the SitAnimation Animator setup")] public bool newGameStartsSitting = true;
    [Tooltip("A new game starts with rain already falling")] public bool newGameStartsRaining = true;
    [Tooltip("Which rain preset the new game starts with (-1 = random). 0 = the first in the RainSystem list")] public int newGameRainPreset = 0;

    // The old saves stay in the list (Load), a new game simply starts
    public void StartNewGame()
    {
        if (IsLoading) return;
        StartCoroutine(LoadWorld(null));
    }

    // Loads the most recent save
    public void ContinueGame()
    {
        string path = LatestSavePath();
        if (path == null) { Debug.LogWarning("Continue: there is no save yet."); return; }
        LoadSave(path);
    }

    // Loads one save of the list (from the main menu or from the game)
    public void LoadSave(string path)
    {
        if (IsLoading) return;
        SaveData data = ReadFile(path);
        if (data == null) { ShowMessage("This save can't be read"); return; }
        StartCoroutine(LoadWorld(data));
    }

    // Old name kept so older buttons still work
    public IEnumerator LoadNewGame()
    {
        yield return LoadWorld(null);
    }

    IEnumerator LoadWorld(SaveData data)
    {
        IsLoading = true;
        StartedFromMenu = true;
        Time.timeScale = 1f; // loading from the pause menu
        bool fromGame = data != null && !TitleScreenManager.IsOpen; // loading a save while playing: short fade
        bool hasIntro = data == null && !string.IsNullOrWhiteSpace(newGameIntroText);
        bool fadeIn = (data == null && newGameFadeSeconds > 0f) || fromGame;
        float fadeSeconds = data == null ? newGameFadeSeconds : 1f;
        bool black = fadeIn || hasIntro;
        if (black) ScreenFade.HoldBlack(newGameFadeSound || hasIntro || fromGame); // black from the click, through the loading
        RemoveOldGameSystems();
        // Use the scene from the save, unless it's the menu we are in now
        string scene = data != null && !string.IsNullOrEmpty(data.sceneName) && data.sceneName != SceneManager.GetActiveScene().name
            ? data.sceneName : worldSceneName;

        AsyncOperation op = !string.IsNullOrEmpty(scene) ? SceneManager.LoadSceneAsync(scene) : SceneManager.LoadSceneAsync(worldSceneIndex);
        if (op == null)
        {
            Debug.LogError($"Could not load the scene '{scene}'. Add it in File > Build Profiles > Scene List.");
            IsLoading = false;
            ScreenFade.Clear();
            yield break;
        }
        while (!op.isDone) yield return null;

        // Let every script run its Start() first (PlayerStats fills health etc. there)
        yield return null;
        yield return null;

        CreateGameSystems();
        PlayerStats player = EnsurePlayer();
        if (data != null) ApplySave(data);
        else if (player != null) { MoveToSpawnPoint(player.transform); GiveStartingGear(); StartRainIfWanted(); StartSeatedIfWanted(); HorseOwnership.Restore(null); ItemPickup.Restore(null); MapSystem.ResetExplored(); }

        ResetAutosaveTimer();
        IsLoading = false;
        if (hasIntro)
        {
            ScreenFade.ShowIntro(newGameIntroText, newGameIntroHint, newGameIntroSkipKey, newGameIntroFontSize);
            while (ScreenFade.IntroShowing) yield return null;
        }
        if (fadeIn) ScreenFade.FadeIn(fadeSeconds, newGameFadeSound || fromGame);
        else if (black) ScreenFade.Clear();
    }

    void StartSeatedIfWanted()
    {
        if (newGameStartsSitting) SitAnimation.StartSeated();
    }

    void StartRainIfWanted()
    {
        if (newGameStartsRaining && RainSystem.Instance != null) RainSystem.Instance.StartRainInstant(newGameRainPreset);
    }

    // New game: the starting weapon and hat go into a free backpack slot and are equipped from there, like clicking them
    void GiveStartingGear()
    {
        PlayerInventoryManager inv = PlayerInventoryManager.Instance;
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (inv == null || gear == null || inv.inventorySlots == null) return;

        SoundManager.Muted = true; // no equip sounds at the start
        try
        {
            foreach (PlayerItemData item in new[] { startingWeapon, startingHelmet })
            {
                if (item == null) continue;
                PlayerItemSlot temp = null;
                foreach (PlayerItemSlot s in inv.inventorySlots)
                    if (s != null && s.IsEmpty) { temp = s; break; }
                if (temp == null) continue;
                temp.SetItem(item, 1);
                gear.EquipItem(temp);
                if (temp.itemData == item) temp.ClearSlot();
            }
        }
        finally { SoundManager.Muted = false; }
    }

    // ---------- Saving ----------

    void Update()
    {
        if (IsLoading) return;

        if (quickSaveKey != KeyCode.None && Input.GetKeyDown(quickSaveKey) && !PauseMenu.IsOpen && !MenuWindow.AnyOpen) SaveGame(SaveKind.Quick);

        if (AutosaveMinutes > 0f && Time.unscaledTime >= nextAutosave)
        {
            ResetAutosaveTimer();
            if (CanSave(out _) && !PauseMenu.IsOpen) SaveGame(SaveKind.Auto);
        }
    }

    void OnApplicationQuit() { if (saveOnQuit && !IsLoading && CanSave(out _)) SaveGame(SaveKind.Auto); }

    bool CanSave(out PlayerStats player)
    {
        player = FindPlayer();
        if (player == null || PlayerInventoryManager.Instance == null) return false;
        if (TitleScreenManager.IsOpen) return false; // never save while in the main menu
        return !player.IsDead && !RespawnSystem.IsDeathScreenOpen;
    }

    // Auto = autosave (only the last few are kept), Quick = F5, Manual = the Save button of the pause menu
    public bool SaveGame(SaveKind kind = SaveKind.Auto)
    {
        if (!CanSave(out PlayerStats player))
        {
            Debug.Log("Can't save right now (no player, or the player is dead).");
            return false;
        }

        var data = new SaveData
        {
            sceneName = SceneManager.GetActiveScene().name,
            savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            kind = kind.ToString(),
            playerPosition = player.transform.position,
            playerYaw = player.transform.eulerAngles.y,
            health = player.currentHealth,
            stamina = player.currentStamina,
            hunger = player.currentHunger,
            thirst = player.currentThirst,
            hour = DayNightCycle.Instance != null ? DayNightCycle.Instance.currentHour : -1f,
        };

        // Equipment
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (gear != null)
        {
            data.gear.weapon = IdIn(gear.weaponSlotUI);
            data.gear.chest = IdIn(gear.chestSlotUI);
            data.gear.helmet = IdIn(gear.helmetSlotUI);
            data.gear.bow = IdIn(gear.bowSlotUI);
            data.gear.backpack = IdIn(gear.backpackSlotUI);
            data.gear.lantern = IdIn(gear.lanternSlotUI);
            data.gear.weaponUpgrade = UpgradeIn(gear.weaponSlotUI);
            data.gear.chestUpgrade = UpgradeIn(gear.chestSlotUI);
            data.gear.helmetUpgrade = UpgradeIn(gear.helmetSlotUI);
            data.gear.bowUpgrade = UpgradeIn(gear.bowSlotUI);
        }

        // Inventory + backpack
        PlayerInventoryManager inv = PlayerInventoryManager.Instance;
        WriteSlots(inv.inventorySlots, data.inventory);
        WriteSlots(inv.backpackSlots, data.backpack);

        // Placed things
        foreach (PlacedObject p in FindObjectsByType<PlacedObject>())
        {
            TentSpawn tent = p.GetComponentInChildren<TentSpawn>();
            data.placed.Add(new PlacedData
            {
                item = p.itemId,
                position = p.transform.position,
                rotation = p.transform.rotation,
                respawnTent = tent != null && TentSpawn.Selected == tent,
            });
        }

        // Delivery in progress
        data.delivery = DeliverySystem.Capture();

        // Horses
        data.horses = HorseOwnership.Capture();

        // Items lying in the world that this character already picked up
        data.pickedUp = new List<string>(ItemPickup.PickedKeys);
        data.mapExplored = MapSystem.ExportExplored();

        // Scene loot bags/chests: what is left inside (emptied bags are gone, so they are simply missing from the list)
        data.hasLootState = true;
        foreach (LootBag bag in FindObjectsByType<LootBag>(FindObjectsInactive.Include))
        {
            if (bag.spawnedAtRuntime || bag.gameObject.scene.name == "DontDestroyOnLoad") continue;
            if (bag.chest != null && !bag.chest.lootOnlyOnce) continue; // respawning chest: full again on every load
            var state = new LootState { key = bag.SaveKey };
            foreach (PlayerItemData item in bag.items) if (item != null) state.items.Add(ItemDatabase.IdOf(item));
            data.loot.Add(state);
        }

        try
        {
            Directory.CreateDirectory(SaveFolder);
            string path = Path.Combine(SaveFolder, $"{kind.ToString().ToLowerInvariant()}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.json");
            WriteSafely(path, JsonUtility.ToJson(data, true));
            if (kind == SaveKind.Auto) DeleteOldAutosaves();
            Debug.Log("Game saved: " + path);
            ShowMessage(kind == SaveKind.Auto ? "Autosave" : "Game saved");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("Could not save the game: " + e.Message);
            return false;
        }
    }

    static string IdIn(PlayerItemSlot slot) => slot != null && slot.itemData != null ? ItemDatabase.IdOf(slot.itemData) : null;
    static string UpgradeIn(PlayerItemSlot slot) => slot != null ? ItemUpgrades.Encode(slot.itemData) : "";

    static void WriteSlots(PlayerItemSlot[] slots, List<SlotData> list)
    {
        if (slots == null) return;
        for (int i = 0; i < slots.Length; i++)
        {
            PlayerItemSlot s = slots[i];
            if (s == null || s.itemData == null) continue;
            list.Add(new SlotData { index = i, item = ItemDatabase.IdOf(s.itemData), amount = s.amount, upgrade = ItemUpgrades.Encode(s.itemData) });
        }
    }

    // ---------- Loading ----------

    // The most recent save
    public SaveData ReadSave()
    {
        string path = LatestSavePath();
        return path != null ? ReadFile(path) : null;
    }

    // Every save, newest first
    public List<SaveInfo> ListSaves()
    {
        var list = new List<SaveInfo>();
        foreach (string path in SaveFiles())
        {
            SaveData data = ReadFile(path);
            if (data == null) continue; // damaged file: not shown
            var info = new SaveInfo { path = path, time = File.GetLastWriteTime(path), hour = data.hour };
            if (!Enum.TryParse(data.kind, out info.kind)) info.kind = SaveKind.Manual;
            list.Add(info);
        }
        list.Sort((a, b) => b.time.CompareTo(a.time));
        return list;
    }

    public string LatestSavePath()
    {
        string best = null;
        DateTime bestTime = DateTime.MinValue;
        foreach (string path in SaveFiles())
        {
            DateTime t = File.GetLastWriteTime(path);
            if (best == null || t > bestTime) { best = path; bestTime = t; }
        }
        return best;
    }

    string[] SaveFiles()
    {
        try { return Directory.Exists(SaveFolder) ? Directory.GetFiles(SaveFolder, "*.json") : new string[0]; }
        catch (Exception e) { Debug.LogError("Could not read the Saves folder: " + e.Message); return new string[0]; }
    }

    // Autosaves: only the newest few stay
    void DeleteOldAutosaves()
    {
        var autos = new List<string>();
        foreach (string path in SaveFiles())
            if (Path.GetFileName(path).StartsWith("auto_", StringComparison.OrdinalIgnoreCase)) autos.Add(path);
        autos.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
        for (int i = Mathf.Max(1, autosavesKept); i < autos.Count; i++) DeleteSave(autos[i]);
    }

    // Before the save list there was a single file: it becomes the first save of the list (the original is renamed, not deleted)
    void MoveOldSave()
    {
        try
        {
            string old = Path.Combine(Application.persistentDataPath, saveFileName);
            if (!File.Exists(old)) return;
            Directory.CreateDirectory(SaveFolder);
            DateTime time = File.GetLastWriteTime(old);
            string target = Path.Combine(SaveFolder, $"manual_{time:yyyyMMdd_HHmmss}_old.json");
            if (!File.Exists(target)) File.Copy(old, target);
            File.SetLastWriteTime(target, time);
            File.Move(old, old + ".moved");
        }
        catch (Exception e) { Debug.LogWarning("Could not move the old save into the Saves folder: " + e.Message); }
    }

    static SaveData ReadFile(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonUtility.FromJson<SaveData>(File.ReadAllText(path)); }
        catch (Exception e)
        {
            Debug.LogError($"Could not read {Path.GetFileName(path)}: {e.Message}");
            return null;
        }
    }

    // Writes a temporary file first and then swaps it in, so a crash while saving never leaves a half-written save.
    // The previous save is kept as a backup (.bak), used if the main file is ever damaged.
    static void WriteSafely(string path, string json)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, json);
        if (!File.Exists(path)) { File.Move(temp, path); return; }
        try { File.Replace(temp, path, path + ".bak"); }
        catch (Exception) // platforms without File.Replace
        {
            File.Copy(path, path + ".bak", true);
            File.Copy(temp, path, true);
            File.Delete(temp);
        }
    }

    public void DeleteSave(string savePath)
    {
        if (string.IsNullOrEmpty(savePath)) return;
        try
        {
            foreach (string path in new[] { savePath, savePath + ".bak", savePath + ".tmp" })
                if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) { Debug.LogError("Could not delete the save: " + e.Message); }
    }

    // Puts the saved state into the world that is open now
    public void ApplySave(SaveData data)
    {
        PlayerStats player = FindPlayer();
        if (player == null) { Debug.LogWarning("Load: no player found in the scene."); return; }

        // Player position (far below the world = broken save: keep the scene's start position)
        if (data.playerPosition.y > -500f) Teleport(player.transform, data.playerPosition, Quaternion.Euler(0f, data.playerYaw, 0f));

        // Stats (never come back dead)
        player.currentHealth = Mathf.Clamp(data.health, 1f, player.maxHealth);
        player.currentStamina = Mathf.Clamp(data.stamina, 0f, player.maxStamina);
        player.currentHunger = Mathf.Clamp(data.hunger, 0f, player.maxHunger);
        player.currentThirst = Mathf.Clamp(data.thirst, 0f, player.maxThirst);

        if (data.hour >= 0f && DayNightCycle.Instance != null) DayNightCycle.Instance.currentHour = data.hour;

        LoadItems(data);
        LoadPlaced(data);
        LoadLoot(data);
        DeliverySystem.Restore(data.delivery);
        HorseOwnership.Restore(data.horses);
        ItemPickup.Restore(data.pickedUp);
        MapSystem.ImportExplored(data.mapExplored);
        Debug.Log($"Game loaded (saved {data.savedAt})");
    }

    void LoadItems(SaveData data)
    {
        PlayerInventoryManager inv = PlayerInventoryManager.Instance;
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (inv == null) return;

        // Empty everything
        if (gear != null)
            foreach (PlayerItemSlot s in new[] { gear.weaponSlotUI, gear.chestSlotUI, gear.helmetSlotUI, gear.bowSlotUI, gear.backpackSlotUI, gear.lanternSlotUI })
                if (s != null && s.itemData != null) gear.ClearGearSlot(s);
        foreach (PlayerItemSlot s in inv.inventorySlots) if (s != null) s.ClearSlot();
        foreach (PlayerItemSlot s in inv.backpackSlots) if (s != null) s.ClearSlot();

        // Gear first (backpack sets the backpack slot count); each item is equipped from an empty slot, like clicking it
        if (gear != null && data.gear != null && inv.inventorySlots.Length > 0)
        {
            SoundManager.Muted = true; // no equip sounds while loading
            try
            {
                string[] ids = { data.gear.backpack, data.gear.chest, data.gear.helmet, data.gear.weapon, data.gear.bow, data.gear.lantern };
                string[] upgrades = { "", data.gear.chestUpgrade, data.gear.helmetUpgrade, data.gear.weaponUpgrade, data.gear.bowUpgrade, "" };
                for (int i = 0; i < ids.Length; i++)
                {
                    PlayerItemData item = ItemUpgrades.Restore(ItemDatabase.Find(ids[i]), upgrades[i]);
                    if (item == null) continue;
                    PlayerItemSlot temp = inv.inventorySlots[0];
                    temp.SetItem(item, 1);
                    gear.EquipItem(temp);
                    temp.ClearSlot();
                }
            }
            finally { SoundManager.Muted = false; }
        }
        inv.RefreshBackpackGrid();

        ReadSlots(inv.inventorySlots, data.inventory);
        ReadSlots(inv.backpackSlots, data.backpack);
    }

    static void ReadSlots(PlayerItemSlot[] slots, List<SlotData> list)
    {
        if (slots == null || list == null) return;
        foreach (SlotData s in list)
        {
            if (s.index < 0 || s.index >= slots.Length || slots[s.index] == null) continue;
            PlayerItemData item = ItemUpgrades.Restore(ItemDatabase.Find(s.item), s.upgrade);
            if (item != null) slots[s.index].SetItem(item, s.amount);
        }
    }

    // Scene loot bags/chests get back what was left in them; bags that were emptied are removed
    void LoadLoot(SaveData data)
    {
        if (!data.hasLootState) return; // old save: leave the loot as the scene has it
        var saved = new Dictionary<string, LootState>();
        foreach (LootState l in data.loot) if (l != null && !string.IsNullOrEmpty(l.key)) saved[l.key] = l;

        foreach (LootBag bag in FindObjectsByType<LootBag>(FindObjectsInactive.Include))
        {
            if (bag.spawnedAtRuntime || bag.gameObject.scene.name == "DontDestroyOnLoad") continue;
            if (bag.chest != null && !bag.chest.lootOnlyOnce) continue; // respawning chest: keeps its fresh loot
            bag.items.Clear();
            if (saved.TryGetValue(bag.SaveKey, out LootState state))
            {
                foreach (string id in state.items)
                {
                    PlayerItemData item = ItemDatabase.Find(id);
                    if (item != null) bag.items.Add(item);
                }
            }
            else if (!bag.IsChest) Destroy(bag.gameObject); // was looted completely
        }
    }

    void LoadPlaced(SaveData data)
    {
        foreach (PlacedObject p in FindObjectsByType<PlacedObject>()) Destroy(p.gameObject);

        TentSpawn respawnTent = null;
        foreach (PlacedData p in data.placed)
        {
            PlayerItemData item = ItemDatabase.Find(p.item);
            if (item == null || item.itemPrefab == null) continue;

            GameObject obj = Instantiate(item.itemPrefab, p.position, p.rotation);
            obj.name = item.itemPrefab.name;
            obj.AddComponent<PlacedObject>().itemId = p.item;
            if (p.respawnTent) respawnTent = obj.GetComponentInChildren<TentSpawn>();
        }
        if (respawnTent != null) TentSpawn.Selected = respawnTent;
    }

    // ---------- Player ----------

    // The player in the open scene. If the menu also created one, the scene's own player wins.
    // After loading the world: makes sure there is exactly one active player (turns one back on, or creates it if none exist)
    PlayerStats EnsurePlayer()
    {
        PlayerStats[] active = FindObjectsByType<PlayerStats>();
        PlayerStats[] every = FindObjectsByType<PlayerStats>(FindObjectsInactive.Include);
        Debug.Log($"Load world: {active.Length} active player(s), {every.Length - active.Length} turned off.");
        if (active.Length > 0) return FindPlayer();

        if (every.Length > 0)
        {
            Scene scene = SceneManager.GetActiveScene();
            PlayerStats revive = Array.Find(every, p => p.gameObject.scene == scene) ?? every[0];
            revive.transform.root.gameObject.SetActive(true);
            revive.gameObject.SetActive(true);
            Debug.Log($"Load world: no active player, turned '{revive.name}' back on.");
            return revive;
        }

        if (playerPrefab == null)
        {
            Debug.LogError("Load world: there is no player in the scene and 'Player Prefab' is empty in the World Save Game Manager.");
            return null;
        }
        GameObject spawn = GameObject.Find("PlayerSpawnPoint");
        GameObject created = spawn != null ? Instantiate(playerPrefab, spawn.transform.position, spawn.transform.rotation) : Instantiate(playerPrefab);
        created.name = playerPrefab.name;
        Debug.Log("Load world: there was no player, so one was created from the prefab.");
        return created.GetComponentInChildren<PlayerStats>();
    }

    PlayerStats FindPlayer()
    {
        PlayerStats[] all = FindObjectsByType<PlayerStats>();
        if (all.Length == 0) return null;
        if (all.Length == 1) return all[0];

        // Every player moves itself to DontDestroyOnLoad (CharacterManager), so the scene can't tell them apart.
        // The right one is the player the equipment/UI of the world is connected to; otherwise not the network copy "(Clone)".
        Scene active = SceneManager.GetActiveScene();
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        Transform wired = gear == null ? null : gear.nakedBodyMesh != null ? gear.nakedBodyMesh.transform : gear.rightHandWeaponHolder;
        PlayerStats chosen = (wired != null ? Array.Find(all, p => wired.IsChildOf(p.transform)) : null)
            ?? Array.Find(all, p => p.gameObject.scene == active)
            ?? Array.Find(all, p => !p.name.EndsWith("(Clone)"))
            ?? all[0];
        foreach (PlayerStats p in all)
        {
            if (p == chosen) continue;
            Debug.Log($"Save system: there were {all.Length} players - kept '{chosen.name}', turned off '{p.name}'.");
            p.gameObject.SetActive(false);
        }
        return chosen;
    }

    // New Game: always move the player (the menu's or the scene's own) to "PlayerSpawnPoint" (if the scene has one)
    static void MoveToSpawnPoint(Transform player)
    {
        GameObject spawn = GameObject.Find("PlayerSpawnPoint");
        if (spawn == null)
        {
            Debug.LogWarning("New Game: the scene has no object called 'PlayerSpawnPoint', so the player stays where he is (" + player.position + ").");
            return;
        }
        Teleport(player, spawn.transform.position, spawn.transform.rotation);
        Debug.Log("New Game: player moved to PlayerSpawnPoint " + spawn.transform.position);
    }

    // The CharacterController must be off to teleport
    static void Teleport(Transform player, Vector3 position, Quaternion rotation)
    {
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.SetPositionAndRotation(position, rotation);
        Physics.SyncTransforms();
        if (cc != null) cc.enabled = true;
    }

    // ---------- "Game saved" message ----------

    void ShowMessage(string text)
    {
        if (!showSaveMessage) return;
        if (messageText == null) CreateMessageText();
        TMP_FontAsset font = messageFont != null ? messageFont : LootWindow.GameFont;
        if (font != null) messageText.font = font;
        messageText.fontSize = messageFontSize;
        messageText.text = text;
        messageUntil = Time.unscaledTime + 2f;
    }

    void LateUpdate()
    {
        if (messageText == null) return;
        float alpha = Mathf.Clamp01(messageUntil - Time.unscaledTime); // fades out in the last second
        messageText.alpha = alpha;
        messageText.enabled = alpha > 0f;
    }

    void CreateMessageText()
    {
        var canvasObj = new GameObject("Save Message Canvas");
        canvasObj.transform.SetParent(transform, false);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        var scaler = canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var textObj = new GameObject("Save Message", typeof(RectTransform));
        textObj.transform.SetParent(canvasObj.transform, false);
        messageText = textObj.AddComponent<TextMeshProUGUI>();
        messageText.alignment = TextAlignmentOptions.BottomRight;
        messageText.raycastTarget = false;
        messageText.color = Color.white;
        RectTransform rect = messageText.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-40f, 30f);
        rect.sizeDelta = new Vector2(600f, 80f);
    }

    // ---------- Game systems (loot, shop, crafting, map...) ----------
    // The menu's copies lack the world scene's look, so they are removed before loading and re-made inside it.

    static readonly System.Type[] GameSystems =
    {
        typeof(LootWindow), typeof(GatherSystem), typeof(PlacementSystem), typeof(RespawnSystem),
        typeof(MapSystem), typeof(ShopWindow), typeof(CraftingWindow), typeof(StableWindow), typeof(HorseSpawner),
    };

    void RemoveOldGameSystems()
    {
        foreach (System.Type type in GameSystems)
            foreach (UnityEngine.Object obj in FindObjectsByType(type, FindObjectsInactive.Include))
                if (obj is Component c && c.gameObject.scene.name == "DontDestroyOnLoad") Destroy(c.gameObject);
    }

    static void CreateGameSystems()
    {
        foreach (System.Type type in GameSystems)
        {
            if (FindAnyObjectByType(type) != null) continue;
            var go = new GameObject(type.Name);
            DontDestroyOnLoad(go);
            go.AddComponent(type);
        }
    }
}
