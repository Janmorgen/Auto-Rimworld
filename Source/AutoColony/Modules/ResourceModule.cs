using System.Collections.Generic;
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

        bool yieldedToEmergency;

        /// <summary>Designations added per pass, per activity.</summary>
        const int MaxPerPass = 25;

        /// <summary>How far from the base colonists are sent to gather.</summary>
        const int GatherRadius = 55;

        protected override void Act(DirectorContext ctx)
        {
            var origin = ctx.layout.established ? ctx.layout.origin : ctx.map.Center;

            // Something is burning or shooting at the colony. Sending people out to fell trees
            // or chase animals now spends the exact labour needed at home, and walks it out of
            // range of the emergency. Gathering waits.
            if (ctx.state.EmergencyAtHome)
            {
                if (!yieldedToEmergency)
                {
                    yieldedToEmergency = true;
                    Chronicle.Record(ChronicleCategory.Economy, string.Format(
                        "holding off gathering: {0} fires and {1} hostiles at the colony",
                        ctx.state.firesNearBase, ctx.state.hostilesNearBase));
                }
                return;
            }
            yieldedToEmergency = false;

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
        /// Hunts when food is short, choosing targets the colony can actually beat.
        ///
        /// Targets are considered safest-first, so easy game is taken while it lasts and
        /// dangerous game is only reached for once nothing else is left. Whether a given animal
        /// is worth fighting is a judgement about these colonists — their skills, health and
        /// weapons — against that animal, eased by how close the colony is to starving. A
        /// well-armed group will take a thrumbo; a comfortable one will not bother; a starving
        /// one with nothing else on the map will try regardless, because starving quietly is
        /// not the safer choice.
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

            // Desperation is mostly about how empty the larder is, nudged by how bold the
            // strategy is in general.
            float desperation = Clamp01(urgency * 0.8f + aggression * 0.2f);
            float strength = CombatAssessment.ColonyStrength(ctx.state);

            int radius = GatherRadius + (int)(urgency * 60f);
            int radiusSq = radius * radius;

            var map = ctx.map;
            var des = DesignationDefOf.Hunt;
            int budget = (int)(8 * effective) + 1;

            // Gather candidates first so they can be taken in order of danger rather than in
            // whatever order the map happens to list them.
            candidates.Clear();
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var animal = pawns[i];
                if (animal == null || animal.Dead || !animal.RaceProps.Animal) continue;
                if (animal.Faction != null) continue;
                if (animal.RaceProps.foodType == FoodTypeFlags.None) continue;
                if (map.designationManager.DesignationOn(animal, des) != null) continue;
                if ((animal.Position - origin).LengthHorizontalSquared > radiusSq) continue;
                candidates.Add(animal);
            }

            candidates.Sort((a, b) => ThreatOf(a).CompareTo(ThreatOf(b)));

            taken.Clear();
            declined.Clear();
            largestDeclined = 0f;
            int done = 0;

            for (int i = 0; i < candidates.Count && done < budget; i++)
            {
                var animal = candidates[i];
                float threat = FightsBack(animal) ? CombatAssessment.ThreatValue(animal) : 0f;

                if (!CombatAssessment.ShouldEngage(strength, threat, desperation))
                {
                    Tally(declined, animal);
                    continue;
                }

                map.designationManager.AddDesignation(new Designation(animal, des));
                Tally(taken, animal);
                done++;
            }

            // Last resort. If the colony is out of food and every animal on the map is one it
            // would rather not fight, it fights anyway: refusing is not survival, it is just a
            // slower way to lose. The least dangerous target is taken, since that is the fight
            // most likely to be survived.
            if (done == 0 && desperation > 0.85f && candidates.Count > 0)
            {
                var lastResort = candidates[0];
                map.designationManager.AddDesignation(new Designation(lastResort, des));
                done++;
                Chronicle.Record(ChronicleCategory.Hunt, string.Format(
                    "LAST RESORT: nothing safe to hunt and {0:0.0} days of food left, so taking on {1} " +
                    "(threat {2:0}) with strength {3:0}",
                    daysOfFood, lastResort.LabelShortCap, ThreatOf(lastResort), strength));
                return done;
            }

            // One line per pass rather than one per animal. The same elephant gets refused on
            // every sweep, and a log that repeats itself is a log nobody can read.
            if (taken.Count > 0 || declined.Count > 0)
            {
                Chronicle.Record(ChronicleCategory.Hunt, string.Format(
                    "{0:0.0} days of food — hunting {1}{2} [{3}]",
                    daysOfFood,
                    taken.Count > 0 ? Describe(taken) : "nothing",
                    declined.Count > 0 ? "; passed over " + Describe(declined) : "",
                    CombatAssessment.Explain(strength, LargestDeclined(), desperation)));
            }

            return done;
        }

        readonly List<Pawn> candidates = new List<Pawn>();
        readonly Dictionary<string, int> taken = new Dictionary<string, int>();
        readonly Dictionary<string, int> declined = new Dictionary<string, int>();
        float largestDeclined;

        static bool FightsBack(Pawn animal)
        {
            var race = animal.RaceProps;
            return race.predator || race.manhunterOnDamageChance > 0.05f;
        }

        static float ThreatOf(Pawn animal)
        {
            return FightsBack(animal) ? CombatAssessment.ThreatValue(animal) : 0f;
        }

        float LargestDeclined()
        {
            return largestDeclined;
        }

        void Tally(Dictionary<string, int> into, Pawn animal)
        {
            float power = animal.kindDef != null ? animal.kindDef.combatPower : 0f;
            if (into == declined && power > largestDeclined) largestDeclined = power;

            string key = animal.LabelShortCap + " (" + power.ToString("0") + ")";
            int n;
            into.TryGetValue(key, out n);
            into[key] = n + 1;
        }

        static string Describe(Dictionary<string, int> tally)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in tally)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(kv.Key);
                if (kv.Value > 1) sb.Append(" x").Append(kv.Value);
            }
            return sb.ToString();
        }

        static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }
    }
}
