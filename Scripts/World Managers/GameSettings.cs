using System.Collections.Generic;
using UnityEngine;

// The player's options from the Settings window (Controls / Gameplay / Video / Audio).
// Saved on this computer (PlayerPrefs), not in the save files, so they are the same for every save.
// Read by the camera, the sound, the save manager... Video options are applied when the game starts.
public static class GameSettings
{
    // ---------- Controls ----------
    public static float MouseSensitivity { get => GetF("set.mouseSensitivity", 1f); set => SetF("set.mouseSensitivity", value); }
    public static bool InvertY { get => GetB("set.invertY", false); set => SetB("set.invertY", value); }

    // ---------- Gameplay ----------
    public static bool CameraShake { get => GetB("set.cameraShake", true); set => SetB("set.cameraShake", value); }
    public static bool ScenicBars { get => GetB("set.scenicBars", true); set => SetB("set.scenicBars", value); }
    // Minutes between autosaves (0 = off, -1 = the value set in the WorldSaveGameManager Inspector)
    public static int AutosaveMinutes { get => PlayerPrefs.GetInt("set.autosaveMinutes", -1); set => SetI("set.autosaveMinutes", value); }

    // ---------- Video ----------
    // Field of view of the camera (0 = the camera's own value)
    public static float Fov { get => GetF("set.fov", 0f); set => SetF("set.fov", value); }
    public static bool VSync { get => GetB("set.vsync", QualitySettings.vSyncCount > 0); set { SetB("set.vsync", value); ApplyVideo(); } }
    // 0 = no limit
    public static int FpsLimit { get => PlayerPrefs.GetInt("set.fpsLimit", 0); set { SetI("set.fpsLimit", value); ApplyVideo(); } }
    public static int Quality
    {
        get => PlayerPrefs.GetInt("set.quality", QualitySettings.GetQualityLevel());
        set { SetI("set.quality", value); ApplyQuality(); }
    }

    // ---------- Audio (0 to 1) ----------
    public static float Master { get => GetF("set.volMaster", 1f); set { SetF("set.volMaster", value); ApplyAudio(); } }
    public static float Music { get => GetF("set.volMusic", 1f); set => SetF("set.volMusic", value); }
    public static float Ambience { get => GetF("set.volAmbience", 1f); set => SetF("set.volAmbience", value); }
    public static float Effects { get => GetF("set.volEffects", 1f); set => SetF("set.volEffects", value); }

    // ---------- Screen ----------

    public static readonly FullScreenMode[] DisplayModes = { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
    public static readonly string[] DisplayModeNames = { "Fullscreen", "Borderless", "Windowed" };

    // Every screen size the monitor supports (each size once, smallest first)
    public static List<Vector2Int> Resolutions()
    {
        var list = new List<Vector2Int>();
        foreach (Resolution r in Screen.resolutions)
        {
            var size = new Vector2Int(r.width, r.height);
            if (!list.Contains(size)) list.Add(size);
        }
        var current = new Vector2Int(Screen.width, Screen.height);
        if (!list.Contains(current)) list.Add(current);
        list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return list;
    }

    public static void SetScreen(Vector2Int size, FullScreenMode mode)
    {
        SetI("set.resWidth", size.x);
        SetI("set.resHeight", size.y);
        SetI("set.displayMode", (int)mode);
        Screen.SetResolution(size.x, size.y, mode);
    }

    // ---------- Applying ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplyOnStart()
    {
        if (PlayerPrefs.HasKey("set.quality")) ApplyQuality();
        else ApplyVideo();
        if (PlayerPrefs.HasKey("set.resWidth") && !Application.isEditor)
            Screen.SetResolution(PlayerPrefs.GetInt("set.resWidth"), PlayerPrefs.GetInt("set.resHeight"), (FullScreenMode)PlayerPrefs.GetInt("set.displayMode", (int)Screen.fullScreenMode));
        ApplyAudio();
    }

    static void ApplyQuality()
    {
        int q = Mathf.Clamp(PlayerPrefs.GetInt("set.quality", QualitySettings.GetQualityLevel()), 0, QualitySettings.names.Length - 1);
        if (q != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(q, true);
        ApplyVideo(); // the quality level resets VSync, so it is applied again after
    }

    static void ApplyVideo()
    {
        if (PlayerPrefs.HasKey("set.vsync")) QualitySettings.vSyncCount = GetB("set.vsync", true) ? 1 : 0;
        int fps = PlayerPrefs.GetInt("set.fpsLimit", 0);
        Application.targetFrameRate = fps > 0 ? fps : -1;
    }

    // The master volume (the screen fade multiplies its own fade by this too)
    public static void ApplyAudio()
    {
        if (!ScreenFade.IsActive) AudioListener.volume = Master;
    }

    // ---------- PlayerPrefs ----------

    static float GetF(string key, float fallback) => PlayerPrefs.GetFloat(key, fallback);
    static bool GetB(string key, bool fallback) => PlayerPrefs.GetInt(key, fallback ? 1 : 0) == 1;
    static void SetF(string key, float value) { PlayerPrefs.SetFloat(key, value); PlayerPrefs.Save(); }
    static void SetB(string key, bool value) { PlayerPrefs.SetInt(key, value ? 1 : 0); PlayerPrefs.Save(); }
    static void SetI(string key, int value) { PlayerPrefs.SetInt(key, value); PlayerPrefs.Save(); }
}
