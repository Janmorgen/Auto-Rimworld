# What a plant is for, as measured

Established with the `plants` scenario (`AUTOCOLONY_SCENARIO=plants`), which classifies every
sowable plant and grades the result against expectations **written down first**. Same method as
the room showcase, for the same reason.

Thirty sowable plants in the base game with no DLC active. Fifteen predictions; the first run
got fourteen.

---

## The six roles

| Role | Plants | Test that decides it |
|---|---|---|
| **Food** | rice, corn, potato, strawberry | harvest `preferability >= RawBad` |
| **Textile** | cotton, devilstrand | harvest is stuff in category `Fabric` |
| **Medicine** | healroot | harvest `IsMedicine` |
| **Fodder** | haygrass | `ingestible.optimalityOffsetFeedingAnimals > 0` |
| **Social** | psychoid, smokeleaf, hops | nutrition below `RawBad`, no animal-feed bonus |
| **Wood** | every tree | `plant.IsTree` |
| **Decorative** | rose, daylily, dandelion | no harvest, positive Beauty |
| **Utility** | tinctoria (dye) | harvest that is none of the above |

Order matters — the tests run most specific first, so a plant answering to two categories is
filed under the one a colony would actually plant it for.

---

## `IsNutritionGivingIngestible` is not "is food"

The trap that started this. **Psychoid leaves give nutrition**, so a director filtering food
crops on that test plants psychoid. A colony did exactly that — seventy-two cells of it as
insurance against blight — then lost its rice to blight and starved beside the field.

**`IsDrug` does not catch it either.** `PsychoidLeaves` carries no `drugCategory` at all. It is
the *ingredient* a drug is made from, not the drug. Same for `SmokeleafLeaves` and `RawHops`.

What they actually say is `preferability: DesperateOnly` — the game stating that nobody eats
this unless they are dying. Real crops inherit `RawBad` or better from `PlantFoodRawBase`.

---

## Beer is not made by a recipe

Worth its own heading, because it defeats the obvious approach.

The natural test for "is this a drug crop" is to walk `DefDatabase<RecipeDef>` for a recipe that
consumes the harvest and produces something `IsDrug`. That classifies psychoid and smokeleaf
correctly and **fails on hops**, twice over:

- `Make_Wort` consumes hops but produces **wort**, which is not a drug.
- Wort becomes beer inside `Building_FermentingBarrel` — a `thingClass`, in code. There is **no
  def edge at all** from wort to beer. No recipe walk, transitive or otherwise, can reach it.

So a recipe-graph test will call hops animal feed for ever.

## What separates hay from hops

Both harvest to something at `DesperateOnly`. The difference is written in the defs and means
exactly what is being asked:

| | thingCategories | `optimalityOffsetFeedingAnimals` |
|---|---|---|
| `Hay` | `Foods` | **7** |
| `RawHops`, `PsychoidLeaves`, `SmokeleafLeaves` | `PlantMatter` | none |

That offset exists to tell a hauler what to put in a trough, which is precisely the question.
With it, fifteen of fifteen.

---

## Other traps

- **Sowable is not implied by anything else.** `Plant_Agarilux` is genuinely food by every test
  above and cannot be sown in a field — it is a wild cave plant. A colony planted seventy-two
  cells of it the moment a `Sowable` check was dropped during a tidy-up. Right category,
  unplantable crop.
- **`Plant_TreeCocoa` harvests chocolate and is still Wood.** A tree is timber whatever else it
  drops, because that is why one is planted. Chocolate also reads `DesperateOnly`, which would
  otherwise have filed it as a drug crop.
- **Healroot is `Plant_Healroot`, not `Plant_HealrootWild`.** The wild variant resolves fine and
  is not sowable, so `Thing("Plant_HealrootWild") ?? Thing("Plant_Healroot")` silently plants
  nothing for ever. `??` chooses on a def existing, never on it being usable.

---

## Implications for the director

- Cotton is the only textile a colony can have without research or a trader, and it grows in a
  season. Nothing else on the map produces cloth — see [[mood-and-labour]] for the colonies that
  froze in tribalwear while a tailor bench stood idle.
- Haygrass is the only answer to a pen that forages nothing in its lean season, which the pen
  report already names — see [[rimworld-pens]].
- A social crop is worth ground only if the colony can process it. Ask the recipe graph whether
  any *unlocked* recipe consumes the harvest; for beer that is `Make_Wort` and its Brewing
  prerequisite, which is reachable even though beer itself is not.
