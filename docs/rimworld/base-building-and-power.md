# Base Building & Power

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

## Construction Basics

- **Walls, floors, and roofs** define rooms (see [Room Types](room-types.md)); roofs are auto-generated over sufficiently enclosed spaces rather than built directly.
- **Zones** mark functional areas: stockpile zones (with item-type and priority filters), growing zones, and the home area (which colonists auto-clean and will fight to defend).
- **Construction skill** governs build speed, the quality of anything with a quality stat, and the chance of an outright "botched" failure that wastes materials — see [Crafting & Materials](crafting-and-materials.md).

## Power Generation

| Source | Research | Notes |
|---|---|---|
| Wood-fired generator | Electricity | Burns wood directly; simple, always-available fuel |
| Chemfuel-powered generator | Electricity | Burns chemfuel (from a biofuel refinery) |
| Watermill generator | Watermill generator | Steady output, needs a river |
| Solar panel | Solar panel | Free fuel, but zero output at night or under thick cloud/pollution |
| Wind turbine | Electricity | Variable output based on wind, no fuel needed |
| Geothermal generator | Geothermal power | Very strong, constant output, but only buildable on a steam geyser |
| Battery | Battery | Stores surplus power for use when generation dips (night, calm wind, etc.) |

A stable base usually combines at least two power sources (e.g., solar + battery, or geothermal as a constant baseline) so a single bad night or dead battery bank doesn't cascade into frozen food and failed defenses.

## Temperature Control

- Every pawn has a comfortable temperature range; going more than ~10°C beyond either edge starts hypothermia or heatstroke, worsening the further outside the range you go.
- **Heaters** and **coolers** actively push a room's temperature toward a target; a **passive cooler** needs no power but is far weaker.
- **Insulation** (walls generally insulate better than doors/open space) slows how fast an indoor room's temperature drifts toward the outdoor temperature.
- Outdoor temperature is set entirely by biome, season, latitude, time of day, and events — no amount of indoor heating/cooling changes it; only shelter protects against it.
- **Freezers** are simply rooms kept below 0°C (32°F) for long-term perishable storage — see [Room Types](room-types.md).

## Defense Integration

Base building and defense are deeply linked — see [Combat & Weapons](combat-and-weapons.md) for chokepoints, traps, turrets, and killbox design. A few building-specific notes:

- Power conduits and batteries left unroofed risk short-circuit fires during storms; hidden conduits avoid this entirely.
- Walling in power infrastructure (generators, batteries) protects it from stray raid damage the same way it protects colonists.
- Auto-doors (Autodoor research) let colonists pass without slowing down, useful for high-traffic chokepoints that still need to be sealable.

---

**See also:** [Room Types](room-types.md) for how rooms are defined · [Combat & Weapons](combat-and-weapons.md) for defense structures · [Research](research.md) for the power/temperature tech tree · [Index](index.md)
