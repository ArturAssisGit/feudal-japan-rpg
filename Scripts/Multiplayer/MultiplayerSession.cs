using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

// Creates itself at game start. Hosting and joining with a code (Unity Relay: works over the internet, no ports to open).
//
//   HOST:   load your save as usual, then MultiplayerSession.Instance.Host()          -> a join code appears (and is copied)
//   JOIN:   MultiplayerSession.Instance.Join("K7Q2XF")  (from the menu or from the game) -> loads the world with YOUR character
//   LEAVE:  MultiplayerSession.Instance.Leave()
//
// Until the main menu has its buttons, F8 opens a small panel with all of this.
// Needs: the "Multiplayer Services" package and the project linked to Unity Cloud (Edit > Project Settings > Services).
public class MultiplayerSession : MonoBehaviour
{
    public enum Mode { Offline, Starting, Host, Client }

    public static MultiplayerSession Instance { get; private set; }
    public static Mode CurrentMode => Instance != null ? Instance.mode : Mode.Offline;
    public static bool IsOnline => CurrentMode == Mode.Host || CurrentMode == Mode.Client;
    public static bool IsHost => CurrentMode == Mode.Host;
    public static bool IsClient => CurrentMode == Mode.Client;
    public static string JoinCode => Instance != null ? Instance.joinCode : "";
    public static string Status => Instance != null ? Instance.status : "";
    public static bool PanelOpen => Instance != null && Instance.panelOpen;
    public static bool InWorldNow => InWorld();

    // For the panel (MultiplayerPanel)
    public string Notice => notice;
    public float NoticeUntil => noticeUntil;
    public float ShowCodeUntil => showCodeUntil;
    public int MaxPlayers => maxPlayers;
    public KeyCode PanelKey => panelKey;
    public void SetNameInput(string value) => nameInput = value;

    // The name the other players see above your head
    public static string PlayerName
    {
        get
        {
            string saved = PlayerPrefs.GetString("PlayerName", "");
            return string.IsNullOrEmpty(saved) ? Environment.UserName : saved;
        }
        set => PlayerPrefs.SetString("PlayerName", value ?? "");
    }

    // Something happened (a player joined, connected...) - the menu can listen to this too
    public static event Action<string> Notified;
    public static event Action StateChanged;

    [Header("Session")]
    [Tooltip("Players in a game, the host included")] [Range(2, 8)] public int maxPlayers = 4;

    [Header("Temporary panel (until the main menu has buttons)")]
    public KeyCode panelKey = KeyCode.F8;
    [Tooltip("Seconds the join code stays on screen after hosting")] public float showCodeSeconds = 25f;

