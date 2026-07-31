using System;
using System.Collections.Generic;

namespace AutoColony.Goals
{
    /// <summary>
    /// Walks a research tree back to a project that can be started today.
    ///
    /// A goal naming the research it needs is not enough on its own: refrigeration wants air
    /// conditioning, and air conditioning wants electricity. That is the same prerequisite walk
    /// the planner already does over goals, one level down, and it exists because the two layers
    /// were previously unconnected — the plan could hold "Power" as its focus for an entire game
    /// while the research bandit studied whatever happened to be cheapest.
    ///
    /// Deliberately free of every game type, so the arbitration can be tested without a colony.
    /// </summary>
    public static class ResearchChain
    {
        /// <summary>Depth cap, so a mod declaring a cycle degrades instead of hanging a tick.</summary>
        public const int MaxDepth = 12;

        /// <summary>
        /// The first project on the way to <paramref name="target"/> whose own prerequisites are
        /// all finished — what the colony should be researching right now to eventually get there.
        ///
        /// Returns null when the target is already finished, unknown, or sits behind a cycle;
        /// in every one of those cases there is nothing useful to steer research towards.
        /// </summary>
        /// <param name="prerequisitesOf">
        /// The projects a given project depends on. Null or empty means it can be started at once.
        /// </param>
        /// <param name="isFinished">
        /// Whether a project is done. Unknown projects should report finished: a mod or DLC that
        /// is not installed must not become an unreachable prerequisite that blocks the goal.
        /// </param>
        public static string FirstStartable(string target,
                                            Func<string, IList<string>> prerequisitesOf,
                                            Func<string, bool> isFinished)
        {
            if (string.IsNullOrEmpty(target)) return null;
            if (prerequisitesOf == null || isFinished == null) return null;

            return Walk(target, prerequisitesOf, isFinished, new HashSet<string>(), 0);
        }

        /// <summary>
        /// As above across several targets, taking the first that yields anything actionable.
        /// A goal listing more than one project gets them in the order it declared them.
        /// </summary>
        public static string FirstStartableOf(IList<string> targets,
                                              Func<string, IList<string>> prerequisitesOf,
                                              Func<string, bool> isFinished)
        {
            if (targets == null) return null;
            for (int i = 0; i < targets.Count; i++)
            {
                var startable = FirstStartable(targets[i], prerequisitesOf, isFinished);
                if (startable != null) return startable;
            }
            return null;
        }

        static string Walk(string project,
                           Func<string, IList<string>> prerequisitesOf,
                           Func<string, bool> isFinished,
                           HashSet<string> visiting,
                           int depth)
        {
            if (depth > MaxDepth) return null;
            if (isFinished(project)) return null;
            if (!visiting.Add(project)) return null;    // cycle

            var prerequisites = prerequisitesOf(project);
            if (prerequisites != null)
            {
                for (int i = 0; i < prerequisites.Count; i++)
                {
                    var prerequisite = prerequisites[i];
                    if (string.IsNullOrEmpty(prerequisite)) continue;
                    if (isFinished(prerequisite)) continue;

                    // The first unmet prerequisite decides the whole branch. If nothing under it
                    // is reachable either, the target is not reachable at all — saying so beats
                    // silently skipping to a sibling that would not unblock anything.
                    return Walk(prerequisite, prerequisitesOf, isFinished, visiting, depth + 1);
                }
            }

            return project;
        }
    }
}
