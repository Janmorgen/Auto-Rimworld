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
                    var bill = new Bill_Production(recipe, null);
                    ConfigureBill(bill, desired);
                    table.billStack.AddBill(bill);
                    changed = true;
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
            bill.repeatMode = BillRepeatModeDefOf.TargetCount;
            bill.targetCount = target;
            bill.pauseWhenSatisfied = true;
            bill.unpauseWhenYouHave = System.Math.Max(1, target / 2);
            ((IRenameable)bill).RenamableLabel = OurTag + ": " + bill.recipe.label;
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
    }
}
