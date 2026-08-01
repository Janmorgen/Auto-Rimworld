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
        public int day;

        public static EpochStart From(ColonyMetrics m)
        {
            var e = new EpochStart();
            e.colonists = m.colonists;
            e.wealthTotal = m.wealthTotal;
            e.researchFinished = m.researchFinished;
            e.day = m.day;
            return e;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref colonists, "colonists", 1);
            Scribe_Values.Look(ref wealthTotal, "wealthTotal", 0f);
            Scribe_Values.Look(ref researchFinished, "researchFinished", 0);
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
        public float minDaysOfFood = 999f;
        public int mentalBreakSamples;
        public int fireSamples;
        public int downedSamples;
        public int emergencySamples;

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
            minDaysOfFood = 999f;
            mentalBreakSamples = 0;
            fireSamples = 0;
            downedSamples = 0;
            emergencySamples = 0;
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
            if (m.daysOfFood < minDaysOfFood) minDaysOfFood = m.daysOfFood;
            if (m.colonistsInMentalState > 0) mentalBreakSamples++;
            if (m.fires > 0) fireSamples++;
            if (m.colonistsDowned > 0) downedSamples++;
            if (m.inEmergency) emergencySamples++;

            latestDeaths = m.cumulativeDeaths;
            latestRaids = m.cumulativeRaids;
        }

        public float AvgMood { get { return samples > 0 ? moodSum / samples : 0.5f; } }
        public float AvgHealth { get { return samples > 0 ? healthSum / samples : 1f; } }
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
            Scribe_Values.Look(ref minDaysOfFood, "minDaysOfFood", 999f);
            Scribe_Values.Look(ref mentalBreakSamples, "mentalBreakSamples", 0);
            Scribe_Values.Look(ref fireSamples, "fireSamples", 0);
            Scribe_Values.Look(ref downedSamples, "downedSamples", 0);
            Scribe_Values.Look(ref emergencySamples, "emergencySamples", 0);
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
        const float WSurvival = 0.28f;
        const float WGrowth = 0.18f;
        const float WFood = 0.14f;
        const float WMood = 0.11f;
        const float WHealth = 0.07f;
        const float WResearch = 0.06f;
        const float WInfrastructure = 0.04f;
        const float WDefense = 0.02f;

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
        const float WConduct = 0.10f;

        /// <summary>Unmet mood this bad per survey counts as thoroughly mismanaged.</summary>
        const float MiseryCeiling = 40f;

        /// <summary>Days of stored food that counts as fully secure.</summary>
        const float FoodSecureDays = 12f;

        public static float Evaluate(EpochStart start, ColonyMetrics end, EpochAccumulator acc,
                                     out List<ScoreTerm> breakdown)
        {
            breakdown = new List<ScoreTerm>();
            if (start == null || acc == null) return 0f;

            int startPop = Math.Max(1, start.colonists);

            // --- survival: the dominant term. Losing colonists is the worst outcome. ---
            int deaths = acc.DeathsThisEpoch;
            float survival = Clamp01(1f - (deaths / (float)startPop) * 1.5f);
            survival *= Clamp01(1f - acc.DownedFraction * 0.5f);
            if (end.colonists == 0) survival = 0f;
            breakdown.Add(new ScoreTerm("Survival", survival, WSurvival));

            // --- growth: log wealth growth (scale-free) blended with population change ---
            float wealthGrowth = 0.5f;
            if (start.wealthTotal > 100f && end.wealthTotal > 0f)
            {
                double ratio = end.wealthTotal / (double)start.wealthTotal;
                // +50% wealth over an epoch maps to 1.0, flat maps to 0.5, shrinking to below.
                wealthGrowth = Clamp01(0.5f + (float)(Math.Log(ratio) / Math.Log(1.5)) * 0.5f);
            }
            float popGrowth = Clamp01(0.5f + (end.colonists - start.colonists) * 0.25f);
            float growth = wealthGrowth * 0.6f + popGrowth * 0.4f;
            breakdown.Add(new ScoreTerm("Growth", growth, WGrowth));

            // --- food security: worst reserve reached, not the comfortable endpoint ---
            float worstFood = acc.samples > 0 ? acc.minDaysOfFood : end.daysOfFood;
            float food = Clamp01(worstFood / FoodSecureDays);
            breakdown.Add(new ScoreTerm("Food security", food, WFood));

            // --- mood: time-averaged, penalised by how often someone was breaking ---
            float mood = Clamp01(acc.AvgMood) * Clamp01(1f - acc.MentalBreakFraction * 0.7f);
            breakdown.Add(new ScoreTerm("Mood", mood, WMood));

            // --- health ---
            float health = Clamp01(acc.AvgHealth);
            breakdown.Add(new ScoreTerm("Health", health, WHealth));

            // --- research throughput ---
            int projects = Math.Max(0, end.researchFinished - start.researchFinished);
            float research = Clamp01(projects / 2f);
            breakdown.Add(new ScoreTerm("Research", research, WResearch));

            // --- infrastructure: everyone housed, with a little slack ---
            float bedRatio = end.colonists > 0 ? end.colonistBeds / (float)end.colonists : 1f;
            float infra = Clamp01(bedRatio) * 0.7f + Clamp01(1f - acc.FireFraction) * 0.3f;
            breakdown.Add(new ScoreTerm("Infrastructure", infra, WInfrastructure));

            // --- defense readiness, scaled by the threat wealth actually attracts ---
            float expectedTurrets = Clamp(end.wealthTotal / 25000f, 0f, 8f);
            float defense = expectedTurrets < 0.5f ? 1f : Clamp01(end.turrets / expectedTurrets);
            breakdown.Add(new ScoreTerm("Defense", defense, WDefense));

            // --- conduct: time spent in crisis, and misery with no answer ---
            //
            // Both come from what the director itself recorded while playing, not from the end
            // state. A colony permanently answering something immediate is never building, and
            // one carrying complaints it has no remedy for is losing mood it cannot recover
            // without being taught something new.
            float calm = Clamp01(1f - acc.EmergencyFraction);
            float contentment = Clamp01(1f - acc.AvgUnmetComplaints / MiseryCeiling);
            float conduct = calm * 0.5f + contentment * 0.5f;
            breakdown.Add(new ScoreTerm("Conduct", conduct, WConduct));

            float score = 0f;
            for (int i = 0; i < breakdown.Count; i++) score += breakdown[i].Contribution;

            // Hard failure state the weighted sum would otherwise soften too much.
            if (end.colonists == 0) score = 0f;

            return Clamp01(score);
        }

        static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        static float Clamp(float v, float min, float max)
        {
            return v < min ? min : (v > max ? max : v);
        }
    }
}
