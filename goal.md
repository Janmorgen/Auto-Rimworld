# goal.md — what this project is for, and how I work on it

Read this at the start of every loop. It is short on purpose.

**Where things are.** How RimWorld itself works is in **`docs/rimworld/`** — twenty-one
reference notes covering every core system, consulted every run and never edited (§9). What
each module reads and changes is in `Source/AutoColony/Core/Connections/Touches.cs` (§4). What
the colony is doing right now is `scripts/check30.sh`, which recites this checklist back at
every check.

Do not answer a question about the game from memory when a note covers it, and do not take a
*number* from the note — read the def through the API probe. §9 says why.

---

## 1. What the project is

**A study of learned versus given.**

The lasting output is an account of what a director can be made to work out for itself, and
what must be handed to it. The colony is the instrument, not the product. A colony that
survives is evidence; a colony that dies is also evidence, and often better evidence.

This has a consequence I keep having to relearn: **a fix that works but teaches nothing is
worth less than a failure I understood.** When something goes wrong, the finding is the
deliverable. Write it down before shipping the repair.

### The ledger

Every change belongs on one side of the question. Record which, in the commit message and in
`docs/` when it is interesting:

- **given** — a fact about RimWorld I read out of the game and handed over
- **sensed** — a new thing the colony can perceive, with the decision left to scoring
- **learned** — a number the colony now derives from its own experience
- **discovered** — a mechanic it worked out by observation rather than being told

A run of commits that are all *given* means I have stopped doing the experiment.

---

## 2. The ladder — where hand-writing is allowed

Prefer in this order. Drop a rung only when the one above genuinely cannot be reached.

1. **Discovered.** Let it work the mechanic out by observation rather than reading it out of
   the defs.
2. **Sensed.** Give it the sense and nothing else. What to do about anything sensed must fall
   out of scoring and arbitration. **No if-statement may decide an action.**
3. **Given.** Hand it the mechanic — wood comes from felled trees, a chair must be cardinally
   adjacent — and let genes or learned memory set every threshold, weight, priority and force.

**Not allowed: ship the behaviour now and dissolve it into a parameter later.** That rung was
considered and rejected. A temporary hard-code is a permanent one with a comment on it, and
it quietly converts the experiment into a bot.

### The test I apply to my own diffs

- Did I add a *number* the colony cannot change? → it belongs in a gene or in memory.
- Did I add a *branch* that picks an action? → it belongs in a score.
- Did I add a *fact about the game*? → fine, rung 3, say so.
- Could the colony have found this out by watching? → try rung 1 before rung 3.

---

## 3. Loop priority

When a check turns up more than one thing, the hour goes to:

> **Whatever the colony lacks a sense for.**

A fault it can perceive is a tuning problem — scoring and learning can reach it. A fault it
cannot perceive is structural, and no amount of learning will find it, because there is
nothing in the loss signal that varies with it. Blind spots first, then structure, then the
specific fault that killed this colony.

Corollary worth remembering: **two names for the same quantity is a blind spot.** Before adding
a measurement, search for one that already exists — see the fault table in §4, where every
class so far is a connection nobody had drawn.

---

## 4. Connections between systems

Hunt for these constantly and record as many as can be found. Every fault that has cost a
colony here has been a connection nobody had written down.

**Each module declares what it reads and what it changes, in the code, beside the module.**
The map is *rendered from those declarations*, never drawn by hand. A diagram maintained
separately goes stale in silence, and a stale map of the thing I use to reason about faults is
worse than no map — it answers confidently and wrongly.

**Trace consequences as far as they go**, however long the chain. Run 134's, in full:

```
DefenseModule drafts       →  hands leave construction
  →  the table stays a blueprint
  →  AteWithoutTable keeps firing (it is a mood thought, not a survey)
  →  AddTable fires again  →  another table
  →  SeatWhatNeedsSeating gives every table one stool per colonist
  →  ten stool orders, queued while three of four colonists lay bleeding
```

No single link there is surprising. The chain is — and no module could see past its own end
of it.

