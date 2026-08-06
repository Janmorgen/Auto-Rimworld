# Mood

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

How the mood bar itself works. For what happens when it drops too far, see [Mental Breaks](mental-breaks.md).

## How Mood Is Built

- Mood is a 0–100% bar per colonist, built from every currently-active **thought** — small, individually-timed modifiers triggered by memories and current situation.
- Common thought sources: ate without a table, slept outdoors or in the cold, saw a colonist die, admired a nice bedroom, ate a lavish meal, worked a passionate skill, was insulted, witnessed a raid.
- Each thought has its own value and its own decay timer; mood at any given moment is roughly the sum of everything currently active.
- A "Mood Target" (shown as a marker under the mood bar in-game) reflects what mood *should* be right now given active thoughts; the visible mood bar chases that target rather than snapping instantly.

## What Shifts the Baseline

- **Traits** — Sanguine adds a steady bonus; Pessimist/Depressive add a steady penalty; Ascetic flips the usual beauty/comfort logic (happier with less).
- **Difficulty settings** — higher difficulties raise the pressure toward mental breaks independent of any individual pawn's traits.
- **Needs** (Food, Rest, Joy, Comfort, Beauty — see [Colonists In-Depth](colonists.md)) feed into thoughts, which feed into mood, rather than affecting mood directly themselves.
- **Passion** — working a skill with Interested or Burning passion gives a direct mood bonus on top of faster XP gain; working one with no passion for long stretches can itself become a low-grade drag.

## Prisoners Have Mood Too

- Prisoners track mood the same way colonists do, driven by their cell quality, food, and treatment.
- Low prisoner mood makes recruitment slower and escape or attacks on wardens more likely — see [Recruiting](recruiting.md).

## Inspirations — The Positive Counterpart

- High mood can occasionally trigger an **inspiration**: a temporary buff to a specific activity.
- **Inspired Creativity** is the best-known example — it guarantees a much higher-quality result for the next crafted/art item (see [Crafting](crafting.md)).
- Other inspirations exist for research speed, work speed, and social situations; inspirations are pure upside and cost nothing to trigger.

## Practical Mood Levers

- Keep Food/Rest/Joy/Comfort/Beauty reasonably full.
- Build genuinely impressive rooms — see [Room Attributes](room-attributes.md).
- Match work assignments to passions where possible.
- Watch for situational penalties that stack fast if ignored: eating without a table, sleeping in the cold or in the open, seeing a corpse, or spending time in a filthy/ugly room.

---

**See also:** [Mental Breaks](mental-breaks.md) for what happens when mood runs out · [Colonists In-Depth](colonists.md) for needs and traits · [Room Attributes](room-attributes.md) for the beauty/impressiveness inputs · [Index](index.md)
