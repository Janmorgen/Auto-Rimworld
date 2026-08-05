# Asking the game the right question

Nine faults in one session had the same shape, in nine different subsystems. Each time a
property existed that looked like the answer and sat one step nearer than the property that
actually was. Each time the nearer one was *true* — just about something else.

This note is the list, because the list is the lesson.

---

## The pattern

| the question | what was asked | what should have been asked |
|---|---|---|
| Is there food? | `ResourceCounter.TotalHumanEdibleNutrition` | reachable food, stockpiled or loose |
| Is there medicine? | `ResourceCounter.GetCount` | everything on the map, unforbidden |
| Can we sew a coat? | count of `Cloth` | everything in `Fabric` **or** `Leathery` |
| Have we material? | stockpiled wood/steel | what a builder can fetch from anywhere |
| Is this crop food? | `IsNutritionGivingIngestible` | `ingestible.preferability >= RawBad` |
| Is this a drug crop? | `harvest.IsDrug` | what the leaves are an *ingredient for* |
| Can we use this crop? | the recipe's research | **its bench's** research too |
| Is anyone healthy? | `SummaryHealthPercent` | that, **and** whether anyone is untended |
| Is the colony fed? | days in the larder | that, **and** whether anyone is starving |
| Why is nothing cooked? | is there a cook, is there food | **is there fuel in the stove** |
| Is this joy building usable? | is it built | is a chair *touching* it |
| Which buildings need fuel? | generators built minus running | `CompRefuelable.ShouldAutoRefuelNow`, of everything |

### The gate is never on the thing

Three times in one session, on three unrelated chains, the research that
*gates making something* was not the research written on the thing:

| looks like | actually |
|---|---|
| `Make_Wort` has no research → hops are usable | its only bench is the brewery, which needs Brewing |
| psychite tea needs a drug lab → expensive | its recipe runs on a **campfire**; only Flake needs the lab |
| `Apparel_Parka` has no research → anyone can sew one | both tailoring benches need ComplexClothing |

A colony with seventy-one cloth froze at −11C because of the third one. The parka was
never gated; the only place to make it was.

So to ask "can this colony make X", check the recipe's `researchPrerequisite` **and** whether
any bench in `RecipeDef.AllRecipeUsers` has its research done. Most recipes are listed on the
bench rather than the other way round, and `AllRecipeUsers` handles both directions.

Two more of the same family, about time rather than category:

- **Is the pen enclosed?** — asked once and latched. Enclosure is a property of a standing
  structure, and walls come down. Ask every pass, speak on change.
- **Is this site buildable?** — asked as "is anything standing here". Marsh and water hold no
  edifice and refuse a fence anyway. `GenConstruct.CanPlaceBlueprintAt` is the real question.

---

## Why the nearer property is so attractive

It is always the one with the obvious name. `IsDrug` is *right there* on the def and reads
exactly like the question being asked. `ResourceCounter` is the class called "the thing that
counts resources". `SummaryHealthPercent` is literally a health percentage.

And it is nearly always correct. `ResourceCounter` gives the right food number for every colony
that has tidied up. `SummaryHealthPercent` is right about every colonist whose problem is a
missing leg. The divergence appears **only in the cases that matter** — a colony too busy to
haul, a colonist dying of an infection — because that is exactly when the two questions come
apart.

That is what makes it dangerous rather than merely wrong. A test in ordinary conditions confirms
it. It fails silently, in the emergency, in the direction that hurts.

---

## What to do instead

**Ask the game the question the player would ask.** Where RimWorld already computes something
for its own UI or alerts, use that: `HasHediffsNeedingTendByPlayer` is behind the "needs
tending" alert, `CompAnimalPenMarker.PenState.Enclosed` is behind "Pen needed",
`FoodPreferability` is what a pawn consults before eating. Agreeing with the player's screen is
worth more than a second implementation that can drift from it.

**Prefer the category to the item.** `Fabric`/`Leathery` rather than `Cloth`. Stuff categories,
`thingCategories`, `ingestible.preferability` — these hold for DLC and mods without anybody
maintaining a list, and they are what the bench itself checks.

**Follow the gate one hop further.** A recipe's research is not the only gate; its bench has one
too. A crop's harvest being nutritious is not the same as anybody eating it. Where a chain has
steps, check the step that actually stops you.

**Measure once, read everywhere.** `colonistsUntended` was added to make the *score* honest and
turned out to be the number the *work priorities* should have been reading all along, replacing
`avgHealth < 0.9` — a proxy that was wrong in both directions. When a real measurement exists,
everything that was guessing at it should read it.

**A remedy that cannot clear its own complaint will run for ever.** The tell is a repeat count:
`AddTable` eight times, `AddRecreation` seven. Before adding a remedy, ask what the survey will
see on the pass *after* it succeeds — if the answer is "the same thing", the remedy is aimed at
something that is not the cause.

**Some faults produce no thought at all.** The defect survey reads colonist moods, so it can only
find what a colonist is unhappy about. A colonist who wants to play chess, finds no chair and
walks away records nothing; they simply take no joy. Every fault found by reading moods was
findable because the game complains — for the rest, the game's *alert bar* is the instrument, and
it is the reason the standing rule is to read the screenshot rather than only capture it.

**Print the number before trusting it.** Four instrument faults in the monitoring scripts this
session were found because a value looked *slightly* wrong: `pens: 3` where `grep -c "pen is"`
matched "the o**pen is** elective"; `died: 0\n0` from `grep -c` printing a zero and exiting 1.
The readings that agree with you are the dangerous ones.

---

## The composition version

The same shape appears between rules rather than inside one. Six times this session, two rules
each correct in isolation closed into a loop:

- Clear the footprint before building + abandon a site that never finishes → a colony that
  abandons every site, because a cell under an un-mined boulder is in no region and the finished
  shell reads as unfinished.
- The planner marks a room's interior `BuildRoof` + Reclaim marks a demolished room `NoRoof` →
  a cell in both areas, and a colonist building and unbuilding the same roof for ever.
- Grow a second crop for blight insurance + count every growing zone as a crop → cotton and hay
  satisfying the food-variety rule.
- Nothing cooked → raise Cooking + refuelling is a Hauling job → a cook standing at an unlit
  stove, while the rule that raised Cooking outranked the Hauling that would have lit it. Run 108
  held this for eighteen days; the stove had 0.85 of a 50-unit hopper. Mood fell to 0.15, Aisu
  broke, and the livestock the pen and fodder plot were built for was slaughtered.
- Place a table when nobody can eat at one + a pawn reaches a table only by finding a chair →
  eight tables in one colony, and the complaint unchanged after every one of them.

The tell is always that each rule, read alone, is obviously right. Ask instead what the *other*
rule believes about the same fact.

---

## The one after this

Everything above is about measuring the wrong thing. There is a second fault that begins where
this one ends — the measurement is right, the diagnosis is right, and the remedy is aimed
through a channel that cannot deliver: raising Cooking at a stove with no fuel, ordering a
deconstruct that ranks below sewing, planning a bed while a colonist burns. Four instances in
the same session as these. See [[acting-through-the-wrong-channel]].

The two share a tell — a remedy that fires over and over while nothing changes — and the repeat
count does not say which of the two it is. It only says to look.

## Related

- [[acting-through-the-wrong-channel]] — the same failure one step later, in the remedy
- [[rimworld-defs-and-chains]] — where each gate actually sits
- [[rimworld-plants]] — the three attempts it took to classify a psychoid plant
- [[rimworld-pens]] — "nothing is standing here" is not "a fence can be built here"