Long chains are conjecture until a run confirms them, so label every edge **observed** (seen
in a chronicle, with the line quoted) or **suspected** (reasoned but unwitnessed). A suspected
edge is a thing to go looking for, not a finding, and must never be reported as one.

### The fault classes this map exists to catch

Seven have bitten so far. Not one is a logic error — every one is a connection, a scope, or a
measurement standing in for the thing it was supposed to track:

| class | what it is | seen in |
|---|---|---|
| **duplicated quantity** | one fact computed in two places, disagreeing | fuel radius; dry-hopper accumulation; readiness; fieldable strength |
| **wrong scope** | the right question asked at the wrong level | a colony's want tallied once per room |
| **contested ownership** | two systems acting on one thing | upkeep placing a table the planner also furnishes. Twice repaired by finding the attribute the two sides disagreed about — first the target number, then the region — and twice recurring, because the supply of attributes is not something anybody enumerated. `Churn` stops paying for the argument without naming it |
| **lagging signal** | a remedy driven by a symptom that cannot clear until the remedy lands | AteWithoutTable ordering tables for days |
| **history read as state** | a tally of events reported where the current answer was wanted | `check30.sh` counting every "pen is closed" ever printed, while the game showed *Pen not enclosed* |
| **the plan counted as the thing** | a tally that includes what has been decided alongside what exists, so intent inflates inventory | run 197 had two Bedrooms planned and one standing, counted two, called the standing one spare and made it a Barn to save 120 material — while "Shelter everyone [0 beds for 3 colonists]" held the plan. Same shape as beds counted before they shelter anyone and meat counted before it is butchered |
| **proxy for the real thing** | a measurable stand-in that stops tracking what it stood for | beds counted as shelter, roofing counted as cover, a gather circle counted as the world. Run 195 day 13: four beds for three colonists, three of them standing in the open against the outside of a finished wall, so the surplus rule pulled a bed while the colony was two short of anywhere to sleep. `shelteredBeds` existed the whole time and `ShelterGoal` was reading it — the sense was there and one decision was not asking for it |
| **present read as future** | a snapshot used where a forecast was needed | `growingSeasonNow` from today's temperature, so a colony farms through summer and starves in fall |
| **an inert read** | code that compiles, runs, and finds nothing — indistinguishable in the log from a condition that is simply never true | the hunt's blast hazard read `CompProperties_Explosive`, which no animal in RimWorld carries. A boomalope's explosion is `race.deathAction.workerClass`. It compiled, shipped, and was caught only when run 197 hunted three boomrats in its first hour and set 82 fires |
| **a decision undone by carrying it out** | acting on a reason erases the evidence for it, so the next pass re-decides the opposite, for ever | the hunger stand-down counted hungry colonists among the *drafted*; standing them down emptied that list, the reason vanished, and they were re-drafted before reaching a meal. Run 206 flipped four times an hour from day 4 20h with `roomsEver: 0` at day 5 |
| **an unrepaid loan** | a trade that is right every single time it is taken and ruinous taken repeatedly, because nothing counts the ones outstanding | the planner puts beds in a bedroom before its walls, on the sound argument that a bed beats the ground tonight and the shell carries on being built around it. Run 195 took that trade five times in thirty-one days and closed none of them: `roomsEver: 5`, six beds, nought sheltered, two standing alone in open grass with no wall within twenty cells |
| **remedy slower than the deadline** | the right answer, arriving after the thing it answers | run 173 held worst mood at 0.00 for six hours with the focus on Comfort — whose remedy is psychoid tea, gated on research that takes days. The colonist broke in hours, attacked a megasloth, and two died |

When a fault doesn't fit these, name the new class and add a row. The table is a record of
what this director actually gets wrong, which is worth more than a list of what might go wrong.

**Ask when a connection is unclear.** Guessing at what a system affects and then writing the
guess down as fact is how a map starts lying, and the map is supposed to be the honest part.

---

## 5. Restart policy

**Restart freely.** A running colony is worth less than a fix in hand. Ship the change,
restart, move on.

Accepted cost: run length is not a clean measurement. Do not quote it as though it were.

---

