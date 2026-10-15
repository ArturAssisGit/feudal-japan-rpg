using System.Collections.Generic;
using UnityEngine;

// Put on the Terrain: painted trees whose prefab has a GatherNode become gatherable (terrain is not changed).
[RequireComponent(typeof(Terrain))]
public class TerrainGatherables : MonoBehaviour
{
    void Start()
    {
        Terrain terrain = GetComponent<Terrain>();
        TerrainData data = terrain.terrainData;
        if (data == null) return;

        TreePrototype[] prototypes = data.treePrototypes;
        GatherNode[] templates = new GatherNode[prototypes.Length];
        bool any = false;
        for (int i = 0; i < prototypes.Length; i++)
        {
            if (prototypes[i].prefab == null) continue;
            templates[i] = prototypes[i].prefab.GetComponentInChildren<GatherNode>(true);
            any |= templates[i] != null;
        }

        if (!any)
        {
            var names = new List<string>();
            foreach (TreePrototype p in prototypes) names.Add(p.prefab != null ? p.prefab.name : "empty");
            Debug.Log("TerrainGatherables: none of the painted trees has a GatherNode component. Trees on this terrain: " + string.Join(", ", names) +
                      ". In Paint Trees > Edit Trees, the Tree Prefab must be the prefab that has the GatherNode, not the model file.");
            return;
        }

        Transform parent = new GameObject("Gather Points (Terrain)").transform;
        parent.SetParent(transform, true);

        int count = 0;
        foreach (TreeInstance tree in data.treeInstances)
        {
            if (tree.prototypeIndex < 0 || tree.prototypeIndex >= templates.Length) continue;
            GatherNode template = templates[tree.prototypeIndex];
            if (template == null) continue;

            GameObject point = new GameObject(template.name + " (Gather)");
            point.transform.SetParent(parent, true);
            point.transform.position = terrain.transform.position + Vector3.Scale(tree.position, data.size);
            point.AddComponent<GatherNode>().CopySettings(template);
            count++;
        }

        Debug.Log($"TerrainGatherables: {count} gatherable trees/bushes/rocks on the terrain.");
    }
}
