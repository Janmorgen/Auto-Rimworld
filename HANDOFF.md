# Handoff — goal planning direction

State of the work as of 2026-08-01. The README describes *what the mod is*; this describes
*where the work is* and what to do next. Read both.

## The round of 2026-08-01: four colonies, four causes

Each launch was watched, the cause of the failure read out of the chronicle, one thing fixed,
and the game relaunched. Every round found something, and the causes were all different — which
is the argument for running it rather than reading it.

Twenty launches over the session. The early ones each found a distinct killer:

| Run | Outcome | What it turned out to be |
|---|---|---|
| 1 | died day 3 | drafted the last standing colonist while three lay downed |
| 2 | died day 3 | six room shells, none finished, no bed ever placed |
| 3 | reached day 4, scored 0.591 | deadlocked on RimWorld's name-your-colony prompt |
| 4 | 3 colonists → 1 | a regression from run 3's own fix: a hungry colony barred from building its kitchen |
| 5 | day 4, scored 0.584 | the naming deadlock again — faction named, settlement not |
| 6 | died day 1 | dealt fourteen hostiles at 990 threat against strength 96; unwinnable, but it drafted and stood down seven times in seven hours |
| 7 | died day 2, 25d food | met a raid at 0.68x on a bare 0.35 gene bar |
| 10 | **day 7, training round running** | first run to clear the day-4 wall and complete trials |
| 18 | **10.1 days of food, nobody down** | healthiest colony of the session |

The naming prompt took three attempts because the first two aimed at plausible causes rather
than confirmed ones — the wrong object, then the wrong predicate. What settled it was three
lines printing what the code had actually found and actually done. That is the cheapest
diagnostic there is and it should have been in the first attempt.

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

- **The room-quality verdict on a lived-in room.** `RoomQuality` judges a finished room against
  the game's own score bands, and the work-room half is confirmed live — run 36's Kitchen came
  out "average-sized, awful" and correctly raised nothing, because no mood reads a kitchen. The
  lived-in half has not fired yet: no colony has finished a Bedroom on this build. Run 35's
  Kitchen scored impressiveness −33.7, so a bedroom built the same way should trip the floor,
  and if it never does the standard is set wrong rather than the colony being fine.
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

## The round of 2026-08-02: two decisions tested on the wrong quantity

Both of these were the director acting on its own initiative, and both chose the worst option
available for the same underlying reason — the test asked where something *was* rather than
where it was *going*. Neither was a coding error; both conditions read correctly and meant
something other than what they said.

**The hunt escalation fired on a false premise.** It exists to break a starve-or-fight deadlock
and its reasoning is right: refusing food is not survival, only a slower way to lose. But it
triggered on "this pass designated nothing", and the candidate list excludes anything already
designated. A working hunt module marks all the safe prey within a pass or two — after which no
pass can designate anything new, every pass concludes there is nothing safe left, and the
escalation picks the least dangerous *undesignated* animal. Which is, for precisely that reason,
the most dangerous animal on the map.

Run 22, three passes inside one in-game hour: marked a Red fox, marked a Rat, then sent everyone
after a Warg at 0.61x. An hour earlier the same reasoning had bought a Megasloth at 0.49x.
Neither animal died. Both went manhunter and followed the hunters home. Three colonists became
one in six hours, and the 11.6 days of meat lying in the fields that afternoon fed nobody.

Now in `HuntPolicy.LastResortWarranted`: standing hunts mean food is already coming, and
casualties mean the strength the fight was judged on has already gone.

**The fire response had the same shape.** Anything outside the response radius was left, on the
reasoning that a distant wildfire was never coming and chasing one leaves the base unattended —
also correct, and also tested on the wrong quantity. Four fires a hundred cells out were
correctly judged distant. Four became thirteen, forty-three, a hundred and twenty-three;
twenty-seven in-game hours later 255 cells were burning and the colony had done nothing
throughout, because the front never crossed the line. It grew until the line was inside it.

