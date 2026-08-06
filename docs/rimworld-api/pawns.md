# Pawns — drafting, stats, work, reach

Mechanics: [colonists.md](../rimworld/colonists.md) ·
[work-priorities.md](../rimworld/work-priorities.md) ·
[work-types.md](../rimworld/work-types.md) ·
[combat.md](../rimworld/combat.md)

## Drafting takes hands from everything that is not the fight

```csharp
pawn.drafter.Drafted = true;    // and false to release
```
**[compiles]**

This is the single most productive fact in these notes, because the same consequence has produced
**three separate faults** and each was found on its own before the pattern was:

| run | what drafting broke | shape |
|---|---|---|
| 135 | three colonists bled out while the last upright one was held in a withdrawal | rescue and tending are work |
| this session | a fire front judged fightable by people already sent to a firing line | `ableColonists` counts the drafted |
| 164 | a downed colonist kidnapped while two able ones withdrew past him | `NearestCarrier` skips the drafted |

A drafted colonist **can act and is not available**. Any question of the form "have we got the
people to do X", where X is not this fight, wants a count that excludes them. Any question about
who can hold a line wants one that includes them. `ColonyState` carries both — `ableColonists`
and `colonistsFreeForWork` — and conflating them is the recurring error.

A drafted pawn also stands where it was put and shoots only what walks into its line of sight.
Drafting is not attacking; an explicit order is a separate act.

## Fighting value

Read from what a pawn is holding and wearing, not from its type **[compiles]**:

```csharp
StatDefOf.MeleeDPS                 // folds in weapon, quality, skill, manipulation
StatDefOf.ShootingAccuracyPawn
StatDefOf.ArmorRating_Sharp / ArmorRating_Blunt
StatDefOf.MoveSpeed
weapon.def.IsRangedWeapon
weapon.def.Verbs[0].burstShotCount / warmupTime / defaultCooldownTime
projectile.projectile.GetDamageAmount(weapon)
```

Ranged has no equivalent of `MeleeDPS`, so damage-over-shot-cycle has to be computed: burst size
across warmup plus cooldown. A weapon that fires three rounds and cycles in two seconds is not the
one that fires once and cycles in one, and a ranged/melee flag cannot tell them apart. Weapon
tiers and the stats that matter are in
[weapons.md](../rimworld/weapons.md#weapon-stats-that-matter).

Not in any of this: **cover**, which
[combat.md](../rimworld/combat.md#cover--terrain) calls the most reliable defensive advantage in
the game. The director's strength model has no positional term at all (#43).

## Work

```csharp
pawn.workSettings.SetPriority(workTypeDef, n);   // 1 highest .. 4 lowest, 0 disabled
pawn.WorkTypeIsDisabled(WorkTypeDefOf.Research)
pawn.WorkTagIsDisabled(WorkTags.Caring)          // and Violent
pawn.skills.GetSkill(SkillDefOf.Medicine).Level
pawn.mindState.IsIdle
```
**[compiles]**

Priority ordering and how jobs actually get picked:
[work-priorities.md](../rimworld/work-priorities.md#how-jobs-actually-get-picked). Worth knowing
that a colonist takes the highest-priority job they are *able* to do and that is reachable —
which makes reachability part of the work model rather than a detail.

Hauling and cleaning give no XP, so they train nobody; the assignment model and the training model
cannot see each other (#46). Skills and passions:
[colonists.md](../rimworld/colonists.md#skills-020-each).

## Reachability

```csharp
using Verse.AI;                                   // required, easy to miss
pawn.CanReach(target, PathEndMode.Touch, Danger.Some)
pawn.CanReach(victim, PathEndMode.OnCell, Danger.Deadly)
```
**[compiles]**

`CanReach` uses the pawn's own traverse parms, so anything they will not walk through reads as
unreachable — a colony with fires burning reads as far more cut off than it is, and the answer
should be held across passes rather than recomputed into a panic.

**Asking whether the chosen pawn can get there is a habit worth keeping.** `TradeModule` picks
the best Social who `CanReach`; the medic choice did not ask, and that is what run 162 died of.
Same question, two places, one of them learned it first.
