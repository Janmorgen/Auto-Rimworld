# `docs/rimworld-api/` — how to ask RimWorld a question in code

These are working notes on the **API and def surface** the director actually calls: type names,
method signatures, what a field means as opposed to what it is called, and the places where the
obvious reading of a name turns out to be wrong.

They are the companion to [`../rimworld/`](../rimworld/index.md), and the two answer different
questions:

| | answers | status |
|---|---|---|
| `docs/rimworld/` | how RimWorld *works* — mechanics, tables, tiers | researched truth, **never edited** |
| `docs/rimworld-api/` | how to *read that out of the install* — types, calls, def fields | working notes, edited freely |

**Links run one way, from here into there.** Never the reverse. `goal.md` §9 makes the reference
files immutable, so a link added to one of them would be an edit; every cross-reference therefore
lives on this side. If a note over there needs context from here, that context goes in the code
comment where it bites, not in the note.

## The rule these exist to serve

`goal.md` §9, in full, because it is the reason this folder is separate:

> Do not answer a question about the game from memory when a note covers it, and do not take a
> *number* from the note — read the def through the API probe.

`docs/rimworld/` tells you a mechanic exists and roughly how it behaves. It says so itself:
[plants.md](../rimworld/plants.md#mechanics-worth-knowing) calls its figures "planning guidance,
not exact math". That is the correct level for deciding *whether* to build something. It is the
wrong level for a threshold in a scoring function, and the gap between the two has produced real
faults — see [disagreements.md](disagreements.md).

## Marking

Every fact carries how it is known. The distinction is the same one `Connections/Touches.cs` draws
between `Observed` and `Suspected`, and it matters for the same reason.

- **[read]** — pulled out of this install, from the def XML or the metadata probe, with the path
  or command given. Trustworthy.
- **[live]** — observed in a running colony, with the run number. Trustworthy about behaviour,
  and only about the case seen.
- **[compiles]** — the director calls it and builds and runs against 1.6.4871. Proves the
  signature, proves nothing about the semantics.
- **[assumed]** — believed, not checked. Treat as a lead. Anything acted on should be promoted
  out of this bracket first.

## Contents

- [probing.md](probing.md) — getting an answer out of the install: the metadata probe, the def
  XML, and which to reach for
- [animals.md](animals.md) — combat power, revenge, body scale, and the hunting surface
- [health.md](health.md) — the bleeding clock, rescue jobs, beds, capacities
- [pawns.md](pawns.md) — drafting, stats, work tags, reachability
- [construction.md](construction.md) — blueprints, designations, terrain, edifices
- [food.md](food.md) — nutrition, rot, meals, what counts as edible
- [season.md](season.md) — the calendar, growing windows, temperature forecasting
- [trading.md](trading.md) — sessions, tradeables, executing a deal
- [disagreements.md](disagreements.md) — where the running game did not match a reference note

## The install

RimWorld 1.6.4871, base game, no DLC. Assemblies under
`/run/media/deck/SD512/steamapps/common/RimWorld/RimWorldLinux_Data/Managed`, defs under
`Data/Core/Defs`. Everything here is version-specific; a game update invalidates it and the
probe is how it gets re-established rather than re-remembered.

## `[compiles]` is weaker evidence than it looks

The marking convention distinguishes `[read]`, `[live]`, `[compiles]` and `[assumed]`, and
`[compiles]` was being treated as near-proof. It is not. A read can compile, run every tick and
find nothing.

Hunting an exploding animal was priced off `def.GetCompProperties<CompProperties_Explosive>()`.
That compiles. It also returns null for every animal in the game, because a boomalope's explosion
is not a comp — it is
`<race><deathAction><workerClass>DeathActionWorker_BigExplosion</workerClass></deathAction></race>`,
reached as `def.race.DeathActionWorker.DangerousInMelee`. **[compiles, and verified non-empty]**

The failure is invisible by construction: an animal that never explodes and an animal whose
explosion is never detected produce identical logs. It was caught by a colony, not by a compiler
— run 197 hunted three boomrats in its first hour and set eighty-two fires.

So: **a read that can return "nothing" needs a number in the record that says how often it did.**
Where a value is only ever reported when non-zero, an inert read is silent forever. That is why
the hunt line now names the blast hazard it accepted even though nothing acts on the figure.

## Mod settings need RimWorld's own wrapper, or the whole file is ignored

A settings file that looks entirely correct will be discarded in silence unless its root element
is `SettingsBlock`. **[live, runs 213-214]**

```xml
<?xml version="1.0" encoding="utf-8"?>
<SettingsBlock>
  <ModSettings Class="AutoColony.AutoColonySettings">
    <trainingMode>True</trainingMode>
  </ModSettings>
</SettingsBlock>
```

The hand-written template had `<ModSettings>` as the root, with no wrapper. RimWorld could not
parse it and fell back to **every** default: training mode read as off despite the file saying
`True`, and `epochDays` read as 10 against the file's 5.

Two things made this expensive to find. The file was right in every respect a human checks —
correct filename for the mod folder (`Mod_<folderName>_<modClassName>.xml`), correct class
attribute, correct field names, correct values — and nothing anywhere reports that a settings
file failed to parse. It presents as the feature not working rather than as a file not loading.

The tell was a size difference: RimWorld's own write was 201 bytes against the template's 520,
because **it persists only values that differ from their defaults**. A settings file much larger
than what the game writes back is a file the game is not reading.
