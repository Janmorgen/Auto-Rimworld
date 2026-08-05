# Mood & Mental Breaks

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

## How Mood Works

- Mood is a 0–100% bar per colonist, built from every currently-active **thought** — small, individually-timed modifiers triggered by memories and current situation (ate without a table, slept outdoors, saw a colonist die, admired a nice bedroom, ate a lavish meal).
- Each thought has its own value and its own decay timer; mood at any moment is roughly the sum of everything currently active.
- A "Mood Target" (shown as a marker under the mood bar) reflects what mood *should* be right now given active thoughts; the visible mood bar chases that target rather than snapping instantly.
- Traits and difficulty settings shift the baseline — e.g., Sanguine adds a steady bonus, higher difficulties raise the baseline mental-break threshold pressure.

## Break Thresholds

Every pawn has three break thresholds (roughly 35% / 20% / 5% mood by default, before trait/difficulty modifiers):

- **Minor break** — the highest threshold; most common.
- **Major break** — a lower threshold; more dangerous.
- **Extreme break** — the lowest threshold; the most severe outcomes.

Traits shift these thresholds up or down: Iron-willed and Steadfast make a pawn noticeably more resistant (break only at a much lower mood); Volatile, Nervous, Neurotic, and Very Neurotic make a pawn snap sooner.

## Break Types (illustrative, not exhaustive)

- **Minor** — wander off, binge on food/drugs, insult others, sad wandering.
- **Major** — berserk (attacks anyone nearby), hide in fear, vandalism, fire-starting.
- **Extreme** — murderous rage, catatonic breakdown, giving up entirely (the pawn becomes non-functional).

Which specific break fires is randomized based on the pawn's traits and current situation — a Bloodlust-flavored or violent pawn is more likely to roll aggressive breaks, a Nervous or Wimp pawn is more likely to roll fear/wandering ones.

## Why Mood Management Matters

- A single break during a raid can turn a survivable fight into a colony-ending one — a berserk colonist may attack an ally, or a fleeing colonist may run into danger.
- Prisoners have their own mood tracked the same way; a low-mood prisoner is both harder to recruit and more likely to attempt an escape or attack a warden.
- Sustained low mood for long enough can cause a colonist to leave the colony outright, independent of any single break.

## Inspirations

The positive counterpart to a break: high mood can occasionally trigger an **inspiration**, a temporary buff to a specific activity (e.g., Inspired Creativity guarantees a much higher-quality crafted/art result for the next item). Inspirations are pure upside and don't cost anything to trigger.

## Practical Mood Levers

- Keep the Food/Rest/Joy/Comfort/Beauty needs reasonably full — see [Colonists In-Depth](colonists.md).
- Build genuinely impressive rooms — see [Room Attributes](room-attributes.md).
- Match work assignments to passions where possible — working a passionate skill gives a mood bonus on top of faster XP.
- Watch for situational penalties: eating without a table, sleeping in the cold or in the open, seeing a corpse, or being in a filthy/ugly room all stack up fast if ignored.

---

**See also:** [Colonists In-Depth](colonists.md) for needs and traits · [Room Attributes](room-attributes.md) for the beauty/impressiveness inputs to mood · [Index](index.md)
