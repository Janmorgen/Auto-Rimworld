# Work Types

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

Every vanilla work type, in their default priority order. For how priorities and assignment actually work, see [Work Priorities](work-priorities.md).

## Full List (Default Order, Highest to Lowest)

| Work type | What it covers | Governing skill |
|---|---|---|
| Firefighting | Extinguishing fires anywhere on the map | None |
| Patient | Following doctor's orders (resting, taking prescribed treatment) | None |
| Doctor | Tending wounds, surgery, administering medicine | Medicine |
| Patient bed rest | Forced bed rest for the severely injured/malnourished | None |
| Basic worker | Miscellaneous small jobs not covered elsewhere | None |
| Warden | Managing prisoners — feeding, recruiting, executing | Social |
| Handling | Taming, training, and slaughtering animals | Animals |
| Cooking | Making meals, butchering, brewing | Cooking |
| Hunting | Shooting designated wild animals | Shooting |
| Construction | Building, repairing, deconstructing | Construction |
| Growing | Sowing and harvesting crops | Plants |
| Mining | Digging out rock, ore, and compacted resources | Mining |
| Plant cutting | Chopping trees and clearing wild plants | Plants |
| Smithing/Crafting | Making weapons, apparel, and other bench goods | Crafting |
| Tailoring | Making apparel specifically (shares Crafting skill) | Crafting |
| Art | Making sculptures and other art | Artistic |
| Research | Working the research bench | Intellectual |
| Cleaning | Removing filth | None |
| Hauling | Moving items to stockpiles/destinations | None |

*(Exact internal ordering can vary slightly by version, but this reflects the standard vanilla layout.)*

## Reading the List

- **Tasks on the left are more important** in Standard mode — a colonist works down the list, doing the highest-priority enabled type they're capable of before moving to the next.
- **No-skill work types** (Firefighting, Patient, Patient bed rest, Basic worker, Cleaning, Hauling) are performed equally well by anyone — there's no reason not to let every colonist help with these at some priority.
- **Skill-gated types** determine both speed and, for several of them, success/quality — see [Colonists In-Depth](colonists.md) for exactly what each skill governs.

## Notes on Specific Types

- **Firefighting** and urgent **Doctor**/**Patient** work sit at the very top of the default order for good reason — fires and dying colonists don't wait for anything else.
- **Warden** covers all prisoner interaction, including the resistance-breaking conversations described in [Recruiting](recruiting.md).
- **Smithing/Crafting** and **Tailoring** both draw on the same Crafting skill even though they're separate columns, so a colonist good at one is equally good at the other.
- **Hauling** and **Cleaning** sit at the very bottom by default since they're always-available filler work — most players leave them low-priority for everyone rather than dedicating a specialist.

---

**See also:** [Work Priorities](work-priorities.md) for how assignment actually works · [Colonists In-Depth](colonists.md) for skill effects · [Crafting](crafting.md) for what the crafting-related types actually produce · [Index](index.md)
