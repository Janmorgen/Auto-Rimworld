# Colonists In-Depth

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

A deeper look at what actually makes each pawn different: skills, passions, traits, backstories, and needs.

## Skills (0–20 each)

Every colonist has all twelve skills, each 0–20. Skill decays slowly if a skill goes unused for a long time (a trait can slow this decay).

| Skill | What it actually governs |
|---|---|
| Shooting | Ranged accuracy, especially at range |
| Melee | Melee hit chance and dodge chance |
| Construction | Build speed, furniture/structure quality, chance of a "botched" construction failure; some builds (traps, electrical) need a minimum level |
| Mining | Speed to mine out rock, and yield from mineral veins |
| Cooking | Meal quality odds, food-poisoning risk, butchering efficiency |
| Plants | Sow/harvest speed for growing zones, hydroponics, and flower pots; tree-cutting speed; caravan foraging rate; a few plants need a minimum level to sow at all |
| Animals | Taming/training success and speed, handling |
| Crafting | Crafting speed and the quality of items produced |
| Artistic | Sculpting speed and the beauty/quality of art produced |
| Medicine | Tend quality and surgery success chance |
| Social | Negotiation ability — prisoner recruitment speed, trade prices, resolving social fights |
| Intellectual | Research speed |

High skill grants faster/better work; low skill risks outright failure — construction botches, wild-animal revenge on a failed tame, surgery failure, poor crop yield, or low-quality crafted goods.

## Passions

Each colonist has **none**, **interested** (one flame), or **burning** (two flames) passion in any given skill:

- **None** — skill still grows, but at a much slower XP rate, and working it doesn't help mood.
- **Interested** (1 flame) — standard XP rate, plus a mood bonus for doing that work.
- **Burning** (2 flames) — faster XP gain than interested, and a bigger mood bonus.

A pawn with a mediocre skill number but a passion for it is usually more valuable long-term than a pawn with a high number and no passion, since passion drives both growth and mood.

## Traits

Pawns usually have 1–3 traits (occasionally a 4th if it's a sexuality trait, which doesn't count against the normal cap). Traits are grouped into **spectrums** — related categories where a pawn can only hold one trait from that group at a time (e.g., a work-speed spectrum, a beauty/attractiveness spectrum, a drug-interest spectrum).

Traits generally fall into a few functional buckets:

- **Mood-affecting** — e.g., Sanguine (steady positive mood boost), Optimist/Pessimist (smaller mood shift), Depressive (persistent mood penalty).
- **Mental-break threshold** — e.g., Iron-willed and Steadfast make breaks less likely (raise the mood bar needed to stay stable); Volatile, Nervous, and Neurotic make them more likely.
- **Work-speed/skill** — e.g., Industrious (faster work) vs. Lazy (slower); Brawler (+4 Melee, −4 Shooting, and generally won't use ranged weapons).
- **Situational** — e.g., Pyromaniac (happy near fires), Ascetic (happier in a plain bedroom, unbothered by raw food), Night owl (works better at night).
- **Sexuality** — Gay, Bisexual, Asexual; purely flavor/relationship-related, doesn't affect work.

A backstory or trait can also outright **disable entire work types** — a pacifist-flavored background might be incapable of Violence, a rough one might refuse Social or Intellectual work. Disabled work types show as blank/dashed on the work tab and can't be assigned no matter the priority.

## Backstories

Every pawn has a childhood and an adult backstory. Together they:

- Set small starting skill bonuses/penalties in relevant skills
- Can disable specific work types entirely (independent of traits)
- Provide flavor text that (loosely) explains the pawn's disposition

Backstories are inherited from the pawn generation pool and can't be edited after the game starts (aside from certain rare events).

## Needs

Every colonist tracks several needs that decay over time and roll up into overall **mood**:

- **Food** — must eat regularly; going too long causes malnutrition.
- **Rest** — must sleep; exhaustion forces a pawn to collapse if ignored long enough.
- **Joy (Recreation)** — fulfilled by rec activities (games, TV, drugs, socializing); different pawns prefer different categories of joy.
- **Comfort** — fulfilled by sitting/lying on quality furniture; higher-quality, more comfortable furniture refills it faster.
- **Beauty** — satisfied just by being in/near attractive surroundings; punished by ugly or filthy ones.

Mood itself is a separate rolling total built from every currently-active "thought" (see [Mood & Mental Breaks](mood-and-mental-breaks.md)), not just the needs bars directly — needs feed into thoughts, which feed into mood.

---

**See also:** [Mood & Mental Breaks](mood-and-mental-breaks.md) for how low needs turn into thoughts and breaks · [Work & Production](work-and-production.md) for how skills translate into daily output · [Index](index.md)
