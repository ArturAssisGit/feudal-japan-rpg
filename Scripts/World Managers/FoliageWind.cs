using System.Collections.Generic;
using UnityEngine;

// Stronger foliage wind in rain/storms (Wind Zone, terrain Waving Grass, wind material props); added by RainSystem, restored on stop.
public class FoliageWind : MonoBehaviour
{
    [Header("Wind Zone (trees)")]
    [Tooltip("Uses the Wind Zone in the scene, or creates one if there is none")] public bool controlWindZone = true;
    [Tooltip("Wind strength when it's not raining (only used when a Wind Zone is created)")] public float calmWindMain = 0.3f;
    [Tooltip("Turbulence when it's not raining (only used when a Wind Zone is created)")] public float calmTurbulence = 0.2f;
    [Tooltip("Turn the Wind Zone to blow the same way as the rain")] public bool followRainDirection = true;

    [Header("Terrain grass")]
    [Tooltip("Changes the Waving Grass settings of the Terrain")] public bool controlTerrainGrass = true;

    [Header("Wind materials")]
    [Tooltip("Looks for materials with these properties and makes them stronger in the rain")]
    public string[] strengthProperties = { "Wind_Intensity", "Wind_Blast", "Wind_Ripples", "_WindStrength", "_Wind_Strength" };
    [Tooltip("These get faster in the rain (half as much as the strength)")]
    public string[] speedProperties = { "Wind_Speed", "_WindSpeed", "_Wind_Speed" };
    [Tooltip("Extra materials to control (if some plant isn't found automatically)")] public Material[] extraMaterials;

    [Header("Debug")]
    [Tooltip("Current wind multiplier (1 = calm)")] [SerializeField] private float currentMultiplier = 1f;

    private WindZone windZone;
    private bool createdWindZone;
    private float baseMain, baseTurbulence, basePulseMagnitude;
    private Quaternion baseRotation;

    private struct GrassBackup { public TerrainData data; public float strength, speed; public Color tint; public float amount; }
    private readonly List<GrassBackup> grass = new List<GrassBackup>();

    private struct PropBackup { public Material mat; public int id; public float value; public bool isSpeed; }
    private readonly List<PropBackup> props = new List<PropBackup>();

    private float appliedMultiplier = -1f;

    void Start()
    {
        if (controlWindZone) SetupWindZone();
        if (controlTerrainGrass) SetupGrass();
        SetupMaterials();
    }

    void SetupWindZone()
    {
        windZone = FindAnyObjectByType<WindZone>();
        if (windZone == null)
        {
            var go = new GameObject("Wind Zone (Weather)");
            go.transform.SetParent(transform, false);
            windZone = go.AddComponent<WindZone>();
            windZone.mode = WindZoneMode.Directional;
            windZone.windMain = calmWindMain;
            windZone.windTurbulence = calmTurbulence;
            windZone.windPulseMagnitude = 0.5f;
            windZone.windPulseFrequency = 0.1f;
            createdWindZone = true;
        }
        baseMain = windZone.windMain;
        baseTurbulence = windZone.windTurbulence;
        basePulseMagnitude = windZone.windPulseMagnitude;
        baseRotation = windZone.transform.rotation;
    }

    void SetupGrass()
    {
        foreach (Terrain t in Terrain.activeTerrains)
        {
            if (t == null || t.terrainData == null) continue;
            TerrainData d = t.terrainData;
            grass.Add(new GrassBackup { data = d, strength = d.wavingGrassStrength, speed = d.wavingGrassSpeed, amount = d.wavingGrassAmount, tint = d.wavingGrassTint });
        }
    }