Now in `FireFront.IsClosing`: two samples separate a front that is coming from one that never
was. Growing and not receding is met where it is; already past what the people present could
beat out is not answered at all, because sending one colonist into two hundred fires loses the
colonist and not the fire.

Offline tests 251 → 270.

Two things about this worth carrying forward:

- **`FightableFiresPerColonist = 6` is a judgement, not a measurement.** It is calibrated to
  make the one observed failure (4 fires, 1 colonist) come out "go", and to make 43-and-1 come
  out "no". The true number depends on travel time and spread rate and is probably lower for a
  distant front than a near one. Watch it in game before trusting it.
- **Meeting a distant front grows the Home area permanently.** Nothing in the director ever
  shrinks it. Claiming is capped at 200 cells per pass and burned ground has little to haul or
  clean, so the cost is bounded and mostly inert — but it is a real cost and it accumulates.

## The six design decisions, and what the catalogue was hiding

Six questions had been parked because each had a real trade-off rather than a right answer. All
six were settled and implemented, and driving them turned up more than they fixed.

| | Decision | Verified in game |
|---|---|---|
| 1 | Casualties carried out at 12 cells; beds counted as safe-or-not | no — needs a fire and a casualty at once |
| 2 | Barely-wanted goals drop a horizon band; anything blocked 3 days gets a turn | yes — runs 27, 29, 30 reached the long-term chain |
| 3 | One concurrency slot kept for the room the plan asks for | yes, and it needed two guards afterwards |
| 4 | A focus that holds half a day without improving is logged and stood down | yes — 6 false stand-downs became 0 |
| 5 | Beds counted over `PlannedRoom.Rect` on both sides | no — needs a colony with two bedrooms |
| 6 | Food security is time spent out of danger, not the worst single reading | yes — discriminates runs 27 and 28 correctly |

Three things are worth carrying forward more than the decisions themselves.

**The arbitration gate must be the goals' own line, not a threshold on top of it.** The starvation
guard was first written to fire when no immediate goal was *pressing*, on an urgency threshold.
That would have promoted a research bench at 1.5 days of food — under any sensible threshold, and
also the exact point past which nothing the colony decides arrives before the larder is empty. The
immediate goals already draw the line where it belongs: a fire burning, hostiles present, under
two days of food. A second line could only move it the wrong way.

**The self-test was not reproducible and nobody had noticed.** Two runs of identical code
disagreed on four of twenty-four probes. Long-term goals separate on urgency alone and several
read theirs off the map, so the winner was the weather. The probe now prints the ranking, and the
distinction is stark: every probe that tests an arbitration *rule* is decided by a clean
hundred-point band, while the unstable ones sat inside six hundredths of a point. Do not trust a
long-term probe's winner; read its margin.

That tie is not cosmetic. One pass in which Masonry beat "Somewhere to research" opened a Workshop
through the spare slot, and a colony of three then split its builders for three days. The extra
slot now waits for the plan to ask for the same room twice.

**A survey of the def database found the director knew 26 things out of 131 buildable.** 108
unknown, 29 of them needing no research at all. Three mapped straight onto failures in the log: a
butcher spot is free and instant where the butcher table costs material and hours (colonies here
starved at 0.0 days with meat in the field); a stool is 25 units and no research where
`NeedComfort` had *no remedy at all* because seating was written up as needing Complex Furniture;
and the table remedy asked only for the 50-unit table, so colonies that could not spare 50 ate off
the floor while holding wood for a smaller one. Still unused and research-free: `Barricade` at 5
units against a Defense term that has scored 0.00 in every epoch ever run.

The lesson generalises past the specific defs. Those remedies were written against an assumed
catalogue, and the assumptions were sitting in comments that read as facts. Check the database.

## Traps worth knowing before you touch it

