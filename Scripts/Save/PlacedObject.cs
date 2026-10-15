using System.Collections.Generic;
using UnityEngine;

// Added automatically to things the player places (tents, bonfires, crafting tables...),
// so the save system knows which item they came from. PlacedObjectPickup uses it to put them back in the inventory.
public class PlacedObject : MonoBehaviour
{
    public static readonly List<PlacedObject> All = new List<PlacedObject>();

    [Tooltip("Name of the item this was placed from")] public string itemId;

    private PlayerItemData item;
    private bool itemLooked;
    private Bounds bounds;
    private bool boundsKnown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    // The item this object was placed from (looked up once)
    public PlayerItemData Item
    {
        get
        {
            if (!itemLooked) { itemLooked = true; item = ItemDatabase.Find(itemId); }
            return item;
        }
    }

    // Distance (meters) from a point to the nearest edge of this object, ignoring height
    public float DistanceTo(Vector3 point)
    {
        if (!boundsKnown)
        {
            boundsKnown = true;
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            bounds = new Bounds(transform.position, Vector3.one * 0.2f);
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
            }
        }
        point.y = Mathf.Clamp(point.y, bounds.min.y, bounds.max.y);
        return Vector3.Distance(point, bounds.ClosestPoint(point));
    }
}
