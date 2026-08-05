# RimWorld — Core Gameplay Mechanics

*Base game only — this excludes all five paid expansions (Royalty, Ideology, Biotech, Anomaly, Odyssey).*

RimWorld is a sci-fi colony management sim by Ludeon Studios. You start with a handful of survivors from a spaceship wreck and guide them through building a self-sufficient colony while an AI "storyteller" throws escalating challenges at you.

## Colonists

- **Skills (0–20 each):** Shooting, Melee, Construction, Mining, Cooking, Plants, Animals, Crafting, Artistic, Medicine, Social, Intellectual. Skills decay slowly if left unused for long stretches.
- **Passions:** Each colonist has none, interested, or burning passion in certain skills, which changes XP gain rate and whether doing that work raises or lowers mood.
- **Traits:** Usually 1–3 per pawn (e.g., Industrious, Lazy, Pyromaniac, Night Owl) that modify stats and can affect behavior.
- **Backstories:** A childhood and adult backstory that can disable entire work types outright — a pacifist background might be incapable of violence, a noble upbringing might refuse menial labor.
- **Needs:** Food, Rest, Joy (recreation), Comfort, and Beauty all decay over time and roll up into overall mood.

## Mood & Mental Breaks

- Mood is driven by a running list of "thoughts" — memories and situational modifiers (ate without a table, slept outdoors, saw a colonist die, admired a nice bedroom) that add or subtract points, each on its own decay timer.
- If mood drops below a threshold, the colonist rolls for a **mental break**:
  - *Minor:* wandering off, binge eating/drinking, insulting others
  - *Major:* berserk rage, hiding, sadness-driven wandering, vandalism
  - *Extreme:* murderous rage, catatonic breakdown, giving up entirely
- Breaks can cascade — one colonist snapping mid-raid can turn a survivable fight into a colony-ending one.

## Work & Production

- Instead of directly commanding each task, you set **work priorities** (or simple on/off toggles) per colonist across roughly 15 work types — cooking, mining, construction, hauling, cleaning, research, and more.
- Colonists auto-select jobs each moment based on priority, then skill level, distance, and reachability.
- Skill level affects both **speed** and **quality/success chance** — a low-skill doctor can botch surgery, a low-skill cook can food-poison the colony.

## Health & Medicine

- The body is modeled **part by part** — each organ and limb has its own health, capacity, and function (a damaged lung reduces breathing capacity, a missing hand reduces manipulation).
- Injuries cause bleeding, infection, pain, and permanent scars; diseases (flu, plague, malaria, gut worms) progress over time and need treatment.
- Medicine has tiers (herbal → industrial → glitterworld) that affect treatment quality.
- **Prosthetics** (peg legs, hooks) restore basic function; **bionics** exceed natural performance.
- Pawns go **downed** before they die, giving a window to rescue, tend, or capture them — untreated bleeding is what actually kills most of the time.

## Base Building & Defense

- Rooms are auto-detected from enclosed walls plus a roof; their size, cleanliness, and furnishings determine an "impressiveness" score that feeds colonist mood (bedrooms, dining rooms, hospitals, and rec rooms all matter).
- **Zones** mark areas for stockpiling (with item-type and priority filters), growing, or a "home area" that colonists will auto-clean and defend.
- **Power:** wood-fired generators, solar panels, wind turbines, geothermal generators (biome-dependent), and batteries for storage.
- **Temperature control:** heaters, coolers, and insulated walls keep rooms livable and food from spoiling.
- **Defense:** sandbags and cover, deadfall/IED traps, turrets, and choke-point ("killbox") layouts are the standard toolkit against raids.

## Combat & Raids

- Ranged combat factors in shooting skill, weapon accuracy, range, cover, and target movement; melee factors in melee skill, weapon quality, and pawn size/health.
- Pawns go down before they die, so triage — rescue your own, capture or execute enemies — is as much a part of combat as the shooting itself.
- Raids are launched by hostile factions or wild "manhunter" animal packs, and scale in size with your colony's total wealth; building up too fast without matching defenses is a classic way to lose a colony.
- Other threats include mechanoid raiders (hostile robotic enemies), sieges, and drop-pod ambushes.

## Prisoners & Recruitment

- Downed enemies can be captured as prisoners, then recruited (via Social skill, wearing down their "resistance"), sold, or used for organ harvesting — a moral flexibility that's core to RimWorld's tone.
- New colonists also arrive as wanderers, refugees, or through quest rewards.

## Research & Crafting

- A **research bench** and tech tree gate new buildings, weapons, apparel, and production benches, running from Neolithic/tribal tech up to spacer-tier.
- Production benches (crafting spot, machining table, tailoring bench, etc.) turn raw resources into usable goods; crafted item **quality** (awful → legendary) depends on the crafter's skill plus some randomness.
- The vanilla endgame is building a spaceship's components and launching it to escape the planet.

## World & Environment

- **Biomes** (tundra, desert, boreal forest, tropical rainforest, ice sheet, and more) set temperature range, soil fertility, rainfall, and wildlife — and hugely change how hard a run is.
- **Seasons** and growing zones govern planting; frost or extreme heat can wipe out crops overnight.
- Terrain, rivers, and elevation affect travel speed, world-map movement, and base layout options.

## The Storyteller & Events

- An AI "director" paces the game rather than throwing pure randomness at you. The three default storytellers are **Cassandra Classic** (steadily rising tension), **Phoebe Chillax** (relaxed, more downtime), and **Randy Random** (no pacing logic — pure chaos).
- Event frequency and severity scale to your colony's wealth and population.
- Random events include raids, wanderers joining, cargo pod crashes, solar flares, cold snaps, heat waves, toxic fallout, disease outbreaks, and manhunter packs.
- Difficulty settings (separate from storyteller choice) tune overall aggression and harshness.

## Animals

- Wildlife ranges from passive herbivores to predators; some can be **tamed** (chance based on the animal's wildness and your handler's Animal skill) and then **trained** in obedience, release, rescue, haul, or attack.
- Tamed animals can haul goods, guard the base, hunt, or serve as pack animals on caravans; strong bonds can form between a pawn and an animal.

## Factions & Trade

- Other settlements/factions have a goodwill meter ranging from hostile to allied, shifted by raids, gifts, quests, and trade.
- **Caravans** let you travel the world map to trade, visit other settlements, or complete quests; a **comms console** lets you call in orbital trade ships or request faction aid without leaving home.
- **Quests** appear periodically, offering rewards for tasks like defending a location, escorting a VIP, or delivering goods.
