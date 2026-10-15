using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Auto-created. Press the sleep key near a placed tent: fade, sky time-lapse of a FIXED number of hours (Sleep Hours),
// wake up rested and the tent becomes your respawn point. Add it to a scene object to change settings in the Inspector.
public class SleepSystem : MonoBehaviour
{
    public static SleepSystem Instance { get; private set; }
    public static bool IsSleeping { get; private set; }

    [Header("Sleep")]
    public KeyCode sleepKey = KeyCode.Z;
    [Tooltip("Game hours that pass while you sleep (fixed, the player can't change it)")] public float sleepHours = 8f;
    [Tooltip("Real seconds the time-lapse lasts")] public float timelapseSeconds = 12f;
    [Tooltip("You can't sleep if a living enemy is closer than this (meters, 0 = always allowed)")] public float enemyCheckRadius = 25f;
    [Tooltip("Seconds of fade to black / back")] public float fadeSeconds = 1f;

    [Header("When you wake up")]
    [Tooltip("The tent becomes your respawn point")] public bool setRespawnPoint = true;
    [Tooltip("Save the game when you wake up")] public bool saveOnWake = true;
    [Tooltip("Health restored (1 = full)")] [Range(0f, 1f)] public float healthRestored = 1f;
    public bool refillStamina = true;
    [Tooltip("Hunger lost during the sleep (it never drops below 5)")] public float hungerLoss = 10f;
    [Tooltip("Thirst lost during the sleep (it never drops below 5)")] public float thirstLoss = 15f;

    [Header("Camera")]
    [Tooltip("How much the camera looks up (degrees)")] [Range(0f, 90f)] public float cameraPitch = 50f;
    [Tooltip("Turns the view around (0 = looks toward where the sun rises)")] public float cameraYawOffset = 0f;
    [Tooltip("Camera height above the ground")] public float cameraHeight = 0.8f;
    [Tooltip("Camera distance from the edge of the tent")] public float cameraDistanceFromTent = 1.5f;
    [Tooltip("The camera looks for a spot with this much free sky in front of it (trees, roofs...)")] public float skyClearDistance = 30f;

    [Header("UI")]
    public bool showClock = true;
    public bool hidePlayer = true;
    public float statusFontSize = 40f;

    private TextMeshProUGUI promptText, messageText, statusText;
    private string sleepPrompt;
    private CanvasGroup fadeGroup;
    private float messageUntil, nextSleepAllowed;

    private Transform player;
    private PlayerStats stats;
    private bool sleeping;

