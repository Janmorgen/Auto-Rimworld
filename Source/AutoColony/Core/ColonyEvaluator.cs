using System;
using System.Collections.Generic;
using Verse;

namespace AutoColony
{
    /// <summary>One named term of the fitness score, kept for display in the status window.</summary>
    public struct ScoreTerm
    {
        public string name;
        public float raw;         // normalised component, 0..1
        public float weight;
        public float Contribution { get { return raw * weight; } }

        public ScoreTerm(string name, float raw, float weight)
        {
            this.name = name;
            this.raw = raw;
            this.weight = weight;
        }
    }

    /// <summary>
    /// The few colony figures an epoch is scored against. Kept separate from the full
    /// <see cref="ColonyState"/> so the baseline survives a save/load mid-epoch.
    /// </summary>
    public class EpochStart : IExposable
    {
        public int colonists = 1;
        public float wealthTotal;
        public int researchFinished;

        /// <summary>Points banked at the epoch's start, so progress can be differenced.</summary>
        public float researchPoints;

        public int day;

        public static EpochStart From(ColonyMetrics m)
        {
            var e = new EpochStart();
            e.colonists = m.colonists;
            e.wealthTotal = m.wealthTotal;
            e.researchFinished = m.researchFinished;
            e.researchPoints = m.researchPoints;
            e.day = m.day;
            return e;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref colonists, "colonists", 1);
            Scribe_Values.Look(ref wealthTotal, "wealthTotal", 0f);
            Scribe_Values.Look(ref researchFinished, "researchFinished", 0);
            Scribe_Values.Look(ref researchPoints, "researchPoints", 0f);
            Scribe_Values.Look(ref day, "day", 0);
        }
    }

    /// <summary>
    /// Running totals gathered across an epoch.
    ///
    /// Endpoint snapshots alone would be easy to game — a colony can look healthy on the last
    /// day of an epoch after starving for the previous fourteen. Time-averaged mood and health
    /// plus worst-case food reserves make the score reflect how the colony was actually run.
    /// </summary>
    public class EpochAccumulator : IExposable
    {
        public int samples;
        public float moodSum;
        public float healthSum;
        public float readinessSum;
        public float minDaysOfFood = 999f;

        /// <summary>
        /// Whether the colony has ever had food in a stockpile this epoch. Until it has, its
        /// reported food is an artefact of where the food is lying rather than how much there is.
        /// </summary>
        public bool foodObserved;

        /// <summary>
        /// Worst food the epoch actually saw. A colony that never stockpiled anything scores
        /// zero rather than the sentinel, which would otherwise read as perfect food security.
        ///
        /// Kept because it is worth reporting — the low point of an epoch is a real fact about
        /// it — but no longer what the score is built on. See <see cref="FoodSecurity"/>.
        /// </summary>
        public float WorstFood { get { return foodObserved ? minDaysOfFood : 0f; } }

        /// <summary>
        /// Food below which the colony counts as in danger for that sample.
        ///
        /// Deliberately the supply lead time rather than a fresh number. That is already this
        /// codebase's definition of too late — the days between deciding to eat and eating, so
        /// below it nothing the colony decides can arrive before the larder is empty. Inventing
        /// a second threshold would mean two answers to one question.
        /// </summary>
        public const float FoodDangerDays = FoodTiming.SupplyLeadDays;

        /// <summary>Samples taken since the larder became measurable at all.</summary>
        public int foodSamples;

        /// <summary>Of those, the ones where the colony was not close to running out.</summary>
        public int foodSecureSamples;

        /// <summary>
        /// How much of the epoch the colony spent with food actually in hand, 0 to 1.
        ///
        /// The score used to be the single worst reading the epoch ever took, divided by a
        /// target. That is an honest measure of the worst moment and a poor measure of how the
        /// colony was run, because one transient hour at zero zeroes a five-day epoch: run 23
        /// scored 0.00 having never actually run out. It cannot tell *briefly empty* from
        /// *chronically starving*, and those deserve very different scores.
        ///
        /// Time spent in danger is the thing the term was reaching for. It cannot collapse on
        /// one sample, it still punishes a colony that is short for days on end, and it reads
        /// directly as a sentence — "secure for four fifths of the epoch".
        ///
        /// A colony that never stockpiled anything still scores zero: no measurable samples
        /// means no evidence of security, which is the same treatment the old minimum gave it.
        /// </summary>
        public float FoodSecurity
        {
            get { return foodSamples > 0 ? (float)foodSecureSamples / foodSamples : 0f; }
        }

        /// <summary>How much of the measured epoch had somebody starving in it.</summary>
        public float StarvingFraction
        {
            get { return foodSamples > 0 ? (float)starvingSamples / foodSamples : 0f; }
        }

        /// <summary>How much of the epoch had somebody carrying an untended condition.</summary>
        public float UntendedFraction
        {
            get { return samples > 0 ? (float)untendedSamples / samples : 0f; }
        }

        /// <summary>How much of the epoch the colony could not afford to build.</summary>
        public float DestituteFraction
        {
            get { return samples > 0 ? (float)destituteSamples / samples : 0f; }
        }
        /// <summary>
        /// Samples in which somebody was actually going hungry, whatever the larder held.
        ///
        /// "Is there food" and "is anybody eating" only diverge when a colony is dying, and
        /// until now only the first was scored. Run 93 died on day 20 with a colonist down for
        /// 30% of the epoch and thirty-three days of food in store, and was awarded Food
        /// security 1.00 — so the search was told that colony fed itself perfectly.
        /// </summary>
        public int starvingSamples;

        /// <summary>
        /// Samples in which somebody was carrying an untended condition.
        ///
        /// avgHealth is SummaryHealthPercent, which counts damage to body parts and ignores
        /// hediffs — so infection, hypothermia and heatstroke all read 1.00 until they kill.
        /// Colonies have died of Infection (extreme) while scoring Health 0.84, and the search
        /// was told the difference was small.
        /// </summary>
        public int untendedSamples;

        /// <summary>
        /// Samples in which the colony could not afford to build anything.
        ///
        /// Being destitute is not bad luck — it is the bill for what was built. Run 96 put up
        /// fifteen rooms and nine beds for three colonists, went destitute at means 0.12, and
        /// the overbuilding remedy answered by pulling down the research room the plan was
        /// still asking for. Every step was a rule working as written; the whole was a colony
        /// spending itself into demolishing what it needed.
        /// </summary>
        public int destituteSamples;
        public int mentalBreakSamples;
        public int fireSamples;
        public int downedSamples;
        public int emergencySamples;

        /// <summary>
        /// Samples in which the game could rate at least one standing room, and the sum of the
        /// fraction of those rooms that met their role's floor.
        /// </summary>
        public int roomQualitySamples;
        public float roomQualitySum;

        /// <summary>
        /// How much of the base met the standard its roles ask for, averaged over the epoch.
        ///
        /// Built on space and impressiveness, which are what the *building* decided — room
        /// dimensions, wall material, what furniture went in. Cleanliness is deliberately not
        /// part of it: the same room rates well or badly depending on whether anybody swept it
        /// that day, so scoring it here would grade the work priorities and call it building.
        ///
        /// A colony with nothing the game can rate scores neutral rather than zero. No enclosed
        /// rooms is no evidence either way, and the colonies that have none are already losing
        /// Infrastructure and Growth for it — taking a third bite would let one fact dominate
        /// three terms.
        /// </summary>
        public float RoomQuality
        {
            get { return roomQualitySamples > 0 ? roomQualitySum / roomQualitySamples : 0.5f; }
        }

        // --- what the chronicle knows and the outcome figures do not ---

        /// <summary>
        /// Running total of the mood the colony was losing to things the director had no remedy
        /// for, sampled whenever the upkeep survey ran.
        ///
        /// This is not the same measurement as average mood, which is the *effect* and saturates
        /// — a colony pinned at zero looks identical whether one thing is wrong or nine. This is
        /// the composition, and specifically the part nobody could act on. It is also the only
        /// number here that says what to build next.
        /// </summary>
        public float unmetComplaintSum;
        public int complaintSamples;

        /// <summary>The complaint that cost the most across the epoch, and how much.</summary>
        public string worstComplaint = "";
        public float worstComplaintMood;

        /// <summary>
        /// Work the colony did and then undid — construction cancelled, furniture re-queued
        /// because it had gone missing. Reported rather than scored: some of it is a raid's
        /// fault rather than the director's, and penalising it would also punish the deliberate
        /// consolidation a destitute colony is *supposed* to do.
        /// </summary>
        public int wastedActions;

        // Cumulative counters captured at epoch start, so deltas can be derived without
        // reaching back into the game for global statistics.
        public int startDeaths;
        public int startRaids;
        public int latestDeaths;
        public int latestRaids;

        public void ResetFor(ColonyMetrics m)
        {
            samples = 0;
            moodSum = 0f;
            healthSum = 0f;
            readinessSum = 0f;
            minDaysOfFood = 999f;
            foodObserved = false;
            foodSamples = 0;
            foodSecureSamples = 0;
            starvingSamples = 0;
            untendedSamples = 0;
            destituteSamples = 0;
            mentalBreakSamples = 0;
            fireSamples = 0;
            downedSamples = 0;
            emergencySamples = 0;
            roomQualitySamples = 0;
            roomQualitySum = 0f;
            unmetComplaintSum = 0f;
            complaintSamples = 0;
            worstComplaint = "";
            worstComplaintMood = 0f;
            wastedActions = 0;

            startDeaths = m.cumulativeDeaths;
            startRaids = m.cumulativeRaids;
            latestDeaths = m.cumulativeDeaths;
            latestRaids = m.cumulativeRaids;
        }

        public void Observe(ColonyMetrics m)
        {
            if (!m.Valid) return;
            samples++;
            moodSum += m.avgMood;
            healthSum += m.avgHealth;
            readinessSum += m.readiness;
            // The larder is not measurable until something has been hauled into it.
            //
            // `daysOfFood` comes off ResourceCounter, which sees only stockpiled goods, so every
            // colony reads 0.0 for its opening hours no matter what it owns. Taking a plain
            // minimum therefore returned 0.0 for every colony that lived through day one, and
            // the Food security term — worst food over the epoch, divided by a target — scored
            // exactly 0.00 in every epoch of every run. A strategy hoarding twenty days of food
            // and one that starved were indistinguishable to the search on the one axis that
            // most decides whether a colony lives.
            //
            // So the minimum is taken from the first moment there was anything to measure. A
            // colony that stocks up and later empties still records the real low, because by
            // then the measurement has started.
            if (m.daysOfFood > 0f) foodObserved = true;
            if (foodObserved && m.daysOfFood < minDaysOfFood) minDaysOfFood = m.daysOfFood;

            // Same measurability rule, counted over time rather than reduced to its low point.
            if (foodObserved)
            {
                foodSamples++;
                if (m.daysOfFood >= FoodDangerDays) foodSecureSamples++;
                if (m.colonistsStarving > 0) starvingSamples++;
            }
            if (m.colonistsUntended > 0) untendedSamples++;
            if (Upkeep.BuildingMeans.Destitute(
                    Upkeep.BuildingMeans.Assess(m.usableMaterial, m.colonists))) destituteSamples++;
            if (m.colonistsInMentalState > 0) mentalBreakSamples++;
            // Only fires that actually threaten the colony. Counting every fire on the map
            // meant a wildfire ninety cells away — one the director is designed to ignore, and
            // was right to ignore — cost the colony infrastructure score for the whole epoch it
            // burned. The search was being penalised for behaving correctly.
            if (m.firesNearBase > 0) fireSamples++;
            if (m.colonistsDowned > 0) downedSamples++;
            if (m.inEmergency) emergencySamples++;

            // Only samples where there was something to rate. A colony three hours old has no
            // enclosed rooms, and counting that as a base failing its standards would score
            // every colony's opening day against it.
            if (m.roomsJudged > 0)
            {
                roomQualitySamples++;
                roomQualitySum += m.roomsUpToStandard / (float)m.roomsJudged;
            }

            latestDeaths = m.cumulativeDeaths;
            latestRaids = m.cumulativeRaids;
        }

        /// <summary>
        /// Fewest observations an epoch needs before its score means anything.
        ///
        /// A full epoch is hundreds of samples. Anything near zero and every term falls back on
        /// a default — same inputs, same output, every time — so the number looks like a result
        /// and carries no information.
        /// </summary>
        public const int MinSamplesToScore = 8;

        /// <summary>Whether this epoch was observed enough to be worth scoring at all.</summary>
        public bool Scorable { get { return samples >= MinSamplesToScore; } }

        public float AvgMood { get { return samples > 0 ? moodSum / samples : 0.5f; } }
        public float AvgHealth { get { return samples > 0 ? healthSum / samples : 1f; } }

        /// <summary>
        /// How armed the colony was across the epoch, rather than at the instant it ended.
        ///
        /// Read at the endpoint this was worse than useless. Readiness is strength over the
        /// raid the colony's own wealth and headcount invite, and a colony with nobody left
        /// invites nothing — so ThreatForecast.Readiness returned its "no threat expected" 1.0
        /// and a wiped-out colony scored as perfectly armed. Run 132 died with all four
        /// colonists bled out and banked 0.35 of its Defense term on that reading.
        ///
        /// Samples only exist while somebody is alive, so averaging over them cannot be
        /// answered by an empty map.
        /// </summary>
        public float AvgReadiness { get { return samples > 0 ? readinessSum / samples : 0f; } }
        public float MentalBreakFraction { get { return samples > 0 ? (float)mentalBreakSamples / samples : 0f; } }
        public float FireFraction { get { return samples > 0 ? (float)fireSamples / samples : 0f; } }
        public float DownedFraction { get { return samples > 0 ? (float)downedSamples / samples : 0f; } }
        public float EmergencyFraction { get { return samples > 0 ? (float)emergencySamples / samples : 0f; } }

        /// <summary>Average mood lost per survey to problems with no remedy.</summary>
        public float AvgUnmetComplaints
        {
            get { return complaintSamples > 0 ? unmetComplaintSum / complaintSamples : 0f; }
        }

        /// <summary>Records what the upkeep survey found it could not answer.</summary>
        public void NoteUnmetComplaints(float totalMood, string worst, float worstMood)
        {
            complaintSamples++;
            unmetComplaintSum += totalMood;

            if (worstMood > worstComplaintMood && !string.IsNullOrEmpty(worst))
            {
                worstComplaint = worst;
                worstComplaintMood = worstMood;
            }
        }

        public void NoteWaste(int actions) { wastedActions += actions; }

        public int DeathsThisEpoch { get { return Math.Max(0, latestDeaths - startDeaths); } }
        public int RaidsThisEpoch { get { return Math.Max(0, latestRaids - startRaids); } }

        public void ExposeData()
        {
            Scribe_Values.Look(ref samples, "samples", 0);
            Scribe_Values.Look(ref moodSum, "moodSum", 0f);
            Scribe_Values.Look(ref healthSum, "healthSum", 0f);
            Scribe_Values.Look(ref readinessSum, "readinessSum", 0f);
            Scribe_Values.Look(ref minDaysOfFood, "minDaysOfFood", 999f);
            Scribe_Values.Look(ref foodObserved, "foodObserved", false);
            Scribe_Values.Look(ref foodSamples, "foodSamples", 0);
            Scribe_Values.Look(ref foodSecureSamples, "foodSecureSamples", 0);
            Scribe_Values.Look(ref mentalBreakSamples, "mentalBreakSamples", 0);
            Scribe_Values.Look(ref fireSamples, "fireSamples", 0);
            Scribe_Values.Look(ref downedSamples, "downedSamples", 0);
            Scribe_Values.Look(ref emergencySamples, "emergencySamples", 0);
            Scribe_Values.Look(ref roomQualitySamples, "roomQualitySamples", 0);
            Scribe_Values.Look(ref roomQualitySum, "roomQualitySum", 0f);
            Scribe_Values.Look(ref unmetComplaintSum, "unmetComplaintSum", 0f);
            Scribe_Values.Look(ref complaintSamples, "complaintSamples", 0);
            Scribe_Values.Look(ref worstComplaint, "worstComplaint", "");
            Scribe_Values.Look(ref worstComplaintMood, "worstComplaintMood", 0f);
            Scribe_Values.Look(ref wastedActions, "wastedActions", 0);
            Scribe_Values.Look(ref startDeaths, "startDeaths", 0);
            Scribe_Values.Look(ref startRaids, "startRaids", 0);
            Scribe_Values.Look(ref latestDeaths, "latestDeaths", 0);
            Scribe_Values.Look(ref latestRaids, "latestRaids", 0);
        }
    }

    /// <summary>
    /// Turns an epoch of colony history into a single fitness number in roughly [0,1].
    ///
    /// Deliberately NOT genome-driven. If the strategy could tune its own scoring weights the
    /// optimiser would simply learn to redefine success rather than to play better, so the
    /// weights below are fixed constants and the only thing evolution may change is behaviour.
    ///
    /// Terms are a mix of levels (how well the colony is running) and rates (how fast it is
    /// improving), chosen to stay comparable across epochs: wealth uses log-growth so it does
    /// not inflate as the colony scales, and survival is measured against colonists at risk.
    /// </summary>
    public static class ColonyEvaluator
    {
        const float WSurvival = 0.26f;
        const float WGrowth = 0.17f;
        const float WFood = 0.13f;
        const float WMood = 0.11f;
        const float WHealth = 0.07f;
        const float WResearch = 0.06f;
        const float WInfrastructure = 0.04f;
        const float WDefense = 0.02f;

        /// <summary>
        /// Whether the base the colony built is any good, as the game itself rates it.
        ///
        /// The planner has always been scored on whether rooms *exist* — beds per colonist, a
        /// powered turret count — and never on whether they were worth building. So a colony
        /// that put up six cramped huts scored exactly like one that put up six good rooms, and
        /// the room dimensions in the genome had nothing pushing on them in either direction:
        /// every width and height was equally fit, so the search had no reason to prefer any of
        /// them and drifted.
        ///
        /// This is the term that gives those genes a gradient. Space is chosen by the siting
        /// width and height; impressiveness follows from wall material and what furniture went
        /// in; both are decisions the strategy makes and can be selected on. Cleanliness is
        /// excluded because it is a work-priority outcome wearing a building's clothes.
        ///
        /// Taken proportionally out of the other weights rather than added on top, so the score
        /// stays in [0,1] — the same treatment Conduct had. Scores from before this term are not
        /// directly comparable with scores after it.
        /// </summary>
        const float WRoomQuality = 0.05f;

        /// <summary>
        /// How the epoch was *run*, as opposed to how it came out.
        ///
        /// Everything above is an outcome, and outcomes hide conduct. Two colonies can close an
        /// epoch with the same mood, food and wealth when one of them spent the fortnight
        /// lurching between emergencies with a standing pile of misery nobody could answer —
        /// and that is the one that dies next epoch, which the score had no way to say.
        ///
        /// Taken proportionally out of the other weights rather than added on top, so the score
        /// stays in [0,1]. Scores from before this term are not directly comparable with scores
        /// after it.
        /// </summary>
        const float WConduct = 0.09f;

        /// <summary>Unmet mood this bad per survey counts as thoroughly mismanaged.</summary>
        const float MiseryCeiling = 40f;

        /// <summary>
        /// Days of stored food that counts as fully secure.
        ///
        /// Only used for the endpoint fallback now: an epoch with no samples at all has nothing
        /// to take a fraction of, so it is scored on where it finished.
        /// </summary>
        const float FoodSecureDays = 12f;

        public static float Evaluate(EpochStart start, ColonyMetrics end, EpochAccumulator acc,
                                     out List<ScoreTerm> breakdown)
        {
            breakdown = new List<ScoreTerm>();
            if (start == null || acc == null) return 0f;

            int startPop = Math.Max(1, start.colonists);

            // --- survival: the dominant term. Losing colonists is the worst outcome. ---
            int deaths = acc.DeathsThisEpoch;
            float survival = AcMath.Clamp01(1f - (deaths / (float)startPop) * 1.5f);
            survival *= AcMath.Clamp01(1f - acc.DownedFraction * 0.5f);
            if (end.colonists == 0) survival = 0f;
            breakdown.Add(new ScoreTerm("Survival", survival, WSurvival));

            // --- growth: log wealth growth (scale-free) blended with population change ---
            float wealthGrowth = 0.5f;
            if (start.wealthTotal > 100f && end.wealthTotal > 0f)
            {
                double ratio = end.wealthTotal / (double)start.wealthTotal;
                // +50% wealth over an epoch maps to 1.0, flat maps to 0.5, shrinking to below.
                wealthGrowth = AcMath.Clamp01(0.5f + (float)(Math.Log(ratio) / Math.Log(1.5)) * 0.5f);
            }
            float popGrowth = AcMath.Clamp01(0.5f + (end.colonists - start.colonists) * 0.25f);
            float growth = wealthGrowth * 0.6f + popGrowth * 0.4f;
            breakdown.Add(new ScoreTerm("Growth", growth, WGrowth));

            // --- food security: how much of the epoch was spent out of danger ---
            //
            // Was the single worst reading the epoch ever took. That is an honest measure of the
            // worst moment and a poor measure of how a colony was run: one transient hour at zero
            // zeroes a five-day epoch, and run 23 scored 0.00 having never actually run out. The
            // search could not tell a colony that dipped once from one that starved for a week.
            // Stocked and fed are different claims, and only the second one keeps anybody
            // alive. A full larder scores nothing for the hours somebody spent starving beside
            // it, in the same shape the Mood term already uses for mental breaks.
            //
            // This is a change to what every colony is measured against, so archived scores
            // from before it are not strictly comparable. It is made deliberately: seven
            // colonies have now died with this term at or near 1.00, which means the search has
            // been repeatedly told that the way they died was a success.
            float food = acc.foodSamples > 0
                ? acc.FoodSecurity
                : AcMath.Clamp01(end.daysOfFood / FoodSecureDays);
            food *= AcMath.Clamp01(1f - acc.StarvingFraction);
            breakdown.Add(new ScoreTerm("Food security", food, WFood));

            // --- mood: time-averaged, penalised by how often someone was breaking ---
            float mood = AcMath.Clamp01(acc.AvgMood) * AcMath.Clamp01(1f - acc.MentalBreakFraction * 0.7f);
            breakdown.Add(new ScoreTerm("Mood", mood, WMood));

            // --- health, discounted by how long anybody went untended ---
            //
            // AvgHealth is SummaryHealthPercent, which measures missing and damaged body parts.
            // It is blind to hediffs, so a colonist dying of an infection reads as perfectly
            // healthy and the term scores the colony as though nothing were wrong — which is
            // the same fault Food security had, in the domain next door.
            //
            // Discounted rather than replaced. Damage to a body part is real and worth
            // measuring; what was missing is that a colony leaving wounds untended is not a
            // healthy one, whatever its parts add up to. Untended is the game's own condition,
            // the one behind its "needs tending" alert, and it is specifically harm a director
            // can prevent by putting somebody on Doctor.
            float health = AcMath.Clamp01(acc.AvgHealth) *
                           AcMath.Clamp01(1f - acc.UntendedFraction * 0.7f);
            breakdown.Add(new ScoreTerm("Health", health, WHealth));

            // --- research throughput ---
            // Points banked, not projects finished.
            //
            // "Finished projects / 2" scored 0.00 in 91 of the 93 epochs this project has
            // measured, and never once reached 1.00. It was not wrong — those colonies really
            // did finish nothing — but a step function on an event that almost never happens is
            // a constant, and a constant teaches the optimiser nothing. Six percent of the score
            // weight has been unreachable for the whole history of the run archive, and a colony
            // ninety-five percent through Pemmican scored the same as one with no bench.
            //
            // Progress moves every hour somebody sits at a bench, which is what a gradient
            // needs. Scaled against a cheap Neolithic project — Pemmican and PsychoidBrewing are
            // 500, Stonecutting 300 — so a colony that banks one of those in an epoch scores
            // well, and finishing several still saturates.
            //
            // Finishing is kept as the bonus it should be rather than the whole measure: it is
            // the milestone, the points are the work.
            int projects = Math.Max(0, end.researchFinished - start.researchFinished);
            float banked = Math.Max(0f, end.researchPoints - start.researchPoints);
            float research = AcMath.Clamp01(banked / 500f) * 0.7f
                           + AcMath.Clamp01(projects / 2f) * 0.3f;
            breakdown.Add(new ScoreTerm("Research", research, WResearch));

            // --- infrastructure: everyone housed, with a little slack ---
            float bedRatio = end.colonists > 0 ? end.colonistBeds / (float)end.colonists : 1f;
            float infra = AcMath.Clamp01(bedRatio) * 0.7f + AcMath.Clamp01(1f - acc.FireFraction) * 0.3f;

            // Discounted by how long the colony could not afford to build anything.
            //
            // Infrastructure paid for rooms and beds and asked nothing about what they cost.
            // Run 96 put up fifteen rooms and nine beds for three colonists, went destitute at
            // means 0.12, and the overbuilding remedy answered by pulling down the research
            // room its own plan was still asking for. Every rule involved worked as written;
            // the whole was a colony spending itself into demolishing what it needed.
            //
            // Being destitute is not bad luck. It is the bill for what was built, and until now
            // the building was scored and the bill was not — so a genome that over-built was
            // rewarded for the rooms and never charged for the poverty. This does not forbid
            // building; it stops paying for the part that leaves the colony unable to act.
            infra *= AcMath.Clamp01(1f - acc.DestituteFraction * 0.6f);
            breakdown.Add(new ScoreTerm("Infrastructure", infra, WInfrastructure));

            // --- defense readiness, scaled by the threat wealth actually attracts ---
            // Defense is what the fighting cost, and only secondarily what was built for it.
            //
            // It used to be turret readiness against wealth and nothing else:
            //
            //   expectedTurrets = wealth / 25000, capped 0..8
            //   defense = expectedTurrets < 0.5 ? 1 : poweredTurrets / expectedTurrets
            //
            // which never read a raid, an injury or a death in either direction. Run 118 was
            // wiped out by one raider and scored 0.00; run 117 lost nobody to combat and also
            // scored 0.00; run 116 lost two colonists and scored 1.00. The term moved with the
            // woodpile, not with the casualties — and it had a cliff at 12,500 wealth where a
            // colony flipped from a free 1.0 to a flat 0.0 for getting slightly richer.
            //
            // Readiness still counts, because building toward turrets is the part the director
            // controls. But it is now the smaller half and it is smooth, and the larger half is
            // whether anybody spent the epoch on the floor. A colony with no turrets that came
            // through unhurt was defended; a rich one behind three turrets whose colonists bled
            // out was not, and the old term said the opposite of both.
            float expectedTurrets = AcMath.Clamp(end.wealthTotal / 25000f, 0f, 8f);
            float turretReadiness = expectedTurrets <= 0f
                ? 1f
                : AcMath.Clamp01(end.poweredTurrets / expectedTurrets);

            // Poverty is an excuse for having no turrets, not a defence. Blended rather than
            // switched, so there is no cliff to sit under.
            float excused = AcMath.Clamp01(1f - end.wealthTotal / 25000f);
            turretReadiness = AcMath.Clamp01(turretReadiness + excused * (1f - turretReadiness));

            // Time with somebody down, already measured for Survival — the closest thing the
            // accumulator holds to how badly the fighting went.
            float unhurt = AcMath.Clamp01(1f - acc.DownedFraction * 1.5f);

            // And whether the colony is armed for what is coming, not only for what came.
            //
            // Turrets and casualties are both backward-looking: one is what was built, the other
            // is what already happened. A colony whose wealth has outrun its weapons is in danger
            // that has not arrived yet, and nothing in the score could see it — which is exactly
            // the position every colony here reaches around day 20, when wealth passes 15,000 and
            // three people are still carrying whatever they landed with.
            //
            // Readiness is colony fighting strength over the storyteller's own points figure for
            // this map, so it rises by arming and falls by getting richer without arming.
            //
            // Averaged over the epoch rather than read at the end, for the same reason `unhurt`
            // is: the endpoint of a colony that lost is an empty map, and an empty map invites
            // no raid, which the forecast reports as being comfortably armed for it.
            float armed = AcMath.Clamp01(acc.AvgReadiness);

            float defense = 0.25f * turretReadiness + 0.40f * unhurt + 0.35f * armed;
            breakdown.Add(new ScoreTerm("Defense", defense, WDefense));

            // --- conduct: time spent in crisis, and misery with no answer ---
            //
            // Both come from what the director itself recorded while playing, not from the end
            // state. A colony permanently answering something immediate is never building, and
            // one carrying complaints it has no remedy for is losing mood it cannot recover
            // without being taught something new.
            float calm = AcMath.Clamp01(1f - acc.EmergencyFraction);
            float contentment = AcMath.Clamp01(1f - acc.AvgUnmetComplaints / MiseryCeiling);
            float conduct = calm * 0.5f + contentment * 0.5f;
            breakdown.Add(new ScoreTerm("Conduct", conduct, WConduct));

            // --- room quality: how much of the base met the standard its roles ask for ---
            //
            // The one term that reaches the siting genes. Everything else about building is
            // scored on whether a room exists, which every room satisfies equally, so the
            // width and height in the genome had no gradient to follow.
            breakdown.Add(new ScoreTerm("Room quality", acc.RoomQuality, WRoomQuality));

            float score = 0f;
            for (int i = 0; i < breakdown.Count; i++) score += breakdown[i].Contribution;

            // Hard failure state the weighted sum would otherwise soften too much.
            if (end.colonists == 0) score = 0f;

            return AcMath.Clamp01(score);
        }


    }
}
