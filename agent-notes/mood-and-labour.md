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
