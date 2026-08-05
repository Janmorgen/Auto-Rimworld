# Research

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

Base game only — DLC-gated research (Royalty techprints, Biotech mechanoid-chip projects, Anomaly entity-study projects) is excluded. Prerequisites listed are direct requirements; a project may also need a specific bench tier (Simple vs. Hi-tech, sometimes with a Multi-analyzer attached).

## How Dependencies Work

- Projects run left-to-right by tech tier: **Neolithic → Medieval → Industrial → Spacer**.
- Most projects need one or more prior projects finished first (shown below). A few have no prerequisite and are available from the start of any save.
- Tribal starts pay 1.5x research cost for Medieval-tier projects and 2x for Industrial-tier and above; Neolithic-tier costs the same for everyone.
- Higher-tier Industrial/Spacer projects need a **Hi-tech research bench**, and the priciest late-game ones also need a **Multi-analyzer** facility attached to it.

## Neolithic Tier

| Project | Requires | Unlocks |
|---|---|---|
| Psychoid brewing | — | Psychite tea |
| Tree sowing | — | Planting the local biome's trees |
| Beer brewing | — | Brewery, fermenting barrel |
| Passive cooler | — | Passive cooler |
| Devilstrand | — | Growing devilstrand |
| Pemmican | — | Pemmican |
| Recurve bow | — | Recurve bow |
| Cocoa | Tree sowing | Cocoa tree |

## Medieval Tier

| Project | Requires | Unlocks |
|---|---|---|
| Complex clothing | — | Tailor benches; pants, dusters, parkas, jackets, t-shirts, and similar apparel |
| Complex furniture | — | Beds, armchairs, dressers, shelves, tables, couches, bookcases, and similar furniture |
| Carpet making | — | Carpet |
| Smithing | — | Smithy; knives, gladii, maces; metal floor tiles |
| Stonecutting | — | Stone blocks; stone tile floors, concrete |
| Long blades | Smithing | Longsword, spear |
| Greatbow | Recurve bow | Greatbow |
| Plate armor | Smithing, Complex clothing | Plate armor |

## Industrial Tier

**Drugs & medicine branch**

| Project | Requires | Unlocks |
|---|---|---|
| Drug production | — | Drug lab |
| Psychite refining | Drug production | Flake, yayo |
| Wake-up production | Drug production | Wake-up |
| Go-juice production | Drug production | Go-juice |
| Penoxycyline production | Drug production | Penoxycyline |
| Medicine production | Drug production, Microelectronics | Standard medicine |

**Power & infrastructure branch**

| Project | Requires | Unlocks |
|---|---|---|
| Electricity | — | Power conduits, lamps, wood-fired/chemfuel generators, wind turbines, heaters |
| Battery | Electricity | Battery |
| Biofuel refining | Electricity | Biofuel refinery (chemfuel) |
| Watermill generator | Electricity | Watermill generator |
| Solar panel | Electricity | Solar generator |
| Geothermal power | Electricity | Geothermal generator |
| Air conditioning | Electricity | Cooler |
| Autodoor | Electricity | Autodoor |
| Advanced lights | Electricity | Flood light |
| Sterile materials | Electricity | Sterile tile |
| Nutrient paste | Electricity | Nutrient paste dispenser, hopper |
| Packaged survival meal | Nutrient paste | Packaged survival meal |
| Hydroponics | Electricity | Hydroponics basin |
| Tube television | Electricity, Complex furniture | Tube television |
| Firefoam | Electricity | Firefoam popper |
| IEDs | Electricity | Improvised explosive traps |

**Weapons, armor & defense branch**

| Project | Requires | Unlocks |
|---|---|---|
| Machining | Electricity, Smithing | Machining table; frag grenades, molotov cocktails |
| Prosthetics | Machining | Prosthetic arm/leg/heart, cochlear implant |
| Gunsmithing | Machining | Revolver, pump shotgun, bolt-action rifle, incendiary launcher |
| Flak armor | Machining, Plate armor | Flak jacket/vest/pants/helmet |
| Smokepop packs | Machining, Complex clothing | Smokepop pack |
| Blowback operation | Gunsmithing | Autopistol, machine pistol |
| Gun turrets | Blowback operation | Mini-turret |
| Foam turret | Gun turrets, Firefoam | Foam turret |
| Mortars | Gunsmithing | Mortar and shells |
| Gas operation | Blowback operation | Chain shotgun, heavy SMG, LMG |
| Precision rifling | Microelectronics, Gas operation | Assault rifle, sniper rifle |
| Autocannon turret | Microelectronics, Gun turrets, Gas operation | Autocannon turret |
| Multibarrel weapons | Microelectronics, Gas operation | Minigun |
| Uranium slug turret | Multi-analyzer, Autocannon turret, Precision rifling | Uranium slug turret |
| Rocketswarm launcher | Multi-analyzer, Autocannon turret | Rocketswarm launcher |

**High-tech branch (Hi-tech research bench)**

| Project | Requires | Unlocks |
|---|---|---|
| Microelectronics | Electricity | Comms console, orbital trade beacon, Hi-tech research bench, EMP weapons |
| Flatscreen television | Microelectronics, Tube television | Flatscreen television |
| Moisture pump | Microelectronics, Machining | Moisture pump |
| Hospital bed | Microelectronics, Sterile materials, Complex furniture | Hospital bed |
| Deep drilling | Microelectronics | Deep drill |
| Ground-penetrating scanner | Deep drilling | Ground-penetrating scanner |
| Long-range mineral scanner | Microelectronics, Machining | Long-range mineral scanner |
| Transport pod | Microelectronics, Biofuel refining, Machining | Transport pod, pod launcher |
| Shields | Microelectronics, Complex clothing | Shield belt |
| Multi-analyzer | Microelectronics, Machining | Multi-analyzer facility (boosts further research) |
| Vitals monitor | Multi-analyzer, Hospital bed | Vitals monitor |
| Fabrication | Multi-analyzer | Fabrication bench |
| Advanced fabrication | Multi-analyzer | Advanced components |

## Spacer Tier — the Ship Path

This is the vanilla win condition. All of these are expensive (roughly 3,000–8,000 points each) and need the Multi-analyzer:

**Starflight basics → Starflight sensors → Vacuum cryptosleep casket → Starflight reactor → Ship computer core**

- Unlocks, respectively: the ship structural beam → sensor cluster → ship cryptosleep casket → ship reactor → ship computer core.
- The **ship computer core** additionally needs a **persona core** item, which can't be researched or crafted — it only comes from trade or as a quest reward.
- Once all parts are built and connected outdoors, activating the reactor starts a 15-day countdown (with intensified raids) before the ship can launch.

## General Tips

- Research cost scales with how advanced a project is, not with colony wealth.
- Multiple colonists can work the same project at once with no penalty, and multiple benches can run in parallel.
- Prioritize whatever most directly reduces current risk — usually the power → machining → gunsmithing → flak armor line — before optional quality-of-life projects.
- Losing or relocating a research bench doesn't lose progress; progress is stored per-project.

---

**See also:** [Health & Medicine](health-and-medicine.md) for the prosthetics/bionics path · [Crafting](crafting.md) for what skills do with these unlocks · [Index](index.md)
