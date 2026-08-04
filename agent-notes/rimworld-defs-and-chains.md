# Defs, gates and chains, as extracted

Every number here was read out of `Data/Core/Defs` rather than remembered. Where a def
inherits a value from an abstract parent this reads only the def's **own** body and says
`(inherited)` — an early version of the extractor walked parents and silently attributed a
watermill's cost list to a wall, which is the kind of table that is worse than no table.

RimWorld 1.6.4871, no DLC active.

---

## Buildings the director places

| def | research | cost (own def) | stuff |
|---|---|---|---|
| `Wall` | none | stuff ×5 | Metallic, Woody, Stony |
| `Door` | none | stuff ×25 | — |
| `Bed` | none | stuff ×45 | — |
| `DoubleBed` | none | stuff ×85 | — |
| `SleepingSpot` | none | free | — |
| `TorchLamp` | none | Wood 20 | — |
| `Campfire` | none | Wood 20 | — |
| `FueledStove` | **none** | Steel 80 | — |
| `TableButcher` | none | Wood 20 + stuff ×75 | Metallic, Woody |
| `SimpleResearchBench` | none | Steel 25 + stuff ×75 | Metallic, Woody, Stony |
| `Fence` | none | stuff ×1 | Metallic, Woody, Stony |
| `FenceGate` | none | stuff ×25 | — |
| `PenMarker` | none | stuff ×30 | Metallic, Woody, Stony |
| `TableStonecutter` | Stonecutting | Steel 30 + stuff ×75 | Metallic, Woody |
| `HandTailoringBench` | **ComplexClothing** | stuff ×75 | Metallic, Woody |
| `ElectricTailoringBench` | ComplexClothing + Electricity | Steel 50, Comp 2 + stuff ×75 | Metallic, Woody |
| `PowerConduit` | Electricity | Steel 1 | — |
| `WoodFiredGenerator` | Electricity | Steel 100, Comp 2 | — |
| `Heater` | Electricity | Steel 50, Comp 1 | — |
| `SolarGenerator` | SolarPanels | Steel 100, Comp 3 | — |
| `Battery` | Batteries | Steel 70, Comp 2 | — |
| `Cooler` | AirConditioning | Steel 90, Comp 3 | — |
| `Turret_MiniTurret` | GunTurrets | Steel 70, Comp 3 + stuff ×30 | Metallic |
| `Brewery` | **Brewing** | Wood 120, Steel 30 | — |
| `FermentingBarrel` | **Brewing** | Steel 10, Wood 30 | — |
| `DrugLab` | DrugProduction | Steel 75, Comp 6 + stuff ×50 | Metallic, Woody |

**Everything a colony needs on day one is ungated.** Walls, doors, beds, a fuelled stove, a
butcher table, a research bench, and the whole pen — fence, gate and marker — need no research
at all. The first hard gate a colony meets is `ComplexClothing`, and it is in front of *both*
tailoring benches including the hand one.

---

## Research

| project | cost | tech | prerequisites |
|---|---|---|---|
| Stonecutting | 300 | Medieval | none |
| Brewing | 400 | Neolithic | none |
| PassiveCooler | 400 | Neolithic | none |
| Batteries | 400 | Industrial | Electricity |
| NutrientPaste | 400 | Industrial | Electricity |
| PsychiteRefining | 400 | Industrial | DrugProduction |
| **Pemmican** | **500** | **Neolithic** | **none** |
| PsychoidBrewing | 500 | Neolithic | none |
| DrugProduction | 500 | Industrial | none |
| AirConditioning | 500 | Industrial | Electricity |
| GunTurrets | 500 | Industrial | BlowbackOperation |
| ComplexClothing | 600 | Medieval | none |
| SolarPanels | 600 | Industrial | Electricity |
| Smithing | 700 | Medieval | none |
| Devilstrand | 800 | Neolithic | none |
| **Electricity** | **1600** | Industrial | none |
| MicroelectronicsBasics | 3000 | Industrial | Electricity |

Electricity at 1600 is the wall this project's colonies rarely reach — most die before day 40.
Everything Neolithic and prerequisite-free (Pemmican, Brewing, PsychoidBrewing, PassiveCooler)
costs 400–500 and is reachable in a run that survives, which is why the cheap answers matter far
more here than the correct-in-general ones.

---

## Chains, and where the gate actually sits

The recurring lesson of this project: **the gate is rarely on the thing you are looking at.**

### Food that keeps

