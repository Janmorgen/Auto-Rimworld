# Animal Types

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

Base game only. Stats below (taming skill, yields, speed, danger) are real vanilla values, though exact numbers can drift slightly between patches.

## Taming & Training Basics

- Every animal can technically be tamed, but each has a **Minimum Handling Skill (MHS)** — the Animal skill level a colonist needs before they can even attempt it. Taming itself then rolls a chance based on the animal's wildness.
- Failing a tame or hunt attempt on an aggressive species risks the animal turning on the handler/hunter — see the "revenge chance" figures below.
- **Pen animals** (most farm herbivores) need an enclosed pen and can't be trained beyond the basics but never lose tameness. **Roaming tamed animals** can be trained in Obedience, Release (attack), Rescue, and Haul, and can bond with a specific pawn.
- Trainability is gated by MHS too — high-MHS animals like wolves or bears need a skilled handler before they can be trained to fight at all.

## Farm & Pack Animals

| Animal | Min. Taming Skill | Diet | Meat Yield | Leather/Wool Yield | Move Speed | Notes |
|---|---|---|---|---|---|---|
| Chicken / Duck | 0 | Plants | 42 | — (egg layer) | 2.1 | Fastest egg production, low meat |
| Sheep | 0 | Plants | 105 | 33 (wool) | 4.8 | Standard wool producer |
| Goat | 0 | Plants | 105 | 33 | 3.9 | Easy starter livestock |
| Cow | 0 | Plants | 336 | 96 | 3.2 | Best milk output of the common animals |
| Pig | 0 | Plants + meat | 238 | 68 | 3.9 | Omnivore, one of the few pen animals that also eats meat |
| Alpaca | 2 | Plants | 140 | 40 (wool, best cold insulation) | 4.1 | Pack animal, popular wool source |
| Dromedary (camel) | 2 | Plants | 294 | 84 | 4.3 | Pack animal, heat-tolerant |
| Horse | 3 | Plants | 336 | 96 | 5.8 | Fastest pack animal; can be ridden |
| Bison | 6 | Plants | 336 | 96 (wool) | 4.7 | Pack animal, 10% hunt-revenge chance |
| Muffalo | 6 | Plants | 336 | 96 (wool) | 4.5 | Pack animal, wild herds common on many maps |

## Predators, Guard & Companion Animals

| Animal | Min. Taming Skill | Meat Yield | Move Speed | Relative Size | Hunt-Revenge Chance |
|---|---|---|---|---|---|
| Husky / Labrador retriever | 0 | ~105–120 | 5 | ~1 | 0% — friendly starting-pet breeds |
| Arctic fox / Red fox / Fennec fox | 8 | 77 | 4.6 | 0.7 | 0% |
| Lynx | 8 | 84 | 5 | 0.8 | 50% |
| Cougar / Panther | 8 | 140 | 5 | 1.3 | 50% |
| Arctic wolf / Timber wolf / Warg | 9 | ~119 | 5 | ~1 | 100% — dangerous to tame or hunt carelessly |
| Grizzly bear / Polar bear | 8–9 | 301 | 4.6 | 2.5 | 50% |

Dogs are the classic rescue/guard pick — trainable from MHS 0, good at Rescue duty, and useful in a fight without the taming risk of true predators.

## Small Wildlife (easy hunting/taming)

| Animal | Min. Taming Skill | Meat Yield | Move Speed | Hunt-Revenge Chance |
|---|---|---|---|---|
| Rat | 5 | 31 | 4 | 0% |
| Hare / Snowhare | 8 | 31 | 6 | 0% |
| Guinea pig / Chinchilla | 6 | 31–49 | 5 | 0% |
| Raccoon | 8 | 56 | 4.1 | 0% |

These are effectively risk-free to hunt — fast to kill, no retaliation, useful early food when you can't yet afford losses.

## Large/Dangerous Wildlife

| Animal | Min. Taming Skill | Meat Yield | Relative Size | Hunt-Revenge Chance |
|---|---|---|---|---|
| Elephant | 8 | 560 | 3.6 | 50% |
| Rhinoceros | 9 | 420 | 3.5 | 50% |
| Megasloth | 10 | 560 | 3.6 | 50% (wool producer once tamed) |
| Thrumbo | very high, ~3% tame chance | — | large | rare/dangerous but hugely valuable (horn used in top-tier gear) |

These take the most work to tame or hunt safely but pay off in bulk meat, leather, or (for megasloth/thrumbo) unique materials.

## Resource Producers

- **Milk** — cows give the highest daily output of the common options; goats are a solid smaller-scale backup.
- **Wool** — alpaca, sheep, bison, muffalo, dromedary, and megasloth; insulation differs slightly by species (alpaca wool has the best cold insulation among the common choices).
- **Eggs** — chickens and ducks lay fastest; turkeys lay more slowly but yield more meat when butchered.
- **Meat & leather** — any tamed or hunted animal can be butchered for both; bigger animals give proportionally more of each, roughly matching the tables above.

## Hunting

- A colonist with a ranged weapon and Hunting enabled will repeatedly shoot a designated wild animal until it's dead or downed.
- Hunting speed and success scale with Shooting skill and weapon quality.
- The **hunt-revenge chance** column above is the odds a wounded animal turns and attacks the hunter instead of fleeing — small/passive wildlife essentially never does this, while wolves and big predators do so most or all of the time.
- Predators may also attack tame livestock or colonists directly if hungry or provoked — keeping animals in enclosed pens protects them.

## Temperature & Biome Fit

- Wild animals naturally spawn in biomes matching their own temperature adaptation — cold-climate species (arctic fox, arctic wolf, caribou) tolerate cold well and struggle in heat, while desert/warm species (dromedary, fennec fox) handle heat well and suffer in cold.
- A tamed animal kept far outside its comfortable range develops the same heatstroke/hypothermia risk colonists do; since animals can't wear clothing, shelter and temperature-controlled housing are the only fix.

---

**See also:** [Biomes](biomes.md) for which animals spawn where · [Nutrition](nutrition.md) for feeding livestock · [Index](index.md)
