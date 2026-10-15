using System.Collections.Generic;
using UnityEngine;

// Put this on the tent prefab. Walk up to a placed tent and press E to make it your respawn point.
public class TentSpawn : MonoBehaviour
{
    public static readonly List<TentSpawn> All = new List<TentSpawn>();
    public static TentSpawn Selected { get; set; }

    [Tooltip("How close you must be to press E")] public float interactRange = 3f;

    void OnEnable()
    {
        All.Add(this);
        // The first tent you place becomes your respawn point automatically
        if (Selected == null) Selected = this;
    }

    void OnDisable()
    {
        All.Remove(this);
        // If the respawn tent disappears, another tent (if there is one) takes its place
        if (Selected == this) Selected = All.Count > 0 ? All[0] : null;
    }

    // A free spot on the ground right next to the tent
    public Vector3 GetSpawnPoint()
    {
        Bounds bounds = new Bounds(transform.position, Vector3.one);
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            bounds = renderers[0].bounds;
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
        }

        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + 0.8f;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * forward;
            Vector3 point = new Vector3(bounds.center.x, bounds.max.y + 2f, bounds.center.z) + dir * radius;
            if (!RespawnSystem.FindGround(point, out Vector3 ground)) continue;

            if (!Physics.CheckCapsule(ground + Vector3.up * 0.5f, ground + Vector3.up * 1.5f, 0.35f, Physics.AllLayers, QueryTriggerInteraction.Ignore)
                || OnlyTerrain(ground)) return ground;
        }
        return transform.position + forward * radius;
    }

    static bool OnlyTerrain(Vector3 ground)
    {
        foreach (Collider c in Physics.OverlapCapsule(ground + Vector3.up * 0.5f, ground + Vector3.up * 1.5f, 0.35f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            if (!(c is TerrainCollider) && !c.CompareTag("Player")) return false;
        return true;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
