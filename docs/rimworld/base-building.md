# Base Building

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

Construction and layout mechanics. For generating power, see [Power](power.md); for what defines a room once it's built, see [Room Types](room-types.md).

## Construction Basics

- **Walls** define enclosed spaces; a **roof** is then auto-generated over any sufficiently enclosed area rather than built directly — you never place a roof tile yourself in vanilla.
- **Floors** are optional but affect cleanliness, beauty, and (for stone/metal tile) fire resistance; unfloored dirt is flammable and gets dirty fastest.
- **Doors** slow a colonist slightly on open/close (stone doors slow the most); **autodoors** (Autodoor research) let colonists pass without stopping, useful for high-traffic points that still need to seal.
- **Construction skill** governs build speed, the quality of anything with a quality stat, and the chance of an outright "botched" failure that wastes materials — see [Crafting](crafting.md).
- A blueprint left unbuilt for too long, or built from the wrong/insufficient materials, simply sits waiting for a hauler and builder; there's no time pressure unless a raid is inbound.

## Zones

- **Stockpile zones** mark where items are hauled to; each can filter by item category, specific type, and even quality/material, with a priority level relative to other stockpiles.
- **Growing zones** define farmable plots and which single crop is sown there; see [Plants](plants.md).
- **Home area** marks the zone colonists will automatically clean and are willing to fight to defend from intruders.
- Zones can overlap in purpose but not in tile — a tile belongs to one stockpile at a time.

## Temperature Control

- Every pawn has a comfortable temperature range; going more than roughly 10°C beyond either edge starts hypothermia or heatstroke, worsening the further outside the range you go.
- **Heaters** and **coolers** actively push a room's temperature toward a target; a **passive cooler** needs no power but is far weaker and simply vents heat out.
- **Insulation** — walls insulate far better than doors or open space — slows how fast an indoor room's temperature drifts toward the outdoor temperature.
- Outdoor temperature is set entirely by biome, season, latitude, time of day, and events (cold snaps, heat waves); no amount of indoor heating/cooling changes it — only enclosing space protects against it.
- **Freezers** are simply rooms kept below 0°C (32°F) for long-term perishable storage — see [Room Types](room-types.md) and [Food](food.md).

## Practical Layout Notes

- Power infrastructure (generators, batteries, conduits) benefits from being walled in like anything else — see [Power](power.md) for the short-circuit/fire risk this protects against.
- Build in modular chunks rather than one giant blueprint; partition cheaply with wood walls early and upgrade later once resources allow.
- Centralize rooms with frequent, urgent foot traffic (hospital, prison — see [Health](health.md) and [Recruiting](recruiting.md)); keep recreation/dining more peripheral since they're used mainly morning and evening.

---

**See also:** [Power](power.md) for generating electricity · [Room Types](room-types.md) for how rooms are defined · [Combat](combat.md) for defensive layout · [Index](index.md)
