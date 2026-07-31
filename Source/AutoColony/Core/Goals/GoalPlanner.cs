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
            new FoodStockGoal(),

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

            ColonyGoal best = null;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < goals.Count; i++)
            {
                var goal = goals[i];
                if (Satisfied(goal, ctx)) continue;

                // Nearer horizons dominate outright rather than competing on urgency: a fire
                // must not lose to a very keenly wanted freezer.
                float score = (10 - (int)goal.Horizon) * 100f + Urgency(goal, ctx);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = goal;
                }
            }

            if (best == null) return plan;

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
