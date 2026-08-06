# Power

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

Base game only. Output figures are real vanilla values but can shift a little between patches — treat them as planning guidance.

## Power Generation

| Source | Research | Output | Notes |
|---|---|---|---|
| Wood-fired generator | Electricity | ~1,000W while fed | Burns wood directly; simplest always-available fuel |
| Chemfuel-powered generator | Electricity | Comparable to wood-fired, more fuel-efficient | Burns chemfuel from a biofuel refinery |
| Wind turbine | Electricity | Variable, roughly 100W–3,500W depending on wind | No fuel needed; needs open space around it to avoid being blocked |
| Solar panel | Solar panel | Roughly double a wood-fired generator's output at peak sun | Zero output at night, in heavy cloud, or under an eclipse |
| Watermill generator | Watermill generator | Constant ~1,100W | Needs a river; fully weather-independent |
| Geothermal generator | Geothermal power | Constant 3,600W | The strongest base-game source; only buildable on a steam geyser |
| Battery | Battery | Stores up to 600 Watt-days, at 50% storage efficiency | Smooths out variable sources (solar, wind) |

## How Power Actually Works

- Power is measured in **Watts (W)**; stored power is measured in **Watt-days (Wd)** — 1 Wd can run a 1W device for 24 hours.
- Batteries only store at 50% efficiency: feeding 1,000W into a battery bank for a day nets roughly 500 Wd of usable power back out.
- Power only exists within a connected **grid** (conduits linking generators, batteries, and consumers); anything generated beyond what's used or stored is simply wasted.
- Appliances typically draw power even when idle unless manually flicked off; lights and heaters/coolers are usually the biggest steady drains.
- **Solar flares** disable most electronics (including generators) for their duration — a real risk if you have no non-electronic backup.
- **EMP** effects (traps, grenades) temporarily disable generators and turrets in range.

## Grid Design & Risk

- **Power conduits** (and batteries) left unroofed can short-circuit and start fires during storms; **hidden conduits** (routed under floors) avoid this entirely.
- Walling in generators and battery banks protects them from stray raid damage the same way walls protect colonists.
- A **power switch** lets you manually cut sections of the grid — useful for isolating a fire or disconnecting a damaged segment without shutting down the whole base.
- A stable base usually combines at least two source types (e.g., solar + battery for day/night smoothing, with geothermal or watermill as a constant baseline) so one bad night or a drained battery bank doesn't cascade into thawed food and failed turrets.

---

**See also:** [Base Building](base-building.md) for construction and temperature control · [Research](research.md) for the power tech tree · [Index](index.md)
