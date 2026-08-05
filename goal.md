# goal.md — what this project is for, and how I work on it

Read this at the start of every loop. It is short on purpose.

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

Corollary worth remembering: **two names for the same quantity is a blind spot.** Four bugs so
far have been one number computed twice and disagreeing (fuel radius, dry-hopper accumulation,
readiness, and now fieldable strength). Before adding a measurement, search for one that
already exists.

---

## 4. Restart policy

**Restart freely.** A running colony is worth less than a fix in hand. Ship the change,
restart, move on.

Accepted cost: run length is not a clean measurement. Do not quote it as though it were.

---

## 5. Every-loop checklist

1. **Screenshot — capture *and read it*.** Not optional. The picture has caught four faults
   the logs could not, including one where the process was dead and the text still read fine.
2. Run `scripts/check30.sh`. Read the CHECK30 line, the causes, and the repeat detector.
3. **If colonists died:** preserve the chronicle to `$JD/wipe-<run>-chronicle.log` *before*
   restarting — restarting deletes it — then read it for the sequence, not just the cause.
4. Ask: what did the colony fail to *see*? Prefer that over what it failed to do.
5. Make one change, on the highest rung of the ladder that reaches.
6. Build, run the tests, restart, commit **and push**.

---

## 6. Standing constraints

- **`rm -rf` has already cost this project 36 epochs of learning and the mod config.** Restart
  inputs live outside anything a restart deletes. Copy before destroying, always.
- **Push at every commit.** Local-only work has been lost here before.
- Temporary files go in `$CLAUDE_JOB_DIR/tmp`, never `/tmp` — parallel jobs clobber it.
- Build: `export PATH="$PATH:$HOME/.dotnet"; dotnet build Source/AutoColony/AutoColony.csproj`
- Tests: `dotnet test Tests/AutoColony.Tests`

---

## 7. Standing user instruction, verbatim

> check every 30 mins, take a screenshot, restart if colonists die, observe the colony for
> ways you can improve the control and perception surface of the director, do not hard code
> behavior as much as possible, aim to create a structure that disincentivizes director
> behaviour to be negative to the colony

The last clause is the design brief. Not "make the director do good things" — **make bad
outcomes cost it something it can measure.** A director that cannot be punished by its own
score for hurting the colony will eventually hurt the colony.
