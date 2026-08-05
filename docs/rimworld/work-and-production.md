# Work & Production

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

## The Priority System

- Instead of directly commanding every task, you set **work priorities** per colonist across roughly 15–20 work types.
- **Standard mode** — each work type is a simple on/off checkbox; colonists work down the default left-to-right order of enabled types.
- **Manual priorities mode** — each work type gets a number 1 (highest) to 4 (lowest) per colonist; blank means never do it. All available work at a given priority is finished before the colonist moves to the next priority level; ties at the same priority are broken left-to-right by the type's default position.
- Colonists auto-select jobs each in-game moment based on priority, then skill level, then distance/reachability.

## Work Types (typical default order)

Firefighting, Patient (bed rest), Doctor, Patient bed rest, Bedwork/childcare-type urgent tasks, Basic worker (misc small jobs), Warden (prisoners), Handling (animals), Cooking, Hunting, Construction, Growing, Mining, Plant cutting, Smithing/Crafting, Art, Tailoring, Research, Cleaning, Hauling.

- **Firefighting** and immediate patient care sit at the top of the default order for good reason — fires and dying colonists don't wait.
- **Hauling** and **Cleaning** sit at the bottom by default since they're always-available filler work; most players leave them low-priority for everyone rather than dedicating a specialist.
- Some work types are skill-gated for quality/success (Cooking, Crafting, Doctor, Construction); others (Hauling, Cleaning, Firefighting) have no skill attached at all — anyone can do them equally well.

## Skill → Output Relationship

- Speed scales with skill for almost every work type — a higher-skill colonist finishes the same job faster.
- Some jobs also gate **quality or success** on skill, not just speed: a low-Medicine doctor can botch surgery, a low-Cooking cook can food-poison a meal, a low-Construction builder can "botch" a build and waste materials, a low-Crafting/Artistic pawn produces lower-quality goods.
- A handful of tasks give no skill XP at all regardless of who does them (e.g., hauling, cleaning, feeding an incapacitated pawn).

## Prioritizing a Colony

- Assign at least one colonist with a real skill (and ideally a passion) to each of the "important four" most players lean on early: Shooting/Melee (defense), Construction, Plants/Cooking, and Medicine.
- Colonists incapable of a work type (from traits or backstory) show it blanked out — no priority can force it.
- Reassigning priorities is free and instant, so it's normal to shuffle assignments as a colony's needs change (e.g., pulling everyone off crafting during a raid).

---

**See also:** [Colonists In-Depth](colonists.md) for what each skill actually does · [Mood & Mental Breaks](mood-and-mental-breaks.md) for the passion mood bonus · [Index](index.md)