    private Mode mode = Mode.Offline;
    private ISession session;
    private string joinCode = "";
    private string status = "";
    private bool panelOpen;
    private string codeInput = "";
    private string nameInput;
    private bool hostWhenWorldLoaded;
    private bool leaving;
    private float showCodeUntil, noticeUntil;
    private string notice = "";
    private CursorLockMode cursorBefore;
    private bool cursorVisibleBefore;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        Notified = null;
        StateChanged = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        GameObject go = WorldUtil.CreatePersistent<MultiplayerSession>("MultiplayerSession").gameObject;
        go.AddComponent<MultiplayerSync>();
        go.AddComponent<MultiplayerPanel>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        nameInput = PlayerName;
    }

    // ---------- Buttons (call these from the main menu later) ----------

    // From the game: opens your world to friends. From the menu: loads your save (or a new game) first
    public void Host() => _ = HostAsync();

    public void HostNewGame()
    {
        if (InWorld()) { _ = HostAsync(); return; }
        WorldSaveGameManager w = WorldSaveGameManager.instance;
        if (w == null) { SetStatus("No WorldSaveGameManager in the scene"); return; }
        hostWhenWorldLoaded = true;
        SetStatus("Loading a new game...");
        w.StartNewGame();
    }

    public void Join(string code) => _ = JoinAsync(code);

    public void Leave() => _ = LeaveAsync();

    // ---------- Host ----------

    public async Task HostAsync()
    {
        if (mode != Mode.Offline) return;

        // From the menu: load the save first, then host
        if (!InWorld())
        {
            WorldSaveGameManager w = WorldSaveGameManager.instance;
            if (w == null) { SetStatus("No WorldSaveGameManager in the scene"); return; }
            hostWhenWorldLoaded = true;
            SetStatus("Loading your game...");
            if (w.HasSave) w.ContinueGame();
            else w.StartNewGame();
            return;
        }

        SetMode(Mode.Starting, "Creating the session...");
        try
        {
            await SignIn();
            NetworkManager nm = PrepareNetworkManager();
            MultiplayerSync.Instance.Hook(nm);

            SessionOptions options = new SessionOptions { MaxPlayers = maxPlayers }.WithRelayNetwork();
            session = await MultiplayerService.Instance.CreateSessionAsync(options);

            joinCode = session.Code ?? "";
            GUIUtility.systemCopyBuffer = joinCode;
            showCodeUntil = Time.unscaledTime + showCodeSeconds;
            SetMode(Mode.Host, $"Hosting. Code: {joinCode} (copied)");
            Notify($"Join code: {joinCode} (copied)");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            await StopEverything();
            SetMode(Mode.Offline, "Could not host: " + ShortError(e));
        }
    }

    // ---------- Join ----------

    public async Task JoinAsync(string code)
    {
        if (mode != Mode.Offline) return;
        code = (code ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0) { SetStatus("Write the host's code first"); return; }

        SetMode(Mode.Starting, "Joining " + code + "...");
        try
        {
            await SignIn();
            NetworkManager nm = PrepareNetworkManager();
            MultiplayerSync.Instance.Hook(nm);

            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
            joinCode = code;
            SetMode(Mode.Client, "Connected");

            // From the menu: open the world with YOUR character (your save, or a new one)
            if (!InWorld())
            {
                WorldSaveGameManager w = WorldSaveGameManager.instance;
                if (w != null)
                {
                    SetStatus("Connected. Loading the world...");
                    if (w.HasSave) w.ContinueGame();
                    else w.StartNewGame();
                }
            }
            ClosePanel();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            await StopEverything();
            SetMode(Mode.Offline, "Could not join: " + ShortError(e));
        }
    }

    // ---------- Leave ----------

    public async Task LeaveAsync()
    {
        if (mode == Mode.Offline) return;
        leaving = true;
        try { await StopEverything(); }
        finally { leaving = false; }
        SetMode(Mode.Offline, "Playing alone");
    }

    async Task StopEverything()
    {
        ISession s = session;
        session = null;
        if (s != null)
        {
            try { await s.LeaveAsync(); }
            catch (Exception e) { Debug.LogWarning("Multiplayer: leaving the session: " + e.Message); }
        }
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening) nm.Shutdown();
        joinCode = "";
        hostWhenWorldLoaded = false;
    }

    // MultiplayerSync tells us when the connection is gone (host left, internet lost, we left)
    public void OnNetworkStopped()
    {
        if (leaving || mode == Mode.Offline || mode == Mode.Starting) return;
        bool wasClient = mode == Mode.Client;
        session = null;
        joinCode = "";
        SetMode(Mode.Offline, wasClient ? "Disconnected from the host" : "Session closed");
        if (wasClient) Notify("Disconnected from the host - you keep playing alone");
    }

    // ---------- Setup ----------

    static async Task SignIn()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            // A different profile per game window, so two copies on one PC can play together
            InitializationOptions options = new InitializationOptions();
            options.SetProfile("p" + Guid.NewGuid().ToString("N").Substring(0, 16));
            await UnityServices.InitializeAsync(options);
        }
        while (UnityServices.State == ServicesInitializationState.Initializing) await Task.Yield();
        if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    // Uses the NetworkManager of the scene if there is one, else makes one. No player prefab, no scene syncing:
    // every player loads the world on their own computer and MultiplayerSync does the rest
    static NetworkManager PrepareNetworkManager()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null)
        {
            GameObject go = new GameObject("NetworkManager");
            DontDestroyOnLoad(go);
            go.AddComponent<UnityTransport>();
            nm = go.AddComponent<NetworkManager>();
        }
        else
        {
            if (nm.transform.parent != null) nm.transform.SetParent(null);
            if (nm.gameObject.scene.name != "DontDestroyOnLoad") DontDestroyOnLoad(nm.gameObject);
        }
        if (nm.IsListening) nm.Shutdown();

        if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
        UnityTransport transport = nm.GetComponent<UnityTransport>();
        if (transport == null) transport = nm.gameObject.AddComponent<UnityTransport>();
        nm.NetworkConfig.NetworkTransport = transport;
        nm.NetworkConfig.EnableSceneManagement = false;
        nm.NetworkConfig.PlayerPrefab = null;
        nm.NetworkConfig.ConnectionApproval = false;
        Application.runInBackground = true; // keep sending while the window is not focused
        return nm;
    }

    // The world scene is open and the player is in it
    static bool InWorld()
    {
        WorldSaveGameManager w = WorldSaveGameManager.instance;
        if (w != null && w.IsLoading) return false;
        if (w != null && !string.IsNullOrEmpty(w.worldSceneName) && SceneManager.GetActiveScene().name != w.worldSceneName) return false;
        return PlayerRef.Exists;
    }

    static string ShortError(Exception e)
    {
        string m = e.InnerException != null ? e.InnerException.Message : e.Message;
        if (m.Contains("not linked") || m.Contains("ProjectId") || m.Contains("project id")) return "link the project to Unity Cloud (Project Settings > Services)";
        return m.Length > 120 ? m.Substring(0, 120) + "..." : m;
    }

    void SetMode(Mode m, string text)
    {
        mode = m;
        status = text;
        StateChanged?.Invoke();
    }

    void SetStatus(string text)
    {
        status = text;
        StateChanged?.Invoke();
    }

    public static void Notify(string text)
    {
        if (Instance != null)
        {
            Instance.notice = text;
            Instance.noticeUntil = Time.unscaledTime + 4f;
        }
        Notified?.Invoke(text);
    }

    // ---------- Every frame ----------

    void Update()
    {
        // Hosting from the menu: the world finished loading
        if (hostWhenWorldLoaded && mode == Mode.Offline && InWorld())
        {
            hostWhenWorldLoaded = false;
            _ = HostAsync();
        }

        if (panelKey != KeyCode.None && Input.GetKeyDown(panelKey) && !PauseMenu.IsOpen)
        {
            if (panelOpen) ClosePanel();
            else OpenPanel();
        }
        if (panelOpen && Input.GetKeyDown(KeyCode.Escape)) ClosePanel();
    }

    public void OpenPanel()
    {
        panelOpen = true;
        nameInput = PlayerName;
        cursorBefore = Cursor.lockState;
        cursorVisibleBefore = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ClosePanel()
    {
        if (!panelOpen) return;
        panelOpen = false;
        if (!string.IsNullOrWhiteSpace(nameInput)) PlayerName = nameInput.Trim();
        Cursor.lockState = cursorBefore;
        Cursor.visible = cursorVisibleBefore;
    }
}