    void SetupMaterials()
    {
        var mats = new HashSet<Material>();

        foreach (Renderer r in FindObjectsByType<Renderer>()) AddMaterialsList(r.sharedMaterials, mats);

        // Terrain trees and detail meshes are drawn with their prefab's materials
        foreach (Terrain t in Terrain.activeTerrains)
        {
            if (t == null || t.terrainData == null) continue;
            foreach (TreePrototype tp in t.terrainData.treePrototypes)
                if (tp.prefab != null) AddPrefabMaterials(tp.prefab, mats);
            foreach (DetailPrototype dp in t.terrainData.detailPrototypes)
                if (dp.prototype != null) AddPrefabMaterials(dp.prototype, mats);
            if (t.materialTemplate != null) mats.Add(t.materialTemplate);
        }

        if (extraMaterials != null) AddMaterialsList(extraMaterials, mats);

        foreach (Material m in mats)
        {
            AddProps(m, strengthProperties, false);
            AddProps(m, speedProperties, true);
        }
        if (props.Count > 0) Debug.Log($"FoliageWind: controlling {props.Count} wind settings on materials.");
    }

    static void AddPrefabMaterials(GameObject prefab, HashSet<Material> mats)
    {
        foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true)) AddMaterialsList(r.sharedMaterials, mats);
    }

    static void AddMaterialsList(Material[] list, HashSet<Material> mats)
    {
        foreach (Material m in list) if (m != null) mats.Add(m);
    }

    void AddProps(Material m, string[] names, bool isSpeed)
    {
        if (names == null) return;
        foreach (string n in names)
        {
            if (string.IsNullOrEmpty(n) || !m.HasProperty(n)) continue;
            var id = Shader.PropertyToID(n);
            props.Add(new PropBackup { mat = m, id = id, value = m.GetFloat(id), isSpeed = isSpeed });
        }
    }

    void Update()
    {
        float k = RainSystem.FoliageWindMultiplier; // 1 = calm, 3 = storm...
        currentMultiplier = k;

        if (windZone != null)
        {
            windZone.windMain = baseMain * k;
            windZone.windTurbulence = baseTurbulence * k;
            windZone.windPulseMagnitude = basePulseMagnitude * Mathf.Lerp(1f, 1.5f, Mathf.Clamp01(k - 1f));

            Vector3 dir = RainSystem.WindDirection;
            dir.y = 0f;
            if (followRainDirection && dir.sqrMagnitude > 0.01f && RainSystem.RainAmount > 0.01f)
                windZone.transform.rotation = Quaternion.Slerp(windZone.transform.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up), Time.deltaTime * 0.5f);
        }

        // Materials and terrain only need updating when the value changes
        if (Mathf.Abs(k - appliedMultiplier) < 0.005f) return;
        appliedMultiplier = k;
        float speedK = 1f + (k - 1f) * 0.5f;

        foreach (GrassBackup g in grass)
        {
            if (g.data == null) continue;
            g.data.wavingGrassStrength = Mathf.Clamp01(g.strength * k);
            g.data.wavingGrassAmount = Mathf.Clamp01(g.amount * k);
            g.data.wavingGrassSpeed = Mathf.Clamp01(g.speed * speedK);
        }

        foreach (PropBackup p in props)
            if (p.mat != null) p.mat.SetFloat(p.id, p.value * (p.isSpeed ? speedK : k));
    }

    // Put everything back (materials and terrain are project files - they must not stay changed)
    void OnDisable() => Restore();
    void OnApplicationQuit() => Restore();

    void Restore()
    {
        foreach (GrassBackup g in grass)
        {
            if (g.data == null) continue;
            g.data.wavingGrassStrength = g.strength;
            g.data.wavingGrassAmount = g.amount;
            g.data.wavingGrassSpeed = g.speed;
            g.data.wavingGrassTint = g.tint;
        }
        foreach (PropBackup p in props)
            if (p.mat != null) p.mat.SetFloat(p.id, p.value);

        if (windZone != null && !createdWindZone)
        {
            windZone.windMain = baseMain;
            windZone.windTurbulence = baseTurbulence;
            windZone.windPulseMagnitude = basePulseMagnitude;
            windZone.transform.rotation = baseRotation;
        }
        appliedMultiplier = -1f;
    }
}
