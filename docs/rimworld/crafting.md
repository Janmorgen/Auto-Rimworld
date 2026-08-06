# Crafting

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

The process side of making things — quality, bills, benches, and how skill turns into output. For the raw resources themselves, see [Materials](materials.md).

## The Quality System

Anything with a quality stat (weapons, armor, furniture, art) is randomly assigned one of seven tiers when crafted or built:

**Awful → Poor → Normal → Good → Excellent → Masterwork → Legendary**

- The result is rolled on a distribution centered on the crafter's relevant skill: the average result becomes "Normal" around skill level 6, and by skill 4 there's already roughly a 50% chance of Normal-or-better. By skill 20, most rolls land Good–Excellent, with a smaller chance each of Masterwork or Legendary.
- Which skill governs quality depends on what's being made: **Crafting** for weapons/apparel/armor made at a smithy, tailor bench, or machining/fabrication bench; **Artistic** for sculptures and other art; **Construction** for buildings and most furniture.
- **Masterwork** and **Legendary** items never spawn on enemies or in ordinary trader stock — they only come from player crafting or quest rewards (masterwork can occasionally appear as loot).
- An **Inspired Creativity** inspiration (see [Mood](mood.md)) bumps the result up two tiers and guarantees at least Normal — timing a high-skill pawn's inspiration well is the most reliable way to get a Legendary item on demand.
- Higher quality means better stats across the board: more damage/armor for weapons and gear, more beauty and comfort for furniture, longer lifespan (HP) for everything.

## The "Stuff" System (Material Selection)

Most craftable and buildable items let you choose their material ("stuff") at the bill — steel vs. plasteel vs. wood for a wall, cloth vs. leather vs. hyperweave for a jacket. The material multiplies the item's *base* stats:

- Each stat (HP, armor value, beauty, flammability, insulation, market value, etc.) has its own **factor** per material.
- Example: a wall has a base 300 HP; granite's HP factor is 1.7, so a granite wall ends up at 1.7 × 300 = **510 HP**. The same multiplication happens independently for every other stat.
- Market value is the one exception — it isn't a simple multiply-through, but a pricier material still raises the finished item's value.
- This is why material choice matters as much as quality: a Normal-quality plasteel weapon can out-damage a Good-quality steel one, and a marble wall is far more beautiful (but weaker) than a granite one.
- See [Materials](materials.md) for the actual per-material numbers.

## Bills — How Crafting Jobs Are Queued

Every production bench runs on **bills**, added from the bench's own menu:

- **Repeat modes** — do this a fixed number of times, **do until you have X** finished (unworn, above a minimum HP/quality threshold) in stock, or repeat forever.
- **Ingredient filters** — restrict which specific materials are allowed (e.g., only cloth-type fabrics, exclude human leather) and set a minimum/maximum quality range for what counts.
- **Ingredient search radius** — caps how far a worker will travel to fetch materials, useful for stonecutting/smelting so a worker doesn't wander the whole map for one chunk.
- Bills can be **suspended**, **reordered**, deleted, or (since 1.0) assigned to a specific colonist and copy-pasted to other benches.
- Multiple bills queue in order at a bench; a worker moves to the next enabled bill once the current one's condition is satisfied.

## Production Benches

| Bench | Research needed | Makes | Governing skill |
|---|---|---|---|
| Campfire | — | Basic meals, doesn't need fuel/power | Cooking |
| Electric stove | Electricity | Meals, faster/better than a campfire | Cooking |
| Butcher table/spot | — | Meat + leather from corpses | Cooking |
| Brewery | Beer brewing | Beer (from hops, via fermenting barrel first) | Cooking |
| Fermenting barrel | Beer brewing | Ferments hops into beer over time | — (passive) |
| Fueled/Electric smithy | Smithing | Early melee weapons, metal floor tiles | Crafting |
| Stonecutter's table | Stonecutting | Stone blocks from rock chunks | Crafting |
| Hand/Electric tailor bench | Complex clothing | Apparel | Crafting |
| Machining table | Machining | Guns, grenades, flak armor components | Crafting |
| Fabrication bench | Fabrication | High-tech items up to power armor, components | Crafting |
| Drug lab | Drug production | Flake, yayo, wake-up, go-juice, penoxycyline | Crafting |
| Art bench / sculptor's table | (with Complex furniture) | Sculptures and other art | Artistic |
| Research bench | — | Not crafting, but the same "bench + bill-like queue" pattern | Intellectual |

## Skill → Speed vs. Quality

- **Speed** for most jobs scales with the relevant skill directly, though a few (hauling, cleaning, firefighting) have no skill attached at all and are done equally well by anyone.
- **Quality/success**, where it applies, is a separate roll layered on top of speed — a fast but low-skill crafter still turns out worse goods, just more of them.
- See [Colonists In-Depth](colonists.md) for exactly what each of the 12 skills governs, and [Work Priorities](work-priorities.md) for how jobs get assigned in the first place.

---

**See also:** [Materials](materials.md) for the resources these benches consume · [Colonists In-Depth](colonists.md) for skill effects · [Research](research.md) for bench prerequisites · [Index](index.md)
