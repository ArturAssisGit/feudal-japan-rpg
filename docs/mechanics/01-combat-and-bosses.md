# Combat and boss fights

**Pillar:** Combat with weight
**Scripts:** `PlayerCombat.cs`, `PlayerDodge.cs`, `PlayerFinisher.cs`, `CameraController.cs`, `EnemyAI_01.cs`, `BossHealthBar.cs`

## Goal

Every swing should feel deliberate and readable. The player should win by timing and positioning, not by clicking fast, and should always understand why they got hit.

## Player experience

- **Weight:** short hit-stop, knockback, sparks and blood on every hit.
- **Fairness:** enemies telegraph attacks with a wind-up, and only one enemy attacks at a time.
- **Drama:** a rare cinematic finisher, and a boss fight where the camera focuses on the duel.

## Rules and numbers

| Action | Input | Stamina | Damage | Notes |
|---|---|---|---|---|
| Light combo | LMB (x2) | 15 / 20 | 1.0x / 1.2x | Next swing at 55% of the animation, 0.4 s input buffer, combo resets after 1.2 s |
| Kick | (random in combo) | – | 0.6x | 15% chance, pushes the enemy back 1.6 m |
| Strong attack | F | 30 | 1.5x | Slower, longer hit delay |
| Block | Q | 10 per hit | blocks all | Only within 120° in front |
| Dodge | Left Alt | 15 | – | 2 m in 0.5 s, 0.3 s invulnerable |
| Bow shot | 2 + RMB + LMB | 5 | 20 | 0.4 s minimum draw, 0.7 s cooldown |

- **Damage taken:** `damage × 50 / (50 + defense)`, so 50 defense halves it.
- **Hit feel:** 0.06 s hit-stop at time scale 0.05, small camera shake, 0.45 m knockback.
- **Assist:** attacks turn toward the enemy the camera is aiming at (7 m, 60°) and lunge up to 3.5 m.
- **Finisher:** 20% chance on a killing sword blow; slow motion at 0.4 for 0.5 s and a close camera.

### Enemies
- **Melee:** 15 damage, 0.4 s wind-up, 2 s cooldown. The player must still be inside the hit zone when the wind-up ends, so stepping back or dodging works.
- **Archer:** 10 damage, keeps its distance (backs away under 5 m), shoots within 16 m with a little aim error.
- **Groups:** one attacker at a time; the others circle at 4.5 m and swap in when hit or approached.

### Boss
- 1000 health, its own name and a health bar at the top of the screen. The part of the bar just lost stays visible for a moment before shrinking.
- **Camera:** when the boss is chasing nearby, the camera locks on (closer, tighter follow, mouse at 40% sensitivity).
- **Movement:** with the sword drawn the player faces the boss and strafes; Shift turns to run away.
- **On player death:** the boss heals to 100% and goes home.

## Iterations

| Problem found while playing | Change |
|---|---|
| Swings often missed an enemy that was slightly off-center | Added aim assist toward the aimed enemy and a short lunge |
| Hits felt weightless | Added hit-stop, knockback, sparks, blood and the random kick |
| The boss camera flickered at the edge of the arena | Separate enter (12 m) and exit (20 m) distances, plus a 0.5 s pause before re-checking |
| Fighting a boss with free camera felt chaotic | Camera lock-on and strafing animations during the boss fight |
| After dying, the next attempt started with a half-dead boss | Bosses heal to full and reset when the player dies, so every attempt is fair |
| Dead enemies came back instantly if they had been dead a while | The respawn countdown now starts when the enemy dies |

## What I would test next

- Boss damage and attack rhythm with players of different skill.
- Whether one attacker at a time is too easy with three or more enemies.
- Stamina costs: whether players run out too often during the boss fight.
