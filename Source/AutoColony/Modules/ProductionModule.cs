using System.Collections.Generic;
using AutoColony.Learning;
using RimWorld;
using Verse;

namespace AutoColony.Modules
{
    /// <summary>
    /// Maintains standing production bills on every work table.
    ///
    /// Rather than hardcoding a recipe list, this walks each table's own recipes and keeps a
    /// bill only when the recipe's product is something the strategy has a stock target for.
    /// That keeps it working with modded benches and recipes, and stops the colony from
    /// burning materials on things nobody asked for.
    /// </summary>
    public class ProductionModule : DirectorModule
    {
        public override string Name { get { return "Production"; } }
        public override int IntervalTicks { get { return 10000; } }

        protected override void Act(DirectorContext ctx)
        {
            var lister = ctx.map.listerBuildings;
            if (lister == null) return;

            int touched = 0;
            foreach (var table in lister.AllBuildingsColonistOfClass<Building_WorkTable>())
            {
                if (table == null || table.billStack == null) continue;
                if (ManageTable(table, ctx)) touched++;
            }

            if (touched > 0) Note("adjusted bills on " + touched + " work tables");
        }

        bool ManageTable(Building_WorkTable table, DirectorContext ctx)
        {
            var recipes = table.def.AllRecipes;
            if (recipes == null) return false;

            bool changed = false;

            for (int i = 0; i < recipes.Count; i++)
            {
                var recipe = recipes[i];
                if (recipe == null || !recipe.AvailableNow) continue;
                if (!recipe.AvailableOnNow(table)) continue;

                int desired = DesiredCount(recipe, ctx);
                var existing = FindBill(table.billStack, recipe);

                if (desired <= 0)
                {
                    // Stop producing things that are no longer wanted, but never touch bills
                    // the player added by hand.
                    if (existing != null && IsOurs(existing))
                    {
                        table.billStack.Delete(existing);
                        changed = true;
                    }
                    continue;
                }

                if (existing == null)
                {
                    // The recipe decides what class of bill it needs, so let it.
                    //
                    // Anything made in stages — all apparel, all smithing — carries an
                    // `unfinishedThingDef`, and RimWorld's own crafting toil casts the bill to
                    // `Bill_ProductionWithUft` without checking. Handed a plain `Bill_Production`
                    // it throws `InvalidCastException` the moment a colonist starts work, so the
                    // job dies, the garment is never made, and it repeats for as long as the bill
                    // stands. `MakeNewBill` returns whichever subclass the recipe requires.
                    //
                    // Watched live: five apparel bills on an electric tailoring bench, and every
                    // attempt to work any of them threw. It is the first exception this mod has
                    // produced in ten colonies, and it hid behind the fault isolation — the throw
                    // happens inside the pawn's job driver, not inside a director module, so
                    // nothing was disabled and nothing was reported.
                    //
                    // It also explains a great deal more than a crash. No clothing could ever be
                    // made, so "Clothe the colony" could never be satisfied, so the short-term
                    // horizon never cleared — which is half of why no colony has ever reached a
                    // long-term goal and why Research and Defense score 0.00 in every epoch.
                    var bill = recipe.MakeNewBill() as Bill_Production;
                    if (bill == null) continue;
                    ConfigureBill(bill, desired);
                    PreferInsulatingStuff(bill, recipe, ctx);
                    table.billStack.AddBill(bill);
                    changed = true;

                    // Said out loud. What the colony has standing orders to make was logged only
                    // at verbose level, so across thirty-odd observed colonies not one line of
                    // production ever appeared — and "the butcher bill paused itself after one
                    // deer" was invisible for exactly that reason.
                    Chronicle.Record(ChronicleCategory.Economy, string.Format(
                        "standing order added: {0} on the {1}{2}",
                        recipe.label ?? recipe.defName,
                        table.def.label ?? table.def.defName,
                        ConsumesCorpses(recipe)
                            ? " — run continuously, since a corpse left alone is worth nothing"
                            : " up to " + desired));
                }
                else if (IsOurs(existing) && existing.targetCount != desired)
                {
                    existing.targetCount = desired;
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>
        /// Marker written into the bill's renameable label, so bills the player created or
        /// edited by hand are recognised and never overwritten.
        /// </summary>
        const string OurTag = "AutoColony";

        static bool IsOurs(Bill_Production bill)
        {
            var label = ((IRenameable)bill).RenamableLabel;
            return label != null && label.StartsWith(OurTag);
        }

        static void ConfigureBill(Bill_Production bill, int target)
        {
            // Anything made from something that rots is run until the input is gone, not until
            // the output reaches a number.
            //
            // Butchering was target-counted on meat: three colonists gave a target of fifteen,
            // and one deer yields far more than that — so the bill satisfied itself on the first
            // corpse and paused, and every animal killed after that lay where it fell and
            // rotted. The colony then read its larder as empty, hunted again, and produced
            // another corpse it had already decided it did not need. Hunting without end and
            // nothing to show for it, which is exactly what it looked like from outside.
            //
            // A corpse is not stock. It is a perishable input that is worth nothing tomorrow,
            // so the only sensible instruction is "process what is there".
            if (ConsumesCorpses(bill.recipe))
            {
                bill.repeatMode = BillRepeatModeDefOf.Forever;
                bill.pauseWhenSatisfied = false;
                ((IRenameable)bill).RenamableLabel = OurTag + ": " + bill.recipe.label;
                return;
            }

            bill.repeatMode = BillRepeatModeDefOf.TargetCount;
            bill.targetCount = target;
            bill.pauseWhenSatisfied = true;
            bill.unpauseWhenYouHave = System.Math.Max(1, target / 2);
            ((IRenameable)bill).RenamableLabel = OurTag + ": " + bill.recipe.label;
        }

        /// <summary>
        /// Whether the recipe eats something that will spoil if left alone.
        ///
        /// Read off the recipe's own ingredient filter rather than by matching def names, so
        /// modded butchery and any other corpse-consuming work behaves the same.
        /// </summary>
        static bool ConsumesCorpses(RecipeDef recipe)
        {
            if (recipe == null) return false;
            if (recipe.defName != null && recipe.defName.StartsWith("ButcherCorpse")) return true;

            try
            {
                var filter = recipe.fixedIngredientFilter;
                if (filter == null) return false;

                foreach (var def in filter.AllowedThingDefs)
                    if (def != null && def.IsCorpse) return true;
            }
            catch (System.Exception) { }
            return false;
        }

        /// <summary>
        /// Narrows a clothing bill to the materials that actually insulate.
        ///
        /// The same parka is a different garment depending on what it is made of — wool and the
        /// heavier furs insulate several times better than plain cloth, and against heat the
        /// ordering is its own, so the material is not a detail of the recipe but half the
        /// answer to the weather.
        ///
        /// Applied only when the colony holds something clearly better than the rest, and only
        /// as a restriction on stuff the colony *has*. A filter that allowed only a material
        /// nobody owns is a bill that never runs, which is worse than a cloth parka — the whole
        /// point is that the colonist gets a coat before winter, not the best possible coat.
        /// </summary>
        static void PreferInsulatingStuff(Bill_Production bill, RecipeDef recipe, DirectorContext ctx)
        {
            var product = recipe.products != null && recipe.products.Count > 0
                ? recipe.products[0].thingDef : null;
            if (product == null || !product.IsApparel) return;
            if (bill.ingredientFilter == null) return;

            bool wantWarm = ctx.state.coldShortfall > 0f;
            bool wantCool = ctx.state.heatExcess > 0f;
            if (!wantWarm && !wantCool) return;

            var stat = wantWarm
                ? StatDefOf.StuffPower_Insulation_Cold
                : StatDefOf.StuffPower_Insulation_Heat;

            // What the colony actually holds, ranked by how well it insulates.
            ThingDef best = null;
            float bestValue = 0f;
            var counter = ctx.map.resourceCounter;

            var all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                var stuff = all[i];
                if (stuff.stuffProps == null) continue;
                if (!recipe.fixedIngredientFilter.Allows(stuff)) continue;
                if (counter != null && counter.GetCount(stuff) < 40) continue;

                float value = SafeStat(stuff, stat);
                if (value > bestValue) { bestValue = value; best = stuff; }
            }

            // Nothing notable in store: leave the bill unrestricted so it can still run.
            if (best == null || bestValue <= 0f) return;

            bill.ingredientFilter.SetDisallowAll();
            bill.ingredientFilter.SetAllow(best, true);

            Chronicle.Record(ChronicleCategory.Economy, string.Format(
                "{0} will be made from {1} — best {2} insulation the colony holds",
                product.label ?? product.defName, best.label ?? best.defName,
                wantWarm ? "cold" : "heat"));
        }

        static Bill_Production FindBill(BillStack stack, RecipeDef recipe)
        {
            for (int i = 0; i < stack.Count; i++)
            {
                var bp = stack[i] as Bill_Production;
                if (bp != null && bp.recipe == recipe) return bp;
            }
            return null;
        }

        /// <summary>
        /// How many of a recipe's product the colony wants on hand. Returns 0 for anything the
        /// strategy has no target for, which is what keeps this from producing indiscriminately.
        /// </summary>
        int DesiredCount(RecipeDef recipe, DirectorContext ctx)
        {
            // Butchering and similar corpse-consuming recipes have no meaningful stock target;
            // run them continuously while there is something to process.
            if (recipe.defName.StartsWith("ButcherCorpse"))
                return ctx.state.colonists * 5;

            if (recipe.products == null || recipe.products.Count == 0) return 0;

            var product = recipe.products[0].thingDef;
            if (product == null) return 0;

            int perBatch = System.Math.Max(1, recipe.products[0].count);
            float buffer = ctx.Gene(Genes.ProductionBuffer);
            int colonists = System.Math.Max(1, ctx.state.colonists);

            float target = 0f;

            // Cooked meals: the single most important standing order in any colony.
            if (product.IsNutritionGivingIngestible && product.ingestible != null &&
                product.ingestible.preferability >= FoodPreferability.MealAwful)
            {
                target = colonists * ctx.Gene(Genes.MealsPerColonist);
            }
            else if (product.IsMedicine)
            {
                target = colonists * ctx.Gene(Genes.MedicinePerColonist);
            }
            else if (product == ThingDefOf.ComponentIndustrial)
            {
                target = ctx.Gene(Genes.ComponentsTarget);
            }
            else if (product == AcDefs.Cloth)
            {
                target = ctx.Gene(Genes.TextilesTarget);
            }
            else if (product == AcDefs.PsychiteTea)
            {
                // The labour-free mood lever. Two cups a head is an evening's comfort in the
                // cupboard, not a habit: consumption is gated by the drug policy to colonists
                // whose mood is already low, so stock beyond that is addiction risk on a shelf.
                target = colonists * 2f;
            }
            else if (product.IsApparel)
            {
                // Clothes were never made at all: apparel fell through to the default and
                // returned zero, so a tailor bench would have sat idle even where one existed.
                // ApparelDamaged, SoakingWet and EnvironmentCold recurred in every run's
                // unfixable list as a direct result.
                target = ApparelWanted(product, ctx, colonists);
            }
            else if (IsStoneBlock(product))
            {
                // Blocks are a building material; want them only while there is building to do.
                target = ctx.state.pendingBlueprints + ctx.state.pendingFrames > 0 ? 200f : 75f;
            }
            else
            {
                return 0;
            }

            int desired = (int)(target * buffer);
            // Bill target counts are in product units; a recipe making 5 at a time still
            // stockpiles to the same total, so no per-batch scaling is needed beyond a floor.
            if (desired < perBatch) desired = perBatch;
            return desired > 0 ? desired : 0;
        }

        static bool IsStoneBlock(ThingDef def)
        {
            return def.defName != null && def.defName.StartsWith("Blocks");
        }

        /// <summary>
        /// How many of a garment the colony wants, judged against the weather it is actually in.
        ///
        /// Clothing is the cheapest answer to temperature there is and the only one that travels
        /// with the colonist — a heater warms a room nobody can stay in all day, and there is no
        /// portable cooler at all. Which garment matters enormously and depends on direction: a
        /// parka is the most insulating thing in the game and useless in a heat wave, where a
        /// duster covers the most skin and helps against sun.
        ///
        /// Everyone gets one of whatever the weather calls for, plus a spare, because apparel
        /// wears out and a damaged garment is its own standing mood penalty.
        /// </summary>
        static float ApparelWanted(ThingDef product, DirectorContext ctx, int colonists)
        {
            // The cold that matters is the cold the wardrobe has to be ready for, not the cold
            // outside the window. Run 195 read the window: ComfortableMin is 16C, day 21 came in
            // at 15C, so this branch was false every pass of the first three weeks and true for
            // the first time on the morning RimWorld raised "Need warm clothes" — fourteen
            // growing days from a fifteen-day barren quadrum, with one parka to its name.
            //
            // The comment below used to carry the assumption that made that safe: "winter
            // arrives eventually and a parka takes two minutes of work to make". Two minutes is
            // the bench time for one garment by one tailor who is standing at the bench with the
            // cloth already there. It is not the time to clothe a colony, and it was doing the
            // work of a deadline without being measured against one.
            float cold = ctx.state.coldShortfall;
            if (ctx.state.coldShortfallComing > cold &&
                ctx.state.daysUntilCold <= ctx.Gene(Genes.ProductionColdLeadDays))
                cold = ctx.state.coldShortfallComing;

            float heat = ctx.state.heatExcess;

            float coldInsulation = SafeStat(product, StatDefOf.Insulation_Cold);
            float heatInsulation = SafeStat(product, StatDefOf.Insulation_Heat);

            // Neither use against the weather and no armour worth the materials: skip it rather
            // than filling the stockpile with tribalwear nobody needs.
            bool warmth = coldInsulation > 4f;
            bool cooling = heatInsulation > 2f;

            if (cold > 0f && warmth) return colonists + 1;
            if (heat > 0f && cooling) return colonists + 1;

            // Basic cover, whatever the weather. Naked and tattered are both mood penalties in
            // their own right, quite apart from temperature.
            if (!warmth && !cooling) return colonists;

            // A garment for the season the colony is not in. Worth one, not a wardrobe — winter
            // arrives eventually and a parka takes two minutes of work to make.
            return 1f;
        }

        static float SafeStat(ThingDef def, StatDef stat)
        {
            try { return def.GetStatValueAbstract(stat); }
            catch (System.Exception) { return 0f; }
        }
    }
}