```
meat/plants --[CookMealSimple, no research, Campfire|Stove]--> MealSimple   (rots in ~1.4 days)
meat/plants --[Make_Pemmican, research Pemmican, Campfire|Stove]--> Pemmican x16  (keeps 70 days)
```

Pemmican is the whole preservation answer for a pre-electric colony: 500 points, Neolithic, no
prerequisites, and made on benches the planner already builds. Refrigeration is the other
answer and costs Electricity (1600) → AirConditioning (500) → a Cooler → a sealed room.

### Beer — the chain that defeats a recipe walk

```
Plant_Hops --harvest--> RawHops
RawHops --[Make_Wort, research NONE, at Brewery]--> Wort x5
Wort --[FermentingBarrel, thingClass Building_FermentingBarrel, NO RECIPE]--> Beer
```

`Make_Wort` carries **no research prerequisite**, so a naive check says hops are usable from day
one. Both its bench (`Brewery`) and the barrel need **Brewing**. And the final step is not a
recipe at all — it happens in code inside `Building_FermentingBarrel`, so there is no def edge
from wort to beer and **no recipe-graph walk can ever reach it**.

Two consequences worth carrying:

- To ask "can the colony use this crop", check the recipe's research **and** whether any bench
  that can run it is researched. `RecipeDef.AllRecipeUsers` gives the benches and handles both
  directions — most recipes are listed on the bench, not the other way round.
- To ask "is this a drug crop", a recipe walk fails on hops. See [[rimworld-plants]] for the
  discriminator that works.

### Psychite tea — cheaper than it looks

```
Plant_Psychoid --harvest--> PsychoidLeaves
PsychoidLeaves --[PsychiteTea, research PsychoidBrewing (500, Neolithic),
                  at Campfire | FueledStove | ElectricStove]--> PsychiteTea
```

Note the benches: **campfire and stove**, not the drug lab. So psychite tea is reachable by a
Neolithic colony with 500 points and no new building, which makes it a far more plausible mood
lever than beer. `Flake` and the harder drugs are what need `DrugLab` and `DrugProduction`.

Smokeleaf joints are made at a `CraftingSpot` or `DrugLab` — the crafting spot is free and
needs no research.

### Clothing — the chain that froze two colonies

```
Plant_Cotton --harvest--> Cloth            (Fabric)
animals      --butcher--> leathers          (Leathery)
Cloth|leather --[at HandTailoringBench, research ComplexClothing (600)]--> Apparel_Parka
```

`Apparel_Parka` accepts `Fabric` **and** `Leathery`. A hunting colony therefore has parka
material without growing anything — which is the fact a director counting only `Cloth` cannot
see. Both tailoring benches need ComplexClothing, including the hand bench, so a colony can hold
material and skill and still be unable to sew.

### Medicine, and animals

```
Plant_Healroot --harvest--> MedicineHerbal        (Plants 8 to sow, ~11 days to grow)
Plant_Haygrass --harvest--> Hay --[Make_Kibble, no research]--> Kibble x50
corpses --[ButcherCorpseFlesh, no research, TableButcher|ButcherSpot]--> meat + leather
```

Butchering is ungated and the spot is free, which matters more than it sounds: a colony that
cannot butcher has meat rotting in the field and no leather to sew with.

### Fuel — the step that is not in any recipe

```
WoodLog --[Refuel, WorkGiver Refuel, workType Hauling, prio 140]--> a bench that will run
```

`FueledStove` holds **50** wood and consumes only while used; a campfire, a smithy and a
wood-fired generator are the same shape. Nothing in the recipe graph mentions it — `CookMealSimple`
lists meat and vegetables, not fuel — so a chain walk says the colony can cook when it cannot.

The question to ask is `CompRefuelable.ShouldAutoRefuelNow`, which is the condition
`WorkGiver_Refuel` itself tests. When it is true the game wants a colonist to carry wood and is
waiting for one to be free, which makes it a **Hauling** problem and never a problem of whatever
the bench does. Run 108 raised Cooking to 4.0 for eighteen days over a stove holding 0.85 of 50.

### Seats — the other step that is not in any recipe

```
ChessTable | PokerTable | any table --[a sittable thing in a touching cell]--> usable
```

A pawn reaches a dining table only through `Toils_Ingest.TryFindChairOrSpot`, which searches for a
*chair* within `ingestible.chairSearchRadius` and validates it on `def.building.isSittable`. No
chair, no table, and `AteWithoutTable` regardless of how many tables stand in the room.

