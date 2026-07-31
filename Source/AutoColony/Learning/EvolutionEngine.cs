using System;
using System.Collections.Generic;
using Verse;

namespace AutoColony.Learning
{
    public enum EpochPhase
    {
        /// <summary>Running a mutated candidate, hoping to beat the incumbent.</summary>
        Challenger = 0,
        /// <summary>Re-running the incumbent to refresh its score against a changed world.</summary>
        IncumbentRecheck = 1
    }

    public class EpochRecord : IExposable
    {
        public int index;
        public float score;
        public int phase;
        public bool accepted;
        public int generation;
        public int dayOfEpochEnd;

        public void ExposeData()
        {
            Scribe_Values.Look(ref index, "i", 0);
            Scribe_Values.Look(ref score, "s", 0f);
            Scribe_Values.Look(ref phase, "p", 0);
            Scribe_Values.Look(ref accepted, "a", false);
            Scribe_Values.Look(ref generation, "g", 0);
            Scribe_Values.Look(ref dayOfEpochEnd, "d", 0);
        }
    }

    /// <summary>
    /// A (1+1) evolution strategy with Rechenberg-style step-size adaptation, specialised
    /// for a noisy, non-stationary environment.
    ///
    /// Each epoch the director plays the colony with one genome and reports a fitness score.
    /// A challenger that beats the incumbent is promoted and the mutation step widens; a
    /// challenger that loses is discarded and the step narrows, so the search anneals from
    /// coarse exploration toward fine tuning.
    ///
    /// Because RimWorld's difficulty rises with wealth and time, an incumbent's score measured
    /// in year one is not comparable to a challenger's score in year five. The engine therefore
    /// periodically re-runs the incumbent (<see cref="recheckEvery"/>) to refresh its score
    /// under current conditions rather than trusting a stale number.
    /// </summary>
    public class EvolutionEngine : IExposable
    {
        public const int MaxHistory = 60;

        // --- search state ---
        StrategyGenome incumbent = StrategyGenome.Default();
        StrategyGenome challenger;
        StrategyGenome bestEver;

        public float incumbentScore = float.NaN;   // NaN until first measurement
        public float bestEverScore = float.NaN;
        public int incumbentSamples;

        public float sigma = 0.15f;
        public float mutationRate = 0.3f;
        public int recheckEvery = 3;

        public EpochPhase phase = EpochPhase.Challenger;
        public int epochIndex;
        public int epochsSinceRecheck;
        public int acceptedCount;

        public List<EpochRecord> history = new List<EpochRecord>();

        const float SigmaMin = 0.01f;
        const float SigmaMax = 0.6f;
        const float ExpandFactor = 1.4f;
        const float ContractFactor = 0.8f;

        public EvolutionEngine()
        {
            challenger = incumbent.Clone();
            bestEver = incumbent.Clone();
        }

        /// <summary>The genome the director should currently play with.</summary>
        public StrategyGenome Active
        {
            get { return phase == EpochPhase.Challenger ? challenger : incumbent; }
        }

        public StrategyGenome Incumbent { get { return incumbent; } }
        public StrategyGenome BestEver { get { return bestEver; } }

        /// <summary>Seeds the search from a previously learned strategy (cross-save carryover).</summary>
        public void SeedFrom(StrategyGenome seed, float seedScore, float startSigma)
        {
            if (seed == null) return;
            incumbent = seed.Clone();
            bestEver = seed.Clone();
            incumbentScore = seedScore;
            bestEverScore = seedScore;
            // Prior scores came from a different colony; treat them as weak evidence.
            incumbentSamples = 1;
            sigma = AcMath.Clamp(startSigma, SigmaMin, SigmaMax);
            challenger = incumbent.Mutate(sigma, mutationRate);

            // Measure the archived strategy itself before mutating away from it. Starting on a
            // challenger epoch would adopt that mutant as the baseline sight unseen and throw
            // away the very strategy we just loaded.
            phase = EpochPhase.IncumbentRecheck;
            epochsSinceRecheck = 0;
        }

