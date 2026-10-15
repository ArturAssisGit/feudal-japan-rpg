using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

// Every sound in the game is assigned here (empty boxes = silent). Put the prefab in BOTH scenes: it survives scene loads, the extra copy removes itself.
// List boxes hold several versions of a sound: one is picked at random with a small pitch change.
public enum Sfx
{
    SwordSwing, SwordHit, Block, BowDraw, ArrowRelease, ArrowHit, Dodge, PlayerHurt, PlayerDeath,
    EnemyAttack, EnemyHurt, EnemyDeath, Menu, Equip, UseItem, Drink,
    FootstepWalk, FootstepRun, Jump, Land, Slide, EnemyHit, BossHit
}

// One sound effect: its audio files plus where to start them and how loud
[System.Serializable]
public class SoundEffect
{
    [Tooltip("Put several versions here: one is picked at random each time")] public AudioClip[] clips;
    [Tooltip("Seconds skipped at the start of the file (cuts the quiet part before the sound)")] [Range(0f, 2f)] public float startAt = 0f;
    [Range(0f, 2f)] public float volume = 1f;

    public bool IsEmpty => clips == null || clips.Length == 0;
}

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }
    // While true, effects are silent (e.g. while a save is loading)
    public static bool Muted;

    [Header("Volume")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Range(0f, 1f)] public float musicVolume = 0.4f;
    [Range(0f, 1f)] public float ambienceVolume = 0.6f;
    [Range(0f, 1f)] public float effectsVolume = 1f;

    [Header("Music (soundtracks)")]
    [Tooltip("Main menu (the title screen scene)")] public AudioClip[] menuMusic;
    [Tooltip("Exploring the world during the day (put several: when one ends another one plays)")] public AudioClip[] exploreDayMusic;
    [Tooltip("Plays while it rains, from the moment the rain starts until it stops (empty = the day songs keep playing)")] public AudioClip[] rainMusic;
    [Tooltip("When enemies are chasing or fighting you")] public AudioClip[] combatMusic;
    [Tooltip("Seconds to fade from one song to another. Also the fade out at the end of a song and the fade in at the start")] public float musicFadeSeconds = 3f;
    [Tooltip("Seconds the combat music keeps playing after the fight ends")] public float combatMusicLinger = 5f;
    [Tooltip("Only enemies closer than this (meters) start the combat music")] public float combatDetectDistance = 35f;
    [Tooltip("Seconds of silence between two exploring songs (random between min and max)")] public Vector2 silenceBetweenSongs = new(0f, 0f);

    [Header("Ambience - background (loops)")]
    [Tooltip("Forest during the day: birds, leaves, wind...")] public AudioClip forestDay;
    [Tooltip("Forest at night: crickets, owls... (empty = uses the day sound)")] public AudioClip forestNight;
    [Tooltip("Short sounds played now and then around you during the day (a bird call, a branch cracking...)")] public AudioClip[] randomDaySounds;
    [Tooltip("Short sounds played now and then around you at night (an owl, a wolf far away...)")] public AudioClip[] randomNightSounds;
    [Tooltip("Seconds between those random sounds (random between min and max)")] public Vector2 randomSoundInterval = new(8f, 20f);
    [Tooltip("How much quieter the forest gets while it rains (0 = not at all, 1 = silent)")] [Range(0f, 1f)] public float rainCoversForest = 0.6f;

    [Header("Weather")]
    [Tooltip("Soft rain loop, for the light rain presets below (e.g. Drizzle). Empty = uses the heavy rain sound. (Leave 'Rain Sound' on RainSystem empty, or you hear it twice)")] public AudioClip rainLight;
    [Range(0f, 1f)] public float rainLightVolume = 0.5f;
    [Tooltip("Heavy rain loop: normal rain and storms. Empty = uses the light rain sound")] [FormerlySerializedAs("rainLoop")] public AudioClip rainHeavy;
    [FormerlySerializedAs("rainVolume")] [Range(0f, 1f)] public float rainHeavyVolume = 0.7f;
    [Tooltip("Names of the rain presets (in RainSystem) that count as light rain")] public string[] lightRainPresets = { "Drizzle" };
    [Tooltip("Seconds to fade between the light and heavy rain sound when the rain changes")] public float rainCrossfadeSeconds = 4f;
    [Tooltip("Played once when it starts to rain")] public AudioClip rainStart;
    [Tooltip("Thunder, played now and then during storms")] public AudioClip[] thunder;
    [Tooltip("Seconds between thunders in a storm (random between min and max)")] public Vector2 thunderInterval = new(15f, 45f);

    [Header("World")]
    [Tooltip("Fire crackling, plays on every bonfire (objects with a CraftingStation called '...fire...')")] public AudioClip bonfire;
    [Range(0f, 1f)] public float bonfireVolume = 0.8f;
    [Tooltip("How far away the bonfire can be heard (meters)")] public float bonfireHearDistance = 15f;

    [Header("Player movement")]
    [Tooltip("Footsteps while walking (put several, one is picked at random each step)")] public SoundEffect footstepsWalkSound = new();
    [Tooltip("Footsteps while running (empty = uses the walking ones)")] public SoundEffect footstepsRunSound = new();
    [Tooltip("Meters walked between two footsteps")] public float walkStepDistance = 1.2f;
    [Tooltip("Meters run between two footsteps")] public float runStepDistance = 2.2f;
    public SoundEffect jumpSound = new();
    [Tooltip("Landing after a jump or a fall")] public SoundEffect landSound = new();
    public SoundEffect slideSound = new();

    [Header("Player combat")]
    public SoundEffect swordSwingSound = new();
    [Tooltip("Seconds after the attack starts that the swing sound plays (to match the animation)")] public float swingSoundDelay = 0.15f;
    [Tooltip("Sword hitting an enemy")] public SoundEffect swordHitSound = new();
    [Tooltip("Blocking an attack with the sword")] public SoundEffect blockSound = new();
    public SoundEffect bowDrawSound = new();
    [Tooltip("Arrow leaving the bow (player and enemy archers)")] public SoundEffect arrowReleaseSound = new();
    [Tooltip("Arrow hitting something")] public SoundEffect arrowHitSound = new();
    public SoundEffect dodgeSound = new();

    [Header("Player")]
    [Tooltip("Player gets hurt")] public SoundEffect playerHurtSound = new();
    public SoundEffect playerDeathSound = new();

    [Header("Enemies")]
    [Tooltip("Enemy swings its weapon")] public SoundEffect enemyAttackSound = new();
    [Tooltip("Enemy gets hurt (voice)")] public SoundEffect enemyHurtSound = new();
    public SoundEffect enemyDeathSound = new();
    [Tooltip("An enemy's weapon hitting the player (empty = uses Sword Hit)")] public SoundEffect enemyHitSound = new();
    [Tooltip("A boss hitting the player - something heavier (empty = uses Enemy Hit)")] public SoundEffect bossHitSound = new();

    [Header("Interface")]
    [Tooltip("Opening or closing any menu: inventory, merchant, map, loot, crafting / bonfire")] public SoundEffect menuSound = new();
    [Tooltip("Equipping or unequipping an item (weapon, armour, hat, bow, backpack)")] public SoundEffect equipSound = new();
    [Tooltip("Using an item: eating, healing...")] public SoundEffect useItemSound = new();
    [Tooltip("Drinking (empty = uses the Use Item sound)")] public SoundEffect drinkSound = new();
    [HideInInspector] public SoundEffect inventoryOpenSound = new(), inventoryCloseSound = new();

    // Old boxes (before each sound had its own Start At / Volume): copied into the new ones automatically
    [HideInInspector] public AudioClip[] footstepsWalk, footstepsRun, jump, land, slide, swordSwing, swordHit, block, bowDraw, arrowRelease, arrowHit, dodge, playerHurt, playerDeath, enemyAttack, enemyHurt, enemyDeath, inventoryOpen, inventoryClose;
    [HideInInspector] public float footstepVolume = -1f;

    [Header("Fine tuning")]
    [Tooltip("Random pitch change on effects (0.08 = +-8%)")] [Range(0f, 0.3f)] public float pitchVariation = 0.08f;
    [Tooltip("Effects are at full volume up to this distance (meters)")] public float effectsFullVolumeDistance = 4f;
    [Tooltip("Effects can't be heard beyond this distance (meters)")] public float effectsMaxDistance = 40f;
    [Tooltip("For sounds whose Start At is 0: start where the sound really begins, skipping the quiet part at the start of the file")] public bool autoSkipSilence = false;
    [Tooltip("How loud the sound must be to count as 'started' for Auto Skip Silence (0.02 = very quiet)")] [Range(0.001f, 0.3f)] public float silenceThreshold = 0.02f;

    private AudioSource daySource, nightSource, rainLightSource, rainHeavySource;
    private float heavyBlend = 1f, nightBlend, nextRandomSound, nextThunder, nextBonfireScan;
    private bool wasRaining;
    private readonly Dictionary<CraftingStation, AudioSource> bonfireSources = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; Muted = false; }

    void OnValidate() => MoveOldBoxes();

    // Sounds dragged into the old boxes move into the new ones
    void MoveOldBoxes()
    {
        Move(ref footstepsWalk, footstepsWalkSound, true);
        Move(ref footstepsRun, footstepsRunSound, true);
        Move(ref jump, jumpSound);
        Move(ref land, landSound);
        Move(ref slide, slideSound);
        Move(ref swordSwing, swordSwingSound);
        Move(ref swordHit, swordHitSound);
        Move(ref block, blockSound);
        Move(ref bowDraw, bowDrawSound);
        Move(ref arrowRelease, arrowReleaseSound);
        Move(ref arrowHit, arrowHitSound);
        Move(ref dodge, dodgeSound);
        Move(ref playerHurt, playerHurtSound);
        Move(ref playerDeath, playerDeathSound);
        Move(ref enemyAttack, enemyAttackSound);
        Move(ref enemyHurt, enemyHurtSound);
        Move(ref enemyDeath, enemyDeathSound);
        Move(ref inventoryOpen, menuSound);
        Move(ref inventoryClose, menuSound);
        MoveEffect(inventoryOpenSound, menuSound);
        MoveEffect(inventoryCloseSound, menuSound);
    }

    static void MoveEffect(SoundEffect old, SoundEffect target)
    {
        if (old == null || old.IsEmpty || target == null) return;
        if (target.IsEmpty) { target.clips = old.clips; target.startAt = old.startAt; target.volume = old.volume; }
        old.clips = new AudioClip[0];
    }

    void Move(ref AudioClip[] old, SoundEffect target, bool footstep = false)
    {
        if (old == null || old.Length == 0 || target == null) return;
        if (target.IsEmpty)
        {
            target.clips = old;
            if (footstep && footstepVolume >= 0f) target.volume = footstepVolume;
        }
        old = new AudioClip[0];
    }

    void Awake()
    {
        MoveOldBoxes();
        if (Instance != null && Instance != this)
        {
            // The SoundManager from the previous scene is still alive (keeps the music going): remove this copy
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
        else Debug.LogWarning("SoundManager: put it on an object without a parent, so the music can continue between scenes.");
        SceneManager.sceneLoaded += OnSceneLoaded;

        musicA = CreateLoop("Music A");
        musicB = CreateLoop("Music B");
        musicA.loop = musicB.loop = false;
        musicA.priority = musicB.priority = 0;
        currentMusic = musicA;

        daySource = CreateLoop("Forest Day");
        nightSource = CreateLoop("Forest Night");
        rainLightSource = CreateLoop("Rain Light");
        rainHeavySource = CreateLoop("Rain Heavy");
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        nextSceneCheck = 0f; // check right away if this is the menu
        lastMenus = -1;
        wasRaining = RainSystem.RainAmount > 0.01f;
    }

    void Start()
    {
        PreloadEffects();
        nightBlend = IsNight() ? 1f : 0f;
        nextRandomSound = Time.time + Random.Range(randomSoundInterval.x, randomSoundInterval.y);
        nextThunder = Time.time + Random.Range(thunderInterval.x, thunderInterval.y);
    }

    void Update()
    {
        ReleaseSources();
        UpdateScene();
        UpdateMusic();
        UpdateAmbience();
        UpdateWeather();
        UpdateBonfires();
        UpdateMenus();
    }

    // ---------- Music ----------

    private enum Mood { None, Menu, Day, Rain, Combat }
    private AudioSource musicA, musicB, currentMusic;
    private Mood mood = Mood.None;
    private AudioClip[] moodList;
    private float nextSongTime = -1f, lastCombatTime = -999f, nextCombatCheck, nextSceneCheck;
    private bool inMenu, rainMusicOn;
    private float fadeA, fadeB; // fade level of each music source (0 = silent, 1 = full)
    private Transform player;

    AudioSource OtherMusicSource() => currentMusic == musicA ? musicB : musicA;

    ref float FadeOf(AudioSource s)
    {
        if (s == musicA) return ref fadeA;
        return ref fadeB;
    }

    // 1 normally, going down to 0 during the last seconds of a song (so it never ends abruptly)
    float EndFade(AudioSource s)
    {
        if (s == null || s.clip == null || !s.isPlaying) return 1f;
        float remaining = s.clip.length - s.time;
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(remaining / Mathf.Max(0.1f, musicFadeSeconds)));
    }

    // Rain music from when the rain starts until it clearly stops (no flicker around the limit)
    Mood ExploreMood()
    {
        if (rainMusic == null || rainMusic.Length == 0) return Mood.Day;
        if (!rainMusicOn && RainSystem.RainAmount > 0.3f) rainMusicOn = true;
        else if (rainMusicOn && RainSystem.RainAmount < 0.1f) rainMusicOn = false;
        return rainMusicOn ? Mood.Rain : Mood.Day;
    }

    void UpdateScene()
    {
        if (Time.unscaledTime < nextSceneCheck) return;
        nextSceneCheck = Time.unscaledTime + 1f;
        inMenu = TitleScreenManager.IsOpen;
        PlayerRef.Find(ref player);
    }

    bool InCombat()
    {
        if (inMenu) return false;
        if (Time.time >= nextCombatCheck && player != null)
        {
            nextCombatCheck = Time.time + 0.5f;
            foreach (EnemyAI_01 enemy in FindObjectsByType<EnemyAI_01>())
                if (enemy != null && enemy.IsInCombat && Vector3.Distance(enemy.transform.position, player.position) <= combatDetectDistance)
                {
                    lastCombatTime = Time.time;
                    break;
                }
        }
        return Time.time - lastCombatTime <= combatMusicLinger;
    }

    AudioClip[] MusicFor(Mood m) => m switch
    {
        Mood.Menu => menuMusic,
        Mood.Combat => combatMusic,
        Mood.Rain => rainMusic,
        _ => exploreDayMusic
    };

    void UpdateMusic()
    {
        Mood wanted = inMenu ? Mood.Menu : InCombat() ? Mood.Combat : ExploreMood();
        if (wanted == Mood.Combat && (combatMusic == null || combatMusic.Length == 0)) wanted = ExploreMood(); // no combat songs: keep exploring music
        AudioClip[] list = MusicFor(wanted);

        if (wanted != mood)
        {
            mood = wanted;
            // A different kind of music (menu, combat, rain, day): crossfade to it now
            if (list != moodList)
            {
                moodList = list;
                StartSong(Pick(list));
            }
        }
        else if (currentMusic.clip == null || !currentMusic.isPlaying)
        {
            // The song ended: next one (after a pause while exploring)
            if (nextSongTime < 0f)
                nextSongTime = Time.unscaledTime + (mood == Mood.Day || mood == Mood.Rain ? Random.Range(silenceBetweenSongs.x, Mathf.Max(silenceBetweenSongs.x, silenceBetweenSongs.y)) : 0f);
            if (Time.unscaledTime >= nextSongTime) StartSong(PickDifferent(list, currentMusic.clip));
        }
        else if (list != null && list.Length > 1 && silenceBetweenSongs.y <= 0f && currentMusic.clip.length - currentMusic.time <= musicFadeSeconds)
        {
            // No pause wanted between songs: start the next one while this one fades out (a smooth crossfade)
            StartSong(PickDifferent(list, currentMusic.clip));
        }

        // Fades: the current song goes up, the old one goes down (smooth curve, so there is no sudden start or stop)
        float target = masterVolume * musicVolume * GameSettings.Music;
        float step = Time.unscaledDeltaTime / Mathf.Max(0.1f, musicFadeSeconds);
        AudioSource old = OtherMusicSource();
        ref float currentFade = ref FadeOf(currentMusic);
        ref float oldFade = ref FadeOf(old);
        currentFade = Mathf.MoveTowards(currentFade, 1f, step);
        oldFade = Mathf.MoveTowards(oldFade, 0f, step);
        currentMusic.volume = target * Mathf.SmoothStep(0f, 1f, currentFade) * EndFade(currentMusic);
        old.volume = target * Mathf.SmoothStep(0f, 1f, oldFade) * EndFade(old);
        if (oldFade <= 0f && old.isPlaying) old.Stop();
    }

    void StartSong(AudioClip clip)
    {
        nextSongTime = -1f;
        if (clip == null)
        {
            // Nothing to play here: fade the current song out
            // (the old song keeps fading out on its own source)
            currentMusic = OtherMusicSource();
            currentMusic.Stop();
            currentMusic.clip = null;
            FadeOf(currentMusic) = 0f;
            return;
        }
        AudioSource next = OtherMusicSource();
        if (currentMusic.clip == clip && currentMusic.isPlaying) return; // already playing it
        next.Stop();
        next.clip = clip;
        next.volume = 0f;
        FadeOf(next) = 0f;
        next.Play();
        currentMusic = next;
    }

    static AudioClip PickDifferent(AudioClip[] clips, AudioClip last)
    {
        if (clips == null || clips.Length == 0) return null;
        for (int i = 0; i < 6; i++)
        {
            AudioClip c = Pick(clips);
            if (c != last) return c;
        }
        return Pick(clips);
    }

    // ---------- Menus ----------
    // One sound whenever a menu opens or closes (inventory, merchant, map, loot, crafting)

    private int lastMenus = -1;

    void UpdateMenus()
    {
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        int menus = (gear != null && gear.IsInventoryOpen ? 1 : 0)
                  | (ShopWindow.IsOpen ? 2 : 0)
                  | (MapSystem.IsMapOpen ? 4 : 0)
                  | (LootWindow.IsOpen ? 8 : 0)
                  | (CraftingWindow.IsOpen ? 16 : 0)
                  | (StableWindow.IsOpen ? 32 : 0);
        if (lastMenus >= 0 && menus != lastMenus)
        {
            Transform listener = ListenerTransform();
            Play(Sfx.Menu, listener != null ? listener.position : transform.position);
        }
        lastMenus = menus;
    }

    // ---------- Ambience ----------

    void UpdateAmbience()
    {
        nightBlend = Mathf.MoveTowards(nightBlend, IsNight() ? 1f : 0f, Time.deltaTime / 6f); // 6 s crossfade
        float rain = Mathf.Clamp01(RainSystem.RainAmount);
        float volume = masterVolume * ambienceVolume * GameSettings.Ambience * (1f - rainCoversForest * rain) * (inMenu ? 0f : 1f);

        AudioClip night = forestNight != null ? forestNight : forestDay;
        bool sameClip = night == forestDay;
        SetLoop(daySource, forestDay, sameClip ? volume : volume * (1f - nightBlend));
        SetLoop(nightSource, sameClip ? null : night, sameClip ? 0f : volume * nightBlend);

        if (Time.time >= nextRandomSound)
        {
            nextRandomSound = Time.time + Random.Range(randomSoundInterval.x, Mathf.Max(randomSoundInterval.x, randomSoundInterval.y));
            AudioClip[] list = IsNight() ? randomNightSounds : randomDaySounds;
            Transform listener = ListenerTransform();
            if (listener != null && rain < 0.5f && !inMenu)
            {
                Vector2 dir = Random.insideUnitCircle.normalized * Random.Range(8f, 25f);
                Vector3 position = listener.position + new Vector3(dir.x, Random.Range(1f, 6f), dir.y);
                PlayClip(Pick(list), position, masterVolume * ambienceVolume * GameSettings.Ambience, 2f, 60f);
            }
        }
    }

    // ---------- Weather ----------

    void UpdateWeather()
    {
        float rain = Mathf.Clamp01(RainSystem.RainAmount) * (inMenu ? 0f : 1f);
        UpdateRainLoops(rain);

        bool raining = rain > 0.01f;
        if (raining && !wasRaining && rainStart != null) Play2D(rainStart, masterVolume * GameSettings.Ambience * (IsLightRain() ? rainLightVolume : rainHeavyVolume));
        wasRaining = raining;

        bool storm = RainSystem.IsRaining && SkyboxCycle.Cloudiness > 0.8f && !inMenu;
        if (!storm) nextThunder = Mathf.Max(nextThunder, Time.time + thunderInterval.x * 0.5f);
        else if (Time.time >= nextThunder)
        {
            nextThunder = Time.time + Random.Range(thunderInterval.x, Mathf.Max(thunderInterval.x, thunderInterval.y));
            AudioClip clip = Pick(thunder);
            if (clip != null) Play2D(clip, masterVolume * ambienceVolume * GameSettings.Ambience * Random.Range(0.6f, 1f));
        }
    }

    // Light rain (drizzle) and heavy rain (rain, storm) use different sounds that fade into each other
    void UpdateRainLoops(float rain)
    {
        string preset = RainSystem.CurrentPresetName;
        if (!string.IsNullOrEmpty(preset)) // while the rain fades out the preset is empty: keep the last sound
            heavyBlend = Mathf.MoveTowards(heavyBlend, IsLightRain() ? 0f : 1f, Time.deltaTime / Mathf.Max(0.1f, rainCrossfadeSeconds));

        AudioClip light = rainLight != null ? rainLight : rainHeavy;
        AudioClip heavy = rainHeavy != null ? rainHeavy : rainLight;
        float lightVolume = masterVolume * GameSettings.Ambience * rainLightVolume * rain * (1f - heavyBlend);
        float heavyVolume = masterVolume * GameSettings.Ambience * rainHeavyVolume * rain * heavyBlend;
        // Only one rain sound assigned: use it for everything (volume changes with light/heavy)
        bool sameClip = light == heavy;
        SetLoop(rainLightSource, sameClip ? null : light, sameClip ? 0f : lightVolume);
        SetLoop(rainHeavySource, heavy, sameClip ? lightVolume + heavyVolume : heavyVolume);
    }

    bool IsLightRain()
    {
        string preset = RainSystem.CurrentPresetName;
        if (lightRainPresets == null || string.IsNullOrEmpty(preset)) return false;
        foreach (string n in lightRainPresets)
            if (!string.IsNullOrEmpty(n) && n == preset) return true;
        return false;
    }

    // ---------- Bonfires ----------

    void UpdateBonfires()
    {
        if (Time.time < nextBonfireScan) return;
        nextBonfireScan = Time.time + 1f;

        foreach (CraftingStation station in CraftingStation.All)
        {
            if (station == null || bonfireSources.ContainsKey(station) || !IsFire(station)) continue;
            GameObject go = new GameObject("Bonfire Sound");
            go.transform.SetParent(station.transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1.5f;
            bonfireSources[station] = source;
        }

        List<CraftingStation> gone = null;
        foreach (var pair in bonfireSources)
        {
            if (pair.Key == null || pair.Value == null)
            {
                (gone ??= new List<CraftingStation>()).Add(pair.Key);
                continue;
            }
            AudioSource source = pair.Value;
            source.maxDistance = Mathf.Max(2f, bonfireHearDistance);
            if (source.clip != bonfire)
            {
                source.clip = bonfire;
                source.time = 0f;
            }
            source.volume = masterVolume * GameSettings.Ambience * bonfireVolume;
            if (bonfire != null && !source.isPlaying)
            {
                source.time = Random.Range(0f, bonfire.length * 0.9f); // two fires don't crackle in sync
                source.Play();
            }
            else if (bonfire == null && source.isPlaying) source.Stop();
        }
        if (gone != null) foreach (CraftingStation s in gone) bonfireSources.Remove(s);
    }

    static bool IsFire(CraftingStation station)
    {
        string a = station.stationName != null ? station.stationName.ToLowerInvariant() : "";
        string b = station.gameObject.name.ToLowerInvariant();
        return a.Contains("fire") || b.Contains("fire");
    }

    // ---------- Effects (called by the game scripts) ----------

    // Plays a sound at a position, e.g. SoundManager.Play(Sfx.SwordHit, hitPoint)
    public static void Play(Sfx sound, Vector3 position, float delay = 0f)
    {
        SoundManager m = Instance;
        if (m == null || !m.isActiveAndEnabled || Muted) return;
        SoundEffect effect = m.Effect(sound);
        if (effect == null) return;
        AudioClip clip = Pick(effect.clips);
        if (clip == null) return;
        if (sound == Sfx.SwordSwing) delay += m.swingSoundDelay;
        float volume = m.masterVolume * m.effectsVolume * GameSettings.Effects * effect.volume;

        // Menu / inventory sounds: heard in both ears right away, and the quiet start of the file is always skipped
        bool ui = sound == Sfx.Menu || sound == Sfx.Equip || sound == Sfx.UseItem || sound == Sfx.Drink;
        if (ui)
        {
            m.PlayUI(clip, volume, effect.startAt);
            return;
        }
        m.PlayClip(clip, position, volume, m.effectsFullVolumeDistance, m.effectsMaxDistance, delay, effect.startAt);
    }

    void PlayUI(AudioClip clip, float volume, float startAt)
    {
        if (clip == null || volume <= 0f) return;
        AudioSource source = RentSource();
        source.transform.localPosition = Vector3.zero;
        source.clip = clip;
        source.volume = volume;
        source.pitch = 1f;
        source.spatialBlend = 0f;
        float start = SoundStart(clip, startAt, true);
        source.time = start;
        source.Play();
        busySources.Add(source);
        busyUntil.Add(Time.unscaledTime + (clip.length - start) + 0.2f);
    }

    // Loads every effect into memory at the start, so the first time a sound plays it isn't late
    void PreloadEffects()
    {
        foreach (Sfx sound in System.Enum.GetValues(typeof(Sfx)))
        {
            SoundEffect effect = Effect(sound);
            if (effect == null || effect.clips == null) continue;
            foreach (AudioClip clip in effect.clips)
                if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        }
    }

    // Meters between footsteps (used by PlayerMovement)
    public static float StepDistance(bool running)
    {
        SoundManager m = Instance;
        if (m == null) return running ? 2.2f : 1.2f;
        return Mathf.Max(0.2f, running ? m.runStepDistance : m.walkStepDistance);
    }

    SoundEffect Effect(Sfx sound) => sound switch
    {
        Sfx.SwordSwing => swordSwingSound,
        Sfx.SwordHit => swordHitSound,
        Sfx.Block => blockSound,
        Sfx.BowDraw => bowDrawSound,
        Sfx.ArrowRelease => arrowReleaseSound,
        Sfx.ArrowHit => arrowHitSound,
        Sfx.Dodge => dodgeSound,
        Sfx.PlayerHurt => playerHurtSound,
        Sfx.PlayerDeath => playerDeathSound,
        Sfx.EnemyAttack => enemyAttackSound,
        Sfx.EnemyHurt => enemyHurtSound,
        Sfx.EnemyDeath => enemyDeathSound,
        Sfx.Menu => menuSound,
        Sfx.Equip => equipSound,
        Sfx.UseItem => useItemSound,
        Sfx.Drink => drinkSound != null && !drinkSound.IsEmpty ? drinkSound : useItemSound,
        Sfx.FootstepWalk => footstepsWalkSound,
        Sfx.FootstepRun => footstepsRunSound != null && !footstepsRunSound.IsEmpty ? footstepsRunSound : footstepsWalkSound,
        Sfx.Jump => jumpSound,
        Sfx.Land => landSound,
        Sfx.Slide => slideSound,
        Sfx.EnemyHit => !enemyHitSound.IsEmpty ? enemyHitSound : swordHitSound,
        Sfx.BossHit => !bossHitSound.IsEmpty ? bossHitSound : !enemyHitSound.IsEmpty ? enemyHitSound : swordHitSound,
        _ => null
    };

    // One-shot sounds reuse a few AudioSources instead of creating and destroying an object for every sound
    private readonly Stack<AudioSource> freeSources = new Stack<AudioSource>();
    private readonly List<AudioSource> busySources = new List<AudioSource>();
    private readonly List<float> busyUntil = new List<float>();

    AudioSource RentSource()
    {
        AudioSource source = null;
        while (freeSources.Count > 0 && source == null) source = freeSources.Pop();
        if (source == null)
        {
            GameObject go = new GameObject("Sound");
            go.transform.SetParent(transform, false);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
        }
        source.gameObject.SetActive(true);
        return source;
    }

    // Sources whose sound has finished go back to the pool
    void ReleaseSources()
    {
        for (int i = busySources.Count - 1; i >= 0; i--)
        {
            AudioSource source = busySources[i];
            if (source != null && Time.unscaledTime < busyUntil[i]) continue;
            if (source != null)
            {
                source.Stop();
                source.clip = null;
                source.gameObject.SetActive(false);
                freeSources.Push(source);
            }
            busySources.RemoveAt(i);
            busyUntil.RemoveAt(i);
        }
    }

    void PlayClip(AudioClip clip, Vector3 position, float volume, float minDistance, float maxDistance, float delay = 0f, float startAt = 0f)
    {
        if (clip == null || volume <= 0f) return;
        AudioSource source = RentSource();
        source.transform.position = position;
        source.clip = clip;
        source.volume = volume;
        source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = Mathf.Max(minDistance + 1f, maxDistance);
        source.dopplerLevel = 0f;
        float start = SoundStart(clip, startAt);
        source.time = start;
        if (delay > 0f) source.PlayDelayed(delay);
        else source.Play();
        busySources.Add(source);
        busyUntil.Add(Time.unscaledTime + delay + (clip.length - start) / Mathf.Max(0.5f, source.pitch) + 0.2f);
    }

    void Play2D(AudioClip clip, float volume)
    {
        if (clip == null || volume <= 0f) return;
        AudioSource source = RentSource();
        source.transform.localPosition = Vector3.zero;
        source.clip = clip;
        source.volume = volume;
        source.pitch = 1f;
        source.spatialBlend = 0f;
        float start = SoundStart(clip);
        source.time = start;
        source.Play();
        busySources.Add(source);
        busyUntil.Add(Time.unscaledTime + (clip.length - start) + 0.2f);
    }

    // ---------- Skip the silence at the start of a sound ----------

    private readonly Dictionary<AudioClip, float> soundStarts = new();
    private bool warnedLoadType;

    // Seconds into the clip where the sound really begins (worked out once per clip, then cached)
    float SoundStart(AudioClip clip, float startAt = 0f, bool alwaysSkip = false)
    {
        if (clip == null) return 0f;
        if (startAt > 0f) return Mathf.Min(startAt, Mathf.Max(0f, clip.length - 0.05f)); // set by hand for this sound
        if (!autoSkipSilence && !alwaysSkip) return 0f;
        if (soundStarts.TryGetValue(clip, out float cached)) return cached;
        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            clip.LoadAudioData(); // ready next time
            return 0f;
        }

        float start = 0f;
        int channels = Mathf.Max(1, clip.channels);
        int frames = Mathf.Min(clip.samples, clip.frequency * 3); // only look at the first 3 seconds
        float[] data = new float[frames * channels];
        if (frames > 0 && clip.GetData(data, 0))
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (Mathf.Abs(data[i]) < silenceThreshold) continue;
                start = Mathf.Max(0f, (float)(i / channels) / clip.frequency - 0.01f); // keep 10 ms so the hit isn't cut
                break;
            }
        }
        else if (!warnedLoadType)
        {
            warnedLoadType = true;
            Debug.LogWarning($"SoundManager: can't read '{clip.name}' to skip its silence. Select the audio file and set Load Type to 'Decompress On Load' (then Apply).");
        }

        start = Mathf.Min(start, Mathf.Max(0f, clip.length - 0.05f));
        soundStarts[clip] = start;
        return start;
    }

    // ---------- Helpers ----------

    AudioSource CreateLoop(string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        AudioSource source = go.AddComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f; // 2D: heard the same everywhere
        source.volume = 0f;
        return source;
    }

    static void SetLoop(AudioSource source, AudioClip clip, float volume)
    {
        if (source.clip != clip) { source.Stop(); source.clip = clip; }
        source.volume = volume;
        bool shouldPlay = clip != null && volume > 0.001f;
        if (shouldPlay && !source.isPlaying) source.Play();
        else if (!shouldPlay && source.isPlaying) source.Stop();
    }

    static AudioClip Pick(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip != null) return clip;
        foreach (AudioClip c in clips) if (c != null) return c; // skip empty boxes
        return null;
    }

    static bool IsNight() => DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;

    static Transform ListenerTransform()
    {
        Camera cam = Camera.main;
        return cam != null ? cam.transform : null;
    }
}
