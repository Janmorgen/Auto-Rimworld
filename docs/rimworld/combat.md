# Combat

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

The mechanics and tactics of fighting. For the actual gear catalog, see [Weapons](weapons.md).

## Core Ranged Mechanics

- Every ranged weapon has an accuracy value at four range bands: **Touch** (3 tiles), **Short** (12 tiles), **Medium** (25 tiles), and **Long** (40 tiles); accuracy between named bands is interpolated, and a weapon simply cannot fire beyond its maximum range.
- Actual hit chance combines the shooter's **Shooting** skill, the weapon's accuracy at that range, target movement, and **cover**.
- **DPS** (damage per second) is a function of raw damage, burst shot count, and the full firing cycle (aim/warm-up time + firing time + cooldown) — a weapon that fires slower needs proportionally more damage per hit to compete.
- **Stopping power** — a weapon stat that, if it meets or exceeds the target's body size, staggers them briefly on a hit. This matters for both offense (interrupting an enemy's approach) and defense.
- **Armor penetration** vs. the target's armor value determines how much of a hit's damage actually gets through.

## Core Melee Mechanics

- Melee depends on **Melee** skill, weapon quality, and the relative size/health of both fighters.
- You cannot fire a ranged weapon at an adjacent (melee-range) target — it's melee-only once someone closes the distance.
- Melee weapons deal either sharp or blunt damage (or both); blunt is generally better against armor, sharp against unarmored targets — see [Materials](materials.md) for how the crafting material affects this.

## Downed, Not Dead

- Pawns go **downed** before they die in most cases — this gives a window to rescue, tend, or capture them, but untreated bleeding after being downed is what actually kills most of the time.
- A downed enemy can be captured as a prisoner rather than left to die — see [Recruiting](recruiting.md).
- See [Health](health.md) for exactly how injuries, bleeding, and capacities interact.

## Cover & Terrain

- **Cover** (sandbags, walls, natural rock, other objects) reduces incoming accuracy significantly; fighting from behind cover while the enemy is caught in the open is one of the most reliable defensive advantages in the game.
- Terrain that slows movement (mud, deep snow, rubble) affects positioning and kiting as much as raw weapon stats do.

## Threats

- **Raids** — hostile factions attack in numbers that scale with your colony's total wealth; can be a straightforward assault, a **drop-pod ambush**, or a **siege** (they dig in outside range and bombard with mortars).
- **Manhunter packs** — a group of animals turns permanently hostile and attacks on sight.
- **Mechanoid raiders** — hostile robotic enemies that feel no pain and don't seek cover, making them fight very differently from human raiders.
- **Insect infestations** — hives spawn underground or in caves and can expand if left unchecked, eventually sending insects out to attack.
- See [The Storyteller](storyteller.md) and [Events](events.md) for how often these actually occur.

## Base Defense Fundamentals

- **Chokepoints** — funnel attackers into a single approach so a small number of defenders (and turrets) can cover it.
- **Traps** — deadfall traps and IED variants (EMP, incendiary, smoke) hidden in approach corridors.
- **Killboxes** — a deliberately engineered chokepoint stacked with turrets, traps, and cover; the classic min-max defense layout, trading aesthetics and space for maximum lethality per attacker.
- Turrets, walls, and power for both need protecting just as much as colonists do — see [Base Building](base-building.md) and [Power](power.md).

---

**See also:** [Weapons](weapons.md) for the actual gear · [Health](health.md) for treating combat injuries · [The Storyteller](storyteller.md) for how raid size scales · [Index](index.md)
