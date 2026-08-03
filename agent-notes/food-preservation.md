# Food that rots, and the deadlock it creates

> **Read this first.** The deadlock below is real and measured, and it is *not* what kills
> colonies. Run 55 went on to score `Food security 1.00` in epoch 2 — perfect, all epoch — and
> `Survival 0.00` in the same breath. With food completely solved the colony still lost people
> and collapsed. Food is the loudest symptom while the weather is hot; mood is the binding
> constraint. Fix the preservation gap because a colony should not spend two thirds of its time
> firefighting the larder, not because it will save anybody.
>
> Run 55 then finished the argument by dying on day 32 with `Food security 1.00`, `Health
> 1.00`, `Infrastructure 1.00` and `Mood 0.11` — and `NeedFood at 26.0` as its worst unmet
> complaint in the same breath. Food *security* measures what is in the larder. `NeedFood`
> measures colonists who have not eaten. A downed pawn cannot feed itself, and the last one
> standing was down too. The colony starved with a full store, which is a mood-and-labour
> failure wearing a food failure's clothes.

Measured on run 55 — an unprovisioned colony on a hot map, three colonists, no deaths across
ten days, and a `Food security` score of **0.35** with **67% of the epoch spent answering an
emergency**.

## The measurement

`daysOfFood` counts *stockpiled* food. Watched over two in-game hours:

```
day 8 10h   food 5.3d   37C
day 8 12h   food 1.7d   37C     ← 3.6 days gone in two hours
day 8 14h   food 3.4d   35C
```

Three colonists eat roughly half a day of food per day. Losing 3.6 days in two hours is rot,
not consumption. The pattern repeats every afternoon: the colony hunts successfully, food
climbs to four or five days, and the heat destroys it.

## Why this is a deadlock rather than a shortage

The colony is not failing to *get* food. It is failing to *keep* it, and every consequence
feeds the cause:

- Food rots, so days-of-food never rises → permanent food emergency
- Permanent emergency → 67% of the epoch answering it → **two rooms built in ten days**
- No rooms → no research bench → no research at all this run
- No research → no Refrigeration and no Pemmican → food keeps rotting

Nothing in that circle breaks on its own. A colony can hunt perfectly and starve indefinitely.

## What RimWorld offers

| | Rots after | Needs |
|---|---|---|
| Simple meal | ~1.4 days | nothing |
| **Pemmican** | **70 days** | `Pemmican` research — **cost 500, Neolithic, no prerequisites** |
| Frozen food | indefinite | electricity, a cooler, a sealed room |

Pemmican is the early answer and the director has never heard of it: `Make_Pemmican` appears
nowhere in the codebase, and the only meal it ever cooks is `MealSimple`. It is made at a
campfire or butcher table — both of which the planner already builds — and 500 points with no
prerequisites is among the cheapest projects in the game.

Refrigeration is the late answer and needs a chain the colony cannot afford while it is
firefighting.

### The passive cooler, and why it is not simply the answer

`PassiveCooler` — 400 points, Neolithic, no prerequisites, fifty wood, no power grid — is
cheaper than Pemmican and the director already builds them. `UpkeepModule` places one against
the `HotRoom` complaint, describing it exactly right: "the answer a colony without electricity
actually has to heat".

It is never used for food. `RoomRole.Freezer` places an electric `Cooler` and nothing else, so
a colony with no electricity can never have any cold store at all, even though it knows how to
build the thing that would give it one. That is the `defA ?? defB` trap this project has
already recorded once, in a different costume: the choice should be made on capability — what
the colony can power and build — and instead there is only one answer wired in.

The caveat matters though. A passive cooler pulls a room roughly 10-15°C below ambient and does
not reach freezing. On the 37°C map measured here that is around 22°C, where food still rots,
only slower. So it is the temperate answer and pemmican is the hot-map one, and a colony that
picked between them by climate would be right more often than one that always picked either.

Neither is researched by the director today.

## What the director does instead

Nothing addresses keeping food. The food goals measure days-of-food and answer it by hunting
and sowing — both of which produce more of the thing that is rotting. `ResourceModule` even
reports "not hunting: 6.5 days of meat already killed and waiting to be butchered", which is a
true statement about a colony that will be at 0.9 days by nightfall.

## The fix, as built

`PreservedFoodGoal` — the codebase's own idiom, since goals carry `RequiresResearch` and the
research module researches whatever the plan names. Satisfied when the colony either has a
working cooler *or* can make pemmican; declares `Pemmican`; urgency scaled by heat and by how
much there is in the larder to lose.

**Nothing else was needed to make the colony cook it.** `ProductionModule` keeps a bill for any
recipe whose product has a stock target, and pemmican is `preferability: MealSimple`, so
`DesiredCount` already treats it as a meal — `colonists × MealsPerColonist` — the moment the
recipe unlocks. The gap was only ever that nobody asked for the research.

Two things about how a research-only goal sits in the plan, both found by running the self-test
rather than by reading the code:

- **It must declare `Requires = ResearchCapacity`.** This goal *is* research: it wants no room
  and builds nothing. As a focus with no bench on the map it left the colony with nothing to do
  and research that could not progress. Stating the dependency makes the planner walk back by
  itself, and the probe then reads `focus=Somewhere to research, wanted=Food that keeps` — both
  actionable and an honest description of what is happening.
- **Blocked is not the same as pressing.** The horizon-promotion rule in `GoalPlanner` now
  requires the blocked goal's urgency to clear `PressingUrgency` before it can pull the research
  room forward. Without that, preserving food on a map cold enough that food keeps by itself
  would still be technically stuck behind its research, and would promote a whole room for a
  problem the colony does not have this season. Seen both ways in one probe set: at urgency
  0.21 research stayed at 800.45, at 0.73 it was promoted to 900.45.

Ordering holds where it should — `Plant fields` at 900.85 still outranks it, because growing
food beats preserving it, and `Clothe the colony` at 901.15 still wins a hard freeze.

**Unverified:** the climate scaling. The self-test probes simulate weather for the clothing goal
without moving `mapTemperature.OutdoorTemp`, so the rot term is never exercised across
temperatures. It reads the same source `RefrigerationGoal` uses, which is consistency and not
evidence.

The passive cooler is still not used for food — `RoomRole.Freezer` places an electric `Cooler`
and nothing else, so a colony with no electricity still has no cold store. Pemmican now covers
the hot-map case that made it urgent, so this is a smaller gap than it was, but it is the same
`defA ?? defB` trap recorded above and still open.
