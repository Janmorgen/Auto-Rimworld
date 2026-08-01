# Handoff — goal planning direction

State of the work as of 2026-08-01. The README describes *what the mod is*; this describes
*where the work is* and what to do next. Read both.

## The round of 2026-08-01: four colonies, four causes

Each launch was watched, the cause of the failure read out of the chronicle, one thing fixed,
and the game relaunched. Every round found something, and the causes were all different — which
is the argument for running it rather than reading it.

| Run | Outcome | What it turned out to be |
|---|---|---|
| 1 | died day 3 | drafted the last standing colonist while three lay downed |
| 2 | died day 3 | six room shells, none finished, no bed ever placed |
| 3 | reached day 4, scored 0.591 | deadlocked on RimWorld's name-your-colony prompt |
| 4 | running | — |

**Three of the four were the same shape.** A threshold read one quantity when the outcome
depended on a second one nobody had modelled:

- Food urgency read the *store* and not the *delivery time*. A hunt is kill → haul → butcher →
  cook, so escalating at one day of food leaves no margin for any of it to fail. Urgency is now
  measured against `daysOfFood - 1.5`, which moves every food decision earlier without changing
  a judgement built on top of it. `FoodTiming`.
- Construction read *material* and not *labour*. `BuildingMeans` knew the colony could afford six
  rooms; nothing knew it had two pairs of hands. Concurrent rooms now scale with builders —
  one room for a colony of two. `BuildingMeans.ConcurrentRooms`.
- Engagement read the *odds* and not the *stake*. Desperation already scales acceptable risk up;
  fragility has to scale it down, because losing the last person standing is not survivable at
  any odds. Required ratio multiplies by `1 + downed/able`. `CasualtyPolicy.EngagementCaution`.

**When a rule fires "too late" or "too eagerly", suspect the quantity before the constant.**

A doctor is also now held back once anyone is down — never the last fighter — and nothing new is
built while the plan is answering something immediate.

### What made all of that findable

The observation work went in first, and it earned its keep in the same session:

- `COLONY LOST` names a cause and the chain behind it. It was *wrong on its first live run* —
  it called an 18-day food store starvation — which is itself the lesson: it keyed on the
  epoch's lowest food reading, and that is 0.0 for every colony that lived through day one,
  because `ResourceCounter` only counts stockpiled goods. Cause is now read from the last
  snapshot in which anyone was alive, since after a wipe the larder *climbs*.
- Trial lines carry `[trial 2/4]`, and a trial says which genes it holds furthest from the
  incumbent. No more checking whether a `session begins` followed a `COLONY LOST`.
- A wall-clock heartbeat with a tick delta. This caught the run-3 deadlock in one line. It was
  itself broken at first — it ran on the tick, and **a paused game issues no ticks**, so it went
  silent exactly when the run stalled. It now runs from the non-tick hooks and names whatever is
  holding the game: `— HELD BY Dialog_NamePlayerFactionAndSettlement`.
- Epoch scores carry sample count and elapsed days.

Offline tests: 134 → 165. Two of the new ones were written after the behaviour they describe was
watched failing in game.

## Then: systems the director could not see at all

A second pass compared RimWorld's own `Data/Core/Defs` categories, and the wiki, against every
system this codebase references. Whole categories had zero coverage. What went in, roughly in
order of how tightly each was coupled to an observed death:

- **Farming as a goal.** Food arrives two ways and they are not equivalent: hunting spends the
  colonists themselves, and nearly every combat death this session began with a colony reaching
  for meat because nothing was planted. `FarmGoal` is short-term and `FoodStockGoal` requires it,
  so "we want more food" resolves into "plant something" rather than another hunt. Two distinct
  crops, because blight takes a whole crop at once. Healroot for herbal medicine — no research,
  ordinary soil, but Plants 8, which a three-colonist start usually lacks and now says so.
- **Game conditions.** Toxic fallout, solar flare, eclipse, cold snap, heat wave, volcanic
  winter, flashstorm — none of them changed behaviour by a line. Fallout inverts the correct
  play: it poisons anything not under a roof, so elective outdoor work stops and everyone is
  confined to a roofed allowed area, released the moment it passes.
- **Temperature, and clothing.** Comfortable is 16–26°C; ten degrees past either edge is
  hypothermia or heatstroke, fatal at full severity. Cold had one answer (a heater needing a
  generator) so a pre-power colony had none — a campfire needs neither power nor research. Heat
  was mapped to nothing at all. And apparel was missing at four links at once: no tailor bench,
  apparel worth zero in `DesiredCount`, no material preference, and the good garments behind
  Complex Clothing that nothing had reason to research.
- **Medical.** A downed colonist cannot be rescued without a free bed, and bed rest multiplies
  immunity gain while an untended infection races immunity to 100%. The colonies that died
  "everyone down, `beds 0`" were dying of that; it had been read as a mood problem.
