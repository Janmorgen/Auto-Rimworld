# Food — nutrition, rot, meals

Mechanics: [food.md](../rimworld/food.md) ·
[storage & spoilage](../rimworld/food.md#storage--spoilage) ·
[nutrition.md](../rimworld/nutrition.md) ·
[meal tiers](../rimworld/food.md#meal-tiers-cooking-work-best-to-most-basic)

## What is edible, and to whom

```csharp
def.ingestible                       // null means not food at all
def.ingestible.CachedNutrition
def.ingestible.preferability         // FoodPreferability.RawBad and up
def.ingestible.foodType              // FoodTypeFlags — Meal, VegetableOrFruit, Meat, ...
def.ingestible.IsMeal                // the game's own dinner/ingredient distinction
```
**[compiles]**

Three separate questions that get conflated:

- **Is it food** — `ingestible != null`
- **Will a colonist eat it** — `preferability >= RawBad`, and the flags exclude kibble and hay
- **Is it dinner** — `IsMeal`. A colonist with no meal to hand eats raw and takes `AteRawFood`
  at −7. One run carried that on all four colonists while holding five days of food and running
  a working kitchen: nutrition was never the problem, none of it had been through a stove.
  **[live]**

Meal tiers and what each costs in cooking work:
[meal tiers](../rimworld/food.md#meal-tiers-cooking-work-best-to-most-basic).

## Counting what the colony has

Colonists eat any **reachable, unforbidden** food, stockpiled or not. Counting only what is in a
stockpile reports a food emergency with the food lying in front of the colony — which happened at
tick zero, before anything had been hauled. **[live]**

```csharp
map.resourceCounter.TotalHumanEdibleNutrition   // stockpiled only — a different question
```
**[compiles]** — useful, but not the answer to "what can we eat".

Pemmican and packaged survival meals barely spoil and never spoil respectively
([meal tiers](../rimworld/food.md#meal-tiers-cooking-work-best-to-most-basic)), which makes them
what a trader is worth buying for.

## Rot

```csharp
var rot = thing.TryGetComp<CompRottable>();
if (rot != null && rot.Active) { float days = rot.TicksUntilRotAtCurrentTemp / 60000f; }
```
**[compiles]**

`TicksUntilRotAtCurrentTemp` already accounts for temperature, so a freezer shows up as food that
is not spoiling **without this code needing to know what a cooler is**. That is the right shape:
ask the game, do not model the room.

**Watch the semantics here.** "Spoiling" as the director computes it means *rots within a
three-day horizon* — food that must be eaten soon, not food already lost. Subtracting it wholesale
from days-of-food understates security badly, because most of it is edible today. The honest loss
is what cannot be consumed before it rots:

```
lost ≈ max(0, daysSpoiling − spoilHorizonDays)
```

This distinction was nearly shipped wrong (#58). 60000 ticks to the day; see
[season.md](season.md) for the rest of the calendar constants.

## Growing

```csharp
RimWorld.Plant.DefaultMinGrowthTemperature          // 0   — below this, nothing
RimWorld.Plant.DefaultMinOptimalGrowthTemperature   // 6   — optimal band opens
RimWorld.Plant.DefaultMaxOptimalGrowthTemperature   // 42  — optimal band closes
RimWorld.Plant.DefaultMaxGrowthTemperature          // 58  — above this, nothing
RimWorld.Plant.MinLeaflessTemperatureOffset         // -18
RimWorld.Plant.MaxLeaflessTemperatureOffset         // -10
```
**[read]** — the namespace is `RimWorld`, not `Verse`, which costs a probe run to discover.

Four thresholds, not one range. The reference note's "roughly 10–42°C" has its upper bound exactly
right and its lower bound a little high; see
[disagreements.md](disagreements.md#growing-temperature) for which to use when. Crop list and
yields: [plants.md](../rimworld/plants.md#domesticated-player-sown-plants--full-list).

## Butchering

Meat locked in an uncollected corpse is not food yet, and a colony can read as empty while days
of nutrition lie in its own fields. Counting it separately — killed but unbutchered — is what
stops the hunt module ordering more killing on top of a full larder. **[live]**

A butcher table is the ordinary route; the butcher *spot* needs no research and no materials and
is the fallback a struggling colony most needs and least often has (#53).
