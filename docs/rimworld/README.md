# RimWorld reference

A linked set of notes on RimWorld's core systems, supplied for this project's use. Base game
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

## Not included

`index.md` links to five files that were not supplied, so those links dangle:

`combat-and-weapons.md` · `factions.md` · `storyteller-and-events.md` · `trading.md`

The index's own reading order references them too. Left as-is rather than edited, so that if
they arrive later they simply drop in.