- **Sieges.** Besiegers build mortars at range and do not come to the door, so the cover rule
  added earlier the same day is actively wrong against them and is now suppressed during one.
- **Wealth drives raid points.** Building buys larger attacks. `ThreatForecast` interpolates the
  published anchors and compares against real fighting strength, so Fortify urgency reflects
  whether the colony is winning or losing that race rather than merely how rich it is.

### Verifying what a test colony cannot reach

A `-quicktest` colony lives about a week in a temperate biome, so it can never see a freezing
winter, and the game will not raise toxic fallout before day 60. `PowerChainSelfTest` was
extended to construct those states directly, and immediately earned it: the clothing goal was
**inert**, because `Satisfied` asked whether a workshop existed rather than whether anyone was
warm. Adding the probes also exposed that new short-term goals with no defaults in the probe
fixture had silently rerouted every power probe in the file — all still passing, testing nothing.

**Three separate versions of the same fault turned up in one day**: a fitness term that scored
exactly zero forever, a goal that could never fire, and probes that no longer probed. None
failed; all reported success. When a number never moves across runs, that is the symptom.

Offline tests: 165 → 193.

## Where things stand

Branch `feat/autonomous-colony-director`, pushed, **not merged to `main`**. `main` is still the
original HelloWorld template commit. Merge with `git checkout main && git merge --ff-only
feat/autonomous-colony-director` when you want it there.

The mod runs in RimWorld 1.6.4871. 193 offline tests pass. It has been played against generated
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

**Complaints it cannot fix are reported, not dropped.** That list is the to-do list, and it is
measured rather than guessed. Working through it is what killed the biggest survival gaps:
burial, recreation, tables and heating all came off it. What is left there now is mostly grief —
`KnowColonistDied`, `PawnWithGoodOpinionDied` — which is not a building problem. Adding a case is
a row in `Complaints` plus a `RemedyKind`.

**Upkeep does not switch off in a crisis; its bar rises.** A module that defers entirely while
anything immediate is happening never gets to anything in a colony that lurches between
emergencies — which is exactly the colony that needs it, and is why one carried an unburied
colonist for eleven days over a building that costs nothing. Burying the dead clears the raised
bar; decorating does not.

**A scoring note.** The `Conduct` term shifted the score scale, so archive entries from before it
are not directly comparable with ones after. If cross-run comparison starts mattering, that wants
a scoring-version key on the archive.

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
- **The epoch-close conduct line has never been seen live**, and neither has the *production*
  prisoner-bed marking — only the harness's. A `-quicktest` colony restarts at day nought, so
  nothing has reached an epoch boundary.
- Production bills beyond butchering, defence positioning under a real firefight, and any
  behaviour over in-game years rather than days.

## What ten hours of unattended running showed

A long overnight run — 16 colony cycles, no exceptions, no code changes — produced a clearer
list of gaps than any amount of reading. Two kinds: things the director cannot **do**, and
things it cannot **see about itself**. The second kind is what made diagnosing the first kind
slow, so it is worth fixing first.

### Observation — what the director records about itself

- **Decisions are logged; outcomes are not.** The chronicle says `WITHDRAWING 2 to (114,0,138)`
  and never says whether anyone arrived. The scenario harness's one-line roster —
  `Nytro[drafted]=Goto  Bowman[drafted]=AttackMelee` — was more diagnostic than anything in the
  chronicle, and it is the line that proved drafted colonists were standing idle. It belongs in
  the chronicle proper, throttled, not in a test-only harness.
- **A training trial is indistinguishable from the live colony.** `COLONY LOST` appeared sixteen
  times overnight and every one needed a manual check of whether a `session begins` followed
  before it could be read. That very nearly caused a wrong intervention. Trials should mark
  their lines — `[trial 2/4]` — so a reader, or a watching agent, can tell a deliberate
  experiment from a real disaster.
- **Nothing distinguishes "quiet" from "stopped".** Non-urgent entries are buffered, so a
  healthy colony can write nothing for many minutes. A periodic heartbeat — even just flushing
  vitals — would make a stalled or exited run obvious instead of ambiguous.
- **A colony loss reports a score, not a cause.** Every post-mortem this run was reconstructed
  by hand from the preceding fifty lines. Everything needed is already in memory at that moment:
  final food, mood, downed count, last threat, the unmet-complaint list. One line —
  `COLONY LOST — starvation: 0.0 days food for 6h, last hunt declined at strength 4` — would
  replace all of that reconstruction.
- **Epoch scores do not say how much epoch there was.** The degenerate-epoch bug was only
  visible because 58 scores shared a timestamp. Had the score line carried its sample count and
  elapsed days it would have been obvious on the first one.
- **Behaviour cannot be traced to a gene.** Candidates were visibly different — one engaged at a
  0.375 ratio where another withdrew — but nothing said *which* parameter differed. Logging the
  few genes furthest from the incumbent when a challenger starts would connect the behaviour to
  the number that caused it.
