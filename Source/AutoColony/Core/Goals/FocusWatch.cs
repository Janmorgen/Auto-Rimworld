namespace AutoColony.Goals
{
    /// <summary>What the watcher decided about a goal holding the plan.</summary>
    public enum FocusVerdict
    {
        /// <summary>Leave it alone. It has not had long enough yet.</summary>
        Hold,

        /// <summary>It is moving, or the work is visibly underway. Start the window again.</summary>
        ResetWindow,

        /// <summary>It held the plan for its whole patience and moved nothing.</summary>
        Demote
    }

    /// <summary>
    /// Whether a goal that holds the plan is getting anywhere, decided on primitives so it can
    /// be tested without a game.
    ///
    /// This lived inside GoalPlanner.WatchTheFocus and could not be reached by any test — the
    /// planner touches Map, Pawn and DefDatabase, which the test project bars by construction.
    /// It is also the part that was wrong, in two ways beyond the flat patience it was built
    /// around.
    ///
    /// <b>Focus amnesia.</b> The old code reset the clock whenever the watched goal changed, so
    /// a goal that lost the plan for a single planner pass got a whole fresh grace period. That
    /// was harmless while grace was half a day and everything was demoted anyway. Under a
    /// patience measured in days it becomes the opposite failure: a goal going nowhere could
    /// hold the colony indefinitely by flickering in and out of the top slot, and never once
    /// accumulate enough continuous focus to be judged. So time is accumulated across the
    /// spell, and only a demotion or the goal being satisfied clears it.
    ///
    /// <b>The report was already lying.</b> The chronicle line said "0.67 then, 0.67 now" and
    /// read as a claim about the whole hold. It was not: the "then" value was refreshed every
    /// time the window reset, so it meant the urgency at the last check, which for a goal that
    /// had been holding for days was a few hours ago. Start-of-spell urgency is kept separately
    /// now, so the line can say either honestly.
    ///
    /// Free of game types on purpose.
    /// </summary>
    public static class FocusWatch
    {
        /// <summary>
        /// Judge one pass.
        ///
        /// <paramref name="patienceTicks"/> of <see cref="GoalPatience.NotDerivable"/> means no
        /// estimate could be made, and the caller is expected to have substituted what it has
        /// learned before getting here — this asks only whether the time is up.
        /// </summary>
        public static FocusVerdict Judge(
            int focusTicks,
            int patienceTicks,
            float urgencyNow,
            float urgencyAtWindowStart,
            bool workUnderway,
            bool isImmediate)
        {
            // Fire and raid report urgency 1 whatever happens, so they read as "not improving"
            // every time. Standing a colony down from a fire because the fire is still burning
            // is precisely the wrong response.
            if (isImmediate) return FocusVerdict.Hold;

            // A goal whose building is going up is not a goal going nowhere. Urgency is a
            // reading of the finished state and building improves it in steps, so a bedroom
            // reads "1 bed for 3 colonists" from the moment its walls are queued until a second
            // bed is finished.
            if (workUnderway) return FocusVerdict.ResetWindow;

            // Improving at all is enough. The question is whether the work is doing anything,
            // not whether it is doing it quickly.
            if (urgencyNow < urgencyAtWindowStart) return FocusVerdict.ResetWindow;

            if (patienceTicks < 0) return FocusVerdict.Hold;   // nothing to judge against
            if (focusTicks < patienceTicks) return FocusVerdict.Hold;

            return FocusVerdict.Demote;
        }
    }
}
