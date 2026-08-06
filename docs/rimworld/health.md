# Health

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

The body model and how damage actually works. For treating disease and organ transplant, see [Medicine](medicine.md); for replacing a lost or damaged part, see [Prosthetics System](prosthetics.md).

## The Body Model

- Each limb and organ — arms, hands, fingers, legs, feet, toes, eyes, ears, nose, jaw, teeth, heart, lungs, liver, kidneys, stomach, brain, spine, ribs, and more — has its own health and **efficiency** percentage.
- Body parts are arranged hierarchically: damage to a hand affects the arm's overall function, damage to an arm affects the torso's, and so on, rather than each part being fully independent.
- Some organs are **non-vital** and survivable to lose entirely (a kidney, the stomach, one lung); others are **vital**, and total loss of them is fatal (both lungs together, the heart, the liver).
- A part that's destroyed or too badly damaged doesn't heal back on its own — it stays missing/non-functional until replaced with a prosthetic, bionic, or archotech part; see the [Prosthetics System](prosthetics.md) for the full tier breakdown.

## Capacities

Damage doesn't just hurt a body part — it reduces whichever **capacity** that part feeds into:

| Capacity | Fed by | Effect when reduced |
|---|---|---|
| Moving | Legs, feet, pelvis | Slower movement speed |
| Manipulation | Arms, hands, fingers | Slower/worse at nearly all manual work |
| Sight | Eyes | Reduced accuracy, work speed |
| Hearing | Ears | Minor effects on some tasks |
| Talking | Jaw, mouth | Slower social interactions |
| Breathing | Lungs, trachea | Throttles almost every other capacity if severely reduced |
| Blood pumping | Heart | Same — a failing heart drags everything else down |
| Blood filtration | Kidneys, liver | Affects resistance to certain diseases (notably malaria) |
| Consciousness | Brain | Multiplies into nearly everything — the single most impactful capacity to protect |
| Eating | Jaw, mouth, digestive tract | Slower eating speed |

- Consciousness is special: it applies a roughly one-for-one multiplier to manipulation and several other capacities, so brain damage, severe pain, or consciousness-reducing drugs quietly tank a pawn's overall usefulness even if no other single part looks badly hurt.

## Injuries & Bleeding

- Wounds cause pain and, if serious, **bleeding** — untreated bleeding is what actually kills in most combat deaths, not the initial hit.
- **Tending** a wound (Medicine work) stops bleeding and starts healing; see [Medicine](medicine.md) for how tend quality is determined.
- Old wounds that heal poorly can leave a **permanent scar**, which is essentially a small, permanent stat penalty on that body part.

## Infection Risk

- Untreated or poorly-tended wounds risk developing a wound **infection**, which then behaves like a disease — see [Medicine](medicine.md).
- Prompt tending in a clean, dry environment (see [Room Attributes](room-attributes.md)) greatly reduces this risk.
- A limb infection that's losing the fight can be stopped instantly by **amputating** the infected part — brutal, but it drops that infection's risk to zero.

## Downed vs. Dead

- Pawns go **downed** — incapacitated but alive — before they die in most cases, giving a window to rescue, tend, or capture them.
- Whether a downed pawn survives usually comes down to how quickly bleeding is stopped, not the severity of the original wound alone.
- See [Combat](combat.md) for how pawns end up downed in the first place, and [Recruiting](recruiting.md) for what happens to captured ones.

---

**See also:** [Medicine](medicine.md) for diseases and treatment · [Prosthetics System](prosthetics.md) for replacing lost body parts · [Combat](combat.md) for how injuries happen · [Room Attributes](room-attributes.md) for the cleanliness that affects infection risk · [Index](index.md)
