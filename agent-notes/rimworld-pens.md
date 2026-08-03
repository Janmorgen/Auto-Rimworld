# Animal pens, as measured

Everything here was established by building pens in-game with the `livestock` scenario
(`AUTOCOLONY_SCENARIO=livestock`) and reading back what RimWorld said about them, plus a
metadata dump of `Assembly-CSharp.dll` to find the API rather than guess at it.

---

## What makes a pen a pen

Three things, and none of them is a wall or a roof:

- **A fence perimeter**, unbroken. `Fence` is `PassThroughOnly` — colonists cross it, roaming
  animals do not.
- **A gate** in that perimeter (`FenceGate`), or nothing gets in or out, including the animals
  it exists to hold. A pen with no gate is the sealed-room trap in fence form.
- **A pen marker** (`PenMarker`) standing anywhere inside. Without it the game does not treat
  the enclosure as a pen at all.

A pen is **deliberately outdoors and unroofed**, which is the whole reason none of the room
machinery applies to it: no walls, no roof, no `Room`, no `RoomRole`. Judging one with the
room code would report the roomless sentinels and mean nothing.

**None of the three needs research.** All three are **made from stuff** — `Fence`
`costStuffCount` 1, `FenceGate` 25, `PenMarker` 30, each accepting Metallic/Woody/Stony. That
last fact cost the feature its entire existence: placing them with `stuff: null` fails
silently, so the pen builder ran for many colonies and never placed a single fence section.

## Pen versus barn

A barn is a room with animal sleeping spots and a trough; somebody has to carry fodder to it
out of the same larder the colonists eat from. A pen **feeds itself** from the vegetation
caught inside the perimeter. That is the entire trade, and it is seasonal — see below.

---

## The forage API

This is the part worth knowing, and it is all public.

| Type | Use |
|---|---|
| `MapPastureNutritionCalculator` | `Reset(map)`, then `CalculateAverageNutritionPerDay(TerrainDef)` → `NutritionPerDayPerQuadrum` |
| `NutritionPerDayPerQuadrum` | `ForQuadrum(Quadrum)` — nutrition per day in each of the four quadrums |
| `PenFoodCalculator` | `ResetAndProcessPen(marker)` or `(pos, map, considerBlueprints)`; `numCells`, `numCellsSoil`, `nutritionPerDayPerQuadrum` |
| `PenFoodCalculator.ComputeExampleAnimals(List<ThingDef>)` | → `PenAnimalInfo.nutritionConsumptionPerDay`, what the herd eats |
| `CompAnimalPenMarker` | `PenState.Enclosed`, `PenFoodCalculator`, `AcceptsToPen(Pawn)` |
| `AnimalPenEnclosureCalculator` | `VisitPen(pos, map)` → `isEnclosed` — **internal**, needs reflection |

### Use the pasture calculator, not the pen calculator, to choose a site

`PenFoodCalculator` describes a pen that exists. Asked about a perimeter that is still
blueprints it returned **0.0 nutrition in all four seasons** on a spring map with the fields
visibly growing — a wrong answer, not an unhelpful one, and one that would have been believed
on an arid map, which is precisely the map where the answer decides something.

`MapPastureNutritionCalculator` works one level down: nutrition is a property of *terrain and
quadrum*, so it can be summed over bare cells with no fence in existence. That makes forage an
input to siting rather than a remark about it.

Measured on the same scenario, choosing the site by fertility versus by forage:

| | soil cells | spring | summer | fall | winter |
|---|---|---|---|---|---|
| fertility proxy | 53 of 81 | 0.0 | 0.0 | 0.0 | 0.0 |
| forage-ranked | **81 of 81** | 0.6 | 0.6 | 0.5 | **0.1** |

`map.fertilityGrid` says whether something *can* grow. The pasture model says how much, and
when. They are different questions and only the second one feeds animals.

### Quadrum is not season

`Quadrum` is a fixed calendar quarter (Aprimay, Jugust, Septober, Decembary); which one is
winter depends on the hemisphere the colony landed in. Convert with
`SeasonUtility.GetReportedSeason(yearPct, latitude)`, taking mid-quadrum as `(q + 0.5f) / 4f`
and latitude from `Find.WorldGrid.LongLatOf(map.Tile).y`. Assuming Decembary is winter is right
about half the time.

### The number that matters is the lean season

A 11×11 pen of good soil forages ~0.6 nutrition/day for three quadrums and **0.1 in winter**.
A single cow eats ~0.9/day. So the pen that comfortably carries the herd for nine months
starves it in the fourth, and on the day it is fenced those two pens look identical.

Anything scoring a pen on today's forage is measuring the wrong quadrum three times out of
four. Score the minimum across all four.

---

## Traps, all found by building one

- **All three defs are made from stuff.** `stuff: null` places nothing, silently.
- **Do not fence the gate cell first.** Every edge cell getting a fence blueprint means the
  gate then fails to place — a blueprint already occupies it — and the pen seals shut.
- **`listerBuildings` cannot see a blueprint.** Guarding on "is a marker built" fences a second
  pen every pass until the first one finishes. Count blueprints and frames too.
- **Do not demand a perfectly clear square.** A 23×23 pen is 529 cells; requiring every one to
  be unobstructed means a large pen never fits on a real map. The *perimeter* must take a fence
  all the way round; the interior can afford a few boulders.
- **Grazing animals are `RaceProps.Roamer`.** A husky needs no pen and proves nothing when
  testing one.

---

## Open

- **A pen is sized once.** It is fenced for the herd standing there at the time; animals bought
  or born later do not widen it, so a winter shortfall can only get worse. The report names the
  shortfall but nothing acts on it.
- **Enclosure is never verified after building.** `CompAnimalPenMarker.PenState.Enclosed` is
  public and would say plainly whether the finished fence actually holds, which is the
  `ReportSettledRoom` pattern applied to pens — a verdict when it stands, not when it is
  ordered. See [[rimworld-rooms]] for why the deferred verdict is the one worth trusting.
