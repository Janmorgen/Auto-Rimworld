# The Storyteller

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

The AI "director" that paces the game. For the actual incidents it triggers, see [Events](events.md).

## How Storytellers Work

RimWorld doesn't throw pure randomness at you — an AI director paces events based on your colony's wealth, population, and recent history (whether a colonist has died or been badly wounded lately, how long since the last major threat, and more).

## The Three Base-Game Storytellers, Compared

| Storyteller | Major-threat cycle | Character |
|---|---|---|
| **Cassandra Classic** | ~10.6-day cycle; can send two threats in one cycle | Steadily rising tension over time; the classic "difficulty curve" experience. Strictly more difficult than Phoebe at the same settings. |
| **Phoebe Chillax** | 16-day cycle: alternates 8 "on" days (max one threat) and 8 "off" days, starting day 13 | The most breathing room of the three; more likely to follow a hard hit with something helpful (a trade caravan, a friendly visitor). Individual hits land at the same intensity as the others — she just spaces them out more. |
| **Randy Random** | No pacing logic — flat random chance regardless of recent history | Capable of both suspiciously quiet stretches and brutal pile-ons. Has a lower minimum event-chance floor than the other two, which matters most for very large, established colonies. |

- All three send major threats at roughly the same *average* size and frequency over a long enough game (~8–9 raids per year) — the real difference is in pacing and predictability, not total volume.
- Difficulty tunes overall severity separately: Cassandra is generally considered the most difficult of the three at matched settings, Randy sits in between and is more difficult "on average" than Phoebe, and Phoebe is the gentlest.

## Difficulty Settings

Difficulty (Peaceful, Community Builder, Adventure Story, Strive to Survive, Blood and Dust, Losing is Fun, and custom settings) is layered on top of storyteller choice — it tunes overall aggression, harshness, and disease/raid frequency independent of which storyteller you picked. Both storyteller and difficulty can be changed mid-game with no penalty.

## Wealth Drives Threat Size

- Wealth — the total market value of everything you own, including buildings, not just stockpiled goods — is the main driver of raid size.
- A colony that hoards resources or builds lavishly without matching defenses is inviting bigger raids than it can handle; some players deliberately convert silver into non-wealth-counting consumables (see [Trading](trading.md)) rather than stockpiling it visibly.
- Population also factors in: more colonists (and, to a lesser extent, prisoners) raises expected threat size independent of wealth.

## Choosing a Storyteller for Your Playstyle

- **New to the game / want to learn systems calmly** → Phoebe.
- **Want the "intended," predictable difficulty curve** → Cassandra.
- **Want maximum unpredictability, good or bad** → Randy.

---

**See also:** [Events](events.md) for the actual incident list · [Combat](combat.md) for handling what gets thrown at you · [Trading](trading.md) for managing wealth · [Index](index.md)
