# Health — bleeding clocks, rescue, beds

Mechanics: [health.md](../rimworld/health.md) ·
[injuries & bleeding](../rimworld/health.md#injuries--bleeding) ·
[capacities](../rimworld/health.md#capacities) ·
[medicine.md](../rimworld/medicine.md) ·
[prosthetics.md](../rimworld/prosthetics.md)

## What actually kills these colonies

Counted across seven preserved chronicles in one session, seventeen deaths:

| cause | deaths |
|---|---|
| Blood loss (extreme) | **13** |
| Burn | 2 |
| Bite (megasloth teeth) | 2 |

Seventy-six percent. [health.md](../rimworld/health.md#injuries--bleeding) said so before any
of it was measured — untreated bleeding rather than the hit is what kills in most combat deaths —
and this is that claim with a number on it for this director specifically. **[live, runs 161-179]**

The shape is consistent and it is not a tending failure. The colony has a deadline
(`TicksUntilDeathDueToBloodLoss`), a medic choice that weighs reach against it, a rescue that
carries casualties out of a retreat, and usually medicine in store. What it does not have is
anybody left standing:

    run 178  four colonists down at once after a manhunter pack, med 0, all four bled out
    run 179  four down, 59 fires, med 30 — "no route to them at all against 3.3 hours left"
    run 174  four down on day 0 from a boomalope blast, two bled out, two burned

Every one of those is *everybody down together*, after which bleeding is unanswerable however
good the tending logic is.

**Confirmed rather than inferred, in run 185.** The "cannot reach the casualty" message used to
say "no route to them at all" for two different situations — nobody could path to them, and
there was nobody to send — because the helper returns the same sentinel for both. Nine of eleven
occurrences said "no route", which made unreachability look like the whole story. Split apart,
the first case to fire said: **[live, run 185]**

```
nobody can reach Kooky before they bleed out
  (there is nobody left standing to send against 2.7 hours left)
```

Three colonists down, the fourth a colonist who had joined that hour with Medicine 0, held back
to tend, and all four dead within five hours. Not a pathing problem. Not a medicine problem —
that colony died holding 21.4 days of food and scoring 1.00 on Infrastructure, Room quality and
Food security, with Survival at 0.00.

**Corrected by run 189: both causes are real.** On the strength of that single firing this note
said the nine earlier "no route" readings were almost certainly the same case. Run 189 produced
one of each within the same hour: **[live, run 189]**

```
nobody can reach Speedy  (0.4 hours of walking against 0.2 hours left)
nobody can reach Erisen  (there is nobody left standing to send against 1.9 hours left)
```

Speedy's is a distance problem and a near miss — a doctor twenty-four minutes away from a
colonist with twelve minutes left. So the leverage is both: fewer casualties at once, and shorter
walks to reach them. The second is why #67 matters beyond tidiness, since a base sited across a
hundred cells makes every internal distance longer.

So the leverage is upstream — not being overwhelmed in a single engagement — which is where #50
(a refuge before the first pack) and #55 (elective fights taken on thin margins) sit, rather than
anywhere in this file.

**And upstream of that is building throughput.** Run 187 carried every fix this session: a goal
that names the missing refuge, a scarcity term that raises the bar for a small colony, a
withdrawal that carries the fallen. Every decision it made was right. **[live, run 187]**

```
day 0 00h  ShortTerm: Somewhere to hold [1 room planned and not one of them closed]
day 0 03h  planner standing down — 66 construction outstanding against a cap of 60
day 4 07h  WITHDRAWING 2 — strength 70 vs threat 122 (0.57x), needed 2.19x
day 4 09h  WITHDRAWING 1 — 4.15x against a needed 7.31x (3 already down)
day 4 11h  Somewhere to hold [3 room(s) planned and not one of them closed]
day 4 13h  three colonists dead of blood loss
```

Four days, three rooms planned, none closed, and the goal saying so continuously the whole time.
The colony could see it needed a refuge, said so every pass, refused every fight it should have
refused — and a withdrawal with nowhere enclosed to withdraw *to* is standing somewhere else.

Perception is no longer the binding constraint here. Construction throughput was: #42 (a flat cap
of 60 regardless of how many hands exist), #67 (a distance cost that could not tell forty-one
cells from four hundred), and #63 (a buildability measure that could not see a forest, because a
tree is a Plant and not an edifice).

**All three fixed, and verified together in run 193.** **[live, run 193]**

```
day 0 13h  the Kitchen room is working
day 3 09h  the Research room is working
day 4 03h  the Bedroom room is working
day 5 18h  the Workshop room is working
```

First room at day 0 hour 13, four by day 5. The mechanism is visible in what the planner now
accepts as a site:

| run | clearing needed | rooms |
|---|---|---|
| 191, before | `clearing 56 obstructions` | none by day 4 |
| 193, after | `clearing 5, 4, 5, 6 obstructions` | four by day 5 |

Fifty-six of eighty-one cells became four to six. The scorer can see the trees, so it stops
choosing woods to build in.

Worth stating plainly because it is easy to keep improving the wrong half: every fix to the
bleeding response this session was correct and none of them would have saved these colonies.

## The state nothing covered: upright and bleeding