        /// <summary>
        /// Reports the fitness achieved over the epoch just finished and advances the search.
        /// </summary>
        public void OnEpochComplete(float score, int currentDay)
        {
            bool accepted = false;

            if (phase == EpochPhase.Challenger)
            {
                if (float.IsNaN(incumbentScore))
                {
                    // First ever measurement: nothing to compare against, adopt it as baseline.
                    incumbent = challenger.Clone();
                    incumbentScore = score;
                    incumbentSamples = 1;
                    accepted = true;
                }
                else if (score > incumbentScore)
                {
                    incumbent = challenger.Clone();
                    incumbentScore = score;
                    incumbentSamples = 1;
                    sigma = AcMath.Clamp(sigma * ExpandFactor, SigmaMin, SigmaMax);
                    accepted = true;
                    acceptedCount++;
                }
                else
                {
                    sigma = AcMath.Clamp(sigma * ContractFactor, SigmaMin, SigmaMax);
                }
            }
            else
            {
                // Incumbent re-measured under current conditions: blend, don't replace outright.
                incumbentSamples++;
                float alpha = Math.Max(0.25f, 1f / incumbentSamples);
                incumbentScore = float.IsNaN(incumbentScore)
                    ? score
                    : incumbentScore * (1f - alpha) + score * alpha;
            }

            if (float.IsNaN(bestEverScore) || incumbentScore > bestEverScore)
            {
                bestEverScore = incumbentScore;
                bestEver = incumbent.Clone();
            }

            RecordHistory(score, accepted, currentDay);

            epochIndex++;
            epochsSinceRecheck++;

            // Choose what to run next.
            if (recheckEvery > 0 && epochsSinceRecheck >= recheckEvery)
            {
                phase = EpochPhase.IncumbentRecheck;
                epochsSinceRecheck = 0;
            }
            else
            {
                phase = EpochPhase.Challenger;
                challenger = incumbent.Mutate(sigma, mutationRate);
            }
        }

        void RecordHistory(float score, bool accepted, int currentDay)
        {
            var rec = new EpochRecord();
            rec.index = epochIndex;
            rec.score = score;
            rec.phase = (int)phase;
            rec.accepted = accepted;
            rec.generation = Active != null ? Active.generation : 0;
            rec.dayOfEpochEnd = currentDay;
            history.Add(rec);
            if (history.Count > MaxHistory) history.RemoveAt(0);
        }

        /// <summary>Score trend over the last <paramref name="n"/> epochs, for the status UI.</summary>
        public float RecentAverage(int n)
        {
            if (history.Count == 0) return float.NaN;
            int start = Math.Max(0, history.Count - n);
            float sum = 0f;
            int count = 0;
            for (int i = start; i < history.Count; i++)
            {
                sum += history[i].score;
                count++;
            }
            return count > 0 ? sum / count : float.NaN;
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref incumbent, "incumbent");
            Scribe_Deep.Look(ref challenger, "challenger");
            Scribe_Deep.Look(ref bestEver, "bestEver");
            Scribe_Values.Look(ref incumbentScore, "incumbentScore", float.NaN);
            Scribe_Values.Look(ref bestEverScore, "bestEverScore", float.NaN);
            Scribe_Values.Look(ref incumbentSamples, "incumbentSamples", 0);
            Scribe_Values.Look(ref sigma, "sigma", 0.15f);
            Scribe_Values.Look(ref mutationRate, "mutationRate", 0.3f);
            Scribe_Values.Look(ref recheckEvery, "recheckEvery", 3);
            Scribe_Values.Look(ref phase, "phase", EpochPhase.Challenger);
            Scribe_Values.Look(ref epochIndex, "epochIndex", 0);
            Scribe_Values.Look(ref epochsSinceRecheck, "epochsSinceRecheck", 0);
            Scribe_Values.Look(ref acceptedCount, "acceptedCount", 0);
            Scribe_Collections.Look(ref history, "history", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (incumbent == null) incumbent = StrategyGenome.Default();
                if (challenger == null) challenger = incumbent.Mutate(sigma, mutationRate);
                if (bestEver == null) bestEver = incumbent.Clone();
                if (history == null) history = new List<EpochRecord>();
            }
        }
    }

    /// <summary>Local clamp helper so the learning layer stays free of UnityEngine references.</summary>
    internal static class AcMath
    {
        public static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public static float Clamp01(float v)
        {
            return Clamp(v, 0f, 1f);
        }
    }
}
