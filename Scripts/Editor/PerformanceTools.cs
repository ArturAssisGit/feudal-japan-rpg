using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Performance helpers (menu: Tools > Performance). Everything can be undone with Ctrl+Z, and nothing touches LODs or meshes.
//   1 - Report            : prints what is heavy (trees, grass, heaviest objects, shadows) in the Console
//   2 - Terrain lighter   : tree / grass distance and density, terrain mesh detail, instanced terrain
//   3 - Mark Static       : sets Static (batching + occlusion) on scene objects that never move
//   4 - Shadows lighter   : shadow distance and cascades in the active render pipeline asset
//   Apply all (2 + 3 + 4) : then save the scene with Ctrl+S
// The numbers to tweak are right below.
public static class PerformanceTools
{
    // ----- Terrain -----
    const float TreeDistance = 250f;        // trees farther than this are not drawn at all (was 5000 = every tree of the map)
    const float TreeBillboardStart = 40f;   // only matters for trees that have billboards
    const int MaxFullLodTrees = 30;
    const float DetailDistance = 45f;       // grass / details
    const float DetailDensity = 0.5f;       // 1 = all grass, 0.5 = half
    const float HeightmapPixelError = 8f;   // higher = fewer vertices in the ground mesh (1 = most detailed, 200 = least)
    const float BasemapDistance = 300f;

    // ----- Shadows (render pipeline asset) -----
    const float ShadowDistance = 60f;
    const int ShadowCascades = 2;

    const string Menu = "Tools/Performance/";

    // ---------- 1 - Report ----------

