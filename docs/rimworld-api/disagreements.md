# Where the running game did not match a reference note

`goal.md` §9 makes [`../rimworld/`](../rimworld/index.md) immutable and requires that any
disagreement be recorded **outside** those files. This is that record.

Almost none of these are the notes being wrong. They are the notes being written for a player
deciding what to do, read by code deciding a threshold — which is the case §9 anticipates when it
says not to take a *number* from a note. Each entry says what the note says, what the install
says, and which to use for what.

---

## Growing temperature

**Note** — [plants.md](../rimworld/plants.md#mechanics-worth-knowing): growth is normal between
roughly 10–42°C, slowing sharply below 10°C.

**Install** — `RimWorld.Plant`, read through the metadata probe **[read]**:

```
DefaultMinGrowthTemperature        = 0
DefaultMinOptimalGrowthTemperature = 6
DefaultMaxOptimalGrowthTemperature = 42
DefaultMaxGrowthTemperature        = 58
```

**Reading** — barely a disagreement at all, and this entry originally overstated it because it was
written before the full constant set had been read. The note's **upper bound of 42 is exactly the
def's optimal maximum**. Only the lower bound differs: the note says 10 where the optimal band
opens at 6, and growth remains possible down to 0.

**Which to use** — the defs, and the right constant for the question. There are four thresholds
here where prose reasonably gives one range, and they are not interchangeable: a growing-season
forecast keyed on the *optimal* minimum calls a shoulder season barren while crops are in fact
still growing, and a colony that stops sowing on that basis loses real days at both ends of the
year.

**Worth recording about the process, not the fact.** The probe run that produced this also
corrected the entry that cited it. Writing down a disagreement from three remembered constants
produced a sharper claim than the four real ones support — which is the same failure as taking a
number from a note, committed in the other direction.

---

## Muffalo revenge chance

**Note** — [animals.md](../rimworld/animals.md#farm--pack-animals) gives Bison "10% hunt-revenge
chance" and describes Muffalo as "Pack animal, wild herds common on many maps", without a figure.

**Install** — both carry `manhunterOnDamageChance` 0.1. **[read]**

**Reading** — a gap rather than a disagreement; the table gives the number for one of a pair and
omits it for the other.

**Which to use** — the def. And note the deeper point that no table conveys: 0.1 is **per wound**,
so what a hunt costs depends on how much shooting the animal absorbs. See
[animals.md](animals.md#manhunterondamagechance-is-per-wound).

---

## Combat power as a measure of danger

**Note** — [combat.md](../rimworld/combat.md) and the animal tables describe danger
qualitatively, which is the honest level for prose.

**Install** — `kindDef.combatPower` is a single number per type and is widely assumed to mean
"how hard this thing fights". Measured against offence × toughness: Boomalope 80 vs 19,
Megasloth 280 vs 221. **[live, run 164]**

**Reading** — `combatPower` is a storyteller raid-points budget. It folds in hazards that are not
fighting ability at all: a boomalope is rated 80 largely because it explodes.

**Which to use** — depends on the question, and neither is a drop-in for the other. Full argument
in [animals.md](animals.md#combat-power-is-not-fighting-ability).

**Settled by run 174, against the measurement.** The colony hunted two boomalopes at day 0 hour
0 with the bar computed against combatPower 80, and passed over four more. The revenge two hours
later put all four colonists on the ground, started five fires, cut every route to the one
bleeding out, and finished the colony before day 1. Had the decision been wired to the measured
19 — which was the plan, and which is why the number was printed beside it first — the four it
passed over would have been hunted too.

The lesson generalises past animals: an instrument built to confirm a hypothesis is worth
having precisely because it can refute it, and this one refuted its own with a body count
attached.

---

## Units

Not a disagreement with a note — an error made against one, recorded here because it is the
failure mode §9 exists to prevent and it has now happened twice.

- A **days-count gene** ("days of food before another mouth is wanted", range 4–10) was passed
  into a parameter that **multiplies** a barren stretch. On a 25-day winter the colony asked for
  150 days of food. **[live, run 167]**
- `manhunterOnDamageChance` was read as a **per-hunt** probability when it is **per wound**.
  **[live, runs 161–164]**

Both survived because the arithmetic was tested with the number it *should* have been given, so
the tests encoded the right intent while the wiring supplied something else. Both surfaced only
once the value was printed in the chronicle beside the thing it was being compared against.

**The lesson that generalises:** a name tells you what a quantity is called, not what it is
denominated in. Ratios and counts read identically at a call site. Print the answer, not just the
inputs — a wrong number is obvious the moment it is next to a right one, and invisible until then.
