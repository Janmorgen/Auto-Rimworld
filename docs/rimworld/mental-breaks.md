# Mental Breaks

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

What happens once mood runs out. For how the mood bar itself is built, see [Mood](mood.md).

## Break Thresholds

Every pawn has a base **Mental Break Threshold** stat (35% by default before trait/difficulty modifiers), which sets three trigger points:

| Break severity | Threshold | Corresponds to mood state |
|---|---|---|
| Minor | The full base threshold (~35%) | "Stressed" |
| Major | 4/7 of the base threshold (~20%) | "On edge" |
| Extreme | 1/7 of the base threshold (~5%) | "About to break" |

- Traits shift the base threshold up or down: **Iron-willed** and **Steadfast** raise it (a pawn tolerates lower mood before snapping); **Volatile**, **Nervous**, and **Neurotic** lower it (a pawn snaps sooner).
- The threshold is capped between roughly 1% and 50% regardless of how many trait modifiers stack.

## Break Types (illustrative, not exhaustive)

- **Minor** — wandering off, binge eating or drug use, insulting others, a sad wander.
- **Major** — berserk (attacks anyone nearby), hiding in fear, vandalism, a fire-starting rampage.
- **Extreme** — murderous rage, catatonic breakdown, giving up entirely (the pawn becomes non-functional).

Which specific break fires is randomized, weighted by the pawn's traits and current situation — a violent or Bloodlust-flavored pawn rolls aggressive breaks more often; a Nervous or Wimp pawn rolls fear/wandering ones more often.

## Why This Matters in Play

- A single break during a raid can turn a survivable fight into a colony-ending one — a berserk colonist may attack an ally, or a fleeing colonist may run straight into danger.
- Sustained low mood for long enough can cause a colonist to leave the colony outright, independent of any single break event.
- Animals can have mental breaks too (most commonly triggered by pain or overcrowding in a pen), though their break types are simpler than a human's.

## Prisoner & Visitor Breaks

- Prisoners run on the same mood/threshold system as colonists — see [Recruiting](recruiting.md) for how this interacts with resistance and recruitment.
- Temporary guests and quest-lent soldiers can also break if badly mistreated or endangered, same as anyone else on the map.

## Preventing Breaks

- The real lever is everything in [Mood](mood.md) — keeping needs met, rooms impressive, and passions matched to work.
- Catching a pawn at "Stressed" (minor-break range) and addressing the cause is far cheaper than dealing with a Major or Extreme break after the fact.
- A single well-timed positive event (a favorite meal, a passionate task, a private moment of recreation) can be enough to pull a borderline pawn back from the edge before anything fires.

---

**See also:** [Mood](mood.md) for how the mood bar itself works · [Colonists In-Depth](colonists.md) for trait effects · [Recruiting](recruiting.md) for prisoner mood · [Index](index.md)
