# RimWorld's room system, as measured

Everything here was established by building the thing in-game and reading back what RimWorld
said about it, using the `showcase` scenario (`AUTOCOLONY_SCENARIO=showcase`). None of it is
from documentation or from reading the game's source, which is not available here — the
`RoomRoleWorker` classes live in `Assembly-CSharp.dll` and there is no decompiler on this
machine. Where something is inferred rather than measured it says so.

The method matters because guessing at this cost most of a day. The reliable loop was: state
what a room is expected to classify as, build it, and let the game disagree out loud.

---

## What makes a room a room

Three conditions, all required:

- **Enclosed.** Walls all the way round, with no gap. One missing cell and the space merges
  with the outdoors.
- **Roofed.** `map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed)` over the interior.
- **Reachable.** A door. A sealed box is a legal room and a useless one — every job into it
  fails in a way that reads like a director bug rather than a construction one.

A space failing any of these returns the **roomless sentinels** rather than an error, which is
the trap: `Space` reads **350.0**, `Impressiveness` **0.0**, and `Role` is `None`. A room
reporting exactly those numbers has not been rated at all. `room.PsychologicallyOutdoors` and
`room.TouchesMapEdge` distinguish "outdoors" from "genuinely bad".

---

## Role classification — measured

Built one of each, stated the expectation in advance, and read `room.Role.defName` back.
**Eleven of twelve as predicted.**

| Built as | Contents | Game calls it |
|---|---|---|
| Bedroom | 1 bed | `Bedroom` ✓ |
| Barracks | 3 beds | `Barracks` ✓ |
| Dining | table + 2 chairs | `DiningRoom` ✓ |
| Recreation | chess, Ur, horseshoes | `RecRoom` ✓ |
| Hospital | 2 medical beds | `Hospital` ✓ |
| Research | research bench | `Laboratory` ✓ |
| Workshop | stonecutter + tailoring bench | `Workshop` ✓ |
| Kitchen | stove + butcher table | `Kitchen` ✓ |
| Storage | 3 shelves + stockpile zone | `Storeroom` ✓ |
| Prison | 1 bed marked `ForPrisoners` | `PrisonCell` ✓ |
| Barn | 2 animal sleeping spots + trough | `Barn` ✓ |
| Tomb | **2 graves** | `Room` ✗ |

### What this establishes

- **A bedroom is a room with a bed in it.** Ownership is *not* required. I claimed it was, to
  explain a room that showed as `Room` — the real cause was that no bed had been placed at all.
  A wrong explanation for a broken measurement is worse than no explanation, because it stops
  the search.
- **One bed is a `Bedroom`, more than one is a `Barracks`.** Not a fuller bedroom — a different
  room with a different and strictly worse mood curve (see below).
- **A storeroom needs a stockpile zone, not just shelves.** A storeroom is a room things are
  *stored in*, and nothing is stored anywhere without a zone or a shelf accepting it. Three
  shelves in an unzoned room read as a plain `Room`.
- **A prison cell needs the bed marked *and* the room told.** `bed.ForOwnerType =
  BedOwnerType.Prisoner`, then `room.Notify_BedTypeChanged()`. `IsPrisonCell` is cached on the
  room rather than derived, so the flag alone leaves the game refusing captures with "no
  enclosed prisoner-marked bed" while every clause of that sentence looks satisfied.
- **A hospital needs `bed.Medical = true`.** Unmarked beds in a room make it a bedroom.
- **Two graves do not make a `Tomb`.** The one prediction that failed. Untested: whether it
  wants more graves, or *occupied* ones. Worth settling before the director builds tombs on the
  assumption that two is enough.

Fifteen `RoomRoleDef`s exist in the base game (`Core/Defs/Rooms/RoomRoles.xml`). `None` and
`Room` are the fallbacks; `PrisonBarracks` is the multi-bed prison, not tested. No DLC active —
Royalty, Ideology and Biotech each add more.

---

## Room stats

Eleven `RoomStatDef`s (`Core/Defs/Rooms/RoomStats.xml`). Five are visible, six are hidden.

**Bands come from the def itself**, so read them rather than hardcoding thresholds:
`stat.GetScoreStage(score).label` returns the exact word the room-stats overlay shows on hover
(`G` in game), and `GetScoreStageIndex(score)` gives an ordinal that works for any stat and
survives a mod redefining the bands.

Impressiveness: `awful` <20, `dull` 20, `mediocre` 30, `decent` 40, `slightly impressive` 50…
Space: `cramped` <12.5, `rather tight` 12.5, `average-sized` 29, `somewhat spacious` 55…

### Which stats the *builder* controls

Space comes from room dimensions. Beauty from wall material and furniture. Impressiveness is
the game's own combination of the two. **Cleanliness is not a building outcome** — the same room
rates well or badly depending on whether anybody swept it, so holding the builder to it scores
the work priorities instead.

That distinction has teeth. The showcase Barracks read **impressiveness +11.5** and the Bedroom
**−22.1**, same construction, same materials — the difference was cleanliness 0.00 against
−1.00. Cleanliness moves impressiveness a long way, and it is not the planner's doing.

The two hidden stats that matter most to rooms a colony builds are **both** derived from
cleanliness by curve, and are therefore work-priority telemetry rather than building feedback:

