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
        int lastPlanTick = -999999;
        int lastStateTick = -999999;
        int lastVitalsTick = -999999;

        /// <summary>
        /// Last known condition of each colonist, so a disappearance can be reported with what
        /// they looked like just before it. "X died" is far less useful than "X died at 14%
        /// health while a fire was burning".
        /// </summary>
        readonly Dictionary<string, string> colonistVitals = new Dictionary<string, string>();
        List<ScoreTerm> lastBreakdown = new List<ScoreTerm>();
        readonly DirectorContext ctx = new DirectorContext();
        readonly Goals.GoalPlanner planner = new Goals.GoalPlanner();
        string lastPlanDescription = "";

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

        /// <summary>
        /// Makes a module run on the next tick instead of waiting out its interval. For
        /// emergencies that cannot wait for the round-robin to come around — a fire will not
        /// pause while work priorities are three in-game hours from being reconsidered.
        /// </summary>
        public void ForceModuleDue(string moduleName)
        {
            EnsureModules();
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i].Name != moduleName) continue;
                modules[i].lastRunTick = -999999;
                return;
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
            Chronicle.BeginSession(ColonyName());
        }

        public override void LoadedGame()
        {
            EnsureModules();
            Chronicle.BeginSession(ColonyName());
            TimeControl.NotifyGameLoaded();

            // A save can carry an epoch deadline that had already passed when it was written.
            // Left alone it closes on the first tick after loading. Clearing it defers to the
            // normal "no epoch running" path, which starts one once there are fresh metrics.
            if (nextEpochTick >= 0 && Find.TickManager != null &&
                Find.TickManager.TicksGame >= nextEpochTick)
            {
                nextEpochTick = -1;
            }
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

            // Throttled inside, so calling it per tick costs one comparison.
            Chronicle.Heartbeat(tick, HeartbeatStatus());

            if (tick - lastStateTick >= StateInterval)
            {
                lastStateTick = tick;
                lastState = ColonyState.Capture(map);
                lastState.AnnotateProximity(
                    layout.established ? layout.origin : map.Center,
                    AutoColonyMod.Settings.masterEnabled && evolution.Active != null
                        ? evolution.Active.Get(Genes.FireResponseRadius)
                        : 45f);
                lastMetrics = lastState.ToMetrics();

                // What the planner is answering right now, carried into scoring. An outcome
                // figure cannot tell a colony that spent the epoch building from one that spent
                // it firefighting, and they are not equally well run.
                lastMetrics.inEmergency = ctx.plan != null && ctx.plan.EmergencyActive;

                if (settings.masterEnabled) accumulator.Observe(lastMetrics);

                TrackColonists(lastState);
                if (tick - lastVitalsTick >= VitalsInterval)
                {
                    lastVitalsTick = tick;
                    Chronicle.RecordVitals(lastMetrics);
                }
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

            // Re-planned with the snapshot, not with the frame.
            //
            // The plan is derived almost entirely from `ctx.state`, which only changes every
            // StateInterval ticks — so running it per tick recomputed the same answer about
            // twelve hundred times over, and each pass allocated a plan, walked ten goals,
            // read the weather for the fire model and did a string lookup per level of the
            // research tree. Every module that reads it runs at 600 ticks or slower.
            if (ctx.plan == null || tick - lastPlanTick >= StateInterval)
            {
                lastPlanTick = tick;
                ctx.plan = planner.Plan(ctx);

                // Only when the answer changes, otherwise this would say the same thing forever.
                var description = ctx.plan.Describe();
                if (description != lastPlanDescription)
                {
                    lastPlanDescription = description;
                    Chronicle.Record(ChronicleCategory.Economy, "working towards — " + description +
                        (ctx.plan.Focus != null ? "  [" + ctx.plan.Focus.Explain(ctx) + "]" : ""));
                }
            }

            RunNextDueModule(tick, settings);

            if (tick >= nextEpochTick) CloseEpoch(tick);
        }

        /// <summary>
        /// The shortest useful description of what the run is doing, for the heartbeat. Enough
        /// that a watcher seeing one line every two minutes knows whether to look closer.
        /// </summary>
        string HeartbeatStatus()
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "day {0}, {1} colonists, {2:0.0}d food, {3}",
                lastMetrics.day, lastMetrics.colonists, lastMetrics.daysOfFood,
                TrainingSession.Active ? TrainingSession.StatusLine : "playing live");
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
            // An epoch nobody watched is not a result. With no samples the evaluator falls back
            // on its defaults and returns the same number every time, which is indistinguishable
            // from a real score to everything downstream — the engine, the bandits, the archive.
            // Start a fresh one instead and let it actually run.
            if (!accumulator.Scorable)
            {
                AcLog.Verbose("Epoch closed with only " + accumulator.samples +
                              " samples — restarting it rather than scoring nothing.");
                BeginEpoch(tick);
                return;
            }

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

            // How much epoch there was, alongside what it scored. The degenerate-epoch bug —
            // 58 of 62 scores identical, because every trial re-scored an epoch that had already
            // elapsed — was only caught because those scores shared a timestamp. Carrying the
            // sample count and elapsed days would have made it obvious on the first line.
            Chronicle.Record(ChronicleCategory.Learning, string.Format(
                "epoch {0} scored {1:0.000} ({2}) over {3} days from {4} samples — {5}",
                evolution.epochIndex - 1, score, phaseBefore,
                lastMetrics.day - epochStart.day, accumulator.samples,
                DescribeBreakdown(breakdown)));

            Chronicle.Record(ChronicleCategory.Learning, string.Format(
                "epoch {0} conduct — {1:0}% of it answering an emergency, {2:0.0} mood per survey " +
                "lost to problems with no remedy{3}, {4} actions undone",
                evolution.epochIndex - 1,
                accumulator.EmergencyFraction * 100f,
                accumulator.AvgUnmetComplaints,
                string.IsNullOrEmpty(accumulator.worstComplaint)
                    ? ""
                    : " (worst: " + accumulator.worstComplaint + " at " +
                      accumulator.worstComplaintMood.ToString("0.0") + ")",
                accumulator.wastedActions));

            ContributeToArchive();

            // The new epoch starts BEFORE the snapshot, not after.
            //
            // BeginRound writes a save, and this method only runs because the epoch was already
            // due — so snapshotting first captured a game whose epoch had elapsed and whose
            // accumulator still held the finished epoch's samples. Every trial then reloaded
            // that, closed an epoch on its first tick, and re-scored the epoch that had already
            // been scored. An overnight run produced 58 identical scores out of 62 that way, and
            // the search saw nothing else for six hours.
            BeginEpoch(tick);

            // With training on, the cycle alternates: one epoch played live so the colony
            // actually advances, then a round of trials replayed over the next stretch.
            if (AutoColonyMod.Settings.trainingMode)
                TrainingSession.BeginRound(evolution, AutoColonyMod.Settings.trialCandidates);
        }

        static string ColonyName()
        {
            try
            {
                return Find.World != null && Find.World.info != null ? Find.World.info.name : "unknown";
            }
            catch (Exception) { return "unknown"; }
        }

        /// <summary>Renders the weakest scoring terms, which is what explains a bad epoch.</summary>
        static string DescribeBreakdown(List<ScoreTerm> breakdown)
        {
            if (breakdown == null || breakdown.Count == 0) return "no breakdown";
            var sorted = new List<ScoreTerm>(breakdown);
            sorted.Sort((a, b) => a.raw.CompareTo(b.raw));

            var sb = new System.Text.StringBuilder("weakest: ");
            int n = Math.Min(3, sorted.Count);
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(sorted[i].name).Append(' ').Append(sorted[i].raw.ToString("0.00"));
            }
            return sb.ToString();
        }

        /// <summary>Colony vitals are written to the chronicle every two in-game hours.</summary>
        const int VitalsInterval = GenDate.TicksPerHour * 2;

        /// <summary>
        /// Notices colonists arriving and, more importantly, leaving.
        ///
        /// A name vanishing from the roster is a death or a departure, and the chronicle wants
        /// it with the condition that preceded it — recorded here because by the time anyone
        /// reads the log the pawn is long gone and unqueryable.
        /// </summary>
        void TrackColonists(ColonyState state)
        {
            var seen = new HashSet<string>();

            for (int i = 0; i < state.allColonists.Count; i++)
            {
                var pawn = state.allColonists[i];
                if (pawn == null) continue;

                string id = pawn.ThingID;
                seen.Add(id);

                float health = pawn.health != null && pawn.health.summaryHealth != null
                    ? pawn.health.summaryHealth.SummaryHealthPercent
                    : 1f;
                float mood = pawn.needs != null && pawn.needs.mood != null ? pawn.needs.mood.CurLevel : -1f;

                string summary = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0} (health {1:0.00}{2}{3})",
                    pawn.LabelShort, health,
                    mood >= 0f ? ", mood " + mood.ToString("0.00") : "",
                    pawn.Downed ? ", downed" : "");

                if (!colonistVitals.ContainsKey(id))
                    Chronicle.Record(ChronicleCategory.Health, "joined: " + summary);

                colonistVitals[id] = summary;
            }

            if (colonistVitals.Count == seen.Count) return;

            var gone = new List<string>();
            foreach (var kv in colonistVitals)
                if (!seen.Contains(kv.Key)) gone.Add(kv.Key);

            for (int i = 0; i < gone.Count; i++)
            {
                Chronicle.Record(ChronicleCategory.Death,
                    "lost from roster — last seen as " + colonistVitals[gone[i]]);
                colonistVitals.Remove(gone[i]);
            }
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

            // The cause, not just the score. Everything needed to say why was already in memory
            // at this moment and used to be thrown away, so every post-mortem was reconstructed
            // by hand from the preceding fifty lines of log.
            Chronicle.Record(ChronicleCategory.Death, Postmortem.Describe(LossEvidenceNow()));
            Chronicle.Record(ChronicleCategory.Death, "final score " + score.ToString("0.000") +
                             " — " + DescribeBreakdown(breakdown));
            Chronicle.Flush();

            if (TrainingSession.Active)
            {
                CloseTrainingTrial(score, tick);
                return;
            }

            evolution.OnEpochComplete(score, lastMetrics.day);
            ContributeToArchive();
        }

        /// <summary>Gathers what is known about the colony at the moment it ended.</summary>
        LossEvidence LossEvidenceNow()
        {
            var e = new LossEvidence();
            e.day = lastMetrics.day;
            e.samples = accumulator.samples;
            e.daysOfFood = lastMetrics.daysOfFood;
            e.minDaysOfFood = accumulator.samples > 0 ? accumulator.minDaysOfFood : lastMetrics.daysOfFood;
            e.avgMood = accumulator.AvgMood;
            e.avgHealth = accumulator.AvgHealth;
            e.downedFraction = accumulator.DownedFraction;
            e.fireFraction = accumulator.FireFraction;
            e.mentalBreakFraction = accumulator.MentalBreakFraction;
            e.deaths = accumulator.DeathsThisEpoch;
            e.raids = accumulator.RaidsThisEpoch;
            e.worstComplaint = accumulator.worstComplaint;
            e.worstComplaintMood = accumulator.worstComplaintMood;
            return e;
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
            if (Scribe.mode == LoadSaveMode.Saving) Chronicle.Flush();

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
