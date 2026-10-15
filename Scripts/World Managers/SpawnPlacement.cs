using UnityEngine;
using UnityEngine.AI;

// Shared checks so enemies never appear on roofs, walls or other places they can't walk out of.
// A spot is good when it is close to the terrain height and the NavMesh connects it to the anchor
// (the spawner's own ground point, or the player). Roof tops are separate NavMesh "islands", so they fail.
public static class SpawnPlacement
{
    // Meters a spawn point may be above the terrain (higher = roof / platform)
    public const float MaxAboveTerrain = 1.5f;

    // The same point moved down/up to the terrain (for sampling the NavMesh near the ground, not on roofs)
    public static Vector3 OnTerrain(Vector3 point)
    {
        if (WorldUtil.TerrainHeight(point, out float h)) point.y = h;
        return point;
    }

    // A NavMesh point near the ground under 'point' (false if none)
    public static bool GroundNavPoint(Vector3 point, float radius, out Vector3 result)
    {
        if (NavMesh.SamplePosition(OnTerrain(point), out NavMeshHit hit, radius, NavMesh.AllAreas) && NotTooHigh(hit.position))
        {
            result = hit.position;
            return true;
        }
        result = point;
        return false;
    }

    public static bool NotTooHigh(Vector3 point)
    {
        return !WorldUtil.TerrainHeight(point, out float h) || point.y - h <= MaxAboveTerrain;
    }

    // Good spot: not high above the terrain, and walkable to/from the anchor
    public static bool IsGoodSpot(Vector3 point, Vector3 anchor)
    {
        if (!NotTooHigh(point)) return false;
        var path = new NavMeshPath();
        return NavMesh.CalculatePath(anchor, point, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
    }
}