    [MenuItem(Menu + "1 - Report (what is heavy)")]
    static void Report()
    {
        var sb = new StringBuilder("=== PERFORMANCE REPORT ===\n");

        // Terrains
        long worstCaseTriangles = 0, worstCaseGrassTriangles = 0;
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            TerrainData data = terrain.terrainData;
            sb.AppendLine($"\n[Terrain] {terrain.name}  heightmap {data.heightmapResolution}  pixelError {terrain.heightmapPixelError}  drawInstanced {terrain.drawInstanced}");
            sb.AppendLine($"  trees: distance {terrain.treeDistance}  billboardStart {terrain.treeBillboardDistance}  fullLOD {terrain.treeMaximumFullLODCount}");
            sb.AppendLine($"  grass: distance {terrain.detailObjectDistance}  density {terrain.detailObjectDensity}  prototypes {data.detailPrototypes.Length}  resolution {data.detailResolution}");

            TreePrototype[] prototypes = data.treePrototypes;
            var counts = new int[prototypes.Length];
            foreach (TreeInstance tree in data.treeInstances)
                if (tree.prototypeIndex >= 0 && tree.prototypeIndex < counts.Length) counts[tree.prototypeIndex]++;

            sb.AppendLine($"  tree instances: {data.treeInstanceCount}");
            for (int i = 0; i < prototypes.Length; i++)
            {
                GameObject prefab = prototypes[i].prefab;
                if (prefab == null) continue;
                bool hasLods;
                int tris = PrefabTriangles(prefab, out hasLods);
                long total = (long)tris * counts[i];
                worstCaseTriangles += total;
                sb.AppendLine($"    {prefab.name}: {counts[i]} trees x {tris} tris = {total / 1000000f:F2}M tris if all drawn  {(hasLods ? "(has LODs)" : "(NO LOD group)")}");
            }

            // Grass / details: how many instances exist and roughly how many are inside the draw distance
            DetailPrototype[] details = data.detailPrototypes;
            for (int d = 0; d < details.Length; d++)
            {
                DetailPrototype dp = details[d];
                int[,] layer = data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, d);
                long instances = 0;
                for (int y = 0; y < layer.GetLength(0); y++)
                    for (int x = 0; x < layer.GetLength(1); x++) instances += layer[y, x];

                int grassTris = 2;
                string kind = "texture (flat quads)";
                if (dp.usePrototypeMesh && dp.prototype != null)
                {
                    bool dummy;
                    grassTris = PrefabTriangles(dp.prototype, out dummy);
                    kind = "MESH " + dp.prototype.name;
                }
                float area = Mathf.Max(1f, data.size.x * data.size.z);
                float inRange = Mathf.Min(1f, Mathf.PI * terrain.detailObjectDistance * terrain.detailObjectDistance / area);
                long visible = (long)(instances * inRange * terrain.detailObjectDensity);
                long visibleTris = visible * grassTris;
                worstCaseGrassTriangles += visibleTris;
                sb.AppendLine($"  grass {d}: {kind}  {instances} instances painted, about {visible} inside {terrain.detailObjectDistance} m x {grassTris} tris = {visibleTris / 1000000f:F2}M tris");
            }
        }
        sb.AppendLine($"\nTrees, worst case with every tree drawn: {worstCaseTriangles / 1000000f:F1}M triangles (before shadows, which draw them again)");
        sb.AppendLine($"Grass near the camera (estimate, if the camera stands in the densest area): {worstCaseGrassTriangles / 1000000f:F1}M triangles");

        // Objects in the scene
        var items = new List<KeyValuePair<int, string>>();
        int renderers = 0, staticOnes = 0, shadowCasters = 0;
        long sceneTriangles = 0;
        foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            MeshFilter filter = r.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            int tris = MeshTriangles(filter.sharedMesh);
            renderers++;
            sceneTriangles += tris;
            if (r.gameObject.isStatic) staticOnes++;
            if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) shadowCasters++;
            items.Add(new KeyValuePair<int, string>(tris, r.gameObject.name));
        }
        items.Sort((a, b) => b.Key.CompareTo(a.Key));
        sb.AppendLine($"\n[Scene objects] {renderers} mesh renderers, {staticOnes} static, {shadowCasters} cast shadows, {sceneTriangles / 1000f:F0}K triangles");
        for (int i = 0; i < Mathf.Min(12, items.Count); i++) sb.AppendLine($"    {items[i].Value}: {items[i].Key} tris");

        // Lights and render pipeline
        int realtimeLights = 0;
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (l.shadows != LightShadows.None) realtimeLights++;
        sb.AppendLine($"\n[Lights] {realtimeLights} lights with shadows");

        RenderPipelineAsset asset = CurrentPipelineAsset();
        if (asset != null)
        {
            var so = new SerializedObject(asset);
            sb.AppendLine($"[Pipeline] {asset.name}: shadowDistance {Read(so, "m_ShadowDistance")}  cascades {Read(so, "m_ShadowCascadeCount")}  SRP Batcher {Read(so, "m_UseSRPBatcher")}  MSAA {Read(so, "m_MSAA")}  renderScale {Read(so, "m_RenderScale")}");
        }
        Debug.Log(sb.ToString());
    }

    static string Read(SerializedObject so, string name)
    {
        SerializedProperty p = so.FindProperty(name);
        if (p == null) return "?";
        switch (p.propertyType)
        {
            case SerializedPropertyType.Float: return p.floatValue.ToString("0.##");
            case SerializedPropertyType.Integer: return p.intValue.ToString();
            case SerializedPropertyType.Boolean: return p.boolValue.ToString();
            default: return "?";
        }
    }

    static int MeshTriangles(Mesh mesh)
    {
        long indices = 0;
        for (int i = 0; i < mesh.subMeshCount; i++) indices += mesh.GetIndexCount(i);
        return (int)(indices / 3);
    }

    // Triangles of one tree prefab (only its most detailed LOD if it has a LOD group)
    static int PrefabTriangles(GameObject prefab, out bool hasLods)
    {
        int total = 0;
        LODGroup group = prefab.GetComponent<LODGroup>();
        hasLods = group != null && group.lodCount > 1;
        if (group != null && group.lodCount > 0)
        {
            foreach (Renderer r in group.GetLODs()[0].renderers)
            {
                if (r == null) continue;
                MeshFilter f = r.GetComponent<MeshFilter>();
                if (f != null && f.sharedMesh != null) total += MeshTriangles(f.sharedMesh);
            }
            if (total > 0) return total;
        }
        foreach (MeshFilter f in prefab.GetComponentsInChildren<MeshFilter>())
            if (f.sharedMesh != null) total += MeshTriangles(f.sharedMesh);
        return total;
    }

    // ---------- 2 - Terrain ----------

    [MenuItem(Menu + "2 - Terrain lighter (trees, grass, ground mesh)")]
    static void LightenTerrain()
    {
        int count = 0;
        foreach (Terrain t in Terrain.activeTerrains)
        {
            Undo.RecordObject(t, "Lighter terrain");
            t.treeDistance = TreeDistance;
            t.treeBillboardDistance = TreeBillboardStart;
            t.treeMaximumFullLODCount = MaxFullLodTrees;
            t.detailObjectDistance = DetailDistance;
            t.detailObjectDensity = DetailDensity;
            t.heightmapPixelError = HeightmapPixelError;
            t.basemapDistance = BasemapDistance;
            t.drawInstanced = true;
            EditorUtility.SetDirty(t);
            count++;
        }
        if (count > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"Performance: {count} terrain(s) updated (trees {TreeDistance} m, grass {DetailDistance} m at {DetailDensity * 100f:F0}% density, pixel error {HeightmapPixelError}). Save the scene with Ctrl+S.");
    }

    // ---------- 3 - Static ----------

    [MenuItem(Menu + "3 - Mark Static (objects that never move)")]
    static void MarkStatic()
    {
        const StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
        int changed = 0, skipped = 0;

        foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            GameObject go = r.gameObject;
            if (go.scene != SceneManager.GetActiveScene()) continue;
            if (!CanBeStatic(go)) { skipped++; continue; }

            StaticEditorFlags current = GameObjectUtility.GetStaticEditorFlags(go);
            if ((current & flags) == flags) continue;
            Undo.RecordObject(go, "Mark static");
            GameObjectUtility.SetStaticEditorFlags(go, current | flags);
            EditorUtility.SetDirty(go);
            changed++;
        }
        if (changed > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"Performance: {changed} object(s) marked Static, {skipped} skipped because they can move (scripts, animations, physics, player, enemies). Save the scene with Ctrl+S.");
    }

    // Anything with a script, animation, physics or UI in itself or its parents might move, so it is left alone
    static bool CanBeStatic(GameObject go)
    {
        for (Transform t = go.transform; t != null; t = t.parent)
        {
            GameObject g = t.gameObject;
            if (g.tag == "Player" || g.tag == "Enemy") return false;
            if (g.GetComponent<Animator>() != null || g.GetComponent<Rigidbody>() != null || g.GetComponent<CharacterController>() != null) return false;
            if (g.GetComponent<Canvas>() != null || g.GetComponent<ParticleSystem>() != null) return false;
            foreach (MonoBehaviour m in g.GetComponents<MonoBehaviour>())
                if (m != null) return false;
        }
        return true;
    }

    // ---------- 4 - Shadows ----------

    [MenuItem(Menu + "4 - Shadows lighter (distance and cascades)")]
    static void LightenShadows()
    {
        RenderPipelineAsset asset = CurrentPipelineAsset();
        if (asset == null) { Debug.LogWarning("Performance: no render pipeline asset found."); return; }

        var so = new SerializedObject(asset);
        string before = $"distance {Read(so, "m_ShadowDistance")}, cascades {Read(so, "m_ShadowCascadeCount")}";
        Undo.RecordObject(asset, "Lighter shadows");
        SetFloat(so, "m_ShadowDistance", ShadowDistance);
        SetInt(so, "m_ShadowCascadeCount", ShadowCascades);
        SetBool(so, "m_UseSRPBatcher", true);
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        Debug.Log($"Performance: {asset.name} shadows {before} -> distance {ShadowDistance}, cascades {ShadowCascades} (SRP Batcher on).");
    }

    static RenderPipelineAsset CurrentPipelineAsset()
    {
        RenderPipelineAsset asset = QualitySettings.renderPipeline;
        return asset != null ? asset : GraphicsSettings.defaultRenderPipeline;
    }

    static void SetFloat(SerializedObject so, string name, float value) { SerializedProperty p = so.FindProperty(name); if (p != null) p.floatValue = value; else Debug.LogWarning("Performance: property not found: " + name); }
    static void SetInt(SerializedObject so, string name, int value) { SerializedProperty p = so.FindProperty(name); if (p != null) p.intValue = value; else Debug.LogWarning("Performance: property not found: " + name); }
    static void SetBool(SerializedObject so, string name, bool value) { SerializedProperty p = so.FindProperty(name); if (p != null) p.boolValue = value; else Debug.LogWarning("Performance: property not found: " + name); }

    // ---------- All ----------

    [MenuItem(Menu + "Apply all (2 + 3 + 4)")]
    static void ApplyAll()
    {
        LightenTerrain();
        MarkStatic();
        LightenShadows();
    }
}
