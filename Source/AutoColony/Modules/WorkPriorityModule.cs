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
        /// <summary>
        /// The weights this module sets are the colony's whole answer to a casualty or a fire —
        /// Doctor to 4x, Firefighter to 6x — and applied three in-game hours after the event
        /// they answer nothing. This is the module where a fixed interval cost most.
        /// </summary>
        public override bool Urgent(DirectorContext ctx)
        {
            var s = ctx.state;
            return s.colonistsDowned > 0 || s.firesNearBase > 0 || s.hostilesNearBase > 0;
        }

        public override int IntervalTicks { get { return 7500; } }

        readonly Dictionary<string, float> needs = new Dictionary<string, float>();

        /// <summary>The best Construction level anybody in the colony has.</summary>
        int bestConstruction;

        /// <summary>
        /// Construction below this botches often enough to be a net loss.
        ///
        /// A failed build consumes the materials and produces nothing, so a poor builder is not
        /// merely slow — they are spending the colony's wood to make rubble. RimWorld scales
        /// ConstructionSuccessChance with the skill, and it is unforgiving at the bottom.
        /// </summary>
        const int ShakyBuilder = 4;

        void NoteTheBestBuilder(DirectorContext ctx)
        {
            bestConstruction = 0;
            var all = ctx.state.allColonists;
            for (int i = 0; i < all.Count; i++)
            {
                var pawn = all[i];
                if (pawn == null || pawn.skills == null) continue;
                var skill = pawn.skills.GetSkill(SkillDefOf.Construction);
                if (skill != null && !skill.TotallyDisabled && skill.Level > bestConstruction)
                    bestConstruction = skill.Level;
            }
        }

        /// <summary>
        /// How much to hold this colonist back from building, given who else could do it.
        ///
        /// Everything here scores a colonist against their *own* other options and never against
        /// the rest of the colony, so when blueprints are pending the need pushes Construction up
        /// everybody's list at once — including the person who cannot lay a wall without
        /// wasting it. Watched on two colonies in a row as "Construction botched" floating over
        /// a half-built room, on a map where the shelter was urgent and the wood was finite.
        ///
        /// A demotion rather than a ban. RimWorld works down the priority list, so a shaky
        /// builder still builds when the good one is asleep, hurt, or busy — which is exactly
        /// the fallback a three-person colony needs and a ban would remove.
        /// </summary>
        float BuilderPenalty(Pawn pawn, WorkTypeDef wt)
        {
            if (wt.defName != "Construction" || pawn.skills == null) return 1f;

            var skill = pawn.skills.GetSkill(SkillDefOf.Construction);
            if (skill == null || skill.TotallyDisabled) return 1f;

            // Somebody has to build. If the best in the colony is shaky too, this changes
            // nothing and the work still gets done by whoever is least bad at it.
            if (bestConstruction <= ShakyBuilder) return 1f;
            if (skill.Level >= ShakyBuilder) return 1f;

            return 0.45f;
        }

        protected override void Act(DirectorContext ctx)
        {
            if (Current.Game != null && Current.Game.playSettings != null)
                Current.Game.playSettings.useWorkPriorities = true;

            ComputeNeeds(ctx);
            NoteTheBestBuilder(ctx);

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
            // Against the food that will be left once a decision taken now could land, for the
            // same reason hunting escalates on it: growing, hunting and cooking all take time
            // the colony has to have started spending before the larder is actually empty.
            float foodShortfall = FoodTiming.Urgency(s.daysOfFood, foodTarget);

            // Emergencies first: fire and untreated casualties outrank everything.
            // Only fires that could reach the colony justify dropping everything; a distant
            // wildfire is not worth a work-hour.
            Need("Firefighter", s.firesNearBase > 0 ? 6f : 1f);
            Need("Patient", 1f);
            Need("PatientBedRest", 1f);
            Need("Doctor", s.colonistsDowned > 0 ? 4f : (s.avgHealth < 0.9f ? 2f : 1f));

            Need("Growing", 1f + foodShortfall * 2f);
            Need("Hunting", 1f + foodShortfall * 2f * ctx.Gene(Genes.HuntAggression));
            Need("Cooking", 1f + foodShortfall * 1.5f);

            Need("Construction", s.pendingBlueprints + s.pendingFrames > 0 ? 2.2f : 0.8f);

            // Against the plan's target as well as the genome's, the same way the resource
            // module designates against both. Otherwise the colony digs and chops for what the
            // goal needs while nobody is especially assigned to do it.
            Need("Mining", 1f + Shortfall(s.steel, Target(ctx, Genes.SteelTarget, "Steel")) * 1.5f
                              * (0.5f + ctx.Gene(Genes.MiningAggression)));
            Need("PlantCutting", 1f + Shortfall(s.wood, Target(ctx, Genes.WoodTarget, "WoodLog")) * 1.5f
                                   * (0.5f + ctx.Gene(Genes.ChopAggression)));

            Need("Research", s.hasResearchBench ? 1.2f : 0.3f);
            Need("Warden", s.prisoners > 0 ? 2f : 0.2f);
            Need("Handling", 1f);
            Need("Cleaning", s.avgMood < 0.6f ? 1.6f : 0.9f);
            // Items outdoors deteriorate wherever they are, and in a dry climate they are also
            // the easiest thing on the map to lose. Getting them into storage is preventative
            // rather than tidy, so it outranks ordinary hauling as the map dries out.
            float fireRisk = FireRisk.Assess(ctx.map, s);
            float outdoorPressure = s.itemsOutdoors > 0 ? AcMath.Clamp01(s.itemsOutdoors / 40f) : 0f;
            Need("Hauling", 1.1f + fireRisk * outdoorPressure * 2f);
            Need("Smithing", 1f);
            // Tailoring rose with the *cloth pile*, which says how much raw material is spare
            // and nothing about whether anyone is cold. The apparel work added a bench, a bill,
            // a material preference and a research prerequisite, and then left the last link
            // out: with nobody assigned to sew, all of that is a workbench with a queue on it.
            // Someone being underdressed is the thing that should pull a colonist to the bench.
            float underdressed = s.colonists > 0
                ? s.colonistsUnderdressed / (float)s.colonists
                : 0f;
            Need("Tailoring", 1f + Shortfall(s.textiles, ctx.Gene(Genes.TextilesTarget)) * 0.5f
                                 + underdressed * 2f);
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

        /// <summary>The larger of the genome's standing target and what the plan is short of.</summary>
        static float Target(DirectorContext ctx, string gene, string thingDefName)
        {
            float target = ctx.Gene(gene);
            if (ctx.plan == null) return target;

            float wanted = ctx.plan.Needs.For(thingDefName);
            return wanted > target ? wanted : target;
        }

        static float Shortfall(int have, float target)
        {
            if (target <= 0f) return 0f;
            return AcMath.Clamp01(1f - have / target);
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
            int bands = AcMath.Clamp(ctx.GeneInt(Genes.WorkBands), 1, 4);

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
                                            + needW * (need - 1f))
                              * BuilderPenalty(pawn, wt);

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


        static int Round(float f)
        {
            return (int)(f + 0.5f);
        }
    }
}
