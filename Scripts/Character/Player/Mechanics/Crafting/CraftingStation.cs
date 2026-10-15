using System.Collections.Generic;
using UnityEngine;

// Put this on a bonfire, crafting table, etc. Walk up to it and press E to open the crafting window.
public class CraftingStation : MonoBehaviour
{
    public static readonly List<CraftingStation> All = new List<CraftingStation>();

    [Tooltip("Title of the window")] public string stationName = "Bonfire";
    [Tooltip("Word in the prompt: [E] Cook, [E] Craft...")] public string actionName = "Cook";
    [Tooltip("How close you must be to press E")] public float interactRange = 2.5f;
    [Tooltip("What can be made here (Create > Inventory > Recipe)")] public List<CraftingRecipe> recipes = new List<CraftingRecipe>();

    [Header("Upgrade Stones")]
    [Tooltip("On = this is a crafting table where you put an item + an upgrade stone (Unmei, Kake, Oni) and press Craft. Recipes are not used then.")]
    public bool upgradeTable;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
