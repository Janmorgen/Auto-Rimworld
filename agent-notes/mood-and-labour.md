# Mood, and why the colony cannot answer it

Two colonies now have ended the same way, and the score says it plainly on both:

| | Run 55, epoch 2 | Run 56, epoch 1 |
|---|---|---|
| Food security | 1.00 | 1.00 |
| Infrastructure | 1.00 | 1.00 |
| Mood | 0.24 | 0.24 |
| Survival | 0.00 | 0.24 |

Every material subsystem at its ceiling. Mood at 0.24 both times, which is close enough to be
worth taking seriously rather than as coincidence.

## The clearest single statement of it

Run 56 ended on day 35 with `Room quality 0.86` — the best base the director has built — and
`Food security 1.00`, `Health 0.99`, `Infrastructure 0.99`, `Mood 0.03`, and a colony of one
lying on the floor beside 8.8 days of food with `NeedFood at 44.0`. Full health, downed by
hunger, nobody left to bring a meal. Somebody had been on the floor for **100% of that epoch**.

The director built its best base ever and the colony starved inside it. Every material term is
a ceiling and the score is 0.000.

## It is not mostly grief

That was the first reading and it was too tidy. Run 56 at mood 0.15, day 21, listed these as
the mood it was losing and could not answer:

```
Pain (-20.0), NeedFood (-12.0), Pain (-10.0), MyMotherDied (-8.0),
AteRawFood (-7.0), AteRawFood (-7.0), ObservedLayingRottingCorpse (-6.0),
NeedRest (-6.0), Sick (-5.0), Sick (-5.0), SleptOutside (-4.0), SleptOnGround (-4.0)
```

Only `MyMotherDied` is grief. The rest is a colony **eating raw food, sleeping on the ground,
untreated, and walking past a body nobody has buried** — every one of which the director knows
how to fix. It cooks, it builds beds, it digs graves, it tends. It was doing none of them.

## Why not: one able colonist

The colony was down to a single person who could work, with three rooms unfinished. Everything
follows from that:

- The room-concurrency gate refuses to open anything new — correctly. A colony that cannot
  finish three rooms should not start a fourth.
- `means 0.53` and then `0.03` — no material to spare, so upkeep remedies fail.
- Cooking, hauling, burying, tending and building all compete for the same one pair of hands.

The complaints are not unfixable. They are unfixed, which the survey reports identically, and
that wording hid the difference for most of a session.

## What this means for the mood response added in this session

`BasePlannerModule` now sites a recreation room when mood is collapsing, using `Postmortem`'s
own thresholds. Measured against the colony it was written for, **it cannot fire**: the
concurrency gate is upstream of role selection, and a mood-collapsed colony is almost always a
small one that is already behind that gate.

That is not a bug in the gate. Ordering a room is simply the wrong shape of answer to a mood
emergency — it costs days and hands, which are the two things the colony has none of. The right
lever is the upkeep remedy, which places a joy building into a room that already stands, and
which does fire.

So the room response is useful only for a mid-sized colony that has slack and is grieving. That
is a real case, and it is not the case that kills colonies.

## Mood collapse also produces violence

Run 57 died on day 25 having taken no predator hunts at all — the floor in `HuntPolicy` worked,
zero revenges — and its epoch began with `Berserk: Stumpy`. A colonist in a mental break is not
merely a colonist who has stopped working; berserk attacks the others. In a colony of three
that is a second attacker inside the walls.

So the spiral has a shorter path than "deaths cost mood, mood costs labour, lost labour costs
lives". It also runs: mood costs a break, the break costs injuries, injuries cost mood. Nothing
external has to arrive for the colony to hurt itself.

Worth knowing before designing a response, because it changes what "in time" means. A rec room
answers boredom over days. A berserk colonist happens in an afternoon.

## The average is the wrong quantity

`MoodIsCollapsing` in `BasePlannerModule` reads `ColonyState.avgMood`, and `Postmortem` scores
`avgMood` too. Run 58 showed why that is the wrong measure: average mood 0.48 to 0.57 — nowhere
near the 0.30 threshold — with `MySonDied (-20.0)` on a single colonist, who went berserk.

Breaks are an individual event. A colonist at 0.05 is one break away from attacking the colony
whatever the other two are feeling, and in a colony of three a contented pair hides them
completely. Nothing in `ColonyState` currently carries the worst mood in the colony, only the
mean, so no rule can key off the person actually in trouble.

Cheap to fix — a `worstMood` beside `avgMood` in the same loop that already sums them — and it
would not change behaviour today, because the response it gates cannot fire anyway (above). It
is recorded here so the next attempt keys off the right number from the start.

## The score cannot see the thing that kills

