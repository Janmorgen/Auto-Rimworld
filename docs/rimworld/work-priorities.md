# Work Priorities

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

The assignment system itself. For what each work type actually entails, see [Work Types](work-types.md).

## Standard vs. Manual Mode

- **Standard mode** — each work type is a simple on/off checkbox per colonist. Enabled types run in a fixed left-to-right default order (see [Work Types](work-types.md) for that order).
- **Manual priorities mode** — each work type instead gets a number from 1 (highest) to 4 (lowest) per colonist; leaving it blank means the colonist never does it.
- Regardless of mode, **all available work at one priority level is finished before moving to the next** — if Hauling is set to priority 1, a colonist will haul everything on the map, however far away, before touching anything else.
- Ties at the same priority level are broken by the work type's default left-to-right position.

## How Jobs Actually Get Picked

- Colonists auto-select jobs each in-game moment based on: current priority level → skill level → distance/reachability.
- Within a single work type, individual task categories have their own internal priority (e.g., inside "Cooking," making a bill-queued meal typically comes before optional extra butchering).
- A colonist working a task will finish that specific job before re-checking priorities, even if a higher-priority task becomes available mid-task — though this can be overridden by drafting/undrafting them.

## Interaction With Schedules

- The **Schedule** tab sets Sleep / Work / Recreation (or "Anything") blocks per hour, separately from work priorities.
- A colonist will still break off scheduled work early if Recreation, Food, or Rest needs fall low enough (roughly below 35%, 30%, and 30% respectively by default) — these thresholds force a break regardless of the work schedule.
- A pawn already asleep when a scheduled block begins generally won't be woken early just because the clock changed, except under an "Anything" schedule with urgent unmet needs.

## Incapable Of

- Traits and backstories can make a pawn entirely incapable of certain work types (most commonly Violence, Social, or Intellectual work) — see [Colonists In-Depth](colonists.md).
- An incapable work type can't be assigned at any priority level; it simply won't appear as available for that pawn.

## Practical Notes

- Assign at least one colonist with real skill (and ideally a passion) to each of the "important four" most early colonies lean on: Shooting/Melee, Construction, Plants/Cooking, and Medicine.
- Reassigning priorities is free and instant — it's normal to shuffle assignments as circumstances change (e.g., pulling everyone off crafting the moment a raid is spotted).
- Hauling and Cleaning are usually left low-priority for everyone rather than given a dedicated specialist, since they're always-available filler work with no skill attached.

---

**See also:** [Work Types](work-types.md) for the full list and what each one does · [Colonists In-Depth](colonists.md) for skill/trait effects on work · [Index](index.md)
