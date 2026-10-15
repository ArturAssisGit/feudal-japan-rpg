using System;
using System.Collections.Generic;
using UnityEngine;

// Small world helpers used by many scripts (one place instead of a copy in every file):
// flat distances, terrain height and slope, hour ranges, allowed areas / safe places and weighted random picks.
public static class WorldUtil
{
    // A new object that survives scene changes, with the component T on it (the auto-created managers use it)
    public static T CreatePersistent<T>(string name) where T : Component
    {
        GameObject go = new GameObject(name);
        UnityEngine.Object.DontDestroyOnLoad(go);
        return go.AddComponent<T>();
    }

    // The vector without its height
    public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // Distance on the ground (height ignored)
    public static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // The terrain under a point (works with several terrains). False when there is no terrain there
    public static bool TerrainAt(Vector3 point, out Terrain terrain)
    {
        foreach (Terrain t in Terrain.activeTerrains)
        {
            if (t == null || t.terrainData == null) continue;
            Vector3 p = t.GetPosition();
            Vector3 size = t.terrainData.size;
            if (point.x < p.x || point.z < p.z || point.x > p.x + size.x || point.z > p.z + size.z) continue;
            terrain = t;
            return true;
        }
        terrain = null;
        return false;
    }

    // Height of the terrain under a point, if there is a terrain there
    public static bool TerrainHeight(Vector3 point, out float height)
    {
        if (TerrainAt(point, out Terrain t))
        {
            height = t.GetPosition().y + t.SampleHeight(point);
            return true;
        }
        height = 0f;
        return false;
    }

    // Steepness of the terrain under a point in degrees (0 when there is no terrain)
    public static float TerrainSlope(Vector3 point)
    {
        if (!TerrainAt(point, out Terrain t)) return 0f;
        Vector3 local = point - t.GetPosition();
        Vector3 size = t.terrainData.size;
        return t.terrainData.GetSteepness(local.x / size.x, local.z / size.z);
    }

    // Ground height under a point: the terrain, else whatever collider is below, else the point's own height
    public static float GroundHeight(Vector3 point)
    {
        if (TerrainHeight(point, out float h)) return h;
        if (Physics.Raycast(point + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
        return point.y;
    }

    // The closest one (on the ground) that is inside its own range, or null. 'usable' skips some (empty bags...)
    public static T Nearest<T>(IEnumerable<T> items, Vector3 from, Func<T, float> rangeOf, Func<T, bool> usable = null) where T : Component
    {
        T best = null;
        float bestDistance = float.MaxValue;
        foreach (T item in items)
        {
            if (item == null || (usable != null && !usable(item))) continue;
            float d = FlatDistance(from, item.transform.position);
            if (d <= rangeOf(item) && d < bestDistance)
            {
                bestDistance = d;
                best = item;
            }
        }
        return best;
    }

    // Is the hour inside From..To? From = To means any time; 20 -> 5 crosses midnight
    public static bool HourInRange(float hour, float from, float to)
    {
        if (Mathf.Approximately(from, to)) return true;
        return from < to ? hour >= from && hour < to : hour >= from || hour < to;
    }

    // The current hour of the day (12 when there is no DayNightCycle)
    public static float CurrentHour => DayNightCycle.Instance != null ? DayNightCycle.Instance.currentHour : 12f;

    // Inside one of the trigger areas? An empty list means "anywhere"
    public static bool InAnyArea(List<Collider> areas, Vector3 position)
    {
        if (areas == null || areas.Count == 0) return true;
        foreach (Collider area in areas)
            if (area != null && area.enabled && area.bounds.Contains(position) && (area.ClosestPoint(position) - position).sqrMagnitude < 0.01f) return true;
        return false;
    }

    // Closer than 'radius' (on the ground) to one of the places?
    public static bool NearAny(List<Transform> places, Vector3 position, float radius)
    {
        if (places == null) return false;
        foreach (Transform place in places)
            if (place != null && Flat(place.position - position).sqrMagnitude < radius * radius) return true;
        return false;
    }

    // Picks one of the options at random: a higher weight = picked more often. Null when nothing can be picked
    public static T PickWeighted<T>(List<T> options, Func<T, float> weightOf) where T : class
    {
        float total = 0f;
        foreach (T o in options) total += Mathf.Max(0f, weightOf(o));
        if (options.Count == 0 || total <= 0f) return null;
        float roll = UnityEngine.Random.value * total;
        foreach (T o in options)
        {
            roll -= Mathf.Max(0f, weightOf(o));
            if (roll <= 0f) return o;
        }
        return options[options.Count - 1];
    }
}
