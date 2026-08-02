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
            new ResearchCapacityGoal(),

            new MasonryGoal(),
            new PowerGoal(),
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
            bool emergency = false;
            for (int i = 0; i < goals.Count; i++)
            {
                var goal = goals[i];
                if (goal.Horizon != GoalHorizon.Immediate) continue;
                if (!Satisfied(goal, ctx)) { emergency = true; break; }
            }

            ColonyGoal best = null;
            float bestScore = float.NegativeInfinity;
            float bestUrgency = 0f;

            for (int i = 0; i < goals.Count; i++)
            {
                var goal = goals[i];
                var record = RecordFor(goal);

                if (Satisfied(goal, ctx))
                {
                    record.blockedSince = -1;   // nothing to wait for
                    continue;
                }

                float urgency = Urgency(goal, ctx);
                if (record.blockedSince < 0) record.blockedSince = now;

                // Nearer horizons pre-empt further ones, but only while they mean it.
                //
                // The rule used to be absolute: a fire must not lose to a very keenly wanted
                // freezer, which is right, and it was implemented as horizon deciding everything
                // and urgency merely breaking ties inside a horizon. So an immediate goal
                // outranked the whole colony however nearly satisfied it was — and "Feed the
                // colony" reports urgency 0 at anything past two days of food while staying
                // unsatisfied until eight. A colony 7.2 days fed published "Immediate: Feed the
                // colony" and did nothing else, for days.
                //
                // A goal that is barely wanted therefore drops into the next horizon's band,
                // where a genuinely pressing goal one horizon out can beat it. Fire and raid
                // report urgency 1 by construction and never fall through this.
                //
                // In practice it is the short-term tier this loosens. Those goals are satisfied
                // at generous targets — eight days of food, everyone clothed, everything roofed —
                // so a colony almost always has one of them mildly outstanding, and mildly
                // outstanding was enough to outrank everything that compounds.
                int band = (int)goal.Horizon;
                if (urgency < PressingUrgency) band++;

                // A goal that held the plan and did not move drops another band for a while.
                if (now < record.demotedUntil) band++;

                // Anything blocked long enough gets a turn, provided nothing is on fire.
                //
                // Banding alone cannot fix this: demoting every goal by one preserves the order
                // among them, so the least-urgent immediate goal still outranks the most urgent
                // long-term one. Research read 0.00 in every colony ever run — twenty-six of
                // them, one lasting 37 days and seven epochs — because something nearer was
                // always mildly unsatisfied and mildly unsatisfied was enough.
                //
                // So a goal nothing has let run for days is promoted outright for one pass. It is
                // gated on no immediate goal being outstanding at all — no fire, no raid, two
                // days of food in hand — which is the guarantee that this can never take the
                // colony's attention off something that would kill it.
                if (!emergency && record.blockedSince >= 0 &&
                    now - record.blockedSince >= BlockedTicksBeforeATurn)
                    band = 0;

                float score = (10 - band) * 100f + urgency;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = goal;
                    bestUrgency = urgency;
                }
            }

            if (best == null) return plan;

            WatchTheFocus(best, bestUrgency, now);

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
        void WatchTheFocus(ColonyGoal goal, float urgency, int now)
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
