using UnityEngine;

// The three upgrade stones used at a crafting table (set on the stone item: Upgrade Stone)
public enum UpgradeStone { None, Unmei, Kake, Oni }

// Feudal Japanese demons the Oni Stone can bind to an item. Each one gives a special perk while the item is equipped.
public enum OniSpirit { None, Oni, Kitsune, Tengu, Kappa, Nue, Gashadokuro }

// Applies upgrade stones (one per item) to weapons, bows, armor and helmets.
// An upgraded item is a copy of the original with new stats, so every other system keeps working.
public static class ItemUpgrades
{
    public const float UnmeiBonus = 0.10f;      // +10% stats, always
    public const float KakeSuccessChance = 0.5f; // 50% double stats, 50% the item is destroyed

    // ---------- Perk values ----------
    public const float OniRageBonus = 0.30f;         // +30% damage while below half health
    public const float KitsuneLifeSteal = 0.10f;     // heals 10% of melee damage dealt
    public const float TenguSpeedBonus = 0.20f;      // +15% movement speed
    public const float KappaThirstMultiplier = 0.5f; // thirst drops half as fast
    public const float NueStaminaBonus = 0.30f;      // stamina recovers 40% faster
    public const float GashadokuroDamageTaken = 0.8f; // takes 20% less damage

    public static bool CanUpgrade(PlayerItemData item) =>
        item != null && !item.IsUpgraded && !item.IsUpgradeStone && item.type != ItemType.Stone &&
        (item.type == ItemType.Weapon || item.type == ItemType.Bow || item.type == ItemType.Chest || item.type == ItemType.Helmet);

    // Uses the stone on the item. Returns the new item, or null when the item was destroyed (Kake Stone).
    public static PlayerItemData Apply(PlayerItemData item, UpgradeStone stone, out string message)
    {
        string itemName = DisplayName(item);
        switch (stone)
        {
            case UpgradeStone.Unmei:
                message = $"{itemName} was refined: stats +{UnmeiBonus * 100f:0}%";
                return Create(item, UpgradeStone.Unmei, OniSpirit.None);

            case UpgradeStone.Kake:
                if (Random.value < KakeSuccessChance)
                {
                    message = $"Success! {itemName} doubled its stats";
                    return Create(item, UpgradeStone.Kake, OniSpirit.None);
                }
                message = $"The Kake Stone shattered... {itemName} was destroyed";
                return null;

            case UpgradeStone.Oni:
                OniSpirit spirit = (OniSpirit)Random.Range(1, System.Enum.GetValues(typeof(OniSpirit)).Length);
                message = $"A {spirit} possessed {itemName}: {SpiritPerk(spirit)}";
                return Create(item, UpgradeStone.Oni, spirit);

            default:
                message = "That is not an upgrade stone";
                return item;
        }
    }

    // Makes the upgraded copy of an item (also used when loading a save)
    public static PlayerItemData Create(PlayerItemData baseItem, UpgradeStone stone, OniSpirit spirit)
    {
        if (baseItem == null || stone == UpgradeStone.None) return baseItem;

        PlayerItemData copy = Object.Instantiate(baseItem);
        copy.name = baseItem.name; // the save file finds it by this name
        copy.baseItem = baseItem;
        copy.appliedStone = stone;
        copy.oniSpirit = stone == UpgradeStone.Oni ? spirit : OniSpirit.None;

        string baseName = DisplayName(baseItem);
        float multiplier = 1f, priceMultiplier = 1f;
        switch (stone)
        {
            case UpgradeStone.Unmei:
                multiplier = 1f + UnmeiBonus;
                priceMultiplier = 1.5f;
                copy.itemName = "Refined " + baseName;
                break;
            case UpgradeStone.Kake:
                multiplier = 2f;
                priceMultiplier = 3f;
                copy.itemName = "Kake-forged " + baseName;
                break;
            case UpgradeStone.Oni:
                priceMultiplier = 2f;
                copy.itemName = $"{baseName} of the {copy.oniSpirit}";
                break;
        }

        copy.weaponDamage = Scale(baseItem.weaponDamage, multiplier);
        copy.armorDefense = Scale(baseItem.armorDefense, multiplier);
        copy.price = Mathf.RoundToInt(baseItem.price * priceMultiplier);
        return copy;
    }

    // At least +1 so small numbers still go up
    static float Scale(float value, float multiplier) =>
        value <= 0f || multiplier <= 1f ? value : Mathf.Max(Mathf.Round(value * multiplier * 10f) / 10f, value + 1f);

    // ---------- Save file ----------

    // Text saved next to the item name ("" = not upgraded)
    public static string Encode(PlayerItemData item) =>
        item == null || !item.IsUpgraded ? "" : item.appliedStone == UpgradeStone.Oni ? "Oni:" + item.oniSpirit : item.appliedStone.ToString();

    public static PlayerItemData Restore(PlayerItemData baseItem, string code)
    {
        if (baseItem == null || string.IsNullOrEmpty(code)) return baseItem;

        string[] parts = code.Split(':');
        if (!System.Enum.TryParse(parts[0], out UpgradeStone stone)) return baseItem;
        OniSpirit spirit = OniSpirit.None;
        if (parts.Length > 1) System.Enum.TryParse(parts[1], out spirit);
        return Create(baseItem, stone, spirit);
    }

    // ---------- Perks (read by combat, movement and stats) ----------

    public static bool Active(OniSpirit spirit) => PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.HasSpirit(spirit);

    // Oni: more damage while hurt
    public static float DamageMultiplier(PlayerStats stats) =>
        stats != null && Active(OniSpirit.Oni) && stats.currentHealth < stats.maxHealth * 0.5f ? 1f + OniRageBonus : 1f;

    // ---------- Texts ----------

    public static string StoneEffect(UpgradeStone stone) => stone switch
    {
        UpgradeStone.Unmei => $"Upgrade stone: +{UnmeiBonus * 100f:0}% stats, always works",
        UpgradeStone.Kake => "Upgrade stone: 50% double stats, 50% destroys the item",
        UpgradeStone.Oni => "Upgrade stone: a demon possesses the item and gives it a perk",
        _ => ""
    };

    public static string UpgradeLabel(PlayerItemData item)
    {
        if (item == null || !item.IsUpgraded) return "";
        return item.appliedStone switch
        {
            UpgradeStone.Unmei => "<color=#9BE08A>Unmei Stone: +10% stats</color>",
            UpgradeStone.Kake => "<color=#E8C66A>Kake Stone: double stats</color>",
            UpgradeStone.Oni => $"<color=#FF6A5A>Possessed by a {item.oniSpirit}: {SpiritPerk(item.oniSpirit)}</color>",
            _ => ""
        };
    }

    public static string SpiritPerk(OniSpirit spirit) => spirit switch
    {
        OniSpirit.Oni => $"Rage, +{OniRageBonus * 100f:0}% damage below half health",
        OniSpirit.Kitsune => $"Fox Fire, heals {KitsuneLifeSteal * 100f:0}% of melee damage dealt",
        OniSpirit.Tengu => $"Mountain Wind, +{TenguSpeedBonus * 100f:0}% movement speed",
        OniSpirit.Kappa => "River Spirit, thirst drops half as fast",
        OniSpirit.Nue => $"Thunder Beast, stamina recovers {NueStaminaBonus * 100f:0}% faster",
        OniSpirit.Gashadokuro => $"Bone Armor, takes {(1f - GashadokuroDamageTaken) * 100f:0}% less damage",
        _ => ""
    };

    public static string DisplayName(PlayerItemData item) =>
        item == null ? "" : string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName;
}