- `ResearchSpeedFactor` — 0.75× at cleanliness −5, 1.15× at +1
- `FoodPoisonChance` — 5% at −5, 0% at −2
- `SurgerySuccessChanceCleanlinessFactor` — **0.60×** at −5, 1.00× at 0, 1.15× at +5
- `InfectionChanceFactor` — **1.0** at −5, 0.5 at 0, 0.2 at +1

The last two are the ones with a body count. A botched surgery can kill the patient outright,
and a filthy room takes success down to six-tenths of what it would otherwise be — while
leaving post-operative infection at full odds. So operating in a dirty room to cure an infection
hands the colonist a fresh one at maximum probability, which is as close to a closed loop as the
game gets.

Nothing here is a building outcome, which is the point: the same room scores all four well or
badly depending only on whether somebody swept it. Cleanliness is work-priority telemetry, and
`Cleaning` is the one input to it the builder cannot supply.

### Furniture shrinks the Space *measurement*

Cells under an impassable building leave the room's region, so a furnished room measures
smaller than the shell that was built. Measured in run 53:

| Room | Shell | Furnished |
|---|---|---|
| Kitchen 7×7 (stove + butcher table) | `average-sized` 35.0 | **`rather tight`** |
| Research 9×7 (3×2 bench) | `average-sized` 49.0 | `average-sized` |

The walls did not move. Anything judging a room on space has to decide which question it is
asking: *how big was it built* (measure the shell, before furniture) or *how much floor is left*
(measure it live). Judged live, a kitchen is marked down for owning a stove — so the director
records the shell's band at completion and judges against that.

It also means a 7×7 is not big enough for a room holding two or three impassable buildings. The
Kitchen profile went to 9×7 on this evidence.

### Measured room sizes

| Room | Interior | Space |
|---|---|---|
| 7×7 | 25 cells | 32–34 |
| 9×7 | 35 cells | 42–49 |
| 9×9 | 49 cells | 62–68 |
| 11×9 | 63 cells | 85–87 |

So a 7×7 is `average-sized` and a 6×6 is `rather tight`. Nothing the director builds by default
gets anywhere near `impressive` — every showcase room came out `awful` on a wooden build with a
plank floor.

---

## Mood curves worth knowing

| Thought | Range |
|---|---|
| `SleptInBedroom` | −2 … +8 |
| `SleptInBarracks` | **−7** … +4 |
| `JoyActivityInImpressiveRecRoom` | +2 … +8 (every stage positive) |
| `AteInImpressiveDiningRoom` | +2 … +8 |

A barracks is worse at the floor *and* lower at the ceiling — worse in every band. A rec room
has no downside band at all: it either pays or it does not exist.

The threshold is one bed. A second bed in the room changes which thought the sleeper gets, so
"a bedroom for two" is not a thing — it is a barracks, and costs about five mood a head a night
against the private room it could have been. Confirmed live: the director's own bedroom was
called a `Bedroom` with one bed built and a `Barracks` the moment the second went in.

---

## API traps, all found the hard way

- **`GenSpawn.Spawn` defaults to `WipeMode.Vanish`, and `CanSpawnAt` returns true for a cell it
  would wipe.** "Can spawn" includes "can spawn *over*". Placing several things by rescanning
  from the first interior cell means each destroys the last, and only the final item survives.
  Check every cell of the footprint for an existing building first. This is the single bug that
  made twelve rooms each hold exactly one torch.
- **`CellRect.CenteredOn(centre, width, height)` takes full dimensions**, not half. Passing half
  builds a 4×3 box where a 9×7 was intended. The tell was sizes coming back *identical on two
  different maps* — that is never a map problem.
- **`room.Cells` excludes cells under impassable buildings.** A cell inside a building belongs
  to no region and therefore to no room, so walking `room.Cells` to inventory a room silently
  skips every research bench, stove and table — exactly the things that decide its role. Walk
  the rectangle instead.
- **An edifice spawned onto a wall replaces the wall.** A 3×2 bench anchored near the edge opens
  the room to the outdoors. Constrain the whole footprint, not the anchor cell.
- **A cooler ships set to 21°C.** Correctly built, faced and wired, it then holds the room at
  exactly the temperature it would have been anyway. `CompTempControl.targetTemperature` is the
  point of the machine and it is not the default.
- **Room stats update on a priority queue, not on demand.** Reading them the instant the last
  wall goes up returns roomless defaults. Wait ~900 ticks.
- **Role is decided by *built* contents.** Reading it at shell completion, while the furniture
  is still blueprints, is honest about the shell and premature about everything else.

---

## Implications for the director

- `room.Role` is a free functional check on whether a planned room actually became what the
  layout calls it — a Research room with no bench reads as `Room`, not `Laboratory`. Read it
  *after* the furniture is built, not at shell completion. The planner now does both: a verdict
  when the walls close (honest about the shell) and a second one when the key furniture is
  actually standing (`ReportSettledRoom`), which is the one that says whether the room works.
- Room-quality judging (`Core/Rooms/RoomQuality.cs`) uses space and impressiveness only, for the
  reason above, and holds shared bedrooms to a higher floor because of the barracks curve.
- A rec room is pure upside and was unreachable before this session — joy buildings were placed
  by an upkeep remedy into whatever room had a free cell, so none ever gathered.
- The `Tomb` result is the open question. Do not assume two graves is enough.
