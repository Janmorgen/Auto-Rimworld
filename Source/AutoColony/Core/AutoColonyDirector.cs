using System;
using System.Collections.Generic;
using AutoColony.Learning;
using AutoColony.Modules;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutoColony
{
    /// <summary>A discrete choice awaiting the epoch score that will judge it.</summary>
    public class PendingCredit : IExposable
    {
        public string banditId = "";
        public string arm = "";

        public void ExposeData()
        {
            Scribe_Values.Look(ref banditId, "b", "");
            Scribe_Values.Look(ref arm, "a", "");
        }
    }

    /// <summary>
    /// The agent that plays the colony.
    ///
    /// Structure is a straightforward perceive-decide-act loop wrapped in an outer learning
    /// loop. Each tick it may run at most one scheduled module, so the whole director costs
    /// a fraction of a tick's budget no matter how many modules exist. Every fixed-length
    /// epoch it scores how the colony fared, hands that score to the evolution engine and to
    /// the bandits whose choices were used, and starts the next epoch with whatever strategy
    /// the search now believes is best.
    /// </summary>
    public class AutoColonyDirector : GameComponent
    {
        /// <summary>Ticks between colony state captures. 1250 ticks is half an in-game hour.</summary>
        const int StateInterval = 1250;

        // --- persistent learning state ---
        public EvolutionEngine evolution = new EvolutionEngine();
        public EpochAccumulator accumulator = new EpochAccumulator();
        public EpochStart epochStart = new EpochStart();
        public BaseLayout layout = new BaseLayout();
        public List<PendingCredit> pendingCredits = new List<PendingCredit>();
        public PlayerModel playerModel = new PlayerModel();

        Dictionary<string, Bandit> bandits = new Dictionary<string, Bandit>();

        public string contextKey = StrategyArchive.GlobalKey;
        public int nextEpochTick = -1;
        public bool seeded;

        /// <summary>Set once a wipe has been scored, so it is recorded exactly once.</summary>
        public bool colonyLost;
        public float lastScore = float.NaN;

        // --- runtime only, rebuilt after load ---
        List<DirectorModule> modules;
        readonly PlayerObservationModule observer = new PlayerObservationModule();
        int moduleCursor;
        ColonyState lastState;
        ColonyMetrics lastMetrics = ColonyMetrics.Neutral();
        int lastStateTick = -999999;
        List<ScoreTerm> lastBreakdown = new List<ScoreTerm>();
        readonly DirectorContext ctx = new DirectorContext();

        public AutoColonyDirector(Game game) { }

        public ColonyState LastState { get { return lastState; } }
        public List<ScoreTerm> LastBreakdown { get { return lastBreakdown; } }
        public List<DirectorModule> Modules { get { EnsureModules(); return modules; } }
        /// <summary>
        /// The strategy currently being played. During a training round that is the trial's
        /// candidate rather than whatever the evolution engine would otherwise be testing.
        /// </summary>
        public StrategyGenome ActiveGenome
        {
            get
            {
                var candidate = TrainingSession.CurrentCandidate;
                return candidate ?? evolution.Active;
            }
        }

        public Bandit BanditFor(string id)
        {
            Bandit b;
            if (!bandits.TryGetValue(id, out b))
            {
                b = new Bandit();
                bandits[id] = b;
            }
            return b;
        }

        public void CreditLater(string banditId, string arm)
        {
            if (string.IsNullOrEmpty(banditId) || string.IsNullOrEmpty(arm)) return;
            // One credit per arm per epoch; repeats would just scale the same signal.
            for (int i = 0; i < pendingCredits.Count; i++)
                if (pendingCredits[i].banditId == banditId && pendingCredits[i].arm == arm) return;

            var pc = new PendingCredit();
            pc.banditId = banditId;
            pc.arm = arm;
            pendingCredits.Add(pc);
        }

        // ---------------------------------------------------------------- lifecycle

        public override void FinalizeInit()
        {
            EnsureModules();
        }

        public override void StartedNewGame()
        {
            EnsureModules();
            seeded = false;
        }

        public override void LoadedGame()
        {
            EnsureModules();
            TimeControl.NotifyGameLoaded();
            // A load may be the game coming back up mid-training round; re-apply the seed so
            // this trial sees the same world as its siblings.
            TrainingSession.OnGameLoaded();
        }

        void EnsureModules()
        {
            if (modules == null) modules = DirectorModules.CreateAll();
        }

        // ---------------------------------------------------------------- main loop

        /// <summary>
        /// Time control cannot live on the tick: a paused game issues no ticks, so a director
        /// that only acted in <see cref="GameComponentTick"/> could never undo a pause it did
        /// not choose.
        ///
        /// It is driven from both of the non-tick hooks. <see cref="GameComponentUpdate"/>
        /// alone was measured *not* to be enough — with the game paused by hand the colony
        /// stopped dead and never resumed — whereas OnGUI keeps running because the interface
        /// stays interactive while paused. Update is kept as well since it is the cheaper of
        /// the two while the game is running normally, and the work is throttled and
        /// idempotent, so being called from both costs nothing.
        /// </summary>
        public override void GameComponentUpdate()
        {
            TimeControl.Update();
        }

        public override void GameComponentOnGUI()
        {
            // OnGUI is raised for both the layout and repaint passes. Closing a window during
            // layout desynchronises Unity's IMGUI control counts and throws, so only act on
            // repaint, by which point the frame's layout is settled.
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            TimeControl.Update();
        }

        public override void GameComponentTick()
        {
            var settings = AutoColonyMod.Settings;
            if (settings == null) return;

            // Nothing to do at all unless we are either playing or watching.
            if (!settings.masterEnabled && !settings.learnFromPlayer) return;

            // A reload is queued; the game is about to be torn down under us.
            if (TrainingSession.ReloadPending) return;

            var map = TargetMap();
            if (map == null) return;

            int tick = Find.TickManager.TicksGame;

            if (tick - lastStateTick >= StateInterval)
            {
                lastStateTick = tick;
                lastState = ColonyState.Capture(map);
                lastMetrics = lastState.ToMetrics();
                if (settings.masterEnabled) accumulator.Observe(lastMetrics);
            }

            if (lastState == null) return;

            // Total colony failure is the most informative outcome the search will ever get,
            // and it used to be dropped silently: with nobody left alive there is no module to
            // run, so this method returned before the epoch could be scored. The strategy that
            // lost the colony was never penalised, here or in the cross-save archive, which is
            // precisely backwards — a wipe is the one result worth learning from most.
            if (!lastState.Valid)
            {
                if (settings.masterEnabled && !colonyLost && nextEpochTick >= 0)
                {
                    colonyLost = true;
                    HandleColonyLoss(tick);
                }
                return;
            }

            colonyLost = false;

            ctx.map = map;
            ctx.state = lastState;
            ctx.director = this;
            ctx.layout = layout;

            // Automation off: watch and learn, but never act.
            if (!settings.masterEnabled)
            {
                ctx.genome = evolution.Active;
                if (observer.ShouldRun(tick)) observer.Run(ctx, tick);
                return;
            }

            if (!seeded) SeedFromArchive(map);
            if (nextEpochTick < 0) BeginEpoch(tick);

            ctx.genome = ActiveGenome;

            RunNextDueModule(tick, settings);

            if (tick >= nextEpochTick) CloseEpoch(tick);
        }

        /// <summary>
        /// Runs at most one due module per tick. Spreading the work this way keeps the
        /// director's per-tick cost flat as modules are added.
        /// </summary>
        void RunNextDueModule(int tick, AutoColonySettings settings)
        {
            EnsureModules();
            for (int i = 0; i < modules.Count; i++)
            {
                moduleCursor = (moduleCursor + 1) % modules.Count;
                var m = modules[moduleCursor];
                if (!settings.IsModuleEnabled(m.Name)) continue;
                if (!m.ShouldRun(tick)) continue;
                m.Run(ctx, tick);
                return;
            }
        }

        static Map TargetMap()
        {
            var current = Find.CurrentMap;
            if (current != null && current.IsPlayerHome) return current;

            var maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
                if (maps[i] != null && maps[i].IsPlayerHome) return maps[i];
            return null;
        }

        // ---------------------------------------------------------------- epochs

        void BeginEpoch(int tick)
        {
            epochStart = EpochStart.From(lastMetrics);
            accumulator.ResetFor(lastMetrics);
            pendingCredits.Clear();

            int lengthTicks = Math.Max(1, AutoColonyMod.Settings.epochDays) * GenDate.TicksPerDay;
            nextEpochTick = tick + lengthTicks;

            AcLog.Verbose("Epoch " + evolution.epochIndex + " begins (" +
                          evolution.phase + ", genome gen " + evolution.Active.generation + ")");
        }

        void CloseEpoch(int tick)
        {
            List<ScoreTerm> breakdown;
            float score = ColonyEvaluator.Evaluate(epochStart, lastMetrics, accumulator, out breakdown);

            lastScore = score;
            lastBreakdown = breakdown;

            // Credit every discrete choice made during the epoch with the epoch's outcome.
            for (int i = 0; i < pendingCredits.Count; i++)
            {
                var pc = pendingCredits[i];
                BanditFor(pc.banditId).Update(pc.arm, score);
            }
            pendingCredits.Clear();

            if (TrainingSession.Active)
            {
                CloseTrainingTrial(score, tick);
                return;
            }

            var phaseBefore = evolution.phase;
            evolution.OnEpochComplete(score, lastMetrics.day);

            AcLog.Message(string.Format(
                "Epoch {0} closed: score {1:0.000} ({2}). Incumbent {3:0.000}, sigma {4:0.000}, gen {5}.",
                evolution.epochIndex - 1, score, phaseBefore,
                evolution.incumbentScore, evolution.sigma, evolution.Incumbent.generation));

            ContributeToArchive();

            // With training on, the cycle alternates: one epoch played live so the colony
            // actually advances, then a round of trials replayed over the next stretch.
            if (AutoColonyMod.Settings.trainingMode &&
                TrainingSession.BeginRound(evolution, AutoColonyMod.Settings.trialCandidates))
            {
                BeginEpoch(tick);
                return;
            }

            BeginEpoch(tick);
        }

        /// <summary>
        /// Scores the epoch that ended in the colony dying. Runs the ordinary evaluation,
        /// which bottoms out at zero once there are no colonists, and feeds it everywhere a
        /// normal epoch score would go so the failure actually costs the strategy something.
        /// </summary>
        void HandleColonyLoss(int tick)
        {
            List<ScoreTerm> breakdown;
            float score = ColonyEvaluator.Evaluate(epochStart, lastMetrics, accumulator, out breakdown);

            lastScore = score;
            lastBreakdown = breakdown;

            for (int i = 0; i < pendingCredits.Count; i++)
            {
                var pc = pendingCredits[i];
                BanditFor(pc.banditId).Update(pc.arm, score);
            }
            pendingCredits.Clear();

            AcLog.Message("Colony lost on day " + lastMetrics.day + ". Strategy scored " +
                          score.ToString("0.000") + "; recorded so the search learns from it.");

            if (TrainingSession.Active)
            {
                CloseTrainingTrial(score, tick);
                return;
            }

            evolution.OnEpochComplete(score, lastMetrics.day);
            ContributeToArchive();
        }

        /// <summary>
        /// Ends one trial of a training round: either roll back and run the next candidate,
        /// or adopt the round's winner and return to live play.
        /// </summary>
        void CloseTrainingTrial(float score, int tick)
        {
            StrategyGenome winner;
            float winnerScore;
            bool roundComplete = TrainingSession.RecordTrialAndAdvance(score, out winner, out winnerScore);

            if (!roundComplete)
            {
                // Roll the world back so the next candidate faces exactly the same situation.
                TrainingSession.QueueBaselineReload();
                return;
            }

            if (winner != null)
            {
                // Candidates were judged against an identical world, so these scores are
                // directly comparable and need no noise margin.
                evolution.AdoptWinner(winner, winnerScore, lastMetrics.day);
                ContributeToArchive();
            }

            TrainingSession.End();
            TrainingSession.QueueBaselineReload();
        }

        void ContributeToArchive()
        {
            if (!AutoColonyMod.Settings.shareAcrossSaves) return;
            if (float.IsNaN(evolution.incumbentScore)) return;

            try
            {
                string colonyName = Find.World != null && Find.World.info != null
                    ? Find.World.info.name
                    : "unknown";

                StrategyArchive.Contribute(contextKey, evolution.Incumbent, evolution.incumbentScore,
                                           evolution.epochIndex, colonyName);
                StrategyArchive.ContributeBandits(BanditFor(ResearchModule.BanditId),
                                                 BanditFor(BasePlannerModule.BanditId));
            }
            catch (Exception e)
            {
                AcLog.WarningOnce("archiveContribute", "Could not update strategy archive: " + e.Message);
            }
        }

        /// <summary>
        /// Starts this colony from the best strategy previously learned for a comparable
        /// situation. This is what makes the mod improve across playthroughs rather than
        /// relearning the same lessons in every new colony.
        /// </summary>
        void SeedFromArchive(Map map)
        {
            seeded = true;
            contextKey = BuildContextKey(map);

            if (evolution.epochIndex > 0) return;   // an in-progress search already has its own state

            // What this player demonstrated in this colony beats a strategy learned in someone
            // else's, so an observed model takes precedence over the archive.
            if (AutoColonyMod.Settings.learnFromPlayer && playerModel != null && playerModel.IsUsable)
            {
                evolution.SeedFrom(playerModel.ToGenome(), float.NaN, 0.12f);
                SeedBanditsFromPlayer();
                AcLog.Message("Seeded strategy from " + playerModel.samples +
                              " observations of your own play.");
                return;
            }

            if (!AutoColonyMod.Settings.shareAcrossSaves) return;

            try
            {
                var seed = StrategyArchive.GetSeed(contextKey);
                if (seed == null || seed.genome == null)
                {
                    AcLog.Message("No prior strategy for context '" + contextKey + "'; starting from defaults.");
                    return;
                }

                // Prior scores were earned in a different colony, so re-open the search
                // wider than a converged run would: treat the seed as a strong hint, not truth.
                evolution.SeedFrom(seed.genome, float.NaN, 0.12f);

                BanditFor(ResearchModule.BanditId).MergeFrom(StrategyArchive.ResearchPrior, 1f);
                BanditFor(BasePlannerModule.BanditId).MergeFrom(StrategyArchive.BuildPrior, 1f);

                AcLog.Message("Seeded strategy from archive entry '" + seed.contextKey +
                              "' (score " + seed.score.ToString("0.000") +
                              ", from colony '" + seed.sourceColony + "').");
            }
            catch (Exception e)
            {
                AcLog.Warning("Could not seed from archive: " + e.Message);
            }
        }

        /// <summary>
        /// Gives the player's observed crop and research picks a positive prior, so the
        /// bandits start by trying what the player favoured rather than sampling blindly.
        /// </summary>
        void SeedBanditsFromPlayer()
        {
            var crop = playerModel.FavouriteCrop;
            if (!string.IsNullOrEmpty(crop)) BanditFor(ZoneModule.BanditId).Update(crop, 0.7f);

            var research = playerModel.FavouriteResearch;
            if (!string.IsNullOrEmpty(research)) BanditFor(ResearchModule.BanditId).Update(research, 0.7f);
        }

        static string BuildContextKey(Map map)
        {
            string biome = "unknown";
            string difficulty = "unknown";
            try
            {
                if (map.Biome != null) biome = map.Biome.defName;
                if (Find.Storyteller != null && Find.Storyteller.difficultyDef != null)
                    difficulty = Find.Storyteller.difficultyDef.defName;
            }
            catch (Exception) { }
            return StrategyArchive.BuildContextKey(biome, difficulty);
        }

        /// <summary>Restarts the search from defaults, keeping the cross-save archive intact.</summary>
        public void ResetLearning()
        {
            evolution = new EvolutionEngine();
            bandits = new Dictionary<string, Bandit>();
            accumulator = new EpochAccumulator();
            pendingCredits.Clear();
            nextEpochTick = -1;
            seeded = false;
            lastScore = float.NaN;
            lastBreakdown = new List<ScoreTerm>();
            if (modules != null)
                for (int i = 0; i < modules.Count; i++) modules[i].ResetRuntimeState();
            AcLog.Message("Learning state reset for this colony.");
        }

        // ---------------------------------------------------------------- save/load

        public override void ExposeData()
        {
            Scribe_Deep.Look(ref evolution, "evolution");
            Scribe_Deep.Look(ref accumulator, "accumulator");
            Scribe_Deep.Look(ref epochStart, "epochStart");
            Scribe_Deep.Look(ref layout, "layout");
            Scribe_Deep.Look(ref playerModel, "playerModel");
            Scribe_Collections.Look(ref bandits, "bandits", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref pendingCredits, "pendingCredits", LookMode.Deep);
            Scribe_Values.Look(ref contextKey, "contextKey", StrategyArchive.GlobalKey);
            Scribe_Values.Look(ref nextEpochTick, "nextEpochTick", -1);
            Scribe_Values.Look(ref seeded, "seeded", false);
            Scribe_Values.Look(ref colonyLost, "colonyLost", false);
            Scribe_Values.Look(ref lastScore, "lastScore", float.NaN);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (evolution == null) evolution = new EvolutionEngine();
                if (accumulator == null) accumulator = new EpochAccumulator();
                if (epochStart == null) epochStart = new EpochStart();
                if (layout == null) layout = new BaseLayout();
                if (playerModel == null) playerModel = new PlayerModel();
                if (bandits == null) bandits = new Dictionary<string, Bandit>();
                if (pendingCredits == null) pendingCredits = new List<PendingCredit>();
                if (lastBreakdown == null) lastBreakdown = new List<ScoreTerm>();
            }
        }
    }
}
