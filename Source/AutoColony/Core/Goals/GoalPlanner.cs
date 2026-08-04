using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace AutoColony.Goals
{
    /// <summary>What the colony is working towards this pass, and why.</summary>
    public class ColonyPlan
    {
        /// <summary>The goal actually being pursued — always something actionable today.</summary>
        public ColonyGoal Focus;

        /// <summary>
        /// The goal that was wanted, when the focus is a prerequisite of it. "Refrigeration,
        /// via Power" reads very differently from "Power" alone.
        /// </summary>
        public ColonyGoal Wanted;

        public GoalHorizon Horizon = GoalHorizon.LongTerm;

        /// <summary>Materials the focus needs before it can proceed.</summary>
        public readonly MaterialNeeds Needs = new MaterialNeeds();

        /// <summary>True while something immediate is happening, which halts discretionary work.</summary>
        public bool EmergencyActive;

        /// <summary>
        /// The research project the focus is waiting on, already walked back to something that
        /// can be started today. Null when the focus needs nothing researched, or already has it.
        /// </summary>
        public string ResearchWanted;

        /// <summary>
        /// How hard the colony as a whole is pulling on each material, summed across every
        /// unsatisfied goal rather than taken from the focus alone.
        ///
        /// Only the focus declared needs before, so the colony's wants were whatever the single
        /// top-ranked goal happened to be short of, and every other layer exerted no force at
        /// all. That is not how a colony wants things. Feeding people this afternoon, arming
        /// them against next month's raid and powering a cooler before summer are all live at
        /// once, and steel that serves three of them is worth more than steel that serves one.
        ///
        /// Weighted by horizon and by urgency, so nearer and more pressing goals pull harder
        /// without further ones pulling zero. A long-term want is still a want.
        /// </summary>
        public readonly Dictionary<string, float> NeedPressure = new Dictionary<string, float>();

        /// <summary>
        /// The largest amount of each material any unsatisfied goal asks for.
        ///
        /// plan.Needs is the focus's bill alone, so gathering served whichever single goal was
        /// top-ranked and nothing else: a colony mining toward the focus's 220 steel while a
        /// further goal quietly needed 280 stopped 60 short and re-mobilised later. Max rather
        /// than sum, because two goals wanting 200 wood do not want 400 — they want the store
        /// to reach 200, and the second is satisfied by the same pile.
        /// </summary>
        public readonly Dictionary<string, int> NeedQuantity = new Dictionary<string, int>();

        /// <summary>The most of this material any unsatisfied goal wants. 0 when none do.</summary>
        public int QuantityWanted(string thingDefName)
        {
            int amount;
            return NeedQuantity.TryGetValue(thingDefName, out amount) ? amount : 0;
        }

        /// <summary>
        /// How hard blocked goals are pulling on each research project, by defName — the same
        /// horizon-times-urgency force as NeedPressure, applied to the project each unsatisfied
        /// goal is actually startable-blocked on. The research chooser reads this as a prior,
        /// so a project three goals are waiting on outranks a merely cheap one without any goal
        /// having to be the focus first.
        /// </summary>
        public readonly Dictionary<string, float> ResearchPressure = new Dictionary<string, float>();

        public float PressureForResearch(string projectDefName)
        {
            float force;
            return ResearchPressure.TryGetValue(projectDefName, out force) ? force : 0f;
        }

        /// <summary>Total pull on a material, 0 when nothing wants it.</summary>
        public float PressureFor(string thingDefName)
        {
            float pressure;
            return NeedPressure.TryGetValue(thingDefName, out pressure) ? pressure : 0f;
        }

        /// <summary>
        /// Every room role an unsatisfied goal is still asking for.
        ///
        /// Published so the rest of the director can tell what the colony is *for* right now.
        /// The upkeep survey needs it: a room the plan is still asking for is not surplus,
        /// however short of material the colony is, and tearing one down to recover its walls
        /// buys material with the very thing the material was going to build.
        /// </summary>
        public readonly HashSet<RoomRole> RolesWanted = new HashSet<RoomRole>();

        /// <summary>
        /// Every room role any goal can ever ask for, satisfied or not.
        ///
        /// The difference from RolesWanted is the whole point. That set answers "what is the
        /// colony short of right now", which is the wrong question to ask before pulling a room
        /// down — a room is not surplus because the goal it serves is satisfied, it is satisfied
        /// *because the room is standing*.
        ///
        /// Run 104 built a Research room, finished it, got its bench working, and then reclaimed
        /// it at means 0.07 — because with the bench built ResearchCapacityGoal was satisfied,
        /// so Research was not in RolesWanted and the room read as spare. Demolishing it made
        /// the goal want it again, and the colony sited another one forty cells away that could
        /// not be built. The guard asked whether the plan wanted the room, when the question is
        /// whether the plan would want it back the moment it was gone.
        /// </summary>
        public readonly HashSet<RoomRole> RolesAnyGoalWants = new HashSet<RoomRole>();

        /// <summary>
        /// True while the growth layer wants another colonist — a spare bed stands and the
        /// larder carries a margin. The recruitment levers read this: joiner letters, prisoner
        /// recruitment. Population is grown by intent rather than by accident.
        /// </summary>
        public bool PopulationWanted;

        public string Describe()
        {
            if (Focus == null) return "nothing outstanding";
            var sb = new StringBuilder();
            sb.Append(Horizon).Append(": ").Append(Focus.Name);
            if (Wanted != null && Wanted != Focus) sb.Append(" (towards ").Append(Wanted.Name).Append(')');
            if (Needs.Any) sb.Append(" — needs ").Append(Needs);
            if (ResearchWanted != null) sb.Append(" — needs research ").Append(ResearchWanted);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Decides what the colony should be doing, and in what order.
    ///
    /// Two rules do most of the work. Nearer horizons pre-empt further ones, so nothing
    /// discretionary happens while the colony is burning — and a goal whose prerequisites are
    /// unmet hands over to whichever prerequisite *is* actionable, so wanting a freezer
    /// resolves by itself into wanting power, then into wanting components, then into mining.
    ///
    /// The planner deliberately does not act. It publishes a focus and the modules aim at it,
    /// which keeps the arbitration in one readable place instead of scattered across eleven
    /// subsystems each with its own opinion about what matters.
    /// </summary>
    public class GoalPlanner
    {
        readonly List<ColonyGoal> goals = new List<ColonyGoal>
        {
            new ExtinguishFireGoal(),
            new RepelRaidGoal(),
            new FeedColonyGoal(),

            new ShelterGoal(),
            new StorageGoal(),
            new FarmGoal(),
            new FoodStockGoal(),
            new WeatherClothingGoal(),
            new PreservedFoodGoal(),
            new ResearchCapacityGoal(),

            new MasonryGoal(),
            new WoodSupplyGoal(),
            new PowerGoal(),
            new GrowColonyGoal(),
            new ComfortGoal(),
            new RefrigerationGoal(),
            new FortifyGoal(),
        };

        readonly Dictionary<string, ColonyGoal> byName = new Dictionary<string, ColonyGoal>();

        public GoalPlanner()
        {
            for (int i = 0; i < goals.Count; i++) byName[goals[i].Name] = goals[i];
        }

        public ColonyPlan Plan(DirectorContext ctx)
        {
            var plan = new ColonyPlan();

            int now = CurrentTick();

            // Is anything actually happening? Any unsatisfied immediate goal counts, with no
            // threshold on top of it.
            //
            // The immediate goals already draw that line where it belongs and each of them means
            // something concrete: a fire burning, hostiles at the colony, or less than two days
            // of food. Adding an urgency threshold would move the line and move it the wrong way
            // — "Feed the colony" reports 0.25 at a day and a half, which is under any sensible
            // threshold and is also the exact point past which nothing the colony decides can
            // arrive before the larder is empty. A colony there is not idle enough to be given a
            // research bench.
            bool emergency = AnyImmediateOutstanding(ctx);

            ColonyGoal best = null;
            float bestScore = float.NegativeInfinity;
            float bestUrgency = 0f;

            for (int i = 0; i < goals.Count; i++)
            {
                var goal = goals[i];
                var record = RecordFor(goal);

                // Recorded for every goal, before the satisfied ones are skipped: a satisfied
                // goal is exactly the one whose room must not be taken away.
                if (goal.WantsRoom.HasValue) plan.RolesAnyGoalWants.Add(goal.WantsRoom.Value);

                if (Satisfied(goal, ctx))
                {
                    record.blockedSince = -1;   // nothing to wait for
                    continue;
                }

                float urgency = Urgency(goal, ctx);
                if (record.blockedSince < 0) record.blockedSince = now;

                if (goal.WantsRoom.HasValue) plan.RolesWanted.Add(goal.WantsRoom.Value);
                if (goal is GrowColonyGoal) plan.PopulationWanted = true;

                // What this goal wants, folded into the colony's total pull. The weight is the
                // horizon — Immediate 3, ShortTerm 2, LongTerm 1 — times how badly the goal
                // wants it, so every layer exerts a force proportional to how near and how
                // pressing it is, and none of them exerts none.
                AccumulatePressure(plan, goal, ctx, urgency);

                float score = ScoreOf(goal, ctx, now, emergency);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = goal;
                    bestUrgency = urgency;
                }
            }

            if (best == null) return plan;

            lastWanted = best.Name;
            WatchTheFocus(best, bestUrgency, now, ctx);

            plan.Wanted = best;
            plan.EmergencyActive = best.Horizon == GoalHorizon.Immediate;

            // Walk back to something that can actually be started today.
            plan.Focus = Actionable(best, ctx, 0);
            plan.Horizon = plan.Focus.Horizon;
            plan.Focus.DeclareNeeds(ctx, plan.Needs);
            plan.ResearchWanted = ResearchFor(plan.Focus);

            return plan;
        }

        /// <summary>
        /// What the focus needs researched, walked back to a project that can be started now.
        ///
        /// Every building in the power chain is gated: conduits, the wood-fired generator and
        /// the electric stove need Electricity, batteries need Batteries, coolers need Air
        /// Conditioning. Without this the plan could sit on "Power" indefinitely while research
        /// was chosen on cheapness alone.
        /// </summary>
        static string ResearchFor(ColonyGoal goal)
        {
            var wanted = goal.RequiresResearch;
            if (wanted == null || wanted.Length == 0) return null;

            try
            {
                return ResearchChain.FirstStartableOf(wanted, PrerequisitesOf, IsFinished);
            }
            catch (System.Exception)
            {
                return null;    // a broken lookup must not stop the rest of the plan
            }
        }

        static IList<string> PrerequisitesOf(string defName)
        {
            var project = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName);
            if (project == null || project.prerequisites == null) return null;

            var names = new List<string>(project.prerequisites.Count);
            for (int i = 0; i < project.prerequisites.Count; i++)
            {
                var prerequisite = project.prerequisites[i];
                if (prerequisite != null) names.Add(prerequisite.defName);
            }
            return names;
        }

        /// <summary>
        /// A project the database has never heard of counts as finished, so a goal naming
        /// research from a DLC or mod that is not installed degrades to "nothing to research"
        /// rather than becoming a prerequisite that can never be met.
        /// </summary>
        static bool IsFinished(string defName)
        {
            var project = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName);
            return project == null || project.IsFinished;
        }

        /// <summary>
        /// Follows unmet prerequisites down to the first goal with none outstanding. Depth is
        /// capped so a mistakenly circular dependency degrades to "work on this" rather than
        /// hanging the game.
        /// </summary>
        ColonyGoal Actionable(ColonyGoal goal, DirectorContext ctx, int depth)
        {
            if (depth > 6) return goal;

            var requires = goal.Requires;
            for (int i = 0; i < requires.Length; i++)
            {
                ColonyGoal prerequisite;
                if (!byName.TryGetValue(requires[i], out prerequisite)) continue;
                if (Satisfied(prerequisite, ctx)) continue;
                return Actionable(prerequisite, ctx, depth + 1);
            }
            return goal;
        }

        // ------------------------------------------------------------ pacing

        /// <summary>
        /// Urgency below which a goal is wanted rather than needed.
        ///
        /// Used only to decide whether a goal keeps its horizon's precedence — never to decide
        /// whether the colony is in trouble, which the immediate goals answer for themselves.
        /// Most short-term goals report urgency as the fraction of a generous target still
        /// missing, so this is roughly "more than a third short of what it wants".
        /// </summary>
        const float PressingUrgency = 0.4f;

        /// <summary>
        /// How much better a rival has to be to take the plan off whatever holds it.
        ///
        /// Larger than the spread that separates the long-term goals from each other — six
        /// hundredths of a point, measured — and vastly smaller than the hundred points between
        /// horizons, so it settles chatter without ever standing in front of an emergency.
        /// </summary>
        const float IncumbentMargin = 0.15f;

        /// <summary>The goal the plan settled on last pass, which keeps a head start on this one.</summary>
        string lastWanted;

        /// <summary>How long a goal may be passed over before it is given a turn. Three days.</summary>
        const int BlockedTicksBeforeATurn = 180000;

        /// <summary>
        /// How long a goal holds the focus before it has to show progress. Half a day.
        ///
        /// Long enough that ordinary work — walking to the site, clearing it, hauling the
        /// material — is not mistaken for failure.
        /// </summary>
        const int FocusGraceTicks = 30000;

        /// <summary>How long a goal that failed to move is passed over afterwards. One day.</summary>
        const int DemotionTicks = 60000;

        class GoalRecord
        {
            public int blockedSince = -1;
            public int focusSince = -1;
            public float urgencyAtFocus;
            public int demotedUntil = -1;
        }

        readonly Dictionary<string, GoalRecord> records = new Dictionary<string, GoalRecord>();

        GoalRecord RecordFor(ColonyGoal goal)
        {
            GoalRecord record;
            if (!records.TryGetValue(goal.Name, out record))
            {
                record = new GoalRecord();
                records[goal.Name] = record;
            }
            return record;
        }

        string watchedGoal;

        /// <summary>
        /// Notices a goal that holds the plan while the thing it measures fails to improve.
        ///
        /// A focus is a claim that working on this will make it better. Nothing ever checked the
        /// claim, so a goal could hold the colony indefinitely while its own measure worsened —
        /// "Clothe the colony" held it for a day and a half as the gap it was closing went from
        /// five degrees to sixteen. Every line in the record looked like ordinary work.
        ///
        /// Immediate goals are never demoted. Fire and raid report urgency 1 whatever happens, so
        /// they would read as "not improving" every time, and standing a colony down from a fire
        /// because the fire is still burning is precisely the wrong response. This is for the
        /// goals that quietly go nowhere, not the ones that are loud about it.
        /// </summary>
        void WatchTheFocus(ColonyGoal goal, float urgency, int now, DirectorContext ctx)
        {
            var record = RecordFor(goal);

            if (watchedGoal != goal.Name)
            {
                watchedGoal = goal.Name;
                record.focusSince = now;
                record.urgencyAtFocus = urgency;
                return;
            }

            record.blockedSince = -1;   // it is being worked on, not waiting

            if (goal.Horizon == GoalHorizon.Immediate) return;
            if (record.focusSince < 0 || now - record.focusSince < FocusGraceTicks) return;

            // A goal whose building is going up is not a goal going nowhere.
            //
            // Urgency is a reading of the finished state, and building improves it in steps: a
            // bedroom reads "1 bed for 3 colonists" from the moment its walls are queued until
            // the moment a second bed is finished, which is a good deal longer than half a day.
            // Measuring the reading alone therefore calls ordinary construction a failure.
            //
            // Watched it do exactly that on its first outing: "Shelter everyone" stood down at
            // 0.67 then, 0.67 now, while the bedroom it had asked for was half-built and the
            // planner's own log said it was waiting on that room. The colony went to research
            // with two of three colonists on the ground.
            //
            // So the question is whether the colony is visibly doing what the goal asked for,
            // and a blueprint or frame standing in the room it wanted is the plainest possible
            // evidence that it is.
            if (WorkIsUnderway(goal, ctx))
            {
                record.focusSince = now;
                record.urgencyAtFocus = urgency;
                return;
            }

            // Improving at all is enough. The question is whether the work is doing anything,
            // not whether it is doing it quickly.
            if (urgency < record.urgencyAtFocus)
            {
                record.focusSince = now;
                record.urgencyAtFocus = urgency;
                return;
            }

            record.demotedUntil = now + DemotionTicks;
            record.focusSince = now;

            Chronicle.Record(ChronicleCategory.Economy, string.Format(
                "'{0}' has held the plan for half a day and is no better for it ({1:0.00} then, " +
                "{2:0.00} now) — standing it down for a day to let something else run",
                goal.Name, record.urgencyAtFocus, urgency));

            record.urgencyAtFocus = urgency;
        }

        /// <summary>
        /// Whether anything is actually being built for this goal right now.
        ///
        /// Only asks about the room the goal named, which is the one thing a goal states plainly
        /// enough to check. A goal that wants no room gets no exemption — there is nothing to
        /// look at — and those are the ones the detector was written for anyway.
        /// </summary>
        static bool WorkIsUnderway(ColonyGoal goal, DirectorContext ctx)
        {
            if (ctx == null || ctx.map == null || ctx.layout == null) return false;

            var wanted = goal.WantsRoom;
            if (!wanted.HasValue) return false;

            var rooms = ctx.layout.rooms;
            for (int i = 0; i < rooms.Count; i++)
            {
                if (rooms[i].role != wanted.Value) continue;

                foreach (var cell in rooms[i].Rect)
                {
                    if (!cell.InBounds(ctx.map)) continue;
                    if (PlacementUtil.HasAnyConstructionAt(ctx.map, cell)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The best few goals with their scores, for the self-test to report alongside a probe.
        ///
        /// The probes exist to answer arbitration questions, and for anything long-term they
        /// could not: two runs of identical code disagreed on four of twenty-four, because
        /// long-term goals are separated by urgency alone and Fortify reads its urgency straight
        /// off the map's fire risk. Whichever colony the quicktest happened to spawn decided the
        /// winner, and a flipped coin looked exactly like a regression.
        ///
        /// Rather than pin the weather — which would make the probe answer a question about a
        /// map that does not exist — the probe now reports what it was choosing between. A
        /// two-point margin and a two-hundred-point one are different findings, and only one of
        /// them is worth investigating.
        /// </summary>
        public string RankingFor(DirectorContext ctx, int take)
        {
            var scored = new List<KeyValuePair<string, float>>();
            int now = CurrentTick();
            bool emergency = AnyImmediateOutstanding(ctx);

            for (int i = 0; i < goals.Count; i++)
            {
                var goal = goals[i];
                if (Satisfied(goal, ctx)) continue;

                scored.Add(new KeyValuePair<string, float>(
                    goal.Name, ScoreOf(goal, ctx, now, emergency)));
            }

            scored.Sort((a, b) => b.Value.CompareTo(a.Value));

            var sb = new StringBuilder();
            for (int i = 0; i < scored.Count && i < take; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(scored[i].Key).Append(' ').Append(scored[i].Value.ToString("0.00"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// What a goal is worth this pass. The one place the ordering is decided, so the planner
        /// and anything reporting on it cannot drift apart.
        ///
        /// Nearer horizons pre-empt further ones, but only while they mean it. The rule used to
        /// be absolute — horizon decided everything and urgency merely broke ties within a
        /// horizon — which is right for a fire and wrong for the tier below it. Short-term goals
        /// are satisfied at generous targets, eight days of food and everyone clothed and
        /// everything roofed, so a colony almost always has one of them mildly outstanding, and
        /// mildly outstanding was enough to outrank everything that compounds. Research read
        /// 0.00 in every colony ever run.
        ///
        /// So a goal below the pressing mark drops into the next band, where a goal one horizon
        /// out that genuinely means it can beat it. Fire and raid report urgency 1 by
        /// construction and never fall through that.
        ///
        /// Banding alone cannot fix the research case, because demoting everything by one
        /// preserves the order among them. A goal nothing has let run for three days is
        /// therefore promoted outright — gated on no immediate goal being outstanding at all, no
        /// fire, no raid and two days of food in hand, which is the guarantee that this can never
        /// take the colony's attention off something that would kill it.
        /// </summary>
        /// <summary>
        /// Whether this goal is the thing a nearer-horizon goal is stuck behind.
        ///
        /// Only research today, because that is the only prerequisite in the goal set that is
        /// itself a goal: a goal can declare RequiresResearch, the research needs a bench, and
        /// the bench arrives with a room nothing was hurrying. Any other blocked prerequisite is
        /// a material the colony can go and fetch.
        /// </summary>
        bool BlocksNearerWork(ColonyGoal goal, DirectorContext ctx)
        {
            if (goal.WantsRoom != RoomRole.Research) return false;
            if (ctx.state.hasResearchBench) return false;      // the block is already lifted
            if (!ctx.state.canResearch) return false;          // nobody could use it anyway

            for (int i = 0; i < goals.Count; i++)
            {
                var other = goals[i];
                if (other == goal) continue;
                if (other.Horizon >= goal.Horizon) continue;   // not nearer
                if (other.Satisfied(ctx)) continue;

                // Blocked is not the same as pressing. A near-horizon goal that wants almost
                // nothing right now — preserving food on a map cold enough that food keeps by
                // itself — is still technically stuck behind its research, and promoting a whole
                // room on its behalf would spend the colony's building on something it does not
                // need this season. Only work the colony actually wants doing can pull the
                // research room forward.
                if (Urgency(other, ctx) < PressingUrgency) continue;

                var needs = other.RequiresResearch;
                if (needs == null) continue;

                for (int r = 0; r < needs.Length; r++)
                    if (!IsFinished(needs[r])) return true;
            }
            return false;
        }

        readonly MaterialNeeds scratch = new MaterialNeeds();

        /// <summary>
        /// Adds one goal's material wants to the plan's total, weighted by horizon and urgency.
        ///
        /// Deliberately separate from plan.Needs, which stays exactly what it was: the material
        /// the *focus* must have before it can proceed. That is a gate and this is a gradient,
        /// and conflating them would let a long-term want block a short-term build.
        /// </summary>
        void AccumulatePressure(ColonyPlan plan, ColonyGoal goal, DirectorContext ctx, float urgency)
        {
            scratch.Clear();
            try { goal.DeclareNeeds(ctx, scratch); }
            catch (System.Exception) { return; }
            if (!scratch.Any) return;

            float horizonWeight = 3f - (int)goal.Horizon;          // Immediate 3, Short 2, Long 1
            float force = horizonWeight * AcMath.Clamp01(urgency);
            if (force <= 0f) return;

            foreach (var want in scratch.All)
            {
                float existing;
                plan.NeedPressure.TryGetValue(want.Key, out existing);
                plan.NeedPressure[want.Key] = existing + force;

                int held;
                plan.NeedQuantity.TryGetValue(want.Key, out held);
                if (want.Value > held) plan.NeedQuantity[want.Key] = want.Value;
            }

            // And the project this goal is stuck behind, if any — walked back to something
            // startable, exactly as the focus's own research direction is.
            var project = ResearchFor(goal);
            if (project != null)
            {
                float held;
                plan.ResearchPressure.TryGetValue(project, out held);
                plan.ResearchPressure[project] = held + force;
            }
        }

        float ScoreOf(ColonyGoal goal, DirectorContext ctx, int now, bool emergency)
        {
            float urgency = Urgency(goal, ctx);
            var record = RecordFor(goal);
            float score_bonus = 0f;

            int band = (int)goal.Horizon;

            // A room that nearer work is waiting on is not a long-term room.
            //
            // Run 72 froze to death building correctly toward something else. Clothing is a
            // ShortTerm goal and already declares that parkas need ComplexClothing; the
            // research needs a bench; the bench lives in the Research room; and wanting
            // somewhere to research is LongTerm. So the colony wanted the right thing, weighted
            // it correctly, and could never act on it — two colonists lost to Hypothermia, one
            // of them at mood 0.65 with the larder full and four score terms at their ceiling.
            //
            // Lifted to ShortTerm and no further. It has to be able to outrank a dining room; it
            // must never outrank food or shelter, which are Immediate and stay above it.
            if (BlocksNearerWork(goal, ctx) && band > (int)GoalHorizon.ShortTerm)
                band = (int)GoalHorizon.ShortTerm;

            if (urgency < PressingUrgency) band++;

            // A goal that held the plan and did not move drops another band for a while.
            if (now < record.demotedUntil) band++;

            if (!emergency && record.blockedSince >= 0 &&
                now - record.blockedSince >= BlockedTicksBeforeATurn)
                band = 0;

            // Whatever the colony is already working on keeps a small head start.
            //
            // Long-term goals separate on urgency alone and several of them read theirs off the
            // map, so three of them sat within six hundredths of a point in the self-test and the
            // ordering among them changed with the weather. Scoring a tie one way or the other is
            // free. Acting on it is not, and everything downstream of the plan acts.
            //
            // Four separate things were damaged by single-pass flips before this went in: a
            // Workshop opened through the spare slot and split a colony's builders for three
            // days; consolidation withdrew a finished research room's bench because the room was
            // not the focus that pass; and then it took the walls of the next research room for
            // the same reason. Each was patched where it surfaced. This is the cause.
            //
            // Deliberately far smaller than the hundred-point gap between horizons, so it can
            // only ever settle a near-tie and can never delay an emergency by even one pass.
            if (goal.Name == lastWanted) score_bonus = IncumbentMargin;

            return (10 - band) * 100f + urgency + score_bonus;
        }

        bool AnyImmediateOutstanding(DirectorContext ctx)
        {
            for (int i = 0; i < goals.Count; i++)
            {
                if (goals[i].Horizon != GoalHorizon.Immediate) continue;
                if (!Satisfied(goals[i], ctx)) return true;
            }
            return false;
        }

        static int CurrentTick()
        {
            try { return Find.TickManager != null ? Find.TickManager.TicksGame : 0; }
            catch (System.Exception) { return 0; }
        }

        static bool Satisfied(ColonyGoal goal, DirectorContext ctx)
        {
            try { return goal.Satisfied(ctx); }
            catch (System.Exception) { return true; }   // a broken goal must not block the rest
        }

        static float Urgency(ColonyGoal goal, DirectorContext ctx)
        {
            try { return goal.Urgency(ctx); }
            catch (System.Exception) { return 0f; }
        }
    }
}
