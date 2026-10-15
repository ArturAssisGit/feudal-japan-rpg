using System;
using System.Collections.Generic;
using UnityEngine;

// Everything that goes into the save file (turned into JSON by WorldSaveGameManager)
[Serializable]
public class SaveData
{
    public int version = 1;
    public string sceneName;
    public string savedAt;
    [Tooltip("Auto, Quick or Manual")] public string kind = "";

    [Header("Player")]
    public Vector3 playerPosition;
    public float playerYaw;
    public float health;
    public float stamina;
    public float hunger;
    public float thirst;

    [Header("World")]
    public float hour = -1f;

    [Header("Items")]
    public GearData gear = new GearData();
    public List<SlotData> inventory = new List<SlotData>();
    public List<SlotData> backpack = new List<SlotData>();

    [Header("Placed tents, bonfires, crafting tables...")]
    public List<PlacedData> placed = new List<PlacedData>();

    [Header("Delivery in progress (package from an NPC)")]
    public DeliveryState delivery = new DeliveryState();

    [Header("Horses you own, the one you chose, and where it is standing")]
    public HorseSave horses = new HorseSave();

    [Header("Items lying in the world that you already picked up (they don't come back)")]
    public List<string> pickedUp = new List<string>();

    [Header("Map: the parts you already explored")]
    public string mapExplored;

    [Header("Loot bags and chests that were in the scene (what is left inside)")]
    public bool hasLootState;
    public List<LootState> loot = new List<LootState>();
}

[Serializable]
public class LootState
{
    public string key;
    public List<string> items = new List<string>();
}

[Serializable]
public class SlotData
{
    public int index;
    public string item;
    public int amount;
    [Tooltip("Upgrade stone used on this item (empty = none)")] public string upgrade;
}

[Serializable]
public class GearData
{
    public string weapon;
    public string chest;
    public string helmet;
    public string bow;
    public string backpack;
    public string lantern;
    public string weaponUpgrade;
    public string chestUpgrade;
    public string helmetUpgrade;
    public string bowUpgrade;
}

[Serializable]
public class PlacedData
{
    public string item;
    public Vector3 position;
    public Quaternion rotation;
    public bool respawnTent;
}

[Serializable]
public class DeliveryState
{
    public bool active;
    public string giverKey;
    public string targetKey;
    public string cargo;
    public int reward;
    public int completed;
}
