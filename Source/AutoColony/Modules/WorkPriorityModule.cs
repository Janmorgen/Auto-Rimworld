using System;
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

        /// <summary>The best level anybody in the colony has, per skill that matters here.</summary>
        int bestConstruction;
        int bestMedicine;
        int bestCooking;

        /// <summary>
        /// The colony's only competent medic, when there is exactly one and others are standing.
        ///
        /// Losing them costs every future casualty, not just this fire — so they are held back
        /// from the work most likely to kill them while somebody else can do it instead.
        /// </summary>
        Pawn irreplaceableMedic;

        /// <summary>How many colonists are on their feet and able to work at all.</summary>
        int ableHands;

        /// <summary>
        /// Whether the colony is past the point of preferring the right person for the job.
        ///
        /// Two people on the floor, or a threat at the door, and the question stops being "who
        /// is best at this" and becomes "who is free". A colony of three cannot afford to leave
        /// a casualty untended because the good doctor is busy — the second-best pair of hands
        /// beats the best pair that is elsewhere.
        /// </summary>
        bool desperate;

        /// <summary>
        /// Construction below this botches often enough to be a net loss.
        ///
        /// A failed build consumes the materials and produces nothing, so a poor builder is not
        /// merely slow — they are spending the colony's wood to make rubble. RimWorld scales
        /// ConstructionSuccessChance with the skill, and it is unforgiving at the bottom.
        /// </summary>
        const int ShakyBuilder = 4;

        /// <summary>
        /// The worst band fetching work may be given.
        ///
        /// Three rather than four: still below anything urgent, still above the floor where a
        /// colonist only gets to it once every other job on the map is done.
        /// </summary>
        const int FetchFloor = 3;

        void NoteTheBestBuilder(DirectorContext ctx)
        {
            bestConstruction = 0;
            bestMedicine = 0;
            bestCooking = 0;
            ableHands = 0;

            var all = ctx.state.allColonists;
            for (int i = 0; i < all.Count; i++)
            {
                var pawn = all[i];
                if (pawn == null || pawn.skills == null) continue;
                if (pawn.Downed || pawn.Dead) continue;      // not available to do anything

                ableHands++;
                bestConstruction = Best(bestConstruction, pawn, SkillDefOf.Construction);
                bestMedicine = Best(bestMedicine, pawn, SkillDefOf.Medicine);
                bestCooking = Best(bestCooking, pawn, SkillDefOf.Cooking);
            }

            irreplaceableMedic = null;
            if (bestMedicine >= ShakyBuilder && ableHands > 1)
            {
                Pawn only = null;
                int competent = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    var pawn = all[i];
                    if (pawn == null || pawn.skills == null || pawn.Downed || pawn.Dead) continue;
                    var med = pawn.skills.GetSkill(SkillDefOf.Medicine);
                    if (med == null || med.TotallyDisabled || med.Level < ShakyBuilder) continue;
                    competent++;
                    only = pawn;
                }
                if (competent == 1) irreplaceableMedic = only;
            }

            var s = ctx.state;
            desperate = s.colonistsDowned >= 2 || s.hostilesNearBase > 0 || s.firesNearBase > 0;
        }

        static int Best(int sofar, Pawn pawn, SkillDef def)
        {
            var skill = pawn.skills.GetSkill(def);
            if (skill == null || skill.TotallyDisabled) return sofar;
            return skill.Level > sofar ? skill.Level : sofar;
        }

        /// <summary>
        /// How this colonist's score for a work type is adjusted against the rest of the colony.
        ///
        /// Everything else here scores a colonist against their *own* other options and never
        /// against anybody else's, so a need pushes a work type up every list at once —
        /// including the person least able to do it. That is right for hauling and wrong
        /// wherever skill decides whether the work helps or hurts.
        ///
        /// Three cases, and the third is why this is not simply "prefer the best":
        ///
        ///  - **Construction.** A botched build eats the materials and yields nothing, so a poor
        ///    builder turns wood into rubble. Demoted below level 4 when somebody better exists.
        ///
        ///  - **Doctor.** Tending quality decides whether a wound heals or festers, so the best
        ///    medic is preferred — but only while there is slack. With two colonists down, or a
        ///    threat at the door, the second-best pair of hands beats the best pair that is
        ///    elsewhere, and the demotion lifts entirely.
        ///
        ///  - **Patient.** Not a skill at all. A hurt colonist has to *accept* treatment, and
        ///    one who is seriously hurt has nothing more important to do than lie still. This
        ///    was a flat 1.0 for everybody, hurt or not, which is the same as telling a bleeding
        ///    colonist that resting is as urgent as hauling.
        /// </summary>
        float SkillFit(Pawn pawn, WorkTypeDef wt)
        {
            switch (wt.defName)
            {
                case "Patient": return PatientUrgency(pawn, 6f);
                case "PatientBedRest": return PatientUrgency(pawn, 4f);
                case "Construction": return Demote(pawn, SkillDefOf.Construction, bestConstruction, false);
                case "Doctor": return Demote(pawn, SkillDefOf.Medicine, bestMedicine, true);

                // A bad cook is not slow, they are poisoning people. FoodPoisonChance runs from
                // 5% a meal at Cooking 0 down to 0.5% at 6, and a poisoned colonist in a colony
                // of three is a third of the workforce vomiting for a day — self-inflicted, and
                // at exactly the moment the food mattered enough to cook it.
                //
                // Lifts when desperate for the same reason the doctor's does: a 5% risk of
                // illness beats a certainty of no meal.
                case "Cooking": return Demote(pawn, SkillDefOf.Cooking, bestCooking, true);

                // The only medic does not go into the fire.
                //
                // Firefighting is where colonists get burned, and burns are what the medic is
                // for. Losing the one person who can tend well costs every casualty after this
                // one, not just this fire. Held back only while somebody else is standing — if
                // they are the last one up, the fire is still theirs to fight.
                case "Firefighter":
                case "Hunting":
                    return pawn == irreplaceableMedic && ableHands > 1 ? 0.5f : 1f;

                default: return 1f;
            }
        }

        /// <summary>
        /// How much a colonist needs to be a patient right now, which is about their body and
        /// not their skills.
        /// </summary>
        static float PatientUrgency(Pawn pawn, float whenSerious)
        {
            try
            {
                if (pawn.health == null) return 1f;

                // Bleeding or on the floor: nothing else this colonist could be doing matters.
                if (pawn.Downed || pawn.health.hediffSet.BleedRateTotal > 0.01f) return whenSerious;

                // Wounded and waiting for treatment. Worth putting above ordinary work, not
                // above everything.
                if (pawn.health.HasHediffsNeedingTend()) return 2f;
            }
            catch (Exception) { }
            return 1f;
        }

        /// <summary>
        /// Holds a colonist back from work somebody else does materially better.
        ///
        /// A demotion and never a ban: RimWorld works down the priority list, so the shaky one
        /// still steps in when the good one is asleep, hurt or busy, which is the fallback a
        /// three-person colony lives on. And if the best in the colony is shaky too it changes
        /// nothing, because the work still has to happen.
        /// </summary>
        float Demote(Pawn pawn, SkillDef def, int bestInColony, bool liftsWhenDesperate)
        {
            if (pawn.skills == null) return 1f;
            if (liftsWhenDesperate && desperate) return 1f;

            var skill = pawn.skills.GetSkill(def);
            if (skill == null || skill.TotallyDisabled) return 1f;

            if (bestInColony <= ShakyBuilder) return 1f;
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
                              * SkillFit(pawn, wt);

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

                // Fetching is what makes the rest of the work possible.
                //
                // Hauling and Cleaning are alwaysStartActive, so they can never be switched off
                // — but that guard drops them to priority 4, the bottom band, and every
                // production need outranks them. A colony where everyone builds, harvests,
                // crafts and cooks and nobody fetches is a colony where the harvest rots in the
                // field, the stove has nothing beside it and the crafting bench has no steel:
                // the production work does not fail loudly, it simply never starts.
                //
                // Cleaning belongs here for a quieter reason. Filth drives the room cleanliness
                // that decides food poisoning and research speed, and it is the one input to
                // those the builder cannot supply.
                //
                // A floor, not a promotion. Anyone whose own scores already put fetching higher
                // keeps it there; this only stops it being ranked last by everybody at once.
                if ((wt.defName == "Hauling" || wt.defName == "Cleaning") && priority > FetchFloor)
                    priority = FetchFloor;

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
