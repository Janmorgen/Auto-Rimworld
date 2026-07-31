# Handoff — goal planning direction

State of the work as of 2026-07-31. The README describes *what the mod is*; this describes
*where the work is* and what to do next. Read both.

## Where things stand

Branch `feat/autonomous-colony-director`, pushed, **not merged to `main`**. `main` is still the
original HelloWorld template commit. Merge with `git checkout main && git merge --ff-only
feat/autonomous-colony-director` when you want it there.

The mod runs in RimWorld 1.6.4871. 59 offline tests pass. It has been played against generated
colonies and against a real save (`AutoColony_trial_baseline`, the colony "Botein-IV").

## The direction this was heading

The last several rounds moved the mod from *a set of subsystems on timers* toward *a planner
that decides what matters and subsystems that aim at it*.

`Core/Goals/` holds that layer. Goals declare a **horizon** (Immediate / ShortTerm / LongTerm)
and their **prerequisites**. Nearer horizons pre-empt further ones outright; a goal whose
prerequisites are unmet hands over to whichever prerequisite is actionable. Wanting
refrigeration therefore resolves by itself into power, then steel and components, then mining.

The planner publishes a focus and does not act. Modules read `ctx.plan`. Adding a goal is the
intended way to teach the colony a new dependency — that is how "hunting does not feed anyone
without a butcher table" got fixed, and it changed behaviour without tuning anything.

**If you extend this, add goals rather than special cases in modules.** The arbitration is
worth keeping in one place.

## Verified in game

- Loads, runs unattended at Superfast, recovers from event pauses, respects manual ones.
- Base planning, work priorities, zones, item claiming, research, hunting, equipment, incidents.
- Epochs score and the evolution engine advances; training rounds complete a full
  snapshot → trial → reload → trial → winner cycle.
- Distance-scaled fire response: 57 fires at 105 cells were left alone and burned out on their
  own, while the plan pre-empted feeding to answer a raid and returned to feeding afterwards.
- Capability-based hunting: strength rose 13 → 73 → 133 as colonists armed themselves, with
  Megasloth and Rhinoceros correctly declined and taken back off the table as food recovered.

## Not verified

- **The power chain end to end.** Generator, conduit and cooler all exist in code. No test
  colony has survived long enough to build them in sequence, so it has never actually run.
  This is the first thing to check.
- Production bills beyond butchering, defence positioning under a real firefight, and any
  behaviour over in-game years rather than days.

## What to do next, roughly in order

1. **Prove the power chain.** Load a healthy colony with steel and components, confirm
   `Power` becomes the focus, and watch for a generator, conduits and a powered cooler. A
   `Freezer` room with an unpowered cooler is the likely first failure.
2. **Apparel and heating.** A cold snap still has no answer: no warm clothes are crafted or
   assigned, no heaters exist. This killed a colony once already and is the largest survival
   gap. It wants a goal with a prerequisite on power.
3. **Cut the search's dimensionality.** ~50 genes against tens of epochs. A live colony
   measured score noise at ±0.061, roughly three times the ~0.02 where offline tests show the
   sequential search going flat. Grouping work-type weights by category is the obvious cut.
4. **Combat positioning.** Everyone rallies to one point; no cover, no chokepoints, and no
   doctor held back when someone is downed.

## Traps worth knowing before you touch it

- **Existence is not function.** A kitchen with no stove cannot cook; an unpowered turret is a
  wall decoration. Both shipped as bugs. Check for the capability, not the object.
- **`ResourceCounter` only counts what is in a stockpile.** On day one everything is on the
  ground, so material checks read zero. Use `PlacementUtil.AvailableCount`.
- **Almost everything arrives forbidden**, including scenario starting resources.
- **Colonists only fight fires inside the home area.**
- **A paused game issues no ticks**, so `GameComponentTick` cannot recover a pause.
  `GameComponentUpdate` alone was measured insufficient; `GameComponentOnGUI` is what works.
- **Verbose logging is off by default** and module activity logs at verbose level, so an
  established colony looks stalled when it is merely quiet. That misread cost a diagnosis.

## How to work on it

```bash
cd Source/AutoColony && dotnet build          # → Assemblies/AutoColony.dll
cd Tests/AutoColony.Tests && dotnet test      # 59 tests, learning layer only
```

Offline tests cover anything free of `Map` and `Pawn`; everything else needs a colony. Launch
an isolated instance so a test cannot disturb a session in progress:

```bash
./RimWorldLinux -savedatafolder=<tmpdir> -quicktest -logfile <path>
```

Read `<savedata>/AutoColony/chronicle.log` rather than the game log — it carries decisions with
their reasoning, which is what makes a failure diagnosable. No command-line argument loads a
specific save; that needs UI clicking.

## Diagnosing a failure

Read the chronicle backwards from the failure, not the end state. Colony deaths are chains: a
raider arrives, nobody is drafted, a fire starts, the fire is not fought, and the response to
the resulting starvation kills the survivors. The end state will say "cold" or "starvation" and
be wrong about the cause — that mistake has already been made once here.

Longer-form knowledge lives in the AgentKnowledge store under `notes/auto-rimworld/` and
`notes/rimworld/`, including the design principles the director is expected to obey.