Every part of the health chain keys on `Downed` — the rescue needs `victim.Downed`, the retreat
carry needs `colonistsDowned`, the reserved medic only runs inside a fight. A colonist who walks
away from a won fight bleeding is none of those things, and RimWorld's tending job wants a
patient **in a bed**: an upright pawn keeps working until they fall over.

Run 193 lost Ivanna to exactly that — eleven hours after the fight ended, with Doctor at 7.0 and
twenty-six medicine of which twenty-five were stockpiled, and not one diagnostic line anywhere,
because nothing was looking. **[live, run 193]**

The fix is to send them to bed, gated on the blood-loss deadline rather than on bleeding at all.
Verified one restart later: **[live, run 195]**

```
day 4 14h  Craggy is bleeding and still on their feet, 4.4 hours from dying of it — sent to bed
           → health 0.87 (0 UNTENDED, 1 LOSING), PatientBedRest 5.0, Doctor 4.0
```

Four and a bit hours from death, in a bed, tended. The difference between the two colonists is
not medicine, doctors or priorities — both colonies had all three — it is whether anybody told
the wounded one to lie down.

## The bleeding clock

```csharp
pawn.health.hediffSet.BleedRateTotal          // > 0.001f means bleeding at all
HealthUtility.TicksUntilDeathDueToBloodLoss(pawn)
```
**[compiles]**, and the second is the number RimWorld puts on the health tab.

This is a **deadline, not a flag**, and the difference has cost colonists twice. "Is downed" and
"is untended" are both true of people who will be fine; the only question that matters is how
long they have. [health.md](../rimworld/health.md#injuries--bleeding) makes the same point about
bleeding being the thing that kills rather than the hit.

Keep the number. Reducing it to a count of who has one leaves every downstream decision unable to
ask whether help arrives in time — which is how run 162 lost Pansy with a Medicine-7 doctor
reserved to tend her and one leg between them. **[live]**

## Rescue

```csharp
var bed = RestUtility.FindBedFor(victim, carrier, false, false, null);
var job = JobMaker.MakeJob(JobDefOf.Rescue, victim, bed);
job.count = 1;
carrier.jobs.TryTakeOrderedJob(job, JobTag.Misc);
```
**[compiles]**

Two things that are not obvious:

- **A drafted pawn refuses the job.** Rescuing is work. `TryTakeOrderedJob` will not stick on a
  drafted colonist, so the draft has to be released first — and during a withdrawal every
  colonist is drafted, which is why three colonists were lost lying where a retreat walked past
  them before anyone noticed. **[live, runs ≤164]**
- **A sleeping spot counts as a bed.** It costs nothing, appears at once, and `FindBedFor` will
  return one, where a real bed is 45 material and hours of hauling that somebody bleeding on the
  floor does not have. **[live]**

## Capacities

```csharp
pawn.health.capacities.GetLevel(PawnCapacityDefOf.Moving)
PawnCapacityDefOf.Manipulation
pawn.health.summaryHealth.SummaryHealthPercent
```
**[compiles]** — and see [capacities](../rimworld/health.md#capacities) for what each one gates.

**A missing part shows up in `MoveSpeed`.** That is the cheap way to notice a crippled colonist
without the code needing to know what a leg is: read
`pawn.GetStatValue(StatDefOf.MoveSpeed)` and a one-legged surgeon reads as the slow walker they
are. Both `MedicChoice` and `RetreatCargo` rely on this.

## Medicine

`ColonyState` distinguishes medicine *on the map* from medicine *in a stockpile*, and the two
disagree often — a colony can hold thirty and have zero stored. Colonists will fetch from
anywhere reachable and unforbidden, so the total is the honest number for "can we treat", but the
game's own low-medicine alert is not counting the same thing. **[live, recurring]**

Tiers and tend quality: [medicine.md](../rimworld/medicine.md#medicine-tiers--tend-quality).
Herbal, industrial and glitterworld are three separate defs; a want expressed as one of them will
be refused by a trader stocking another, which is why a shortfall should name a list rather than
a product.

## Surgery

Fitting a peg leg needs one wood log and no research, and a slower colonist beats a bedridden
one. The tier ladder — peg leg, prosthetic, bionic, archotech, each replacing the last — is in
[prosthetics.md](../rimworld/prosthetics.md#the-four-tiers). **[live]**

## Surgery has two inputs, and only one was guarded

The amputation decision carried four guards — is the disease winning, is the part one the game
would suggest removing, is the room clean, is a bill already queued — and every one of them
answers *when* to cut. None asked whether the colony could.

RimWorld runs the operation with no medicine at all and prices it accordingly, the same way it
prices a filthy floor. Run 197: **[live, run 197]**

```
day 13 04h  INCIDENT answered 'Surgery failed on Blackrose' with 'close'
day 13 13h  INCIDENT answered 'Surgery failed on Blackrose' with 'close'
            ... med 0 at both, and for the whole day either side
```

The gate now mirrors the cleanliness one exactly, including its threshold: hold unless the
disease is past two fifths of lethal, past which bad odds beat none. Sharing the number is the
point — an empty cupboard and a dirty table are the same kind of bad bet, and two separate
constants for "how late is too late to be fussy" would drift apart.

It also reports to `CapabilityGaps`, so a colony that cannot operate for want of medicine has
that on the roadmap with a clock on it rather than only in a message.
