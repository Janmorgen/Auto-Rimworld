using AutoColony.Learning;
using RimWorld;
using Verse;

namespace AutoColony.Modules
{
    /// <summary>
    /// Issues the standing orders that feed the colony's material supply: chopping wood,
    /// mining ore, and hunting.
    ///
    /// Each activity is gated on a stock target from the genome and scaled by its own
    /// aggression gene, so the optimiser can learn a materially different economy — a
    /// wood-poor tundra colony that mines hard, or a forest colony that barely mines at all.
    /// </summary>
    public class ResourceModule : DirectorModule
    {
        public override string Name { get { return "Resource gathering"; } }
        public override int IntervalTicks { get { return 12500; } }

        /// <summary>Designations added per pass, per activity.</summary>
        const int MaxPerPass = 25;

        /// <summary>How far from the base colonists are sent to gather.</summary>
        const int GatherRadius = 55;

        protected override void Act(DirectorContext ctx)
        {
            var origin = ctx.layout.established ? ctx.layout.origin : ctx.map.Center;

            int chopped = MaybeChopWood(ctx, origin);
            int mined = MaybeMine(ctx, origin);
            int hunted = MaybeHunt(ctx, origin);

            if (chopped + mined + hunted > 0)
                Note("designated " + chopped + " trees, " + mined + " rock, " + hunted + " animals");
        }

        int MaybeChopWood(DirectorContext ctx, IntVec3 origin)
        {
            float target = ctx.Gene(Genes.WoodTarget);
            if (ctx.state.wood >= target) return 0;

            float aggression = ctx.Gene(Genes.ChopAggression);
            int budget = (int)(MaxPerPass * (0.2f + aggression));
            if (budget <= 0) return 0;

            var map = ctx.map;
            var des = DesignationDefOf.HarvestPlant;
            int done = 0;

            foreach (var cell in GenRadial.RadialCellsAround(origin, GatherRadius, true))
            {
                if (done >= budget) break;
                if (!cell.InBounds(map)) continue;

                var plant = cell.GetPlant(map);
                if (plant == null || !plant.def.plant.IsTree) continue;
                if (plant.def.plant.harvestedThingDef != ThingDefOf.WoodLog) continue;
                if (map.designationManager.DesignationOn(plant, des) != null) continue;
                if (plant.Growth < 0.4f) continue;

                map.designationManager.AddDesignation(new Designation(plant, des));
                done++;
            }

            return done;
        }

        int MaybeMine(DirectorContext ctx, IntVec3 origin)
        {
            float target = ctx.Gene(Genes.SteelTarget);
            bool needSteel = ctx.state.steel < target;
            bool needComponents = ctx.state.components < ctx.Gene(Genes.ComponentsTarget);
            if (!needSteel && !needComponents) return 0;

            float aggression = ctx.Gene(Genes.MiningAggression);
            int budget = (int)(MaxPerPass * (0.2f + aggression));
            if (budget <= 0) return 0;

            var map = ctx.map;
            var des = DesignationDefOf.Mine;
            int done = 0;

            foreach (var cell in GenRadial.RadialCellsAround(origin, GatherRadius, true))
            {
                if (done >= budget) break;
                if (!cell.InBounds(map)) continue;
                if (map.designationManager.DesignationAt(cell, des) != null) continue;

                var edifice = cell.GetEdifice(map);
                if (edifice == null || !edifice.def.mineable) continue;

                var yield = edifice.def.building != null ? edifice.def.building.mineableThing : null;
                if (yield == null) continue;

                bool wanted = (needSteel && yield == ThingDefOf.Steel)
                           || (needComponents && yield == ThingDefOf.ComponentIndustrial);
                if (!wanted) continue;

                // Only mine what colonists can actually reach without tunnelling blind.
                if (!HasOpenNeighbour(map, cell)) continue;

                map.designationManager.AddDesignation(new Designation(cell, des));
                done++;
            }

            return done;
        }

        static bool HasOpenNeighbour(Map map, IntVec3 cell)
        {
            for (int i = 0; i < 4; i++)
            {
                var n = cell + GenAdj.CardinalDirections[i];
                if (n.InBounds(map) && n.Walkable(map)) return true;
            }
            return false;
        }

        /// <summary>
        /// Hunts when food is short, and hunts harder the shorter it gets.
        ///
        /// A fixed radius and a fixed risk tolerance are wrong at both ends. Observed in-game:
        /// a desert colony with crops planted but weeks from harvest sat on a starvation alert
        /// while this designated zero animals two passes running — everything nearby was either
        /// out of range or filtered out as dangerous. A colony with a full larder can afford to
        /// be picky about muffalo; a starving one cannot, and walking further is free by
        /// comparison to not eating.
        /// </summary>
        int MaybeHunt(DirectorContext ctx, IntVec3 origin)
        {
            float foodTarget = ctx.Gene(Genes.FoodDaysPerColonist);
            float daysOfFood = ctx.state.daysOfFood;
            if (daysOfFood >= foodTarget) return 0;

            // 0 when comfortably stocked, 1 when the larder is empty.
            float urgency = foodTarget > 0f ? Clamp01(1f - daysOfFood / foodTarget) : 1f;

            float aggression = ctx.Gene(Genes.HuntAggression);
            float effective = Clamp01(aggression + urgency * 0.5f);
            if (effective < 0.15f) return 0;

            // Range the colony will walk for a meal, widening as the situation worsens.
            int radius = GatherRadius + (int)(urgency * 60f);
            int radiusSq = radius * radius;

            var map = ctx.map;
            var des = DesignationDefOf.Hunt;
            int budget = (int)(8 * effective) + 1;
            int done = 0;

            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count && done < budget; i++)
            {
                var animal = pawns[i];
                if (animal == null || animal.Dead || !animal.RaceProps.Animal) continue;
                if (animal.Faction != null) continue;
                if (animal.RaceProps.foodType == FoodTypeFlags.None) continue;
                if (map.designationManager.DesignationOn(animal, des) != null) continue;
                if ((animal.Position - origin).LengthHorizontalSquared > radiusSq) continue;

                // Dangerous game is a last resort, not a permanent no.
                bool dangerous = animal.RaceProps.manhunterOnDamageChance > 0.25f;
                if (dangerous && effective < 0.7f) continue;

                map.designationManager.AddDesignation(new Designation(animal, des));
                done++;
            }

            return done;
        }

        static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }
    }
}