- **Instances are indistinguishable from outside.** Two RimWorld processes on one machine cannot
  be told apart without inspecting their command line; a savedata path in the chronicle header
  would make any external watcher unambiguous. (Written from experience: a monitor matching on
  process name alone reported a stopped game as healthy for half an hour.)

### Control — what the director cannot do

- **Touch the schedule at all.** `NightOwlDuringTheDay (-10)` is one of the largest recurring
  penalties and is fixable purely by assigning that colonist a night shift. The schedule tab is
  untouched by any module.
- **Answer a kidnapping.** A downed colonist carried off by raiders is simply gone; there is no
  pursuit and no ransom.
- **Route conduit under cover.** The director lays long runs across open ground, which
  manufactures the short-circuit fire risk it now correctly detects.
- **Snapshot a trial from a healthy baseline.** Every candidate was scored on escaping a
  near-hopeless position in two in-game days, which compresses the score distribution and wastes
  most of the information a trial could carry.

## What to do next, roughly in order

2. **The rest of what the colony asks for.** Burial, recreation, tables and heating are done.
   Still unanswered on the live list: `NeedComfort` (chairs need `ComplexFurniture` research),
   `NeedRoomSize`, `SleepDisturbed`, `NightOwlDuringTheDay` (a scheduling problem, not a building
   one — worth noting because it shows the survey finds things no construction module could
   fix), and `ProsthophileNoProsthetic`. Each is a row in `Complaints` plus a remedy.
3. **Give the director eyes on itself.** The observation gaps above are what made every other
   problem slow to find. A cause-of-death line, a trial marker, and sample counts on the epoch
   score are each a few lines and would have turned a night of manual log archaeology into three
   greps.
4. **Make the consequential rules testable.** `CombatAssessment` and `FireRisk` are pure
   arithmetic behind a `Map` read, so fight-or-withdraw — now gene-driven — cannot be exercised
   offline. `Prisoners/` and `Upkeep/` show the pattern. The goal prerequisite walk in
   `GoalPlanner.Actionable` is the same algorithm as `ResearchChain` one level up, but inline and
   untested, and it lacks that one's cycle detection.
5. **Cut the search's dimensionality.** ~50 genes against tens of epochs. A live colony
   measured score noise at ±0.061, roughly three times the ~0.02 where offline tests show the
   sequential search going flat. Grouping work-type weights by category is the obvious cut.
6. **Combat positioning.** Everyone rallies to one point; no cover, no chokepoints, and no
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
- **Mod settings files are named `Mod_<modFolder>_<ModClass>.xml`** — here
  `Mod_Auto-Rimworld_AutoColonyMod.xml` — with the values nested inside
  `<ModSettings Class="AutoColony.AutoColonySettings">`. A file under any other name is ignored
  in silence, which cost several test runs that appeared to ignore `epochDays` entirely.
- **`pgrep -x RimWorldLinux` cannot tell two instances apart.** Match on the `-savedatafolder`
  argument instead; a watcher that does not will happily report someone else's game as yours.
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
- **Which means every colony has an empty larder in its history.** `daysOfFood` reads 0.0 for the
  first hours of every colony whatever it actually has, so any *lowest-seen* food statistic is
  0.0 forever after. A post-mortem keyed on that called an 18.5-day store starvation. Corroborate
  a low-water mark against the end state before believing it.
- **The first save of a colony raises a naming prompt that force-pauses.** `-quicktest` colonies
  have no faction or settlement name, and `GameDataSaveLoader.SaveGame` asks for one through
  `Dialog_NamePlayerFactionAndSettlement`. `TimeControl` will not close windows it does not
  recognise, so an unattended run deadlocks silently. `TrainingSession.EnsureColonyNamed` sets
  both before snapshotting; anything else that saves must too.
- **Anything that must survive a pause cannot live on the tick.** This is the same trap as time
  control, and the heartbeat fell into it: a paused game issues no ticks, so a signal meant to
  prove the run is alive goes quiet exactly when the run stops. `GameComponentUpdate` and
  `GameComponentOnGUI` are where such things belong.
- **Almost everything arrives forbidden**, including scenario starting resources.
- **Colonists only fight fires inside the home area.**
- **A paused game issues no ticks**, so `GameComponentTick` cannot recover a pause.
  `GameComponentUpdate` alone was measured insufficient; `GameComponentOnGUI` is what works.
- **Verbose logging is off by default** and module activity logs at verbose level, so an
  established colony looks stalled when it is merely quiet. That misread cost a diagnosis.

## How to work on it

```bash
cd Source/AutoColony && dotnet build          # → Assemblies/AutoColony.dll
cd Tests/AutoColony.Tests && dotnet test      # 134 tests, learning layer, goals, upkeep, prisoners
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
