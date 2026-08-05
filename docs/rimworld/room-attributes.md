# Room Attributes (Impressiveness)

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

Most rooms colonists spend real time in (bedrooms, dining rooms, rec rooms, barracks) generate a mood buff or penalty based on a composite score called **Impressiveness**.

## The Four Inputs

- **Wealth** — total market value of everything in the room (furniture, floors, stored items).
- **Beauty** — average beauty rating of the room's tiles and contents. Raised by sculptures/artwork, fine flooring (stone, silver/gold-plated tile), and flowering plants; lowered by filth, blood, vomit, and cheap or damaged furniture. Furniture beauty scales with its crafted **quality** — see [Crafting & Materials](crafting-and-materials.md).
- **Space** — effective walkable floor area. Bigger generally helps, but a huge, sparsely-furnished room scores worse than a smaller, well-furnished one.
- **Cleanliness** — average floor cleanliness. Dirt, mud, and filth drag it down, and it keeps decaying until a colonist cleans it (or it sits on a self-cleaning floor type).

## How They Combine

- Impressiveness isn't a simple average — it's **heavily weighted toward whichever of the four stats is weakest**. A room with excellent wealth and beauty but a filthy floor scores much worse than the wealth/beauty numbers alone would suggest.
- Practical takeaway: fix your worst stat first. One expensive sculpture rarely rescues an otherwise ugly, dirt-tracked room.
- The resulting number maps to a named tier — roughly ten steps running from poor/mediocre up through impressive, very impressive, and wondrously impressive at the top — and that tier sets the actual mood buff or penalty size.

## What Improves Each Stat

| Stat | Improve with |
|---|---|
| Wealth | Higher-value furniture, finer flooring, valuable stored goods |
| Beauty | Sculptures/artwork (especially higher quality), potted plants, fine floors, avoiding damaged furniture |
| Space | A bigger footprint, fewer space-eating obstacles |
| Cleanliness | Regular cleaning, sterile/smooth flooring, keeping animals and muddy foot traffic out |

## Where Cleanliness Matters Beyond Mood

- **Hospitals** — a cleaner room reduces infection risk during treatment; see [Health & Medicine](health-and-medicine.md).
- **Kitchens** — a cleaner room reduces food-poisoning risk in prepared meals; see [Food & Nutrition](food-and-nutrition.md).
- Sterile tile flooring boosts cleanliness a lot but is visually plain (low beauty), so hospitals and kitchens often trade away some beauty for function.

## Beyond Colonist Rooms

- Prisoner cells use the same mood/impressiveness math, and prisoner mood directly speeds up recruitment — see [Recruiting](recruiting.md).
- A "pleasant/ugly environment" thought applies even in rooms with no dedicated Impressiveness moodlet (workshops, labs) — see [Room Types](room-types.md).

---

**See also:** [Room Types](room-types.md) for which furniture defines each room · [Health & Medicine](health-and-medicine.md) for infection risk · [Index](index.md)