- **Review the loop a rule closes, not the rule.** Eight times now, two individually correct
  rules composed into a cycle. The bandit picks the next room from the roles the layout
  *lacks*, and repurposing satisfies that pick by relabelling a shell — so converting the only
  workshop into a research room puts workshop straight back in the bandit's list, and one shell
  went Workshop, Research, Workshop, Dining, Hospital, Workshop in fourteen hours. Withdrawing a
  room's blueprints set `wallsQueued = false`, which made the planner re-queue them on the very
  next pass. The spare slot opens one room beyond the allowance and consolidation, seeing the
  allowance exceeded, takes that exact room back. And a long-term scoring tie of six hundredths
  of a point flipped the focus for single passes, which damaged three separate mechanisms
  downstream before the cause was addressed.
  Each rule was reasoned about carefully in isolation and each was fine there. None of these
  showed up in the offline tests; every one was found by watching one colony. When adding a
  control surface, write down what it does on the pass *after* it acts, and against every rule
  already present.
- **The instruments fail more often than the mechanisms.** Across one long session, twelve
  separate faults were in the measurement rather than in the thing measured — and each time an
  explanation for the wrong number arrived before the check did. `beds` counted sleeping spots
  as beds. `room.Cells` skipped every cell under a workbench, so a room's inventory missed
  exactly the furniture that decides its role. `growingCells` counted marked ground rather than
  ground that grows. `Food security` counts the larder and reads 1.00 while a colonist starves
  beside it. `lost from roster` merged deaths with kidnappings. A hunt line printed a threshold
  the decision had not used. When a result is surprising, check the instrument before theorising
  about the subject; when it is *un*surprising, check it anyway, because that is when a broken
  instrument agrees with you.
- **Improving a message breaks whatever reads it.** Twice in one night: `lost from roster`
  became `died of X` and the watcher counting deaths silently read zero through three deaths;
  the chronicle's wording changed and a monitor kept grepping the old string. Anything that
  parses a log line is coupled to it — change the line, change the reader, in the same commit.
- **Count what works, not what exists.** Beds that are sleeping spots, fields under a roof,
  food in a store nobody can reach, a Research room with no bench: every one of these was
  counted as the thing it resembled. The question is never "how many of these are there" but
  "how many of these are doing their job".
- **A remedy that queues a blueprint does not clear the complaint that fired it.** The complaint
  clears when the thing is *built*, which is many hours later and may be never. So a remedy with
  no memory of what it already ordered re-fires every pass for as long as the colony is unhappy,
  and the duplicates crowd out the work that would have fixed the actual problem. Run 35 queued
  seven joy buildings between day 1 18h and day 3 06h — Ur, Ur, chess, chess, poker, poker,
  horseshoes — with `Cheerless` pinned at severity 1.00 throughout because not one was ever
  built; the Bedroom sited on day 1 was still open on day 4 with the colony sleeping on the
  ground. `AddBeauty` had the same shape latent, walking a room's twenty-five cells and putting a
  plant pot in whichever one was free. `CountIn`/`CountInRoom` count blueprints and frames for
  exactly this reason — but they are per-def, so a remedy that walks a *list* of defs escapes
  them by falling through to the next one. Guard the remedy, not just the def.
- **Remedies handed a game `Room` had no duplicate rule at all.** `CountIn` takes a `PlannedRoom`
  and was the only counter, so the half of the remedies that work off `defect.room` were
  unguarded for as long as they have existed. `CountInRoom` is the `Room` counterpart; use one or
  the other in anything that places.
- **A gene with no gradient does not evolve, it drifts.** Building was scored only on whether
  rooms *exist* — beds per colonist, a powered turret count — and every room satisfies that
  equally, so the per-role width and height genes in `RoomProfiles`/`Genes` had nothing pushing
  them either way for the whole life of the project. Six cramped huts scored exactly like six
  good rooms. The `Room quality` term in `ColonyEvaluator` is what gives them a slope: space
  comes from the siting dimensions, impressiveness from wall material and furniture, and both
  are things the strategy chose. If you add a term meant to select on some behaviour, check
  first that the behaviour actually varies the number — otherwise you have added cost, not
  pressure.