## 6. Every-loop checklist

1. **Screenshot — capture *and read it*.** Not optional. The picture has caught four faults
   the logs could not, including one where the process was dead and the text still read fine.
2. Run `scripts/check30.sh`. Read the CHECK30 line, the causes, and the repeat detector.
3. **If colonists died:** preserve the chronicle to `$JD/wipe-<run>-chronicle.log` *before*
   restarting — restarting deletes it — then read it for the sequence, not just the cause.
4. Ask: what did the colony fail to *see*? Prefer that over what it failed to do.
5. **Trace it before touching it.** Read the module's declared reads and affects, confirm they
   are still true, and follow the chain out from them — the fault is usually one link further
   along than the symptom.
6. **Consult `docs/rimworld/` before assuming anything about how the game works.** See §9.
7. Make one change, on the highest rung of the ladder that reaches.
8. Update the declarations I invalidated, and add any new edge to the map, labelled
   **observed** or **suspected**.
9. Build, run the tests, restart, commit **and push**.

---

## 7. Standing constraints

- **`rm -rf` has already cost this project 36 epochs of learning and the mod config.** Restart
  inputs live outside anything a restart deletes. Copy before destroying, always.
- **Push at every commit.** Local-only work has been lost here before.
- Temporary files go in `$CLAUDE_JOB_DIR/tmp`, never `/tmp` — parallel jobs clobber it.
- Build: `export PATH="$PATH:$HOME/.dotnet"; dotnet build Source/AutoColony/AutoColony.csproj`
- Tests: `dotnet test Tests/AutoColony.Tests`
- **Game reference:** `docs/rimworld/` — see §9. Consulted every run. Never edited.

---

## 8. Standing user instruction, verbatim

> check every 30 mins, take a screenshot, restart if colonists die, observe the colony for
> ways you can improve the control and perception surface of the director, do not hard code
> behavior as much as possible, aim to create a structure that disincentivizes director
> behaviour to be negative to the colony

The last clause is the design brief. Not "make the director do good things" — **make bad
outcomes cost it something it can measure.** A director that cannot be punished by its own
score for hurting the colony will eventually hurt the colony.

---

## 9. The game reference — `docs/rimworld/`

Twenty-one linked notes covering every core system: colonists, mood, work, health, plants,
animals, food, crafting, research, base building, room attributes and types, biomes,
recruiting, questing, combat, factions, the storyteller, and trading. Base game only, matching
the install the director runs against.

**Consult it every run.** Any question about how RimWorld actually works goes here first —
before assuming, before inferring it from a chronicle, and before writing the change. This
director has repeatedly built systems without knowing the mechanism existed at all: seating
adjacency, fuel as a haulable good rather than a property of a stove, the tree-sowing research
gate. Each cost a colony before it was found.

**It is researched truth, and it is never modified.** Not corrected, not annotated, not
reorganised, not "brought up to date". It stands as supplied.

That has one consequence worth stating, because the alternative would be to quietly edit:
**if the running game ever appears to disagree with a note, the disagreement is recorded
outside these files** — in the task list, in `docs/`, or in the code comment where it bites.
Never by touching the note. A reference that gets rewritten whenever it is inconvenient stops
being a reference, which is the same failure §4 guards the connection map against.

The API probe under `$JD/tmp/apiprobe/` remains how the code reads a literal value at runtime
— `TreeBase.harvestedThingDef`, a fuel capacity, an alert threshold. That is a mechanism for
getting exact numbers out of the running install, not a second opinion on what these notes
say.

### What it has already been worth

Three findings in its first sitting, which is the case for step 6 existing at all:

- Corroborated the fix that broke a three-run wipe streak — untreated bleeding, not the hit,
  is what kills in most combat deaths.
- Exposed a missing term in the strength model: cover is called the most reliable defensive
  advantage in the game, and `FightingValue` has no positional term at all (#43).
- Revealed an entire absent system: there is no trade capability anywhere in the director,
  and medicine — the thing that stops the bleeding deaths — is what these colonies run out of
  (#44). Only findable by reading about a system and finding nothing on the other side.
