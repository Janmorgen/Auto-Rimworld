# Colonist Health & Medicine

*Part of the RimWorld core-mechanics reference. [← Back to index](index.md)*

Every pawn's body is modeled part by part — this file covers how damage, disease, and repair actually work.

## The Body Model

- Each limb and organ — arms, hands, fingers, legs, feet, toes, eyes, ears, nose, jaw, teeth, heart, lungs, liver, kidneys, stomach, brain, spine, ribs, and more — has its own health and **efficiency** percentage.
- Damage to a part reduces the capacities tied to it: a ruined leg cuts moving speed, a destroyed lung cuts breathing (which throttles most other capacities in turn), a missing hand cuts manipulation, brain damage cuts consciousness (which itself multiplies into nearly everything else).
- Some organs are non-vital and survivable to lose (a kidney, the stomach, one lung); others are vital and total loss is fatal (both lungs together, the heart, the liver).

## Injuries, Bleeding & Infection

- Wounds cause pain and, if serious, bleeding — untreated bleeding is what actually kills in most combat deaths, not the initial hit.
- **Tending** a wound (Medicine work) stops bleeding and starts healing. Better medicine and higher Medicine skill both raise "tend quality," which matters for how well the wound heals.
- Untreated or poorly-tended wounds risk developing a wound **infection**, which then behaves like a disease (below). Prompt tending in a clean, dry environment greatly reduces this risk.
- A limb infection that's losing the fight can be stopped instantly by **amputating** the infected part — brutal, but it drops that infection's risk to zero.

## Diseases

**Fatal-risk diseases** (progress over time, cured by outlasting them via immunity):
- **Flu** — mild, any climate, usually just needs bed rest.
- **Plague** — the most lethal common disease; can kill in under two days untreated, but a well-fed, well-treated patient also builds immunity fast, so quick response matters a lot.
- **Malaria** — mainly tropical/temperate-forest biomes; impairs blood filtration, so healthy kidneys and liver matter for surviving it.
- **Sleeping sickness** — slow-progressing, mostly tropical rainforest; not very lethal on its own but eats a lot of medicine and hospital time.

**Non-fatal diseases** (cured by accumulated treatment quality rather than time):
- **Gut worms** — digestive parasite, mild ongoing debuff.
- **Muscle parasites** — reduces physical capacities until treated.
- **Fibrous mechanites / sensory mechanites** — nanomachine-based conditions affecting manipulation or sight respectively.

**Injury-triggered:**
- **Wound infection** — see above; curable by amputating a non-vital infected limb.

**Animal-only:**
- **Scaria** — causes berserk rage in animals rather than death.

**Mechanics that apply to most diseases:**

- Severity rises over time while the pawn's **Immunity Gain Speed** (affected by age and overall health) fights back in parallel.
- Tending with medicine slows progression, buying time for immunity to win — it doesn't instantly cure the disease outright.
- **Penoxycyline** is a *preventive* drug: taken regularly by a healthy pawn, it blocks new cases of plague, malaria, and sleeping sickness (not flu), but does nothing once someone is already sick.
- Medicine comes in three tiers — **herbal** (weakest), **medicine** (standard industrial-grade), and **glitterworld medicine** (best) — and both medicine tier and the treating doctor's Medicine skill raise tend quality.

## Prosthetics, Bionics & Organs

- **Peg legs / hooks** — the crudest replacements, minimal or no research needed, restore only partial function.
- **Prosthetics** — steel + components, built at a Machining Table after researching Prosthetics; roughly 50–85% of the natural part's efficiency.
- **Bionics** — plasteel + advanced components, later-tier research; exceed natural performance (e.g. a bionic leg boosts move speed, and both legs stack for more).
- **Archotech parts** — the best in the game; can't be crafted, only found or acquired, and outperform bionics outright.
- **Organ harvesting** — a live pawn's healthy organ (kidney, lung, heart, liver, etc.) can be surgically removed for transplant into someone else or for sale. Amputating a limb does *not* yield a usable spare part — only the dedicated harvest-organ surgery does.
  - Harvesting causes a mood penalty for the "donor," and sometimes a smaller colony-wide penalty too.
  - Removing a vital organ (e.g. the heart) without installing a replacement first kills the patient.
  - A few conditions are cured specifically by transplant — a heart transplant cures artery blockage, a lung transplant cures asthma.
- Downed pawns can be rescued, tended, or (if hostile) captured — see [Recruiting](recruiting.md).

## Hospitals & Treatment Quality

- Surgery success and general recovery odds improve in a clean **Hospital** room — see [Room Types](room-types.md) and [Room Attributes](room-attributes.md) for what drives that cleanliness score.
- A dedicated doctor with high Medicine skill, stocked with good medicine, in a clean hospital room is the single biggest lever over survival odds for anything short of instant death.

---

**See also:** [Research](research.md) for the Prosthetics path · [Recruiting](recruiting.md) for what happens to captured pawns · [Room Types](room-types.md) for hospital requirements · [Index](index.md)
