# The Storyteller & Events

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

## The AI Storyteller

RimWorld doesn't throw pure randomness at you — an AI "director" paces events based on your colony's wealth and population. The three base-game storytellers:

- **Cassandra Classic** — steadily rising tension over time; the most classically "difficulty curve" experience.
- **Phoebe Chillax** — relaxed pacing with more downtime between events, and more likely to follow a hard hit with something helpful (a trade caravan, a friendly visitor).
- **Randy Random** — no pacing logic at all; events fire with flat random chance regardless of how recently you were hit. This makes Randy capable of both suspiciously quiet stretches and brutal pile-ons, but he also has a lower minimum event-chance floor than the other two, which matters for very large, established colonies.

Difficulty (Peaceful, Community Builder, Adventure Story, Strive to Survive, Blood and Dust, Losing is Fun, etc.) is a separate setting layered on top of storyteller choice — it tunes overall aggression and harshness independent of which storyteller you picked.

## Event Categories

Random events are internally either **good**, **bad**, or a **quest**. A few of the most common:

**Weather/temperature**
- **Cold snap** — outdoor temperature drops sharply (~20°C) for 1.5–3.5 days; 30-day cooldown. Crops start dying around −10°C; hypothermia is the main colonist risk.
- **Heat wave** — outdoor temperature rises sharply for a similar duration; risks heatstroke and cooler failures, and can spawn fires if summer temps are already high.
- **Volcanic winter** — a longer, colony-wide temperature and light drop that also halves wildlife density.
- **Toxic fallout** — a cloud of toxins blankets the map, forcing colonists indoors and threatening outdoor animals/plants.

**Electrical/structural**
- **Short circuit ("Zzztt")** — can spark fires at exposed power conduits/batteries; avoidable by using hidden conduits and keeping equipment roofed.

**Wildlife**
- **Manhunter pack** — a group of animals turns permanently hostile.
- **Animal insanity / herd migration / wildlife join** — various one-off wildlife events, some helpful (a tame animal joins for free), some dangerous.
- **Insect infestation** — hives spawn underground or in caves and can spread if unchecked.

**Raids & threats**
- **Raids** — hostile factions attack in numbers that scale with colony wealth; can be a straightforward assault, a drop-pod ambush, or a siege (they dig in and bombard with mortars).
- **Mechanoid raiders** — hostile robots that feel no pain and don't use cover.

**Positive/neutral**
- **Wanderer joins / refugee arrives** — a free potential colonist.
- **Trade caravan / orbital trader** — a chance to buy/sell.
- **Item stash / ancient ruins discovery** — a small side objective with loot.
- **Quests** — a broad category of their own; see [Questing](questing.md).

## Why This Matters for Play

- Wealth (total value of everything you own, including buildings) is the main driver of raid size — a colony that hoards resources or builds lavishly without matching defenses is inviting bigger raids than it can handle.
- Storyteller choice changes *pacing*, not just difficulty — Cassandra gives predictable escalation, Phoebe gives breathing room, Randy gives volatility.
- Preparing for temperature-swing events (extra medicine, backup heating/cooling, harvestable crops at 65%+ maturity) blunts most of the "bad weather" category before it becomes a crisis.

---

**See also:** [Combat & Weapons](combat-and-weapons.md) for handling raids · [Biomes](biomes.md) for how location affects event frequency · [Index](index.md)
