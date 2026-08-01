# Handoff — goal planning direction

State of the work as of 2026-07-31. The README describes *what the mod is*; this describes
*where the work is* and what to do next. Read both.

## Where things stand

Branch `feat/autonomous-colony-director`, pushed, **not merged to `main`**. `main` is still the
original HelloWorld template commit. Merge with `git checkout main && git merge --ff-only
feat/autonomous-colony-director` when you want it there.

The mod runs in RimWorld 1.6.4871. 97 offline tests pass. It has been played against generated
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

## Defects — the other half of that

Goals say what the colony *lacks*. `Core/Upkeep/` says what it *has wrong*, which nothing covered
before: every construction path only ever added, and the one repair path fired only when a room's
key furniture was missing. A stove standing in the rain is not missing.

`DefectSurvey` names specific targets — a `Thing` or a `Room`, never a tally — and draws on two
sources. The colonists' own thoughts say what is costing mood, so the colony answers its measured
experience rather than a rule someone guessed. Direct inspection catches what nobody complains
about because it has not hurt yet; an unroofed generator costs nothing until it rains.

**Complaints it cannot fix are reported, not dropped.** That list is the to-do list — a live run
named `EnvironmentCold`, `AteWithoutTable`, `NeedComfort`, `NeedBeauty` and
`NightOwlDuringTheDay`. Adding a case is a row in `Complaints` plus a `RemedyKind`.

**Sharing is not a fault.** `BuildingMeans` scales the whole question on material per colonist: a
destitute colony puts every bed in one room and is not nagged about it, a comfortable one
separates them. The converse matters as much — a colony that built out and then fell on hard
times is not short of resources, it is standing inside them, so `Reclaim` takes surplus rooms back
down for the ~120 units of material in each shell. The two are mutually exclusive by test;
without that the colony would oscillate between spreading out and consolidating forever.

**Moving beats demolishing, and orders can be withdrawn.** Anything `Minifiable` is carried to a
new spot intact or uninstalled and kept as an item; only what cannot be lifted is knocked down.
`CancelStaleOrders` withdraws work whose reason has gone — the case that matters is a colony
that decided to break up a barracks while comfortable and is destitute by the time anyone starts
the job.

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

Watching it finally build turned up three more, all in the base planner rather than the power
code: a pending blueprint read as destroyed furniture (so the room re-queued forever and built
duplicates), the planner opened new rooms while the one the plan asked for stood half-built (two
colonists, seven shells, nothing finished), and the kitchen's `ElectricStove ?? FueledStove ??
Campfire` was not a fallback chain at all — `??` gives way only on null, and all three defs
always resolve.

Verified with `AUTOCOLONY_POWERTEST=1`, which runs `PowerChainSelfTest`. It settles both halves
in seconds instead of hours:

- *Decisions* — hands the real planner hand-built colony states. Every case behaves, including
  the ones that used to pass for the wrong reason (a generator built but producing 0W still
  leaves Power unsatisfied; a freezer whose cooler is dead still wants Refrigeration).
- *Wiring* — stands a fuelled generator and a stranded consumer on the map and lets
  `PowerModule` do the rest:

```
wiring probe: generator (1000W, fuel 75), consumer 19 cells away, on a grid: False
day 0 03h  wiring Electric stove to Wood-fired generator, 19 cells away
day 0 12h  generators 1 (1 running, 1000W), conduits 14, unpowered 0
```

A live colony also reached Power by itself, built the room and queued the generator, so the
decision path and the physical path have both been walked — the live run just never survived
long enough to do the whole thing in one go.

## Not verified

- **The chain over a long run, in one colony.** Both halves are proven, but no single colony has
  been carried from nothing to a powered freezer across seasons.
- **Whether the wood-fired generator is the right long-term choice.** It works indoors and needs
  only Electricity, which is why it was picked, but it burns fuel forever. Solar placed on open
  ground is the better answer once `SolarPanels` is researched — and would need placement
  outside the roofed room, which nothing does yet.
- **Conduit routing.** The director can now *see* that unroofed electrical equipment in rain is
  a fire risk, but it still creates it: `PowerModule` runs an L-shaped path across open ground
  without preferring cells that are already roofed. Prevention is the obvious follow-on.
- Production bills beyond butchering, defence positioning under a real firefight, and any
  behaviour over in-game years rather than days.

## What to do next, roughly in order

1. **Apparel and heating.** Still the largest survival gap, and now confirmed by the colony
   itself rather than by argument: `EnvironmentCold (-4.0)` came top of the unfixable-complaints
   list on a live run. `Heater` needs only Electricity, so this is a goal requiring `Power` with
   `RequiresResearch` of Electricity, a capability check on a *working* heater, and an
   `EnvironmentCold` row in `Complaints`. The probe harness proves it without playing a colony
   into a cold snap.
2. **The rest of what the colony asked for.** The same run named `AteWithoutTable` (no dining
   table), `NeedComfort` (nothing comfortable to sit on), `NeedBeauty`, and
   `NightOwlDuringTheDay` — that last one a scheduling problem rather than a building one, which
   is worth noting because it shows the survey finds things no construction module could fix.
   Each is a row in `Complaints` plus a remedy.
3. **Cut the search's dimensionality.** ~50 genes against tens of epochs. A live colony
   measured score noise at ±0.061, roughly three times the ~0.02 where offline tests show the
   sequential search going flat. Grouping work-type weights by category is the obvious cut.
4. **Combat positioning.** Everyone rallies to one point; no cover, no chokepoints, and no
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
- **`defA ?? defB` is not a fallback between buildings.** Every vanilla def resolves whatever the
  colony has researched, so the first one always wins and the rest are dead code. It cost the
  colony its early kitchen. Choose on capability — researched, powered, affordable — not on the
  def existing.
- **Neighbouring rooms share a wall on purpose.** The layout budges them together to keep the
  base cheap, so anything demolishing a room cell by cell will breach the room next door and
  open it to the sky unless it skips cells another room also claims.
- **A pending blueprint is not a destroyed building.** Any "this is missing, re-place it" check
  has to treat a `Blueprint` or `Frame` as present, and has to scan the room's *interior* —
  furniture stands inside, so a walls-only guard re-queues every pass and places duplicates.
- **Deconstruction is the expensive way to move something.** Anything `Minifiable` should be
  reinstalled or uninstalled instead: that keeps all the material and the quality, where
  deconstructing returns only `resourcesFractionWhenDeconstructed`, which several vanilla defs
  set to zero. Test `def.Minifiable` at runtime rather than listing defs — it inherits from
  `FurnitureBase`, and the electric stove qualifies where you would not expect it to.
- **A reinstall is a blueprint at the destination, not a designation on the building.** Ask
  `InstallBlueprintUtility.ExistingBlueprintFor(thing)`, or a "is this already handled?" check
  will miss a building that is halfway across the base in someone's arms.
- **Rain sets unroofed electrical things on fire**, via `ShortCircuitUtility`, and adds an
  explosion when the net holds charged batteries. Conduit run across open ground is the usual
  victim, so this is a hazard the director manufactures. `FireRisk` used to treat rain as pure
  safety and read 0.00 during exactly that weather.
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
cd Tests/AutoColony.Tests && dotnet test      # 97 tests, learning layer, goals and upkeep policy
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
