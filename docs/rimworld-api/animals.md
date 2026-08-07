# Animals — combat power, revenge, and hunting

Mechanics: [animals.md](../rimworld/animals.md) ·
[hunting](../rimworld/animals.md#hunting) ·
[large/dangerous wildlife](../rimworld/animals.md#largedangerous-wildlife) ·
[combat.md](../rimworld/combat.md)

## Def fields that matter

All under `Data/Core/Defs/ThingDefs_Races/`, inside `<race>`. **[read]**

| field | meaning | note |
|---|---|---|
| `manhunterOnDamageChance` | revenge roll **per wound** | not per hunt — see below |
| `baseHealthScale` | damage the body absorbs | how many wounds a hunt takes |
| `baseBodySize` | physical size | drives meat yield, not toughness |
| `herdAnimal` | travels in groups | |
| `packAnimal` | can carry in a caravan | |
| `predator` | hunts other animals | treated as unconditional revenge by the director |

Measured values, for calibration:

| animal | revenge/wound | healthScale | herd |
|---|---|---|---|
| Elephant | 0.50 | 3.6 | true |
| Muffalo | 0.1 | 1.75 | true |
| Bison | 0.1 | 1.75 | true |
| Caribou | 0.1 | 2.0 | true |
| Warg | 1.00 | 1.4 | — |
| Deer | 0 | 0.9 | true |
| Turkey | 0 | 0.6 | false |
| Rat / Squirrel | 0 | 0.29 / 0.25 | — |

The reference table at
[hunt-revenge chance](../rimworld/animals.md#largedangerous-wildlife) gives the same figures as
percentages and is a faster read when the question is "is this thing dangerous". Take the number
from the def.

## `manhunterOnDamageChance` is per wound

The field name says so and
[animals.md](../rimworld/animals.md#largedangerous-wildlife) agrees — "the odds a wounded animal
turns and attacks the hunter". Per wounding, not per animal. **[read]**

This is the single most expensive misreading found so far. A muffalo at 0.1 reads as a safe hunt;
it carries `baseHealthScale` 1.75 against a rat's 0.29, so it absorbs several times the shooting,
and every shot is another roll. Ten percent across roughly seven wounds is a shade over fifty
percent per hunt. **[live, runs 161–164]**

Compounding across a hunting session is the second half of it. Five muffalo designated in one
pass, each individually a comfortable fight, is a ~97% chance that something turns. `HuntRisk`
exists for exactly this.

## Combat power is not fighting ability

`kindDef.combatPower` is the storyteller's **raid-points budget** for sizing an encounter. It is
not damage per second times toughness, and it is not on the same scale as the director's own
`FightingValue`. **[live, run 164]**

Measured side by side:

| animal | `combatPower` | measured offence × toughness |
|---|---|---|
| Boomalope | 80 | 19 |
| Megasloth | 280 | 221 |

The boomalope is the whole lesson. It barely fights — nineteen is about right for its melee. It
is rated eighty because it **explodes when killed**, and an explosion is not damage per second
from a weapon it is holding. So `combatPower` carries hazards a DPS-and-armour reading is blind
to by construction, while a megasloth that does fight with its body scores close under both.

**Run 174 settled it, and settled it against the measurement.** Day 0, hour 0:

```
hunting Boomalope (80, measured 19) x2; passed over Boomalope (80, measured 19) x4
  [strength 61 vs threat 80, need 1.6x at desperation 0.90]
```

Two hours later: `Boomalope revenge`, five fires, all four colonists down, no route through
the flames to the one bleeding out. Susumu died of blood loss with 9.2 hours of clock left and
no path; Flea died of burns. The colony was finished on day 0.

The bar had been computed against 80 and the colony still took two. Against **19** it would have
taken the four it passed over as well. The number that looks like the honest measurement is the
one that would have killed the colony faster, and it is worth being plain about why: a
boomalope's danger was never in its bite. **[live, run 174]**

Neither number is simply the honest one. Which to use depends on the question:

- *how much will this hurt me in a stand-up fight* — measure it
- *how dangerous is this thing to have on the map* — `combatPower` knows about explosions,
  toxicity and the rest

Note the asymmetry in the director's own code: humanlike raiders are measured rather than read
from `combatPower`, on the argument that a type-average cannot see what a pawn is carrying. That
argument was never carried across to animals, and whether it should be is still open (#55).

## Ask the def what it does when it dies

```csharp
def.GetCompProperties<CompProperties_Explosive>();   // null for anything that does not explode
comp.explosiveRadius;
comp.explosiveDamageType;                            // Flame means it starts fires
```
**[compiles]**

This is the answer to the boomalope, and it is better than either number above because it is
neither a measurement nor a rating — it is the mechanism. `combatPower` knew about the explosion
only in the sense that somebody folded it into a raid-points figure; this *is* the explosion.

**It cost a second colony before it was fixed.** Run 196 was starving at 1.9 days of food on a
map with nothing left within its gather radius, so desperation was high and it hunted what it
could reach: **[live, run 196]**

```
day 10 18h  gathering: marked 0 trees, 0 rock, 3 animals within 55 cells of the base
day 10 19h  INCIDENT answered 'Boomalope revenge' with 'close'
day 10 19h  FIRE     19 fires burning and 1 able colonists — past what they could beat out,
                     so nobody is sent into it
day 10 20h  DEATH    died of Burn — last seen as Stephanie (health 0.15, mood 0.23, downed)
```

Nineteen fires, one colonist able to fight them, and the fire logic did everything right — it
correctly refused to send one person into nineteen fires, and correctly refused to claim a 154C
room. The decision that lost the colonist was made an hour earlier, by a hunt that priced a
boomalope's bite.

**The hazard is certain, not a chance.** Revenge is a roll per wound; this is not. Hunting an
animal means killing it, and killing this one means the blast — so it enters the session risk at
certainty rather than as a probability, and an animal carrying one can never read as free however
harmless its bite. Nothing in the code names a boomalope: anything the game gives an explosive
comp is priced the same way, including a mod's.

## Stats available on animals

`GetStatValue` works on animals for the same stats as colonists **[compiles]**:

- `StatDefOf.MeleeDPS` — folds in the animal's tools, so a bite is priced without knowing what a
  bite is
- `StatDefOf.ArmorRating_Sharp` / `ArmorRating_Blunt` — natural armour
- `StatDefOf.MoveSpeed`
- `PawnCapacityDefOf.Moving`

`PawnCapacityDefOf.Manipulation` is the trap. It is meaningful for a colonist because a weapon is
held in hands; averaging it into an animal's toughness understates every animal, since a wolf's
fighting does not depend on it.

## Hunting surface

```csharp
map.designationManager.AddDesignation(new Designation(animal, DesignationDefOf.Hunt));
map.designationManager.DesignationOn(animal, DesignationDefOf.Hunt);   // already marked?
map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Hunt);
```
**[compiles]**, and the enumeration must be copied before removing from it — removing a
designation mutates the manager's own list.

Colonists hunt the nearest *designated* animal, not the one most recently chosen, so a standing
designation keeps pulling hunters onto it long after the reasoning that produced it expired.
Withdrawing designations the colony no longer endorses is part of deciding, not cleanup.
**[live]**

Candidate filter that matches what is actually huntable **[compiles]**:

```csharp
animal.RaceProps.Animal && animal.Faction == null &&
animal.RaceProps.foodType != FoodTypeFlags.None
```
