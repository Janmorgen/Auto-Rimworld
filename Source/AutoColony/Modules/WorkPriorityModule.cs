using System.Collections.Generic;
using AutoColony.Learning;
using RimWorld;
using Verse;

namespace AutoColony.Modules
{
    /// <summary>
    /// Assigns every colonist's work priorities.
    ///
    /// This is the single highest-leverage thing a RimWorld player does, so it is also where
    /// most of the learnable signal lives. A colonist's fitness for a job combines three
    /// evolvable weights — raw skill, passion, and what the colony currently needs — plus a
    /// per-work-type weight the optimiser tunes independently, letting the strategy discover
    /// things like "in this biome, hunting deserves more hands than crafting".
    /// </summary>
    public class WorkPriorityModule : DirectorModule
    {
        public override string Name { get { return "Work priorities"; } }

        // Roughly every three in-game hours: often enough to react to a crisis, rare enough
        // that colonists are not constantly abandoning half-finished jobs.
        public override int IntervalTicks { get { return 7500; } }

        readonly Dictionary<string, float> needs = new Dictionary<string, float>();

        protected override void Act(DirectorContext ctx)
        {
            if (Current.Game != null && Current.Game.playSettings != null)
                Current.Game.playSettings.useWorkPriorities = true;

            ComputeNeeds(ctx);

            var workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            int assigned = 0;

            for (int i = 0; i < ctx.state.allColonists.Count; i++)
            {
                if (AssignFor(ctx.state.allColonists[i], workTypes, ctx)) assigned++;
            }

            if (assigned > 0) Note("re-prioritised " + assigned + " colonists");
        }

        /// <summary>
        /// Multiplier per work type reflecting what the colony is short of right now.
        /// Values above 1 pull colonists toward that work; below 1 push them away.
        /// </summary>
        void ComputeNeeds(DirectorContext ctx)
        {
            needs.Clear();
            var s = ctx.state;

            float foodTarget = ctx.Gene(Genes.FoodDaysPerColonist);
            float foodShortfall = foodTarget > 0f ? Clamp01(1f - s.daysOfFood / foodTarget) : 0f;

            // Emergencies first: fire and untreated casualties outrank everything.
            Need("Firefighter", s.fires > 0 ? 6f : 1f);
            Need("Patient", 1f);
            Need("PatientBedRest", 1f);
            Need("Doctor", s.colonistsDowned > 0 ? 4f : (s.avgHealth < 0.9f ? 2f : 1f));

            Need("Growing", 1f + foodShortfall * 2f);
            Need("Hunting", 1f + foodShortfall * 2f * ctx.Gene(Genes.HuntAggression));
            Need("Cooking", 1f + foodShortfall * 1.5f);

            Need("Construction", s.pendingBlueprints + s.pendingFrames > 0 ? 2.2f : 0.8f);

            Need("Mining", 1f + Shortfall(s.steel, ctx.Gene(Genes.SteelTarget)) * 1.5f
                              * (0.5f + ctx.Gene(Genes.MiningAggression)));
            Need("PlantCutting", 1f + Shortfall(s.wood, ctx.Gene(Genes.WoodTarget)) * 1.5f
                                   * (0.5f + ctx.Gene(Genes.ChopAggression)));

            Need("Research", s.hasResearchBench ? 1.2f : 0.3f);
            Need("Warden", s.prisoners > 0 ? 2f : 0.2f);
            Need("Handling", 1f);
            Need("Cleaning", s.avgMood < 0.6f ? 1.6f : 0.9f);
            Need("Hauling", 1.1f);
            Need("Smithing", 1f);
            Need("Tailoring", 1f + Shortfall(s.textiles, ctx.Gene(Genes.TextilesTarget)) * 0.5f);
            Need("Crafting", 1f);
            Need("Art", s.avgMood < 0.5f ? 1.3f : 0.7f);
        }

        void Need(string defName, float value)
        {
            needs[defName] = value;
        }

        float NeedFor(string defName)
        {
            float v;
            return needs.TryGetValue(defName, out v) ? v : 1f;
        }

        static float Shortfall(int have, float target)
        {
            if (target <= 0f) return 0f;
            return Clamp01(1f - have / target);
        }

        static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        bool AssignFor(Pawn pawn, List<WorkTypeDef> workTypes, DirectorContext ctx)
        {
            if (pawn == null || pawn.Dead) return false;
            if (pawn.workSettings == null) return false;
            // Initialise before reading anything: the priority table does not exist until then.
            if (!pawn.workSettings.Initialized) pawn.workSettings.EnableAndInitialize();
            if (!pawn.workSettings.EverWork) return false;

            float skillW = ctx.Gene(Genes.WorkSkillWeight);
            float passionW = ctx.Gene(Genes.WorkPassionWeight);
            float needW = ctx.Gene(Genes.WorkNeedWeight);
            float spread = ctx.Gene(Genes.WorkSpread);
            int bands = Clamp(ctx.GeneInt(Genes.WorkBands), 1, 4);

            var scored = new List<KeyValuePair<WorkTypeDef, float>>();

            for (int i = 0; i < workTypes.Count; i++)
            {
                var wt = workTypes[i];
                if (wt == null || !wt.visible) continue;
                if (pawn.WorkTypeIsDisabled(wt)) continue;

                float skill = 0.5f;
                float passion = 0f;
                if (pawn.skills != null)
                {
                    skill = pawn.skills.AverageOfRelevantSkillsFor(wt) / 20f;
                    var p = pawn.skills.MaxPassionOfRelevantSkillsFor(wt);
                    passion = p == Passion.Major ? 1f : (p == Passion.Minor ? 0.5f : 0f);
                }

                float geneWeight = ctx.Gene(Genes.WorkKey(wt.defName));
                float need = NeedFor(wt.defName);

                // Gene weight scales the whole term, so a work type can be suppressed entirely
                // if the optimiser finds that useful; the other factors shape the ordering.
                float score = geneWeight * (0.4f
                                            + skillW * skill
                                            + passionW * passion
                                            + needW * (need - 1f));

                scored.Add(new KeyValuePair<WorkTypeDef, float>(wt, score));
            }

            if (scored.Count == 0) return false;
            scored.Sort((a, b) => b.Value.CompareTo(a.Value));

            // How many work types this colonist takes on at all. Always at least a few, so a
            // small colony never ends up with nobody willing to haul or cook.
            int assignCount = Round(scored.Count * (0.4f + 0.6f * spread));
            if (assignCount < 4) assignCount = 4;
            if (assignCount > scored.Count) assignCount = scored.Count;

            for (int r = 0; r < scored.Count; r++)
            {
                var wt = scored[r].Key;
                int priority;

                if (r < assignCount)
                {
                    // Spread ranks across the available bands: best work gets priority 1.
                    priority = 1 + (r * bands) / assignCount;
                    if (priority > 4) priority = 4;
                }
                else
                {
                    priority = 0;
                }

                // Emergency work is never switched off, whatever the genome says.
                if (wt.alwaysStartActive && priority == 0) priority = 4;
                if (NeedFor(wt.defName) >= 4f && priority == 0) priority = 2;

                if (pawn.workSettings.GetPriority(wt) != priority)
                    pawn.workSettings.SetPriority(wt, priority);
            }

            return true;
        }

        static int Clamp(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        static int Round(float f)
        {
            return (int)(f + 0.5f);
        }
    }
}
