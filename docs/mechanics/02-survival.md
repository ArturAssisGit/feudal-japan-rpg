# Survival: needs, sleep and the sea border

**Pillar:** Survival that pressures without frustrating
**Scripts:** `PlayerStats.cs`, `QuickSlots.cs`, `SleepSystem.cs`, `RespawnSystem.cs`, `WaterVolume.cs`

## Goal

Make the player plan trips (food, water, a place to sleep) without turning survival into a chore or killing them by surprise.

## Player experience

- **Rhythm:** the player keeps a light eye on two meters, and they become important on long trips.
- **Warning before damage:** an icon appears before anything bad happens.
- **Safe camp:** the tent is the safe place where the player sleeps, saves and respawns.

## Rules and numbers

| Need | Drop | Empty after | Low (< 25%) | Empty (0) |
|---|---|---|---|---|
| Hunger | 5 / min | ~20 min | No health regen, stamina regen x0.5 | -1 health / s |
| Thirst | 8 / min | ~12.5 min | Same | -1 health / s |

- **Health** regenerates 3/s after 4 s without damage, but only while both needs are above 25%.
- **Quick slots (3, 4, 5):** hover a consumable with the inventory open and press a number to assign it; in game the same key eats or drinks one. The slots fade away when not used, so the HUD stays clean.
- **Sleep (Z, near a tent):** skips 8 hours with a sky time-lapse, not allowed with enemies within 25 m. Waking up restores health, costs a little hunger and thirst, saves the game and sets the respawn point.
- **Death:** the player chooses **Wake up nearby** (30–50 m away) or **Respawn at tent**. No items are lost for now.

### The sea border
- The player can wade into the water normally.
- When the water reaches 1.2 m above the feet, the screen fades out (0.8 s), the player is placed 10 m back on dry land in idle, and the screen fades in (0.8 s).
- Dry spots are recorded every 0.25 s while the player is grounded out of the water, so the return point is always a place the player really stood on.

## Iterations

| Version | What happened | Decision |
|---|---|---|
| Swimming prototype | Surface-only swimming (like *Red Dead*): two Animator states (swim idle, swim) and a float height. The body height depended on the animation, and the swim clip crossed the legs at the end of the loop | Too much cost for little gameplay value in this slice |
| Sea as a border | Fade to black and return to land; first version used 2 s fades and 4 m, which felt slow and too close to the water | Faster fades (0.8 s), 10 m back, the character returns standing in idle instead of still running |
| Quick load (F9) | One key that instantly replaced the current session with the single save file | Removed; there are now several saves, and loading goes through a Load window with a confirmation |

## What I would test next

- Whether 12 minutes for thirst is too short when wells are far apart.
- Item loss on death (a risk/reward choice) versus the current forgiving respawn.
- Whether cold and wet weather should affect health.
