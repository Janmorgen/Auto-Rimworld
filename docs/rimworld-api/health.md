# Health — bleeding clocks, rescue, beds

Mechanics: [health.md](../rimworld/health.md) ·
[injuries & bleeding](../rimworld/health.md#injuries--bleeding) ·
[capacities](../rimworld/health.md#capacities) ·
[medicine.md](../rimworld/medicine.md) ·
[prosthetics.md](../rimworld/prosthetics.md)

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