`Food security` counts days of stockpiled food. Seven colonies have now died with it at or
near 1.00 — run 59 finished with `Food security 1.00`, `Health 1.00`, `Infrastructure 1.00`,
9.4 days of food, and `NeedFood at 44.0`, both survivors dead at `health 1.00` and downed.
Malnutrition, beside a full store, because `summaryHealth` does not count needs and a downed
pawn cannot feed itself.

So the term is not wrong, it is answering a different question from the one that matters. "Is
there food" and "is anybody eating" diverge exactly when the colony is dying, and only the
first is measured. A search optimising this score is told nothing about the failure mode that
ends most of its colonies, which is a poor thing for a fitness function to be silent about.

Cheap to close: `NeedFood` already appears in the upkeep survey's complaint list, so the
evaluator has access to the fact that colonists are hungry. A Food security term built on
"days in store *and* nobody starving" would separate a stocked colony from a fed one.

### Measured at last — run 82

`ColonyState` now carries `colonistsStarving` (`Need_Food.Starving`) and `minFood`, and the
vitals line says so. The first colony to run with it made the divergence unarguable:

```
food 9.1d (1 STARVING, hungriest 0.00)   colonists 3 (down 1)
food 5.3d (1 STARVING, hungriest 0.00)   colonists 2 (down 1)
```

A colonist at food need **0.00** — not low, zero — beside **9.1 days of food**. Before this the
vitals read `food 9.1d` and looked healthy. Final score: `Food security 1.00`, with the
postmortem naming `NeedFood at 26.0` as the worst unmet complaint in the same breath.

**The work response fired and was not enough.** `Doctor 5.0` and `Hauling 6.0` appear fourteen
times, exactly as intended — but the same lines read `Firefighter 6.0` and `colonists 2 (down
1)`. One upright colonist, a fire burning for 43% of the epoch, and a warg and a grizzly taking
the others. Raising the priority of feeding a patient does nothing when the only person who
could do it is fighting a fire.

Which is the same conclusion this note reached before, now with numbers behind it: the colonies
dying here have a *labour* problem, and re-ordering one pair of hands cannot answer it. The
value of the instrument is not that it saved run 82 — it did not — but that the failure is
finally visible while it is happening rather than inferred afterwards from a corpse.

## A near-term goal blocked behind a far-term room

Run 72 lost Tamii to `Hypothermia (extreme)` at -12C, with the colony dutifully making
tribalwear and a veil the whole time. Nothing in that chain is a mistake on its own:

- `WeatherClothingGoal` is ShortTerm, and correctly declares `RequiresResearch =
  ComplexClothing` with a comment explaining that parkas sit behind it.
- Parkas need a tailoring bench. **Both** benches need `ComplexClothing` — the hand bench too,
  which needs no power but does need the research.
- `ComplexClothing` needs a research bench.
- The research bench lives in the Research room, and `ResearchCapacityGoal` is **LongTerm**.

So a goal the colony needs this week is gated behind a room the plan treats as a luxury, and
the colony freezes while building correctly toward something else. The Workshop *was* finished
on day 8 and the game called it a plain `Room`, because everything it was meant to hold needed
research the colony did not have. An empty workshop is what this looks like from the outside.

The shape of a fix is a promotion rule rather than a new goal: when a nearer-horizon goal is
blocked on research and there is nowhere to research, the research room stops being long-term.
That is one rule in goal arbitration, which is also the most loop-prone code in the project —
four of the twelve composition loops have come from it — so it wants doing deliberately rather
than at the end of a session.

Worth noting the whole chain was invisible until two things landed on the same day: the cause
of death naming hypothermia rather than "lost from roster", and the work-leaning line showing
tribalwear being made at -12C.

## Where a fix would actually go

Not another builder. The colonies dying here need *labour*, and the levers that do not need it
are the ones worth looking at:

- **Prevent the spiral rather than answer it.** The deaths that start it are the target — see
  the dangerous-prey floor in `HuntPolicy`, which came from two colonies mauled by a cougar the
  director chose to hunt.
- **Cheap consumables.** `PsychiteTea` is already resolved in `AcDefs` and used nowhere. Beer
  and tea buy mood for material rather than for hands.
- **Double beds for couples.** `AcDefs.Bed` is the single bed and the only one the planner ever
  places, so every couple carries `WantToSleepWithSpouseOrLover (-4.0)` each, for ever — seen
  twice at once in run 57. A `DoubleBed` costs about what two singles cost and removes the
  complaint outright. It also needs no research and no extra labour beyond the build that was
  happening anyway, which is what makes it the right shape: the colonies dying here have
  material problems and labour problems, and this one is answered by choosing a different def
  rather than by doing more work.
- **Triage the work rather than adding to it.** With one colonist upright, what they do first
  decides the outcome, and nothing currently reorders that under collapse.

None of these are built. This note exists so the next attempt does not start where the last one
did, by adding a builder to a colony with nobody to build.
