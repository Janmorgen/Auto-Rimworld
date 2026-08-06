# Factions & Diplomacy

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

Trading itself is covered in [Trading](trading.md); this file is about the relationship layer that sits underneath it.

## Faction Types (base game)

- **Tribal factions** — low-tech, often friendly to a low-tech colony; value medicine, textiles, and simple weapons in trade.
- **Outlander/civil factions** — mid-tech settlers; the most common source of ordinary trade caravans and quests.
- **Pirates/raiders** — always hostile, never tradeable or recruitable through normal diplomacy; exist mainly as a raid source.
- Random map generation places several of each near your colony, and hostile/neutral/allied status is rolled independently per faction at world creation.

## Goodwill

- Every non-pirate faction tracks a **goodwill** meter (roughly -100 to +100) toward your colony, which determines whether they're hostile, neutral, or allied.
- Goodwill shifts from: completing or failing their quests, trading (small positive nudge), gifting items via caravan or transport pod, raiding or killing their pawns (large negative hit), and slowly drifting back toward a neutral baseline over time if left alone.
- **Allied** factions will send military aid if you're raided (if you have the relevant call-for-aid option available), sell more freely, and offer better quest rewards.
- **Hostile** factions raid you periodically and can't be traded or negotiated with until goodwill recovers above the hostile threshold.

## Communicating With Factions

- A **comms console** (Microelectronics research) lets you contact any known faction directly to request trade caravans, ask for military aid, or pay ransom/bribes — see [Trading](trading.md) for the mechanics.
- Visiting a faction's home settlement via caravan lets you trade, recruit visitors, and pick up faction-specific quests in person.
- Killing or imprisoning a faction's pawns, destroying their property, or failing their quests all cost goodwill — sometimes enough to flip a neutral faction hostile outright.

## Quests as Diplomacy

- Completing a faction's quests is the most reliable way to build goodwill deliberately rather than just avoiding damage — see [Questing](questing.md).
- Some quests are explicitly diplomatic in nature (peace talks, escort a faction leader, mediate a dispute) and reward larger goodwill swings than a typical delivery or defense quest.

## Practical Notes

- Keeping at least one or two factions solidly allied is worth prioritizing early — their trade caravans and potential military aid are a real safety net.
- Wealth still matters here too: a very wealthy colony gets bigger raids regardless of how many factions like you, so diplomacy reduces *some* threats (hostile-faction raids) but doesn't replace defense — see [The Storyteller](storyteller.md).

---

**See also:** [Trading](trading.md) for the caravan/comms-console mechanics · [Questing](questing.md) for goodwill-building quests · [Index](index.md)
