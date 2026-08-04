using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoColony.Plants
{
    /// <summary>
    /// What a plant is *for*, from the colony's point of view.
    ///
    /// The director had one category — "food" — and tested for it with
    /// <c>IsNutritionGivingIngestible</c>. That let psychoid through, because psychoid leaves
    /// give nutrition; a colony planted seventy-two cells of it as insurance against blight,
    /// lost its rice to blight, and starved beside a full field of something nobody will eat
    /// unless they are dying. Adding "and not a drug" did not fix it either — psychoid leaves
    /// carry no drugCategory, being the ingredient a drug is made from rather than the drug.
    ///
    /// Two categories cannot describe a system with six. Everything below is derived from the
    /// game's own data rather than from a list of names, so mods and DLC crops classify without
    /// anyone updating a table.
    /// </summary>
    public enum PlantRole
    {
        /// <summary>Nothing the colony would deliberately sow.</summary>
        None,

        /// <summary>Dinner. Rice, corn, potatoes, strawberries.</summary>
        Food,

        /// <summary>Cloth on the loom. Cotton, devilstrand.</summary>
        Textile,

        /// <summary>Medicine. Healroot.</summary>
        Medicine,

        /// <summary>Animal feed. Haygrass — eaten in the pen, or made into kibble.</summary>
        Fodder,

        /// <summary>Mood, by way of a still or a lab. Psychoid, smokeleaf, hops.</summary>
        Social,

        /// <summary>Timber. Every tree.</summary>
        Wood,

        /// <summary>Beauty and nothing else. Daylilies, roses, dandelions.</summary>
        Decorative,

        /// <summary>Useful, but none of the above — dye, for instance.</summary>
        Utility
    }

    public static class PlantTaxonomy
    {
        static readonly Dictionary<ushort, PlantRole> cache = new Dictionary<ushort, PlantRole>();
        static readonly Dictionary<ushort, bool> drugIngredientCache = new Dictionary<ushort, bool>();

        /// <summary>
        /// What this plant is for. Order matters: the tests run from most specific to least, so
        /// a plant that could answer to two categories is filed under the one the colony would
        /// actually plant it for. Devilstrand is ingestible by nothing and makes cloth, so it is
        /// Textile; hay is ingestible and makes no cloth, so it is Fodder.
        /// </summary>
        public static PlantRole RoleOf(ThingDef plant)
        {
            if (plant == null || plant.plant == null) return PlantRole.None;

            PlantRole known;
            if (cache.TryGetValue(plant.shortHash, out known)) return known;

            var role = Classify(plant);
            cache[plant.shortHash] = role;
            return role;
        }

        static PlantRole Classify(ThingDef plant)
        {
            var harvest = plant.plant.harvestedThingDef;

            // A tree is timber whatever else it drops, because that is why one is planted.
            if (plant.plant.IsTree) return PlantRole.Wood;

            // Nothing to take off it. The game still gives it a beauty stat, which is the whole
            // reason it exists.
            if (harvest == null)
                return plant.GetStatValueAbstract(StatDefOf.Beauty) > 0f
                    ? PlantRole.Decorative
                    : PlantRole.None;

            if (harvest.IsMedicine) return PlantRole.Medicine;

            // Cloth is a stuff category, not an item list — devilstrand and cotton both answer
            // here, and so does anything a mod adds that weaves.
            if (harvest.IsStuff && harvest.stuffProps != null &&
                Shares(harvest.stuffProps.categories, StuffCategoryDefOf.Fabric))
                return PlantRole.Textile;

            // Drugs before food, because their ingredients give nutrition and would otherwise
            // be filed as dinner. Asked as "does anything drinkable or smokable come out of
            // this", which is what psychoid, smokeleaf and hops have in common and what hay
            // does not.
            if (harvest.IsDrug || FeedsADrug(harvest)) return PlantRole.Social;

            if (harvest.ingestible != null)
            {
                // What the game says a colonist will eat. RawBad is where real crops start;
                // hay and the drug leaves all sit below it.
                if (harvest.ingestible.preferability >= FoodPreferability.RawBad)
                    return PlantRole.Food;

                // Below that, "nobody will eat this" describes four different things — hay,
                // hops, psychoid and smokeleaf — and only one of them is animal feed. The game
                // says which: hay carries optimalityOffsetFeedingAnimals 7 and sits in the Foods
                // category, while the drug leaves carry no such bonus and sit in PlantMatter.
                // That offset exists precisely to tell a hauler what to put in a trough.
                if (harvest.ingestible.optimalityOffsetFeedingAnimals > 0f)
                    return PlantRole.Fodder;

                // Nutrition nobody will eat and no animal is steered towards: it is grown to be
                // processed into something else. In vanilla that is exactly the three drug
                // crops, and hops needed this — beer is not made by any recipe at all. It goes
                // hops, Make_Wort, wort, and then a fermenting barrel turns wort into beer in
                // code, with no def edge to follow. A recipe walk can never reach it.
                return PlantRole.Social;
            }

            return PlantRole.Utility;
        }

        /// <summary>
        /// Whether anything made from this ends up a drug.
        ///
        /// This is the test that separates hops from hay. Both harvest to something colonists
        /// will only eat when desperate; one of them becomes beer. Walking the recipe graph
        /// answers it from the game's own data, so a modded brew classifies correctly without
        /// anybody adding it to a list.
        /// </summary>
        static bool FeedsADrug(ThingDef harvest)
        {
            bool known;
            if (drugIngredientCache.TryGetValue(harvest.shortHash, out known)) return known;

            bool feeds = false;
            var recipes = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int i = 0; i < recipes.Count && !feeds; i++)
            {
                var recipe = recipes[i];
                if (recipe.products == null || recipe.ingredients == null) continue;

                bool makesDrug = false;
                for (int p = 0; p < recipe.products.Count && !makesDrug; p++)
                {
                    var product = recipe.products[p];
                    if (product != null && product.thingDef != null && product.thingDef.IsDrug)
                        makesDrug = true;
                }
                if (!makesDrug) continue;

                for (int g = 0; g < recipe.ingredients.Count && !feeds; g++)
                {
                    var ingredient = recipe.ingredients[g];
                    if (ingredient == null || ingredient.filter == null) continue;
                    if (ingredient.filter.Allows(harvest)) feeds = true;
                }
            }

            drugIngredientCache[harvest.shortHash] = feeds;
            return feeds;
        }

        static bool Shares(List<StuffCategoryDef> categories, StuffCategoryDef wanted)
        {
            if (categories == null || wanted == null) return false;
            for (int i = 0; i < categories.Count; i++)
                if (categories[i] == wanted) return true;
            return false;
        }

        /// <summary>Every plant the colony could sow, for reporting and for choosing one.</summary>
        public static List<ThingDef> Sowable()
        {
            var found = new List<ThingDef>();
            var all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                if (def.plant != null && def.plant.Sowable) found.Add(def);
            }
            return found;
        }
    }
}
