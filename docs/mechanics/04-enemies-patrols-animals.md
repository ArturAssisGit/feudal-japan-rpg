# Enemies, patrols and animals

**Pillar:** A living world / Combat with weight
**Scripts:** `EnemySpawner.cs`, `RoamingPatrolSpawner.cs`, `SpawnPlacement.cs`, `AnimalSpawner.cs`, `BirdAI.cs`, `FoxAI.cs`

## Goal

The world should feel inhabited and unpredictable during travel, without filling the map with enemies standing still. The player should *meet* life in front of them, not hear about it behind them.

## Player experience

- **Camps feel alive:** fixed enemy camps are there when the player arrives and come back after a while.
- **Travel has surprises:** a patrol crosses the road ahead, birds take off as the player passes, a fox runs away.
- **No unfair spawns:** nothing appears right next to the player or on a roof.

## Rules and numbers

### Camps (EnemySpawner)
- Active when the player is within 80 m; enemies farther than 150 m are removed and come back later.
- A dead enemy is replaced after a countdown that starts at its death.
- Spawn points are checked to be near the terrain height and connected on the NavMesh, so enemies never appear on roofs or in closed spaces.

### Surprise patrols (RoamingPatrolSpawner)
| Setting | Value |
|---|---|
| Try every | 90–180 s |
| Chance per try | 50% |
| Max patrols at once | 1 |
| Appears at | 28–38 m from the player (x1.6 on horseback) |
| Direction | 75% ahead of the player (±50°), otherwise anywhere |
| Path | Marches past at 4–14 m from the player |
| Never near | Villages and camps (60 m) |

Patrols are picked by weight, can be limited to hours of the day, and fight as soon as they see the player.

### Animals (AnimalSpawner)
- Up to 8 animals at a time, in groups (for example a small flock of birds).
- They appear 20–55 m away, inside a **180° arc in front of where the player is going** (or looking, when standing still), avoiding the camera view, steep ground, water and safe places.
- **Birds:** idle, walk and peck; when the player comes close they run forward for 1.5 s, then climb at no more than 22°, alternating flaps and glides.
- **Foxes:** wander, flee when the player is near, die with an animation when hit by a sword or an arrow (20 health).

## Iterations

| Problem | Change |
|---|---|
| Patrols only appeared on foot, so riding felt empty | Patrols also appear while riding, 1.6x farther away, because the player moves faster and needs time to see them |
| Animals often spawned behind the player and were never seen | Spawns limited to a 180° arc around the movement direction; more placement attempts (12 → 20) |
| Birds shot straight up when scared, which looked unnatural | They run forward first and climb at a gentle angle (max 22°) |
| Birds were placed by hand in the scene | A spawner creates animal prefabs on the ground around the player and removes far ones |

## What I would test next

- Patrol frequency on long horse rides: exciting or tiring?
- Hunting: should foxes drop meat or fur for crafting?