A joy building needs one when a `JoyGiverDef` lists it with `requireChair` **and** a
`JoyGiver_InteractBuildingSitAdjacent` worker. Both halves matter: `requireChair` defaults to true,
so alone it catches billiards and horseshoes, which are played standing; the worker class alone
catches Game-of-Ur, which sets `requireChair` false. Together they give chess and poker — exactly
the set the game ships `Alert_ChessTableNoChairs` and `Alert_PokerTableNoChairs` for.

Cheapest seat in the game is the **Stool**: 25 of any stuff, no research.

### Bodies — amputation and the prosthetic ladder

An infection is a race: the disease climbs toward `lethalSeverity` while the body builds
immunity toward 1, and whichever arrives first decides. Tending speeds the immunity side and
does not guarantee it. When the race is lost the answer is **amputation** — removing the part
removes the disease — which leaves a permanent capacity loss that a prosthetic buys back.

```
infection losing the race --[amputate]--> missing part  (permanent capacity loss)
missing part --[InstallPegLeg,     NO research]--> peg leg
missing part --[InstallSimpleProsthetic*]--------> simple prosthetic
missing part --[InstallBionic*]------------------> bionic
```

**Every `Install*` recipe is ungated.** What gates the ladder is *making* the part:

| rung | research | cumulative cost | cost per part |
|---|---|---|---|
| peg leg | **none** | 0 | wood |
| simple prosthetic | Prosthetics 600 ← Machining 1000 ← Electricity 1600 + Smithing 700 | ~3,900 | Steel 40, Comp 4 |
| bionic | Bionics 2000 ← Fabrication 4000 ← MultiAnalyzer | ~10,000+ | Plasteel 15, ComponentSpacer 4 |

For colonies that die around day 30 having finished no research, **the peg leg is the entire
ladder**. Everything above it sits behind Electricity, which this project's colonies almost
never reach — see the research table above, where Electricity alone is 1600.

The upgrade path matters as much as the rungs: a peg leg is *replaced* by a simple prosthetic
and that by a bionic, so an early amputation is not a permanent verdict on that colonist. It is
a debt research can pay off later, which is the clearest case in the game of a long-term goal
buying back a short-term loss.

---

## Work types, and what the director weights them on

Nineteen work types are given weights: Firefighter, Patient, PatientBedRest, Doctor, Hunting,
Cooking, Growing, PlantCutting, Mining, Construction, Hauling, Cleaning, Crafting, Tailoring,
Smithing, Art, Research, Warden, Handling.

Priorities in RimWorld are 1–4 where **lower runs first**, so a higher weight here becomes a
lower number. The director's weights are relative, not absolute.

Shapes worth knowing, because they are the ones that have gone wrong:

- **Emergencies are step functions.** Firefighter 6.0 when a fire is near the colony, 1.0
  otherwise. Doctor 5.0 / 4.0 / 3.0 / 2.0 by whether somebody is starving beside food, downed,
  untended, or merely damaged.
- **Gathering scales with shortfall**: `1 + shortfall × 1.5 × aggression`, so Mining and
  PlantCutting climb to ~2.5 when stores are low.
- **Building scales with backlog**: `2.2 + clamp(pending/30) × 1.5`, up to ~3.7. This was flat at
  2.2 until a colony sat on 390 material chopping wood while three colonists shared two beds —
  gathering could say it was behind and building could not.
- **Fetching has a floor, not a promotion.** Hauling and Cleaning are clamped to at least
  priority 3, because a colony where nobody hauls cannot cook, craft or build: harvested plants
  never reach the stockpile and stockpiled food never reaches the stove.
- **Season multiplies growing work.** Sowing outside the growing season produces nothing, so
  Growing is scaled by `growingSeasonNow` and Hunting takes up the slack.

### Skill curves that justify the weighting

| stat | at level 0 | at level 8+ |
|---|---|---|
| `ConstructSuccessChance` | 0.75 | 1.00 (from 8) |
| `FoodPoisonChance` | 5.0% | 0.5% (from 6) |
| `TameAnimalChance` | 0.04 + 0.03/level | — |

So a construction botch is a *materials* loss a colony at means 0.18 cannot afford, and cooking
with an unskilled colonist is a food-poisoning tax on every meal.

---

## Related

- [[rimworld-plants]] — what each sowable plant is for, and why "gives nutrition" is not "is food"
- [[rimworld-rooms]] — room roles, stats and the placement traps
- [[rimworld-pens]] — fences, forage and the seasonal shortfall
- [[food-preservation]] — the rot deadlock these chains exist to break
- [[mood-and-labour]] — why labour, not knowledge, is what these colonies run out of
