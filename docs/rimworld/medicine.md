# Medicine

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

Treatment, disease, and repair. For the underlying body model and injury mechanics, see [Health](health.md).

## Diseases — Full Base-Game List

**Fatal-risk diseases** (severity rises over time; cured by building immunity faster than severity climbs):

| Disease | Where | Notes |
|---|---|---|
| Flu | Any climate | Mild; usually just needs bed rest |
| Plague | Any climate | The most lethal common disease — can kill in under two days untreated, but immunity also builds fast with good care |
| Malaria | Tropical/temperate-forest biomes | Impairs blood filtration, so healthy kidneys/liver matter for survival |
| Sleeping sickness | Mostly tropical rainforest | Slow-progressing, not very lethal alone, but consumes a lot of medicine and hospital time |

**Non-fatal diseases** (cured by accumulated treatment quality rather than time):

| Disease | Notes |
|---|---|
| Gut worms | Digestive parasite, mild ongoing debuff |
| Muscle parasites | Reduces physical capacities until treated |
| Fibrous mechanites | Nanomachine condition affecting manipulation |
| Sensory mechanites | Nanomachine condition affecting sight |

**Injury-triggered:** Wound infection (see [Health](health.md)) — curable by amputating a non-vital infected limb.

**Animal-only:** Scaria — causes berserk rage in animals rather than death.

## How Disease Progression Works

- Severity rises over time while the pawn's **Immunity Gain Speed** (affected by age and overall health) fights back in parallel.
- Tending with medicine slows progression, buying time for immunity to win — it doesn't instantly cure the disease outright.
- **Penoxycyline** is a *preventive* drug: taken regularly by a healthy pawn, it blocks new cases of plague, malaria, and sleeping sickness (not flu), but does nothing once someone is already sick.

## Medicine Tiers & Tend Quality

| Tier | Relative strength |
|---|---|
| Herbal medicine | Weakest, grown from healroot |
| Medicine (industrial) | Standard, crafted from herbal medicine + neutroamine + cloth |
| Glitterworld medicine | Best, trade/quest only |

- **Tend quality** is set by both the medicine tier used and the treating doctor's **Medicine** skill — better of either helps, but a skilled doctor with poor medicine can still outperform an unskilled one with good medicine in some cases.
- Surgery (implants, amputation, organ work) uses the same tend-quality inputs, plus carries its own separate success-chance roll — a failed surgery can range from a minor setback to lethal, depending on the operation.

## Organ Transplant & Harvesting

Artificial replacement parts (prosthetics, bionics, archotech) now have their own dedicated file — see the [Prosthetics System](prosthetics.md). This section covers *natural* organ transplant instead:

- **Organ harvesting** — a live pawn's healthy organ (kidney, lung, heart, liver, etc.) can be surgically removed for transplant into someone else or for sale. Amputating a limb does *not* yield a usable spare part — only the dedicated harvest-organ surgery does.
  - Harvesting causes a mood penalty for the "donor," and sometimes a smaller colony-wide penalty too.
  - Removing a vital organ without installing a replacement first kills the patient.
  - A few conditions are cured specifically by transplant — a heart transplant cures artery blockage, a lung transplant cures asthma.
- Installing an artificial part over a damaged-but-present natural one still requires removing what's left of the original first — see [Prosthetics System](prosthetics.md#installing--removing) for how that interacts with vital organs.

## Hospitals & Treatment Quality

- Surgery success and general recovery odds improve in a clean **Hospital** room — see [Room Types](room-types.md) and [Room Attributes](room-attributes.md) for what drives that cleanliness score.
- A dedicated doctor with high Medicine skill, stocked with good medicine, in a clean hospital room is the single biggest lever over survival odds for anything short of instant death.

---

**See also:** [Health](health.md) for the body model and injuries · [Prosthetics System](prosthetics.md) for artificial body parts · [Recruiting](recruiting.md) for captured pawns · [Index](index.md)
