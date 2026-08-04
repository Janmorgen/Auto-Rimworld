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
        int bestPlants;

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
            bestPlants = 0;
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
                bestPlants = Best(bestPlants, pawn, SkillDefOf.Plants);
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

                // A bad harvester destroys the crop they were sent to bring in.
                //
                // Watched on screen before it was found in any log: "Harvest botched" floating
                // over a colonist in a field the colony was living off. PlantHarvestYield takes
                // a skillNeedFactor on Plants, so an unskilled harvester returns less of every
                // plant they touch and wastes the rest — the loss is silent, permanent, and
                // lands on the one supply that does not have to be fought for.
                //
                // Sowing is not the same job. Getting seed into the ground is nearly
                // skill-independent and the field is worthless unsown, so Growing stays open to
                // everybody; it is the harvest that wants the grower.
                //
                // Lifts when desperate, like the cook and the doctor: a clumsy harvest beats a
                // field nobody reaps.
                case "PlantCutting": return Demote(pawn, SkillDefOf.Plants, bestPlants, true);

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
            ReportTheShape(ctx);
        }

        string lastShape = "";

        /// <summary>
        /// Says what the colony is currently leaning towards, when that changes.
        ///
        /// The needs table is the director's whole opinion about what matters this hour, and it
        /// was never written down anywhere — so a run could be watched for a day without ever
        /// learning whether it had noticed winter, or a fire, or that nobody was fetching.
        /// Reported only when the ordering changes, which is rare enough to read.
        /// </summary>
        void ReportTheShape(DirectorContext ctx)
        {
            var top = new List<KeyValuePair<string, float>>();
            foreach (var kv in needs)
                if (kv.Value >= 1.5f || kv.Value <= 0.5f) top.Add(kv);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < top.Count && i < 6; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(top[i].Key).Append(' ').Append(top[i].Value.ToString("0.0"));
            }

            string shape = sb.ToString();
            if (shape.Length == 0 || shape == lastShape) return;
            lastShape = shape;

            var s = ctx.state;
            Chronicle.Record(ChronicleCategory.Economy, string.Format(
                "work is leaning — {0}  [{1}{2}, {3:0}C]",
                shape, s.season,
                s.growingSeasonNow ? ", fields growing" : ", nothing grows outdoors",
                s.outdoorTemperature));
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

            // An empty larder is an emergency and was not being priced as one.
            //
            // Every food term tops out at 2.5 — one plus a shortfall of at most one, times
            // one and a half. Tailoring reaches 3.5, because being badly dressed adds two on
            // its own. So a colony at zero days of food put clothes above dinner, and the
            // arithmetic made it impossible to do otherwise: feeding itself could never be the
            // top priority however empty the store got.
            //
            // Watched in run 69 at day 20, food 0.0d and mood 0.03 in a 39C summer, leaning
            // "Tailoring 3.5, Growing 3.0, PlantCutting 2.8, Cooking 2.5".
            //
            // Firefighter already shows the shape this wants: an ordinary weight most of the
            // time and a large one when the thing has actually happened. Starving is that kind
            // of condition. Clothes matter — colonists die of heat and cold — but they die of
            // those over weeks and of an empty larder in days.
            bool starving = s.daysOfFood < 1f;

            // Somebody going hungry with a full larder is a different emergency, and answering
            // it the same way makes it worse.
            //
            // Seven colonies have died with Food security at or near 1.00 — run 56 with 8.8
            // days in store and its last colonist on the floor, run 59 with 9.4. The larder was
            // never the problem: the food was not reaching the person. Hunting harder there
            // spends the hands that would have carried a meal over, so this raises the two work
            // types that actually close the distance — feeding a patient is a Doctor job, and
            // food nobody has hauled into a stockpile is food nobody can be fed from.
            bool notReachingThem = s.colonistsStarving > 0 && s.daysOfFood >= 1f;

            // An empty larder with meat lying in the field is not a hunting problem.
            //
            // A corpse is food two jobs away — haul it, butcher it — and answering it with more
            // hunting produces another corpse beside the first. A colony here once hunted
            // thirteen gazelles and starved at 0.0 days with all of it lying where it fell.
            //
            // So when the larder is short and there is more than a day of meat waiting, the
            // work that closes the gap is butchering and the hauling that feeds it, not another
            // kill. Hunting is left alone rather than suppressed: the animals may still be
            // needed, and a colony that stops hunting on a bad reading starves twice.
            bool meatWaiting = s.daysOfFood < 4f && s.daysOfFoodUnbutchered >= 1f;

            // A generator that is built and producing nothing is usually an empty fuel hopper.
            //
            // Run 105 reached day 21 with a Power room finished, a wood-fired generator standing
            // in it, a tailor bench wired to it, and "0 generators running (1 built but producing
            // nothing)" in the plan. Wood was not short — PlantCutting was not even in the work
            // table. Nobody had loaded it.
            //
            // Refuelling is a Hauling job (WorkGiver Refuel, workType Hauling), and Hauling sits
            // at 1.1 against Construction at 2.3, so with two colonists it never won. PowerGoal
            // has computed this idle count all along, for its explain line; nothing acted on it.
            //
            // Same shape as the two above: the colony owns the thing it needs and the last step
            // of getting it there has not happened.
            bool idleGenerator = s.generators > s.workingGenerators;

            // Food in the store and nothing cooked is a mood bill nobody had to pay.
            //
            // A colonist with no meal to hand eats raw and takes AteRawFood at -7. Run 107 held
            // that thought on all four colonists at once, with five days of food and a working
            // kitchen — nutrition was never short, none of it had been through a stove. The
            // larder said "fed" and the colony was eating potatoes off the floor.
            //
            // Fourth of the same family: the colony owns the thing and the last step of making
            // it useful has not happened.
            bool nothingCooked = s.daysOfFood >= 1f && s.daysOfMeals < 1f;

            // Emergencies first: fire and untreated casualties outrank everything.
            // Only fires that could reach the colony justify dropping everything; a distant
            // wildfire is not worth a work-hour.
            Need("Firefighter", s.firesNearBase > 0 ? 6f : 1f);
            Need("Patient", 1f);
            Need("PatientBedRest", 1f);
            // Untended is the sharper signal, and avgHealth is the blind one.
            //
            // avgHealth is SummaryHealthPercent, which counts damaged body parts and cannot see
            // a hediff — so an infection reads as perfect health and never lifts Doctor at all,
            // while a colonist with an old scar drags it up for ever. Asking the game who
            // actually needs tending replaces a proxy with the fact it was standing in for.
            //
            // Not a new rule: the same rung of the same ladder, given a better input. avgHealth
            // stays underneath it, because damaged parts are still worth a doctor's time.
            // An untended infection is a downed colonist that has not happened yet.
            //
            // Lubov died of one while Doctor sat at 3.0 and Tailoring at 3.2 — a heat wave with
            // nobody dressed for it, so sewing was genuinely urgent and it still should not have
            // outranked an infection with twenty medicine in the cupboard. The two are the same
            // urgency class and differ only in how far along they are, so a condition the game
            // says can kill now ranks beside a colonist already on the floor.
            //
            // Ordinary untended stays where it was. A grazed knuckle is not an emergency, and
            // treating every scratch as one would hold Doctor at the top for ever.
            Need("Doctor", notReachingThem ? 5f
                         : (s.colonistsDowned > 0 || s.colonistsUntendedLethal > 0
                            || s.colonistsLosingToDisease > 0) ? 4f
                         : s.colonistsUntended > 0 ? 3f
                         : (s.avgHealth < 0.9f ? 2f : 1f));

            // Sowing in a season nothing grows in is work that produces nothing.
            //
            // The colony still tends and harvests what is standing, so this is a reduction and
            // not a shutdown — but with the fields frozen the hands are better spent hunting,
            // hauling and building, and the food that matters now is the food already stored.
            Need("Growing", (starving ? 4f : 1f + foodShortfall * 2f)
                            * (s.growingSeasonNow ? 1f : 0.35f));
            // Hunting is what feeds a colony whose fields are dead, so it takes up the slack
            // exactly when growing cannot.
            Need("Hunting", (starving ? 5f : 1f + foodShortfall * 2f * ctx.Gene(Genes.HuntAggression))
                            * (s.growingSeasonNow ? 1f : 1.4f));
            // Preserving the harvest matters more with the cold coming, because what is not
            // cooked and stored before the fields die is not eaten in the months after.
            // Butchering is Cooking work, so this is the same lever for both jobs.
            Need("Cooking", (starving ? 4.5f
                           : meatWaiting || nothingCooked ? 4f
                           : 1f + foodShortfall * 1.5f)
                            * (s.winterComing ? 1.3f : 1f));

            // Building scales with the backlog, the way gathering scales with the shortfall.
            //
            // These two were asymmetric and the asymmetry had a direction. Mining and
            // PlantCutting are 1 + shortfall x 1.5 x aggression, so they climb to 2.5 when
            // stores are low. Construction was a flat 2.2 whenever any work existed at all,
            // however much was waiting — so a colony could not express "there is a great deal
            // to build" the way it could express "there is not much wood".
            //
            // Run 99 spent day ten with five rooms sited, one finished, 390 material in hand
            // and means at 1.00, chopping wood at 2.5 against building at 2.2 while three
            // colonists shared two beds. Nothing was misweighted; building simply had no way to
            // say it was falling behind.
            int backlog = s.pendingBlueprints + s.pendingFrames;
            Need("Construction", backlog > 0
                ? 2.2f + AcMath.Clamp01(backlog / 30f) * 1.5f
                : 0.8f);

            // Against the plan's target as well as the genome's, the same way the resource
            // module designates against both. Otherwise the colony digs and chops for what the
            // goal needs while nobody is especially assigned to do it.
            Need("Mining", 1f + Shortfall(s.steel, Target(ctx, Genes.SteelTarget, "Steel")) * 1.5f
                              * (0.5f + ctx.Gene(Genes.MiningAggression)));
            Need("PlantCutting", 1f + Shortfall(s.wood, Target(ctx, Genes.WoodTarget, "WoodLog")) * 1.5f
                                   * (0.5f + ctx.Gene(Genes.ChopAggression)));

            // Research at a flat 1.2 is how a colony waits sixteen days for a 500-point
            // project while sewing at 3.2. The plan already publishes the project the focus is
            // blocked on; when it does, and a bench exists to work at, research is the work
            // that unblocks everything downstream of it — weighted by the genome, so how hard
            // a strategy leans into study is learned rather than asserted.
            bool planBlockedOnResearch = ctx.plan != null && ctx.plan.ResearchWanted != null;
            Need("Research", !s.hasResearchBench ? 0.3f
                           : planBlockedOnResearch ? ctx.Gene(Genes.ResearchUrgentWeight)
                           : 1.2f);
            Need("Warden", s.prisoners > 0 ? 2f : 0.2f);
            Need("Handling", 1f);
            // A pending surgery makes the mop a medical instrument: the theatre's cleanliness
            // sets surgery success (0.60x filthy) and post-operative infection (full odds
            // filthy, a fifth sterile), so when somebody is losing to a disease the room they
            // will be cut open in is the most consequential floor in the colony.
            Need("Cleaning", s.colonistsLosingToDisease > 0 ? 3f
                           : s.avgMood < 0.6f ? 1.6f : 0.9f);
            // Items outdoors deteriorate wherever they are, and in a dry climate they are also
            // the easiest thing on the map to lose. Getting them into storage is preventative
            // rather than tidy, so it outranks ordinary hauling as the map dries out.
            float fireRisk = FireRisk.Assess(ctx.map, s);
            // Pressure from what the weather is taking, not how many pieces it is in. A rifle
            // and a slag chunk are one item each; only one of them is worth an afternoon.
            float outdoorPressure = AcMath.Clamp01(AcMath.Max(
                s.itemsOutdoors / 40f, s.valueOutdoors / 1500f));
            // Hauling is normally kept off the top of the table on purpose, but food nobody has
            // carried into a stockpile is food a hungry colonist cannot be fed from.
            // Hauling matters for both cases and for the same reason: food that nobody has
            // carried anywhere is food nobody can eat, whether it is in a stockpile or a corpse.
            Need("Hauling", (notReachingThem || meatWaiting || idleGenerator ? 4f : 1.1f)
                            + fireRisk * outdoorPressure * 2f);
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

            // Standing bills pull their own work type.
            //
            // The production module keeps bills on every table — blocks at the stonecutter,
            // meals at the stove, coats at the tailor bench — and none of that waiting work
            // could raise the priority that performs it: Crafting and Smithing sat at a flat
            // 1.0 for the life of the project. The mapping from bench to work type is the
            // game's own (WorkGiver_DoBill definitions), so a modded bench pulls the right
            // work without being named anywhere here.
            RaiseBillBacklogs(ctx);
        }

        static Dictionary<ushort, string> benchWorkType;

        /// <summary>
        /// The work type that performs bills on each bench, from the game's own
        /// WorkGiver_DoBill definitions — the same lookup the job system uses, cached once.
        /// </summary>
        static string WorkTypeForBench(ThingDef bench)
        {
            if (benchWorkType == null)
            {
                benchWorkType = new Dictionary<ushort, string>();
                var givers = DefDatabase<WorkGiverDef>.AllDefsListForReading;
                for (int i = 0; i < givers.Count; i++)
                {
                    var giver = givers[i];
                    if (giver == null || giver.workType == null) continue;
                    if (giver.fixedBillGiverDefs == null) continue;
                    for (int b = 0; b < giver.fixedBillGiverDefs.Count; b++)
                    {
                        var def = giver.fixedBillGiverDefs[b];
                        if (def != null && !benchWorkType.ContainsKey(def.shortHash))
                            benchWorkType[def.shortHash] = giver.workType.defName;
                    }
                }
            }
            string workType;
            return benchWorkType.TryGetValue(bench.shortHash, out workType) ? workType : null;
        }

        void RaiseBillBacklogs(DirectorContext ctx)
        {
            var lister = ctx.map.listerBuildings;
            if (lister == null) return;

            var backlog = new Dictionary<string, int>();
            try
            {
                foreach (var table in lister.AllBuildingsColonistOfClass<Building_WorkTable>())
                {
                    if (table == null || table.billStack == null || table.billStack.Count == 0) continue;
                    var workType = WorkTypeForBench(table.def);
                    if (workType == null) continue;

                    int waiting = 0;
                    for (int i = 0; i < table.billStack.Count; i++)
                    {
                        var bill = table.billStack[i];
                        if (bill != null && !bill.suspended && bill.ShouldDoNow()) waiting++;
                    }
                    if (waiting == 0) continue;

                    int held;
                    backlog.TryGetValue(workType, out held);
                    backlog[workType] = held + waiting;
                }
            }
            catch (System.Exception) { return; }

            float weight = ctx.Gene(Genes.BillBacklogWeight);
            foreach (var kv in backlog)
                RaiseTo(kv.Key, 1.5f + AcMath.Clamp01(kv.Value / 5f) * weight);
        }

        void Need(string defName, float value)
        {
            needs[defName] = value;
        }

        /// <summary>Raises a weight without ever lowering one another rule already set.</summary>
        void RaiseTo(string defName, float value)
        {
            float existing;
            if (!needs.TryGetValue(defName, out existing) || value > existing)
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

            // The focus's bill, and the largest bill any other unsatisfied goal holds. A
            // colony mining toward the focus's 220 steel while a further goal quietly needs 280
            // stops 60 short and mobilises twice.
            float wanted = ctx.plan.Needs.For(thingDefName);
            float layered = ctx.plan.QuantityWanted(thingDefName);
            if (layered > wanted) wanted = layered;
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
