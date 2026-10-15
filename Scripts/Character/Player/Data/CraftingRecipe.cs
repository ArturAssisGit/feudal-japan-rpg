using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class RecipeIngredient
{
    public PlayerItemData item;
    [Min(1)] public int amount = 1;
}

// Right click in the Project > Create > Inventory > Recipe
// Ingredients go in, the Result comes out (e.g. Red Berry + Clean Water = Red Tea)
[CreateAssetMenu(fileName = "NewRecipe", menuName = "Inventory/Recipe")]
public class CraftingRecipe : ScriptableObject
{
    [Tooltip("Name shown in the list (empty = the result's name)")] public string recipeName;
    public List<RecipeIngredient> ingredients = new List<RecipeIngredient>();
    public PlayerItemData result;
    [Min(1)] public int resultAmount = 1;

    public string DisplayName => !string.IsNullOrEmpty(recipeName) ? recipeName
        : result == null ? name
        : string.IsNullOrEmpty(result.itemName) ? result.name : result.itemName;
}
