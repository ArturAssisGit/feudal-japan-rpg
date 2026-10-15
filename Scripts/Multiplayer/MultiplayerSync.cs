using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Everything that is shared while playing together, with custom network messages (no NetworkObjects, no prefabs to register):
// - players: position, animation, riding pose, gear, their horse   (every player -> host -> everyone)
// - world: hour of the day and rain                                  (host -> clients)
// - enemies: the host runs them, the clients see copies              (host -> clients; hits on copies go client -> host)
// Inventory, loot, saves, shops... stay local to each player.
public class MultiplayerSync : MonoBehaviour
{
    public static MultiplayerSync Instance { get; private set; }

    [Header("Send rates")]
    [Tooltip("Player updates per second")] public float playerSendRate = 15f;
    [Tooltip("Enemy updates per second")] public float enemySendRate = 10f;
    [Tooltip("Seconds between hour/rain updates")] public float worldSendInterval = 1f;

    [Header("Rules (decided by the host)")]
    [Tooltip("Off = loot from enemies only drops in the host's world. On = every player gets their own loot bag")] public bool lootForEveryone = true;

    const string MsgPlayer = "mp.player", MsgGear = "mp.gear", MsgLeft = "mp.left", MsgHello = "mp.hello", MsgWorld = "mp.world";
    const string MsgEnemySpawn = "mp.eSpawn", MsgEnemyStates = "mp.eStates", MsgEnemyHit = "mp.eHit", MsgEnemyDie = "mp.eDie";
    const string MsgEnemyGone = "mp.eGone", MsgEnemyDamage = "mp.eDamage", MsgHurt = "mp.hurt";
    static readonly string[] Messages =
        { MsgPlayer, MsgGear, MsgLeft, MsgHello, MsgWorld, MsgEnemySpawn, MsgEnemyStates, MsgEnemyHit, MsgEnemyDie, MsgEnemyGone, MsgEnemyDamage, MsgHurt };

    private NetworkManager nm;
    private NetworkManager hookedManager;
    private bool registered;
    private readonly List<ulong> targets = new List<ulong>();

    // Local player
    private Transform localPlayer;
    private Transform syncedPlayer;
    private Animator localAnimator;
    private AnimSync localAnim;
    private Animator localHorseAnimator;
    private AnimSync localHorseAnim;
    private GearState lastGear;
    private float nextPlayerSend, nextGearSend, forceGearAt, nextEnemySend, nextWorldSend;

    // Other players
    private readonly Dictionary<ulong, RemotePlayerAvatar> avatars = new Dictionary<ulong, RemotePlayerAvatar>();
    private readonly Dictionary<ulong, GearState> gears = new Dictionary<ulong, GearState>();

    // Host: real enemies
    private readonly Dictionary<int, EnemyAI_01> enemiesById = new Dictionary<int, EnemyAI_01>();
    private readonly Dictionary<int, AnimSync> enemyAnims = new Dictionary<int, AnimSync>();
    private readonly HashSet<int> deadSent = new HashSet<int>();
    private int nextEnemyId = 1;
    private ulong damageSender = NetworkManager.ServerClientId;
    private readonly List<int> scratchIds = new List<int>();

    // Client: copies of the host's enemies
    class Mirror
    {
        public EnemyAI_01 ai;
        public AnimSync anim;
        public Vector3 position;
        public float yaw;
    }
    private readonly Dictionary<int, Mirror> mirrors = new Dictionary<int, Mirror>();
    private readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();
    private readonly List<GameObject> sceneTemplates = new List<GameObject>();
    private readonly HashSet<string> missingTemplates = new HashSet<string>();
    private readonly List<EnemySpawner> disabledSpawners = new List<EnemySpawner>();
    private bool adopted, teleportedToHost, hostLootForEveryone;
    private bool savedRain, savedAutoWeather;
    private KeyCode savedRainKey;

