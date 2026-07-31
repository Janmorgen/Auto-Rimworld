# Handoff — goal planning direction

State of the work as of 2026-07-31. The README describes *what the mod is*; this describes
*where the work is* and what to do next. Read both.

## Where things stand

Branch `feat/autonomous-colony-director`, pushed, **not merged to `main`**. `main` is still the
original HelloWorld template commit. Merge with `git checkout main && git merge --ff-only
feat/autonomous-colony-director` when you want it there.

The mod runs in RimWorld 1.6.4871. 71 offline tests pass. It has been played against generated
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

Research is now part of the same chain. A goal declares `RequiresResearch`, and the planner
walks that tree back to something startable exactly as it walks goal prerequisites, so wanting
refrigeration ends up studying electricity. `ResearchChain` holds that walk and is deliberately
free of game types, so it is covered offline.

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

## The power chain

It had never run, and could not have. Four faults, fixed together — see the commit message on
"Research what the plan is blocked on" for the detail:

- Research was never steered by the plan, and every power building is research-gated.
- `PowerGoal` was satisfied by a Power *room* existing, which is true the moment it is reserved.
- The Power room was furnished with a solar panel, under the roof the planner had just asked for.
- A battery counted as a power source.

Verified with `AUTOCOLONY_POWERTEST=1`, which runs `PowerChainSelfTest`: it hands the real
planner hand-built colony states and records what it decides, so the arbitration is settled in
seconds instead of hours. Every case behaves — including the ones that used to pass for the
wrong reason, like a generator that is built but producing 0W.

## Not verified

- **The chain over a long run.** Proven at the decision layer and through construction, but no
  colony has been carried across seasons to see how it holds up.
- Production bills beyond butchering, defence positioning under a real firefight, and any
  behaviour over in-game years rather than days.

## What to do next, roughly in order

1. **Apparel and heating.** A cold snap still has no answer: no warm clothes are crafted or
   assigned, no heaters exist. This killed a colony once already and is the largest survival
   gap. It is now cheap: `Heater` needs only Electricity, so this is a goal requiring `Power`
   with `RequiresResearch` of Electricity, plus a capability check on a *working* heater — and
   `PowerChainSelfTest` already has the probe harness to prove it without playing a colony into
   a cold snap.
2. **Cut the search's dimensionality.** ~50 genes against tens of epochs. A live colony
   measured score noise at ±0.061, roughly three times the ~0.02 where offline tests show the
   sequential search going flat. Grouping work-type weights by category is the obvious cut.
3. **Combat positioning.** Everyone rallies to one point; no cover, no chokepoints, and no
   doctor held back when someone is downed.

## Traps worth knowing before you touch it

- **Existence is not function.** A kitchen with no stove cannot cook; an unpowered turret is a
  wall decoration; a Power room is not power; a solar panel under a roof produces nothing. All
  four shipped as bugs. Check for the capability, not the object — and when you write the check,
  ask what state would satisfy it for the wrong reason.
- **Placement does not check research.** `GenConstruct.CanPlaceBlueprintAt` ignores the tech
  tree, because the build menu is what enforces it and the director does not go through one.
  `PlacementUtil.TryPlace` now tests `IsResearchFinished`; anything placing blueprints another
  way must too.
- **Every power building sits behind a different project.** Electricity for conduits, the
  generator and the electric stove; Batteries; SolarPanels; AirConditioning for coolers. A goal
  that wants any of them should say so in `RequiresResearch`.
- **A `-quicktest` colony starts with Electricity already researched**, which will quietly hide
  any research-gating bug. `ResearchManager.ResetAllProgress()` winds it back.
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
cd Tests/AutoColony.Tests && dotnet test      # 71 tests, learning layer and goal plumbing
```

Offline tests cover anything free of `Map` and `Pawn`; everything else needs a colony. Launch
an isolated instance so a test cannot disturb a session in progress:

```bash
./RimWorldLinux -savedatafolder=<tmpdir> -quicktest -logfile <path>
```

Read `<savedata>/AutoColony/chronicle.log` rather than the game log — it carries decisions with
their reasoning, which is what makes a failure diagnosable. No command-line argument loads a
specific save; that needs UI clicking.

For anything the colony would take in-game weeks to reach, set `AUTOCOLONY_POWERTEST=1` and
extend `PowerChainSelfTest`. It hands the real planner constructed colony states and logs what
it decides, against the real def database — which answers arbitration questions in seconds and
is the only practical way to test a state a colony rarely survives into. It also clears the
short-term goals outright, so a run reaches its long-term horizon in minutes.

## Diagnosing a failure

Read the chronicle backwards from the failure, not the end state. Colony deaths are chains: a
raider arrives, nobody is drafted, a fire starts, the fire is not fought, and the response to
the resulting starvation kills the survivors. The end state will say "cold" or "starvation" and
be wrong about the cause — that mistake has already been made once here.

Longer-form knowledge lives in the AgentKnowledge store under `notes/auto-rimworld/` and
`notes/rimworld/`, including the design principles the director is expected to obey.
