# Inventory, backpack and loot cleanup

**Pillar:** Rewarding exploration
**Scripts:** `PlayerInventoryManager.cs`, `PlayerEquipmentManager.cs`, `DroppedBackpack.cs`, `ItemPickup.cs`, `LootBag.cs`, `WorldCleanup.cs`, `ItemUpgrades.cs`

## Goal

Equipment is the main way to progress, so finding, carrying and improving items must feel good and must never feel unfair, for example by losing everything to a bug.

## Player experience

- **Visible gear:** what the player equips appears on the character, including sheathed weapons.
- **A trade-off:** the backpack gives storage and a perk, but slows the player by 20% unless it has the right perk.
- **Safety:** the player's things are never deleted behind their back.

## Rules and numbers

- **Slots:** weapon, bow, chest, helmet, backpack, lantern. Consumables, utilities and stones stack to 99.
- **Backpack:** extra storage while worn, plus one perk:

| Perk | Effect |
|---|---|
| No Speed Penalty | The backpack does not slow you |
| Half Sprint Stamina | Running uses half the stamina |
| Double Money | Coins picked up are doubled |
| Quiver | Arrows +25% damage |
| Free Block | Blocking uses half the stamina |
| Lucky Find | Enemies sometimes drop an extra item |
| Quick Draw | Draw and sheathe twice as fast |
| Heavy Hitter | Strong attack +30%, walk 10% slower |

- **Dropping the backpack (R):** it falls with everything inside, appears on the map and is picked up with E. A backpack placed in a level can be equipped directly with E.
- **Upgrade stones** (once per item): Unmei +10% stats; Kake doubles the stats with a 50% chance of destroying the item; Oni gives a random demon perk (and a ghostly afterimage in combat).
- **Dropping items:** they go into a loot bag that merges with any bag nearby.

### World cleanup
| Leftover | Removed after |
|---|---|
| Enemy loot bags | 5 min (adjustable) |
| Items the player dropped | 5 min; the timer restarts when more is dropped into the bag |
| Arrows | 10 s |
| Enemy / animal bodies | 5 s / 90 s |

- Loot never disappears while the player is within 10 m, so it never vanishes in front of them or while they loot it.
- **Protected forever:** dropped backpacks, chests, and any bag that has a backpack inside.

## Iterations

| Problem | Change |
|---|---|
| Loot bags and dropped items piled up over long sessions | One central cleanup system instead of a separate timer in every script, with times set in one place |
| Risk: if a backpack full of loot disappeared, the player would lose everything | Backpacks are protected at registration *and* checked again right before removal |
| A backpack placed in the level showed no "Equip" prompt | A backpack item placed in a scene now turns itself into a dropped backpack, so it works like any other |

## What I would test next

- Whether 5 minutes is enough time to come back for loot after a hard fight.
- How often players use the Kake stone gamble, and whether 50% feels fair.
