# RimWorld reference

The complete set — twenty-one linked notes on RimWorld's core systems, supplied for this project's use. Base game
only — nothing here covers Royalty, Ideology, Biotech, Anomaly or Odyssey, which matches the
install the director runs against (1.6.4871, no DLC).

Start at [index.md](index.md).

## What this is for, and what it is not

**Orientation, not ground truth.** These notes say so themselves in several places — "exact
numbers can drift slightly between patches", "relative comparisons rather than fixed values".
The director must not encode a number read from here as though it were a fact about the game.

The project already has the tool for that distinction: the API metadata probe under
`$JD/tmp/apiprobe/`, which loads the real `RimWorldLinux_Data/Managed` assemblies and the defs
in `Data/Core/Defs`. Every mechanical constant this director relies on was read that way —
`TreeBase.harvestedThingDef`, `FueledStove` fuel capacity, `Alert_LowMedicine`'s threshold of
2 per colonist, `JoyGiverDef.requireChair`.

So the working rule, which is just goal.md §2's ladder applied to a document:

- Use these notes to **know a mechanism exists** and to know what to go and look for.
- Read the **defs or the IL** for the number before any code depends on it.
- If a note and the game disagree, the game is right and the note gets a correction here.

They are genuinely useful for the first of those. Several systems in this director were built
without knowing a mechanism existed at all — seating adjacency, fuel as a haulable good, the
tree-sowing research gate — and each cost a colony before it was found.

## Where these corroborate decisions already made

Worth recording, because agreement from an independent source is evidence and disagreement
would be a bug:

- **"Untreated bleeding is what actually kills in most combat deaths, not the initial hit"**
  ([health-and-medicine.md](health-and-medicine.md)) — this is precisely the failure that ended
  runs 132, 134 and 135, and the reason `ShouldReserveMedic` now treats bleeding as a clock
  that overrides the fighting line rather than as one more casualty.
- **Hunt-revenge chance** ([animals.md](animals.md)) — megasloth and the big predators at 50%,
  wolves at 100%. Already handled: `ResourceModule` reads the real
  `race.manhunterOnDamageChance` rather than a table, which is the right way round.
- **Impressiveness is weighted toward whichever of the four stats is weakest**
  ([room-attributes.md](room-attributes.md)) — worth checking the room-quality scorer against,
  since a mean would rate a filthy but expensive room far too kindly.
- **Research progress is stored per project and survives losing the bench**
  ([research.md](research.md)) — supports scoring research by points banked rather than
  projects finished, which is what the evaluator now does.

## Where they expose a gap

- **Cover** ([combat-and-weapons.md](combat-and-weapons.md)): "fighting from behind cover while
  the enemy is in the open is one of the most reliable defensive advantages in the game."
  `CombatAssessment.FightingValue` is offence x toughness, where toughness is health, working
  limbs and armour. **There is no positional term at all.** The colony decides whether a fight
  is winnable from the two sides' bodies and equipment, and cannot express the advantage the
  notes call the most reliable one available. `FiringPosition` knows about cover; the strength
  model it feeds does not. Recorded as a task rather than patched, because a positional term
  changes every engagement decision and wants a run to measure against.
- **Ranged weapons cannot fire at an adjacent enemy** (same file). `Offence` takes the ranged
  profile whenever a pawn carries a ranged weapon, so a bow-armed colonist in melee is scored
  at a value they cannot actually deliver. Smaller, and it cuts both ways since raiders are
  scored the same, but it is a known inaccuracy rather than a simplification.

## An entire system the director does not have

[trading.md](trading.md) describes a capability with **no counterpart anywhere in the
director**. There is no trade module; the only mention of a trader in the whole codebase is a
doc comment in `IncidentModule` listing "traders offering deals" among the things incidents
can be. A caravan visiting the map is answered the same way any other incident is, and never
traded with.

That matters most for one resource. These colonies reach `med 0` — run 136 was at zero
medicine on day 21 with a Low medicine alert standing — and medicine is precisely what stops
the bleeding deaths that ended runs 132, 134 and 135. A visiting caravan needs no research to
trade with, only a colonist sent to talk to it.

Recorded as a task. Noted here because the gap is only visible by reading a file about a
system and finding nothing on the other side of it, which is the argument for having these
notes at all.

Two smaller points from the same file, neither actionable yet:

- Trade is a lever on colony **wealth**, and wealth sets raid size. `ThreatForecast` already
  models that relationship and `Outgrowing()` already watches for readiness falling as wealth
  climbs — but these colonies die below the 14,000 wealth floor where wealth contributes
  nothing, so it is not the binding constraint at the stage they are failing.
- The negotiating colonist's **Social** skill sets prices, which would be the first thing a
  trade module needed to choose whom to send.