    // Remembered while sleeping, put back afterwards
    private DayNightCycle clock;
    private bool oldPauseTime;
    private float oldAmbientInterval;
    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        IsSleeping = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null || FindAnyObjectByType<SleepSystem>() != null) return;
        WorldUtil.CreatePersistent<SleepSystem>("SleepSystem (auto)");
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        if (Instance == this) BuildUI();
    }

    void OnDisable()
    {
        if (Instance == this) EndSleep();
    }

    void Update()
    {
        if (Instance != this || promptText == null) return;

        if (messageText.text.Length > 0 && Time.time > messageUntil) messageText.text = "";
        if (sleeping)
        {
            promptText.gameObject.SetActive(false);
            return;
        }

        TentSpawn tent = FindPlayer() ? FindTentInRange() : null;
        promptText.gameObject.SetActive(tent != null && messageText.text.Length == 0);
        if (tent == null) return;

        if (sleepPrompt == null) sleepPrompt = $"[{sleepKey}] Sleep ({sleepHours:0.#}h)"; // built once, not every frame
        promptText.text = sleepPrompt;
        if (Input.GetKeyDown(sleepKey) && Time.time >= nextSleepAllowed && !ScreenFade.IsActive) TrySleep(tent);
    }

    TentSpawn FindTentInRange()
    {
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if ((gear != null && gear.IsOpen) || PlacementSystem.IsPlacing || RespawnSystem.IsDeathScreenOpen) return null;
        if (stats == null || stats.IsDead) return null;

        return WorldUtil.Nearest(TentSpawn.All, player.position, t => t.interactRange);
    }

    bool FindPlayer()
    {
        if (player != null && player.gameObject.activeInHierarchy && stats != null) return true;
        if (!PlayerRef.Find(ref player)) return false;
        stats = PlayerRef.Stats;
        return stats != null;
    }

    void ShowMessage(string text, float seconds = 2.5f)
    {
        messageText.text = text;
        messageUntil = Time.time + seconds;
    }

    // ---------- Sleeping ----------

    void TrySleep(TentSpawn tent)
    {
        DayNightCycle dayNight = DayNightCycle.Instance;
        if (dayNight == null)
        {
            ShowMessage("There is no day/night cycle in this scene");
            return;
        }
        if (stats.currentHunger <= 0f || stats.currentThirst <= 0f)
        {
            ShowMessage(stats.currentHunger <= 0f ? "You are too hungry to sleep" : "You are too thirsty to sleep");
            return;
        }
        if (enemyCheckRadius > 0f)
        {
            foreach (EnemyAI_01 enemy in EnemyAI_01.All)
            {
                if (enemy == null || enemy.IsDead || !enemy.gameObject.activeInHierarchy) continue;
                if (WorldUtil.FlatDistance(enemy.transform.position, player.position) <= enemyCheckRadius)
                {
                    ShowMessage("You can't sleep with enemies nearby");
                    return;
                }
            }
        }
        StartCoroutine(SleepRoutine(tent, dayNight));
    }

    IEnumerator SleepRoutine(TentSpawn tent, DayNightCycle dayNight)
    {
        sleeping = true;
        IsSleeping = true;
        bool finished = false, died = false;
        float startHour = dayNight.currentHour;
        float lastHealth = stats.currentHealth;

        try
        {
            yield return Fade(1f);

            foreach (PlayerCombat combat in player.GetComponentsInChildren<PlayerCombat>()) combat.ResetCombatState();
            if (hidePlayer) HidePlayer();

            PlaceCamera(tent);

            clock = dayNight;
            oldPauseTime = clock.pauseTime;
            oldAmbientInterval = clock.ambientUpdateInterval;
            clock.pauseTime = true;
            clock.ambientUpdateInterval = 0.5f;
            float rate = clock.dayLengthMinutes * 60f * sleepHours / (24f * Mathf.Max(1f, timelapseSeconds));
            SkyboxCycle.BlendSpeedMultiplier = Mathf.Clamp(rate * 0.25f, 1f, 30f);

            statusText.gameObject.SetActive(showClock);
            yield return Fade(0f);

            // Time-lapse: starts and ends gently
            float elapsed = 0f;
            float duration = Mathf.Max(1f, timelapseSeconds);
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float p = t * t * (3f - 2f * t);
                clock.currentHour = Mathf.Repeat(startHour + sleepHours * p, 24f);
                if (showClock) statusText.text = "Sleeping...\n" + FormatHour(clock.currentHour);

                if (stats.IsDead)
                {
                    died = true;
                    break;
                }
                if (stats.currentHealth < lastHealth - 0.01f) break; // something hurt you: you wake up
                lastHealth = stats.currentHealth;
                yield return null;
                if (elapsed >= duration) finished = true;
            }
            if (!died && !finished && elapsed >= duration) finished = true;

            statusText.gameObject.SetActive(false);
            if (died)
            {
                fadeGroup.alpha = 0f; // the death screen takes over
            }
            else
            {
                yield return Fade(1f);
                RestoreWorld();
                if (finished) ApplyRest(tent);
                yield return Fade(0f);
            }
        }
        finally
        {
            EndSleep();
        }

        if (died) yield break;

        nextSleepAllowed = Time.time + 1f;
        ShowMessage(finished ? "You feel rested" : "You were woken up!");
        if (finished && saveOnWake && WorldSaveGameManager.instance != null) WorldSaveGameManager.instance.SaveGame();
    }

    void ApplyRest(TentSpawn tent)
    {
        if (stats == null || stats.IsDead) return;
        stats.Heal(stats.maxHealth * healthRestored);
        if (refillStamina) stats.currentStamina = stats.maxStamina;
        stats.currentHunger = Mathf.Max(stats.currentHunger - hungerLoss, Mathf.Min(stats.currentHunger, 5f));
        stats.currentThirst = Mathf.Max(stats.currentThirst - thirstLoss, Mathf.Min(stats.currentThirst, 5f));
        if (setRespawnPoint && tent != null) TentSpawn.Selected = tent;
    }

    // Puts everything back (safe to call more than once)
    void RestoreWorld()
    {
        CameraController.ClearOverride();
        SkyboxCycle.BlendSpeedMultiplier = 1f;
        foreach (Renderer r in hiddenRenderers) if (r != null) r.enabled = true;
        hiddenRenderers.Clear();
        if (clock != null)
        {
            clock.pauseTime = oldPauseTime;
            clock.ambientUpdateInterval = oldAmbientInterval;
            clock = null;
        }
    }

    void EndSleep()
    {
        RestoreWorld();
        sleeping = false;
        IsSleeping = false;
        if (statusText != null) statusText.gameObject.SetActive(false);
        if (fadeGroup != null && !isActiveAndEnabled) fadeGroup.alpha = 0f;
    }

    void HidePlayer()
    {
        hiddenRenderers.Clear();
        foreach (Renderer r in player.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            r.enabled = false;
            hiddenRenderers.Add(r);
        }
    }

    // ---------- Camera ----------

    void PlaceCamera(TentSpawn tent)
    {
        Bounds bounds = new Bounds(tent.transform.position, Vector3.one);
        Renderer[] renderers = tent.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            bounds = renderers[0].bounds;
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
        }
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + cameraDistanceFromTent;

        float yaw = (DayNightCycle.Instance != null ? DayNightCycle.Instance.sunDirection : 0f) + cameraYawOffset;
        Quaternion rotation = Quaternion.Euler(-cameraPitch, yaw, 0f);
        Vector3 lookDirection = rotation * Vector3.forward;

        // First candidate: the side of the tent where the player is standing
        Vector3 toPlayer = Vector3.ProjectOnPlane(player.position - bounds.center, Vector3.up);
        if (toPlayer.sqrMagnitude < 0.01f) toPlayer = Vector3.forward;
        toPlayer.Normalize();

        Vector3 bestPosition = player.position + Vector3.up * (cameraHeight + 0.5f);
        float bestClear = -1f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * toPlayer;
            Vector3 point = new Vector3(bounds.center.x, bounds.max.y + 2f, bounds.center.z) + dir * radius;
            if (!RespawnSystem.FindGround(point, out Vector3 ground)) continue;

            Vector3 position = ground + Vector3.up * cameraHeight;
            float clear = ClearDistance(position, lookDirection, tent.transform);
            if (clear > bestClear)
            {
                bestClear = clear;
                bestPosition = position;
            }
            if (clear >= skyClearDistance) break;
        }
        CameraController.SetOverride(bestPosition, rotation);
    }

    // How far the camera can look that way before something blocks it (ignores the tent and the player)
    float ClearDistance(Vector3 from, Vector3 direction, Transform tent)
    {
        RaycastHit[] hits = Physics.RaycastAll(from, direction, skyClearDistance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            Transform t = hit.collider.transform;
            if (t.IsChildOf(tent) || t.IsChildOf(player)) continue;
            return hit.distance;
        }
        return skyClearDistance;
    }

    // ---------- UI ----------

    IEnumerator Fade(float target)
    {
        float speed = 1f / Mathf.Max(0.05f, fadeSeconds);
        while (!Mathf.Approximately(fadeGroup.alpha, target))
        {
            fadeGroup.alpha = Mathf.MoveTowards(fadeGroup.alpha, target, Time.unscaledDeltaTime * speed);
            yield return null;
        }
    }

    static string FormatHour(float hour)
    {
        int h = Mathf.FloorToInt(hour) % 24;
        int m = Mathf.FloorToInt((hour - Mathf.Floor(hour)) * 60f) % 60;
        return $"{h:00}:{m:00}";
    }

    void BuildUI()
    {
        TMP_FontAsset font = LootWindow.GameFont;
        Color textColor = UIStyle.Text;

        Transform canvas = UIKit.CreateCanvas("SleepCanvas", transform, 150).transform; // under the death screen (200)

        // Black screen for the fades
        GameObject fade = new GameObject("Fade", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        fade.transform.SetParent(canvas, false);
        UIKit.SetRect(fade.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image fadeImage = fade.GetComponent<Image>();
        fadeImage.color = Color.black;
        fadeImage.raycastTarget = false;
        fadeGroup = fade.GetComponent<CanvasGroup>();
        fadeGroup.alpha = 0f;
        fadeGroup.blocksRaycasts = false;
        fadeGroup.interactable = false;

        // Clock at the bottom while sleeping
        statusText = UIKit.CreateText("SleepStatus", canvas, "", statusFontSize, textColor, font);
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.textWrappingMode = TextWrappingModes.Normal;
        RectTransform statusRect = statusText.rectTransform;
        statusRect.anchorMin = statusRect.anchorMax = statusRect.pivot = new Vector2(0.5f, 0f);
        statusRect.anchoredPosition = new Vector2(0f, 110f);
        statusRect.sizeDelta = new Vector2(900f, 160f);
        statusText.gameObject.SetActive(false);

        // Interaction texts (top-left corner, under the "set respawn point" line)
        promptText = UIKit.CornerText("SleepPrompt", canvas, 1, true);
        messageText = UIKit.CornerText("SleepMessage", canvas, 1);
    }
}