- **Score the subsystem that decided the outcome.** RimWorld rates a room on space, beauty,
  cleanliness and impressiveness, and it is tempting to hold the builder to all four. Cleanliness
  is not the builder's: the same room rates well or badly on different days depending on whether
  anybody swept it, so it measures work priorities. `RoomQuality` judges space and beauty only —
  what dimensions, material and furniture decide — and impressiveness because it is the game's
  own combination of those two. Note that `ResearchSpeedFactor` and `FoodPoisonChance`, the two
  hidden stats that matter most to rooms the director builds, are *both* derived from cleanliness
  by curve, so they are work-priority telemetry and not building feedback either.
- **A retry cap on something essential is a permanent failure, not a delay.** `PlaceMany`
  stopped offering an item cells after eight tries. A research room's interior is twenty-five
  cells and the bench is three by two, so the eight best-scoring cells were tried, the game
  refused the footprint at each, and seventeen were never looked at — and because scoring is
  deterministic, the same eight were chosen and refused on every pass for the life of every
  colony. Research scored 0.00 in all thirty-seven runs on that one constant. Caps like this
  only bind in the failing case, which is the case that needed more looking; the loop now runs
  out of cells, and `best.IsValid` going false already distinguished "nothing placeable here"
  from "refused everywhere".
- **The full set of RoomRoleDefs is fifteen, and the planner covers twelve.** Storeroom,
  Kitchen, DiningRoom, Bedroom, Workshop, Laboratory, Hospital, PrisonCell, RecRoom, Tomb and
  Barn map onto planner roles; `Power` and `Freezer` are machinery the game does not classify
  and neither wants to be. `Barracks` and `PrisonBarracks` are not targets — they are what the
  game calls a bedroom or a cell with more than one bed in it, which is a thing the planner
  *produces* rather than aims at. See the barracks note below. With no DLC active that is the
  whole set; Royalty, Ideology and Biotech each add more.
- **A shared bedroom is a different room, not a fuller one.** `SleptInBedroom` pays −2 up to
  +8; `SleptInBarracks` pays −7 up to +4 — worse at the floor and lower at the ceiling, so
  sharing is worse in every band. `BuildingMeans.BedsPerRoom` returns the gene when the colony
  is comfortable and *every colonist* when it is destitute, so a barracks is the normal
  outcome rather than the exception. `RoomQuality.StandardFor(role, beds)` holds a shared room
  to a higher impressiveness floor for this reason.
- **Ask the game what the room became.** The planner keeps its own `RoomRole` and RimWorld keeps
  its own classification of every enclosed room, into one of fifteen `RoomRoleDef`s. They can
  disagree, and the disagreement is free to detect: a Research room whose bench never went in
  reads as a plain `Room` rather than a `Laboratory`. Read via `room.Role`; the band words the
  room-stats overlay shows on hover (`G` in game) come from `RoomStatDef.GetScoreStage(score)`,
  so a chronicle line can be checked against the screen.
- **A hand-built fixture silently reroutes every probe.** Adding a researcher check to
  `ResearchCapacityGoal` removed "Somewhere to research" from every ranking in the self-test
  overnight: the probes construct `ColonyState` directly, so anything the real snapshot derives
  from pawns defaults to false. All 24 probes still passed and had simply stopped asking. This is
  the second time this exact fault has been recorded here. When a goal starts reading a new field,
  set it in `PowerChainSelfTest` in the same commit.
- **A condition that reads correctly can still mean something else.** `done == 0` was a true
  statement about the pass and a false statement about the colony; `distance > radius` was a true
  statement about the fire and a false statement about the danger. Both cost a colony. When a
  decision keys off a derived quantity, ask what that quantity excludes — the hunt candidates
  excluded exactly the animals that made the premise false.
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