    public bool IsOnline => nm != null && nm.IsListening && registered;
    public bool IsServer => IsOnline && nm.IsServer;
    public int RemotePlayerCount => avatars.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Unhook();
        if (Instance == this) Instance = null;
    }

    // ---------- Network start / stop ----------

    // Called by MultiplayerSession before it starts hosting or joining
    public void Hook(NetworkManager manager)
    {
        if (manager == null || hookedManager == manager) return;
        Unhook();
        hookedManager = manager;
        manager.OnServerStarted += OnNetworkStarted;
        manager.OnClientStarted += OnNetworkStarted;
        manager.OnClientConnectedCallback += OnClientConnected;
        manager.OnClientDisconnectCallback += OnClientDisconnected;
        manager.OnServerStopped += OnNetworkStopped;
        manager.OnClientStopped += OnNetworkStopped;
    }

    void Unhook()
    {
        if (hookedManager == null) return;
        hookedManager.OnServerStarted -= OnNetworkStarted;
        hookedManager.OnClientStarted -= OnNetworkStarted;
        hookedManager.OnClientConnectedCallback -= OnClientConnected;
        hookedManager.OnClientDisconnectCallback -= OnClientDisconnected;
        hookedManager.OnServerStopped -= OnNetworkStopped;
        hookedManager.OnClientStopped -= OnNetworkStopped;
        hookedManager = null;
    }

    void OnNetworkStarted()
    {
        nm = NetworkManager.Singleton;
        if (registered || nm == null || nm.CustomMessagingManager == null) return;

        CustomMessagingManager m = nm.CustomMessagingManager;
        m.RegisterNamedMessageHandler(MsgPlayer, OnPlayerMessage);
        m.RegisterNamedMessageHandler(MsgGear, OnGearMessage);
        m.RegisterNamedMessageHandler(MsgLeft, OnLeftMessage);
        m.RegisterNamedMessageHandler(MsgHello, OnHelloMessage);
        m.RegisterNamedMessageHandler(MsgWorld, OnWorldMessage);
        m.RegisterNamedMessageHandler(MsgEnemySpawn, OnEnemySpawnMessage);
        m.RegisterNamedMessageHandler(MsgEnemyStates, OnEnemyStatesMessage);
        m.RegisterNamedMessageHandler(MsgEnemyHit, OnEnemyHitMessage);
        m.RegisterNamedMessageHandler(MsgEnemyDie, OnEnemyDieMessage);
        m.RegisterNamedMessageHandler(MsgEnemyGone, OnEnemyGoneMessage);
        m.RegisterNamedMessageHandler(MsgEnemyDamage, OnEnemyDamageMessage);
        m.RegisterNamedMessageHandler(MsgHurt, OnHurtMessage);
        registered = true;

        EnemyAI_01.MirrorHit = SendEnemyDamage;
        EnemyAI_01.Damaged -= OnEnemyDamaged;
        EnemyAI_01.Damaged += OnEnemyDamaged;
        lastGear = null;
        adopted = false;
        teleportedToHost = false;
    }

    void OnNetworkStopped(bool _) => GoOffline();

    // Back to playing alone (left, host closed, connection lost)
    void GoOffline()
    {
        if (registered && nm != null && nm.CustomMessagingManager != null)
            foreach (string name in Messages) nm.CustomMessagingManager.UnregisterNamedMessageHandler(name);
        registered = false;

        EnemyAI_01.MirrorHit = null;
        EnemyAI_01.Damaged -= OnEnemyDamaged;
        EnemyAI_01.TargetPicker = null;
        EnemySpawner.ExtraPlayers.Clear();

        ClearAvatars();
        gears.Clear();
        ClearHostEnemies();
        if (adopted) RestoreSolo();
        adopted = false;

        if (MultiplayerSession.Instance != null) MultiplayerSession.Instance.OnNetworkStopped();
    }

    void OnClientConnected(ulong clientId)
    {
        if (nm == null) nm = NetworkManager.Singleton;
        if (nm != null && nm.IsServer && clientId != NetworkManager.ServerClientId)
            MultiplayerSession.Notify("A player joined");
    }

    void OnClientDisconnected(ulong clientId)
    {
        if (nm == null) return;
        if (nm.IsServer)
        {
            if (clientId == NetworkManager.ServerClientId) return;
            RemoveAvatar(clientId);
            gears.Remove(clientId);
            MultiplayerSession.Notify("A player left");
            var w = new FastBufferWriter(16, Allocator.Temp);
            try
            {
                w.WriteValueSafe(clientId);
                SendToClients(MsgLeft, w, NetworkDelivery.ReliableSequenced);
            }
            finally { w.Dispose(); }
        }
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        // Everything that lived in the old scene is gone
        ClearAvatars();
        if (IsServer) ClearHostEnemies(true);
        mirrors.Clear();
        DestroySceneTemplates();
        disabledSpawners.Clear();
        adopted = false;
        teleportedToHost = false;
        localPlayer = null;
        syncedPlayer = null;
        forceGearAt = 0f;
    }

    // ---------- Every frame ----------

    void Update()
    {
        if (!IsOnline) return;

        PlayerRef.Find(ref localPlayer);
        if (localPlayer != syncedPlayer)
        {
            syncedPlayer = localPlayer;
            localAnimator = localPlayer != null ? localPlayer.GetComponentInChildren<Animator>() : null;
            localAnim = new AnimSync(localAnimator);
            lastGear = null;
        }

        float now = Time.unscaledTime;
        bool worldReady = localPlayer != null && !WorldLoading();

        if (worldReady && now >= nextPlayerSend)
        {
            nextPlayerSend = now + 1f / Mathf.Max(1f, playerSendRate);
            SendLocalPlayer();
        }
        if (worldReady && now >= nextGearSend)
        {
            nextGearSend = now + 0.25f;
            GearState gear = GearState.CaptureLocal(MultiplayerSession.PlayerName);
            if (lastGear == null || !gear.SameAs(lastGear) || now >= forceGearAt)
            {
                lastGear = gear;
                forceGearAt = now + 4f;
                SendGear(nm.LocalClientId, gear, null);
            }
        }

        if (nm.IsServer)
        {
            UpdateHostTargets();
            if (now >= nextEnemySend)
            {
                nextEnemySend = now + 1f / Mathf.Max(1f, enemySendRate);
                HostSendEnemies();
            }
            if (now >= nextWorldSend)
            {
                nextWorldSend = now + Mathf.Max(0.2f, worldSendInterval);
                SendWorld(null);
            }
        }
        else if (worldReady)
        {
            if (!adopted) Adopt();
            UpdateMirrors();
            TeleportToHostOnce();
        }
    }

    static bool WorldLoading() => WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.IsLoading;

    // ---------- Sending helpers ----------

    // To every client except the host itself (and except 'skip')
    void SendToClients(string message, FastBufferWriter w, NetworkDelivery delivery, ulong skip = ulong.MaxValue)
    {
        if (!IsServer) return;
        targets.Clear();
        foreach (ulong id in nm.ConnectedClientsIds)
            if (id != NetworkManager.ServerClientId && id != skip) targets.Add(id);
        if (targets.Count > 0) nm.CustomMessagingManager.SendNamedMessage(message, targets, w, delivery);
    }

    void SendToServer(string message, FastBufferWriter w, NetworkDelivery delivery)
    {
        if (IsOnline && !nm.IsServer) nm.CustomMessagingManager.SendNamedMessage(message, NetworkManager.ServerClientId, w, delivery);
    }

    // ---------- Players ----------

    void SendLocalPlayer()
    {
        var w = new FastBufferWriter(512, Allocator.Temp, 1300);
        try
        {
            w.WriteValueSafe(nm.LocalClientId);
            WriteLocalPlayer(ref w);
            if (nm.IsServer) SendToClients(MsgPlayer, w, NetworkDelivery.UnreliableSequenced);
            else SendToServer(MsgPlayer, w, NetworkDelivery.UnreliableSequenced);
        }
        catch (Exception e) { WarnOnce("player", e); }
        finally { w.Dispose(); }
    }

    void WriteLocalPlayer(ref FastBufferWriter w)
    {
        PlayerStats stats = localPlayer.GetComponent<PlayerStats>();
        bool dead = stats != null && stats.IsDead;
        bool mounted = HorseMount.PlayerMounted;
        HorseMount horse = HorseMount.Main;
        bool horsePresent = horse != null && horse.IsPresent;

        byte flags = (byte)((dead ? 1 : 0) | (mounted ? 2 : 0) | (horsePresent ? 4 : 0));
        w.WriteValueSafe(localPlayer.position);
        w.WriteValueSafe(localPlayer.eulerAngles.y);
        w.WriteValueSafe(flags);
        localAnim.Write(ref w);

        if (mounted) RemotePlayerAvatar.WritePose(ref w, localAnimator);

        if (horsePresent)
        {
            Animator horseAnimator = horse.GetComponentInChildren<Animator>();
            if (horseAnimator != localHorseAnimator)
            {
                localHorseAnimator = horseAnimator;
                localHorseAnim = new AnimSync(horseAnimator);
            }
            w.WriteValueSafe(horse.ownershipKey ?? "");
            w.WriteValueSafe(horse.transform.position);
            w.WriteValueSafe(horse.transform.eulerAngles.y);
            w.WriteValueSafe(horseAnimator != null ? horseAnimator.speed : 1f);
            localHorseAnim.Write(ref w);
        }
    }

    void OnPlayerMessage(ulong sender, FastBufferReader reader)
    {
        try
        {
            reader.ReadValueSafe(out ulong owner);
            if (nm.IsServer)
            {
                owner = sender; // a client can only speak for itself
                int size = reader.Length - reader.Position;
                byte[] payload = new byte[size];
                reader.ReadBytesSafe(ref payload, size);

                // Pass it on to the other clients
                var w = new FastBufferWriter(size + 16, Allocator.Temp);
                try
                {
                    w.WriteValueSafe(owner);
                    w.WriteBytesSafe(payload, size);
                    SendToClients(MsgPlayer, w, NetworkDelivery.UnreliableSequenced, owner);
                }
                finally { w.Dispose(); }

                var copy = new FastBufferReader(payload, Allocator.Temp, size);
                try { ApplyPlayer(owner, ref copy); }
                finally { copy.Dispose(); }
            }
            else
            {
                if (owner == nm.LocalClientId) return;
                ApplyPlayer(owner, ref reader);
            }
        }
        catch (Exception e) { WarnOnce("playerIn", e); }
    }

    void ApplyPlayer(ulong owner, ref FastBufferReader r)
    {
        RemotePlayerAvatar avatar = GetAvatar(owner);
        if (avatar == null) return; // our own world is not loaded yet

        r.ReadValueSafe(out Vector3 position);
        r.ReadValueSafe(out float yaw);
        r.ReadValueSafe(out byte flags);
        if (!Valid(position) || !Valid(yaw)) return; // broken data: keep the avatar where it is
        bool mounted = (flags & 2) != 0;
        avatar.SetState(position, yaw, (flags & 1) != 0, mounted);
        avatar.Sync.Read(ref r);

        if (mounted) avatar.ReadPose(ref r);

        if ((flags & 4) != 0)
        {
            r.ReadValueSafe(out string key);
            r.ReadValueSafe(out Vector3 horsePosition);
            r.ReadValueSafe(out float horseYaw);
            r.ReadValueSafe(out float horseSpeed);
            avatar.SetHorse(key, horsePosition, horseYaw, horseSpeed);
            if (avatar.HorseSync != null) avatar.HorseSync.Read(ref r);
            else AnimSync.Skip(ref r);
        }
        else avatar.ClearHorse();
    }

    // Limits for what a client may send (a single hit can't do more than this)
    const float MaxClientDamage = 1000f, MaxClientKnockback = 20f;

    static bool Valid(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    static bool Valid(Vector3 v) => Valid(v.x) && Valid(v.y) && Valid(v.z);

    RemotePlayerAvatar GetAvatar(ulong owner)
    {
        if (avatars.TryGetValue(owner, out RemotePlayerAvatar avatar) && avatar != null) return avatar;
        if (localPlayer == null || WorldLoading()) return null;

        avatar = RemotePlayerAvatar.Create(owner);
        if (avatar == null) return null;
        avatars[owner] = avatar;
        if (gears.TryGetValue(owner, out GearState gear)) avatar.SetGear(gear);
        return avatar;
    }

    void RemoveAvatar(ulong owner)
    {
        if (avatars.TryGetValue(owner, out RemotePlayerAvatar avatar) && avatar != null) Destroy(avatar.gameObject);
        avatars.Remove(owner);
    }

    void ClearAvatars()
    {
        foreach (RemotePlayerAvatar avatar in avatars.Values) if (avatar != null) Destroy(avatar.gameObject);
        avatars.Clear();
    }

    void OnLeftMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong owner);
        RemoveAvatar(owner);
        gears.Remove(owner);
        MultiplayerSession.Notify("A player left");
    }

    // ---------- Gear ----------

    // owner's gear -> to the host (from a client) or to the clients (from the host). 'onlyTo' = one client
    void SendGear(ulong owner, GearState gear, ulong? onlyTo)
    {
        var w = new FastBufferWriter(256, Allocator.Temp, 4096);
        try
        {
            w.WriteValueSafe(owner);
            gear.Write(ref w);
            if (!nm.IsServer) SendToServer(MsgGear, w, NetworkDelivery.ReliableSequenced);
            else if (onlyTo.HasValue) nm.CustomMessagingManager.SendNamedMessage(MsgGear, onlyTo.Value, w, NetworkDelivery.ReliableSequenced);
            else SendToClients(MsgGear, w, NetworkDelivery.ReliableSequenced, owner);
        }
        catch (Exception e) { WarnOnce("gear", e); }
        finally { w.Dispose(); }
    }

    void OnGearMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong owner);
        if (nm.IsServer) owner = sender;
        if (!nm.IsServer && owner == nm.LocalClientId) return;
        GearState gear = GearState.Read(ref reader);
        gears[owner] = gear;
        if (avatars.TryGetValue(owner, out RemotePlayerAvatar avatar) && avatar != null) avatar.SetGear(gear);
        if (nm.IsServer) SendGear(owner, gear, null); // pass it on
    }

    // ---------- A client finished loading: send it everything it needs ----------

    void OnHelloMessage(ulong sender, FastBufferReader reader)
    {
        if (!nm.IsServer) return;
        SendWorld(sender);
        if (lastGear != null) SendGear(NetworkManager.ServerClientId, lastGear, sender);
        foreach (KeyValuePair<ulong, GearState> g in gears)
            if (g.Key != sender) SendGear(g.Key, g.Value, sender);
        foreach (KeyValuePair<int, EnemyAI_01> e in enemiesById)
            if (e.Value != null) SendEnemySpawn(e.Key, e.Value, sender);
    }

    // ---------- World: hour + rain ----------

    void SendWorld(ulong? onlyTo)
    {
        var w = new FastBufferWriter(128, Allocator.Temp, 1024);
        try
        {
            w.WriteValueSafe(DayNightCycle.Instance != null ? DayNightCycle.Instance.currentHour : -1f);
            w.WriteValueSafe(RainSystem.CurrentPresetName ?? "");
            w.WriteValueSafe(lootForEveryone);
            if (onlyTo.HasValue) nm.CustomMessagingManager.SendNamedMessage(MsgWorld, onlyTo.Value, w, NetworkDelivery.ReliableSequenced);
            else SendToClients(MsgWorld, w, NetworkDelivery.UnreliableSequenced);
        }
        catch (Exception e) { WarnOnce("world", e); }
        finally { w.Dispose(); }
    }

    void OnWorldMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out float hour);
        reader.ReadValueSafe(out string preset);
        reader.ReadValueSafe(out hostLootForEveryone);
        if (nm.IsServer || !adopted) return;

        DayNightCycle day = DayNightCycle.Instance;
        if (day != null && hour >= 0f)
        {
            float diff = Mathf.Abs(Mathf.DeltaAngle(day.currentHour * 15f, hour * 15f)) / 15f;
            if (diff > 0.05f) day.currentHour = hour; // ~3 game minutes apart: follow the host
        }

        RainSystem rain = RainSystem.Instance;
        if (rain != null && preset != (RainSystem.CurrentPresetName ?? ""))
        {
            if (string.IsNullOrEmpty(preset)) rain.StopRain();
            else
            {
                int index = Array.FindIndex(rain.presets, p => p != null && p.name == preset);
                if (index >= 0) rain.SetPreset(index, false);
            }
        }
    }

    // ---------- Client: become part of the host's world ----------

    void Adopt()
    {
        adopted = true;
        BuildTemplates();

        // The host runs the enemies: ours go away and our spawners stop
        foreach (EnemySpawner spawner in FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None))
        {
            if (!spawner.enabled) continue;
            spawner.enabled = false;
            disabledSpawners.Add(spawner);
        }
        foreach (EnemyAI_01 enemy in FindObjectsByType<EnemyAI_01>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!enemy.isNetworkMirror) Destroy(enemy.gameObject);

        // The host decides the weather
        RainSystem rain = RainSystem.Instance;
        if (rain != null)
        {
            savedRain = true;
            savedAutoWeather = rain.automaticWeather;
            savedRainKey = rain.toggleKey;
            rain.automaticWeather = false;
            rain.toggleKey = KeyCode.None;
        }

        var w = new FastBufferWriter(8, Allocator.Temp);
        try
        {
            w.WriteValueSafe((byte)1);
            SendToServer(MsgHello, w, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }

    void RestoreSolo()
    {
        foreach (Mirror m in mirrors.Values) if (m.ai != null) Destroy(m.ai.gameObject);
        mirrors.Clear();
        foreach (EnemySpawner spawner in disabledSpawners) if (spawner != null) spawner.enabled = true;
        disabledSpawners.Clear();
        DestroySceneTemplates();

        RainSystem rain = RainSystem.Instance;
        if (savedRain && rain != null)
        {
            rain.automaticWeather = savedAutoWeather;
            rain.toggleKey = savedRainKey;
        }
        savedRain = false;
    }

    void TeleportToHostOnce()
    {
        if (teleportedToHost || localPlayer == null) return;
        if (!avatars.TryGetValue(NetworkManager.ServerClientId, out RemotePlayerAvatar host) || host == null || !host.HasState) return;
        teleportedToHost = true;

        Vector3 spot = host.TargetPosition + host.transform.right * 1.5f + Vector3.up * 0.3f;
        if (NavMesh.SamplePosition(spot, out NavMeshHit hit, 3f, NavMesh.AllAreas)) spot = hit.position + Vector3.up * 0.1f;

        CharacterController cc = localPlayer.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        localPlayer.SetPositionAndRotation(spot, host.transform.rotation);
        Physics.SyncTransforms();
        if (cc != null) cc.enabled = true;
        MultiplayerSession.Notify("Joined the host's world");
    }

    // ---------- Enemies: host side ----------

    void UpdateHostTargets()
    {
        EnemySpawner.ExtraPlayers.Clear();
        foreach (RemotePlayerAvatar avatar in avatars.Values)
            if (avatar != null && !avatar.IsDead && avatar.HasState) EnemySpawner.ExtraPlayers.Add(avatar.transform);
        EnemyAI_01.TargetPicker = EnemySpawner.ExtraPlayers.Count > 0 ? PickTarget : (Func<Vector3, Transform>)null;
    }

    // Enemies go for the closest living player
    Transform PickTarget(Vector3 from)
    {
        Transform best = null;
        float bestDistance = float.MaxValue;
        if (localPlayer != null && localPlayer.gameObject.activeInHierarchy)
        {
            PlayerStats stats = localPlayer.GetComponent<PlayerStats>();
            if (stats == null || !stats.IsDead)
            {
                best = localPlayer;
                bestDistance = (localPlayer.position - from).sqrMagnitude;
            }
        }
        foreach (Transform t in EnemySpawner.ExtraPlayers)
        {
            if (t == null) continue;
            float d = (t.position - from).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = t; }
        }
        return best;
    }

    void HostSendEnemies()
    {
        // New enemies
        foreach (EnemyAI_01 e in EnemyAI_01.All)
        {
            if (e == null || e.isNetworkMirror) continue;
            if (e.netId != 0) continue; // already known (the id lives on the enemy)
            int id = nextEnemyId++;
            enemiesById[id] = e;
            e.netId = id;
            enemyAnims[id] = new AnimSync(e.anim != null ? e.anim : e.GetComponentInChildren<Animator>());
            SendEnemySpawn(id, e, null);
        }

        // Gone (destroyed) and dead
        scratchIds.Clear();
        foreach (KeyValuePair<int, EnemyAI_01> pair in enemiesById)
        {
            if (pair.Value == null) { scratchIds.Add(pair.Key); continue; }
            if (pair.Value.IsDead && deadSent.Add(pair.Key)) SendEnemyEvent(MsgEnemyDie, pair.Key);
        }
        foreach (int id in scratchIds) ForgetEnemy(id, true);

        // Positions + animation, in small packets
        if (targetsCount() == 0) return;
        scratchIds.Clear();
        foreach (KeyValuePair<int, EnemyAI_01> pair in enemiesById) scratchIds.Add(pair.Key);
        const int perPacket = 14;
        for (int start = 0; start < scratchIds.Count; start += perPacket)
        {
            int count = Mathf.Min(perPacket, scratchIds.Count - start);
            var w = new FastBufferWriter(1024, Allocator.Temp, 1300);
            try
            {
                w.WriteValueSafe((byte)count);
                for (int i = 0; i < count; i++)
                {
                    int id = scratchIds[start + i];
                    EnemyAI_01 e = enemiesById[id];
                    w.WriteValueSafe(id);
                    w.WriteValueSafe(e.transform.position);
                    w.WriteValueSafe(e.transform.eulerAngles.y);
                    w.WriteValueSafe(e.NetInCombat);
                    enemyAnims[id].Write(ref w);
                }
                SendToClients(MsgEnemyStates, w, NetworkDelivery.UnreliableSequenced);
            }
            catch (Exception ex) { WarnOnce("enemyStates", ex); }
            finally { w.Dispose(); }
        }
    }

    int targetsCount()
    {
        int n = 0;
        foreach (ulong id in nm.ConnectedClientsIds) if (id != NetworkManager.ServerClientId) n++;
        return n;
    }

    void SendEnemySpawn(int id, EnemyAI_01 e, ulong? onlyTo)
    {
        var w = new FastBufferWriter(256, Allocator.Temp, 2048);
        try
        {
            w.WriteValueSafe(id);
            w.WriteValueSafe(NetVisuals.KeyOf(e.gameObject.name));
            w.WriteValueSafe(e.transform.position);
            w.WriteValueSafe(e.transform.eulerAngles.y);
            w.WriteValueSafe(e.IsDead);
            if (onlyTo.HasValue) nm.CustomMessagingManager.SendNamedMessage(MsgEnemySpawn, onlyTo.Value, w, NetworkDelivery.ReliableSequenced);
            else SendToClients(MsgEnemySpawn, w, NetworkDelivery.ReliableSequenced);
        }
        catch (Exception ex) { WarnOnce("enemySpawn", ex); }
        finally { w.Dispose(); }
    }

    void SendEnemyEvent(string message, int id)
    {
        var w = new FastBufferWriter(16, Allocator.Temp);
        try
        {
            w.WriteValueSafe(id);
            SendToClients(message, w, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }

    void ForgetEnemy(int id, bool tellClients)
    {
        if (tellClients && IsServer) SendEnemyEvent(MsgEnemyGone, id);
        enemiesById.Remove(id);
        enemyAnims.Remove(id);
        deadSent.Remove(id);
    }

    void ClearHostEnemies(bool tellClients = false)
    {
        if (tellClients && IsServer)
            foreach (int id in enemiesById.Keys) SendEnemyEvent(MsgEnemyGone, id);
        foreach (EnemyAI_01 e in enemiesById.Values) if (e != null) e.netId = 0;
        enemiesById.Clear();
        enemyAnims.Clear();
        deadSent.Clear();
    }

    // A client hit one of the copies: the real enemy (here) takes the damage
    void OnEnemyDamageMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int id);
        reader.ReadValueSafe(out float damage);
        reader.ReadValueSafe(out Vector3 from);
        reader.ReadValueSafe(out float knockback);
        // Never trust a client blindly: broken or impossible numbers are ignored
        if (!Valid(damage) || damage <= 0f || damage > MaxClientDamage || !Valid(from) || !Valid(knockback)) return;
        knockback = Mathf.Clamp(knockback, 0f, MaxClientKnockback);
        if (!nm.IsServer || !enemiesById.TryGetValue(id, out EnemyAI_01 e) || e == null || e.IsDead) return;
        damageSender = sender;
        try { e.NetworkDamage(damage, from, knockback); }
        finally { damageSender = NetworkManager.ServerClientId; }
    }

    // A real enemy got hurt (by anyone): the clients show the blood (the one who hit already did)
    void OnEnemyDamaged(EnemyAI_01 e, Vector3 from)
    {
        if (!IsServer || e == null || !enemiesById.ContainsKey(e.netId) || enemiesById[e.netId] != e) return;
        var w = new FastBufferWriter(48, Allocator.Temp);
        try
        {
            w.WriteValueSafe(e.netId);
            w.WriteValueSafe(from);
            w.WriteValueSafe(e.HealthFraction); // health left (boss bar)
            w.WriteValueSafe(damageSender);     // whoever hit it already saw the blood
            SendToClients(MsgEnemyHit, w, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }

    // An enemy (here) hit a player who is on another computer
    public void SendHurt(ulong owner, float amount, Vector3 from)
    {
        if (!IsServer || owner == NetworkManager.ServerClientId) return;
        var w = new FastBufferWriter(32, Allocator.Temp);
        try
        {
            w.WriteValueSafe(amount);
            w.WriteValueSafe(from);
            nm.CustomMessagingManager.SendNamedMessage(MsgHurt, owner, w, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }

    // ---------- Enemies: client side ----------

    void OnHurtMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out float amount);
        reader.ReadValueSafe(out Vector3 from);
        if (nm.IsServer || localPlayer == null) return;
        PlayerStats stats = localPlayer.GetComponent<PlayerStats>();
        if (stats == null) return;
        // Impact sound: a boss near the attacker's position = heavier hit
        Sfx hit = Sfx.EnemyHit;
        foreach (EnemyAI_01 e in EnemyAI_01.All)
            if (e != null && e.isBoss && (e.transform.position - from).sqrMagnitude < 2.25f) { hit = Sfx.BossHit; break; }
        stats.TakeDamage(amount, from, hit); // blocking and dodging work as usual
    }

    // Called by a copy when this player hits it
    void SendEnemyDamage(EnemyAI_01 mirror, float damage, Vector3 from, float knockback)
    {
        if (!IsOnline || nm.IsServer || mirror == null) return;
        var w = new FastBufferWriter(48, Allocator.Temp);
        try
        {
            w.WriteValueSafe(mirror.netId);
            w.WriteValueSafe(damage);
            w.WriteValueSafe(from);
            w.WriteValueSafe(knockback);
            SendToServer(MsgEnemyDamage, w, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }

    void OnEnemySpawnMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int id);
        reader.ReadValueSafe(out string key);
        reader.ReadValueSafe(out Vector3 position);
        reader.ReadValueSafe(out float yaw);
        reader.ReadValueSafe(out bool dead);
        if (nm.IsServer || !adopted || mirrors.ContainsKey(id)) return;
        CreateMirror(id, key, position, yaw, dead);
    }

    void OnEnemyStatesMessage(ulong sender, FastBufferReader reader)
    {
        if (nm.IsServer) return;
        try
        {
            reader.ReadValueSafe(out byte count);
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out int id);
                reader.ReadValueSafe(out Vector3 position);
                reader.ReadValueSafe(out float yaw);
                reader.ReadValueSafe(out bool inCombat);
                if (adopted && mirrors.TryGetValue(id, out Mirror m) && m.ai != null)
                {
                    m.position = position;
                    m.yaw = yaw;
                    m.ai.MirrorSetCombat(inCombat);
                    m.anim.Read(ref reader);
                }
                else AnimSync.Skip(ref reader);
            }
        }
        catch (Exception e) { WarnOnce("enemyStatesIn", e); }
    }

    void OnEnemyHitMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int id);
        reader.ReadValueSafe(out Vector3 from);
        reader.ReadValueSafe(out float health);
        reader.ReadValueSafe(out ulong hitBy);
        if (nm.IsServer || !mirrors.TryGetValue(id, out Mirror m) || m.ai == null) return;
        if (hitBy != nm.LocalClientId) m.ai.MirrorHitFeedback(from);
        m.ai.SetMirrorHealth(health);
    }

    void OnEnemyDieMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int id);
        if (!nm.IsServer && mirrors.TryGetValue(id, out Mirror m) && m.ai != null) m.ai.MirrorDie(hostLootForEveryone);
    }

    void OnEnemyGoneMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int id);
        if (nm.IsServer) return;
        if (mirrors.TryGetValue(id, out Mirror m) && m.ai != null) Destroy(m.ai.gameObject);
        mirrors.Remove(id);
    }

    void UpdateMirrors()
    {
        float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
        foreach (Mirror m in mirrors.Values)
        {
            if (m.ai == null) continue;
            Transform t = m.ai.transform;
            if ((t.position - m.position).sqrMagnitude > 25f) t.position = m.position;
            else t.position = Vector3.Lerp(t.position, m.position, k);
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0f, m.yaw, 0f), k);
        }
    }

    void CreateMirror(int id, string key, Vector3 position, float yaw, bool dead)
    {
        if (!templates.TryGetValue(key, out GameObject template) || template == null)
        {
            if (missingTemplates.Add(key))
                Debug.LogWarning($"Multiplayer: the host has an enemy '{key}' that this game can't find (it needs to be in an EnemySpawner or in the scene).");
            return;
        }

        GameObject copy = Instantiate(template, NetVisuals.HiddenRoot);
        EnemyAI_01 ai = copy.GetComponent<EnemyAI_01>();
        if (ai == null) { Destroy(copy); return; }
        ai.isNetworkMirror = true;
        ai.netId = id;
        NavMeshAgent agent = copy.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;
        Animator animator = ai.anim != null ? ai.anim : copy.GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;

        copy.name = key + " (net)";
        copy.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        copy.transform.SetParent(null, true);
        SceneManager.MoveGameObjectToScene(copy, SceneManager.GetActiveScene());

        mirrors[id] = new Mirror { ai = ai, anim = new AnimSync(animator), position = position, yaw = yaw };
        if (dead) ai.MirrorDie(false);
    }

    // The enemy types this game knows: the EnemySpawner prefabs and the enemies placed in the scene
    void BuildTemplates()
    {
        templates.Clear();
        DestroySceneTemplates();
        foreach (EnemySpawner spawner in FindObjectsByType<EnemySpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            foreach (EnemySpawner.SpawnGroup group in spawner.enemyGroups)
                if (group != null && group.prefab != null) AddTemplate(group.prefab);
            if (spawner.enemyPrefab != null) AddTemplate(spawner.enemyPrefab);
        }
        // Roaming patrols (the host spawns them near the players)
        foreach (RoamingPatrolSpawner roaming in FindObjectsByType<RoamingPatrolSpawner>(FindObjectsInactive.Include))
            foreach (GameObject prefab in roaming.AllPrefabs()) if (prefab != null) AddTemplate(prefab);
        foreach (EnemyAI_01 enemy in FindObjectsByType<EnemyAI_01>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (enemy.isNetworkMirror) continue;
            string key = NetVisuals.KeyOf(enemy.gameObject.name);
            if (templates.ContainsKey(key)) continue;
            GameObject copy = Instantiate(enemy.gameObject, NetVisuals.HiddenRoot); // kept switched off
            templates[key] = copy;
            sceneTemplates.Add(copy);
        }
    }

    void AddTemplate(GameObject prefab)
    {
        string key = NetVisuals.KeyOf(prefab.name);
        if (!templates.ContainsKey(key)) templates[key] = prefab;
    }

    void DestroySceneTemplates()
    {
        foreach (GameObject t in sceneTemplates) if (t != null) Destroy(t);
        sceneTemplates.Clear();
    }

    // ---------- Misc ----------

    private readonly HashSet<string> warned = new HashSet<string>();

    void WarnOnce(string what, Exception e)
    {
        if (warned.Add(what)) Debug.LogWarning($"Multiplayer ({what}): {e.Message}");
    }
}
