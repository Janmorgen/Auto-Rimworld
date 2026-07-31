using System;
using System.Collections.Generic;
using AutoColony.Learning;
using RimWorld;
using Verse;
using Verse.Profile;

namespace AutoColony
{
    /// <summary>
    /// Runs several candidate strategies against an identical world and keeps the winner.
    ///
    /// A colony's score is far noisier than the difference between two decent strategies, so
    /// judging one strategy per epoch and comparing across epochs is close to hopeless — the
    /// offline tests measure the sequential search flatlining once noise reaches ~0.02, which
    /// is well below what a real colony produces.
    ///
    /// The fix is to remove the noise rather than average it away. Each round snapshots the
    /// game, then replays the same stretch of time once per candidate, reloading the snapshot
    /// and re-seeding the game's RNG identically each time. Every candidate therefore meets
    /// the same raids, the same weather and the same traders, so the shared luck that dominates
    /// a colony score cancels out of the comparison and what remains is mostly strategy. The
    /// same tests show this buys roughly a fourfold tolerance to noise.
    ///
    /// State lives in statics because loading a save destroys the <see cref="AutoColonyDirector"/>
    /// along with the rest of the <c>Game</c>; only the assembly survives a reload.
    /// </summary>
    public static class TrainingSession
    {
        /// <summary>Dedicated save slot. Never touches the player's own saves or autosaves.</summary>
        public const string BaselineSaveName = "AutoColony_trial_baseline";

        public static bool Active;
        public static int RoundIndex;
        public static int TrialIndex;
        public static int TrialSeed;

        public static List<StrategyGenome> Candidates = new List<StrategyGenome>();
        public static List<float> Scores = new List<float>();

        /// <summary>Set when a reload has been queued, to stop the director acting meanwhile.</summary>
        public static bool ReloadPending;

        public static StrategyGenome CurrentCandidate
        {
            get
            {
                if (!Active || Candidates == null) return null;
                if (TrialIndex < 0 || TrialIndex >= Candidates.Count) return null;
                return Candidates[TrialIndex];
            }
        }

        public static string StatusLine
        {
            get
            {
                if (!Active) return "not training";
                return "round " + RoundIndex + ", trial " + (TrialIndex + 1) + "/" + Candidates.Count;
            }
        }

        /// <summary>
        /// Training repeatedly reloads the game, which is incompatible with permadeath and
        /// pointless without a colony to snapshot.
        /// </summary>
        public static bool CanTrain(out string reason)
        {
            reason = null;
            if (Current.Game == null) { reason = "no active game"; return false; }
            if (Current.Game.Info != null && Current.Game.Info.permadeathMode)
            {
                reason = "permadeath mode forbids reloading";
                return false;
            }
            if (Find.CurrentMap == null) { reason = "no map loaded"; return false; }
            return true;
        }

        /// <summary>Snapshots the game and queues the first candidate.</summary>
        public static bool BeginRound(EvolutionEngine evolution, int candidateCount)
        {
            string reason;
            if (!CanTrain(out reason))
            {
                AcLog.WarningOnce("noTrain", "Training mode unavailable: " + reason);
                return false;
            }

            if (candidateCount < 2) candidateCount = 2;

            try
            {
                GameDataSaveLoader.SaveGame(BaselineSaveName);
            }
            catch (Exception e)
            {
                AcLog.Error("Could not snapshot the game for a training round: " + e.Message);
                return false;
            }

            Candidates = evolution.SpawnCandidates(candidateCount);
            Scores = new List<float>();
            for (int i = 0; i < Candidates.Count; i++) Scores.Add(float.NaN);

            // One seed for the whole round. Every trial replays the same world from it, which
            // is what makes the candidates' scores comparable.
            TrialSeed = Find.TickManager.TicksGame ^ (RoundIndex * 7919);
            TrialIndex = 0;
            Active = true;
            ReloadPending = false;

            ApplyTrialSeed();

            AcLog.Message("Training round " + RoundIndex + " begins: " + Candidates.Count +
                          " candidates on seed " + TrialSeed + ".");
            return true;
        }

        /// <summary>
        /// Forces the game's RNG to the round's seed so each trial rolls the same events.
        ///
        /// This is the one place the mod deliberately writes to RimWorld's global RNG — the
        /// whole point is that every trial should experience an identical world. Divergence
        /// still creeps in once the colonies differ enough to consume draws at different
        /// rates, so the early part of each trial is the most comparable part.
        /// </summary>
        public static void ApplyTrialSeed()
        {
            try
            {
                Rand.EnsureStateStackEmpty();
                Rand.Seed = TrialSeed;
            }
            catch (Exception e)
            {
                AcLog.WarningOnce("seedFail", "Could not reseed for a trial: " + e.Message);
            }
        }

        /// <summary>
        /// Records a finished trial and returns the winning genome once the round is complete,
        /// or null if more trials remain.
        /// </summary>
        public static bool RecordTrialAndAdvance(float score, out StrategyGenome winner, out float winnerScore)
        {
            winner = null;
            winnerScore = float.NaN;

            if (!Active || Candidates == null || Candidates.Count == 0) return true;

            if (TrialIndex >= 0 && TrialIndex < Scores.Count) Scores[TrialIndex] = score;
            AcLog.Message("  trial " + (TrialIndex + 1) + "/" + Candidates.Count +
                          " scored " + score.ToString("0.000"));

            TrialIndex++;
            if (TrialIndex < Candidates.Count) return false;

            // Round complete: pick the best-scoring candidate.
            int bestIndex = 0;
            float best = float.NegativeInfinity;
            for (int i = 0; i < Scores.Count; i++)
            {
                if (float.IsNaN(Scores[i])) continue;
                if (Scores[i] > best) { best = Scores[i]; bestIndex = i; }
            }

            winner = Candidates[bestIndex];
            winnerScore = best;

            AcLog.Message("Training round " + RoundIndex + " won by candidate " + (bestIndex + 1) +
                          " with " + best.ToString("0.000") +
                          (bestIndex == 0 ? " (the incumbent held)" : " (a mutant improved on it)"));

            RoundIndex++;
            return true;
        }

        public static void End()
        {
            Active = false;
            TrialIndex = 0;
            Candidates = new List<StrategyGenome>();
            Scores = new List<float>();
        }

        /// <summary>
        /// Queues a reload of the round's snapshot. Runs as a long event because the game
        /// cannot be torn down from inside the tick loop that called this.
        /// </summary>
        public static void QueueBaselineReload()
        {
            if (ReloadPending) return;
            ReloadPending = true;

            LongEventHandler.QueueLongEvent(delegate
            {
                try
                {
                    MemoryUtility.ClearAllMapsAndWorld();
                    Current.Game = new Game();
                    Current.Game.InitData = new GameInitData();
                    Current.Game.InitData.gameToLoad = BaselineSaveName;
                }
                catch (Exception e)
                {
                    AcLog.Error("Training reload failed, abandoning the round: " + e);
                    End();
                    ReloadPending = false;
                }
            }, "Play", "LoadingLongEvent", true, null);
        }

        /// <summary>Called by a freshly loaded director so the next trial starts cleanly.</summary>
        public static void OnGameLoaded()
        {
            ReloadPending = false;
            if (Active) ApplyTrialSeed();
        }

        /// <summary>Abandons training entirely, e.g. when the player switches it off.</summary>
        public static void Abort(string why)
        {
            if (!Active) return;
            AcLog.Message("Training aborted: " + why);
            End();
            ReloadPending = false;
        }
    }
}
