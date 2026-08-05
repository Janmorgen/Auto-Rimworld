# Right diagnosis, wrong channel

[[asking-the-right-question]] is about measuring the wrong thing. This is the fault that
appears *after* the measurement is correct: the director sees the problem, names it accurately,
reaches for a remedy — and the remedy runs through a channel that cannot deliver in time, or
cannot deliver at all.

Four instances in one session, in four unrelated subsystems.

---

## The pattern

| correctly saw | reached for | why it did nothing |
|---|---|---|
| nothing is cooked | raised **Cooking** to 4.0 | the stove was empty; a cook cannot light it, and Cooking outranked the Hauling that would have |
| this wall is the one sealing him in | ordered a **deconstruct** | deconstruction is Construction work, which sat at 2.3 under Tailoring at 3.5 — the order was re-issued four times while somebody sewed |
| every bed is taken or in the fire's path | woke the **base planner** | siting, walling and building a bed takes minutes; the fire took one |
| the colony needs wood for its fires | raised the **designation** target | the *priority* target was a different number, so trees were marked and nobody was in a hurry |

Each remedy is defensible read alone. Each was aimed through a channel whose latency or work
type did not match the problem.

## What they have in common

**A remedy has two halves and only one of them is usually written.** Ordering the work is the
visible half. The invisible half is making sure the work outranks whatever else is being done —
and in a colony of three, everything competes with everything.

**Latency is part of correctness.** "Build a bed" and "put down a sleeping spot" are the same
intent at two timescales, and against a fire only one of them is an answer. `SleepingSpot` costs
nothing and has zero work to build; the colonist died while a proper bed was being planned.

**A designation is not a priority.** ResourceModule marking trees and WorkPriorityModule
weighting PlantCutting read two different targets for the same resource. A colony that
designates what it will not prioritise has done the paperwork and none of the work.

## The tell

The signature is a **repeat count**: a remedy firing again and again while the thing it answers
does not change. `AddTable` eight times. The deconstruct order four times. `AddCooler` three.
Cooking held at 4.0 for eighteen days.

If a remedy fires more than twice for the same complaint, it is not fixing it — either it is
aimed at a symptom (see [[asking-the-right-question]]) or it is aimed through a channel that
cannot deliver. The repeat count tells you to look; it does not tell you which of the two.

## What to do instead

**Ask what work type actually performs this, and raise that.** Refuelling is Hauling.
Deconstruction and building are Construction. Cutting rock is Mining. Chopping is PlantCutting.
The work type is a property of the job, not of the subsystem that noticed the need — and it is
almost never the work type the subsystem is named after.

**Ask how long the remedy takes against how long the problem gives you.** A fire gives minutes.
An infection gives days. Research gives weeks. Where the remedy is slower than the deadline,
there is nearly always a cheaper stopgap in the game already — a spot instead of a bed, herbal
medicine instead of a hospital, a campfire instead of a heater.

**Where two places compute the same target, make them read one number.** The wood target was
the clearest case: two correct implementations of "how much wood do we want", disagreeing, in
modules that had no reason to know about each other.

**Measure whether it worked, not whether it was ordered.** The walled-in scenario passes on the
colonist reaching food again, not on a deconstruct being designated — which is the only reason
the 23.8-hour version was caught at all. An order that is issued and never worked looks
identical to a fix, from the inside.

---

## Related

- [[asking-the-right-question]] — the same failure one step earlier, in the measurement
- [[mood-and-labour]] — why labour, not knowledge, is what these colonies run out of
- [[rimworld-defs-and-chains]] — the fuel and seat steps that appear in no recipe
