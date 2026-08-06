namespace AutoColony.Goals
{
    /// <summary>
    /// How long a goal deserves to hold the plan, given what it is actually waiting on.
    ///
    /// The planner used one number for this — half a day, for every goal that was not an
    /// emergency. That is a claim about payoff distance dressed up as a claim about work, and
    /// the two are not the same thing. A bedroom takes days of building. A research project
    /// takes days of somebody sitting at a bench. Half a day is not long enough to see either
    /// of them move, so the goal that asked for them was stood down for being slow, when slow
    /// was simply what it was.
    ///
    /// Three goals could never pass the test at all. PreservedFood, Comfort and WoodSupply are
    /// gated on research, their urgency cannot change until a project completes, and no project
    /// completes in half a day — so each was demoted every single time it took the focus. Run
    /// 142 shows the shape in the goal that could at least sometimes escape it: "Shelter
    /// everyone" asked from hour zero, was stood down on day 1, again at 1 15h, 2 03h, 2 15h
    /// and 4 03h, and did not get a bedroom sited until day 15.
    ///
    /// So patience is the estimated time for the blocker to clear, and the estimate comes from
    /// rates the colony measures on itself rather than from a table somebody wrote. Nothing
    /// here knows what a research project is, or a wall — it is arithmetic over a remaining
    /// quantity and a rate, so the same reasoning serves both and can be tested offline.
    ///
    /// Free of game types on purpose.
    /// </summary>
    public static class GoalPatience
    {
        /// <summary>Returned wherever an estimate cannot honestly be made.</summary>
        public const int NotDerivable = -1;

        /// <summary>
        /// Ticks for <paramref name="remaining"/> units of work to finish at
        /// <paramref name="ratePerTick"/>.
        ///
        /// A rate of zero does not mean infinite patience. It means this goal is not waiting on
        /// the work taking time — it is waiting on the work being possible at all, which is a
        /// different goal's problem and not something to sit still for. A colony with no
        /// researcher should not wait forever on Pemmican, so a zero rate is undefined rather
        /// than enormous, and the caller falls through to what it has learned instead.
        /// </summary>
        public static int TicksToFinish(float remaining, float ratePerTick)
        {
            if (ratePerTick <= 0f) return NotDerivable;
            if (remaining <= 0f) return 0;

            double ticks = remaining / ratePerTick;
            if (ticks > int.MaxValue) return int.MaxValue;
            return (int)ticks;
        }

        /// <summary>
        /// The longer of two estimates, where either may be undefined.
        ///
        /// A goal waiting on two things is not done until the slower of them lands — the Power
        /// goal wants both a room and Electricity, and finishing the room early buys nothing.
        /// Expressed as a max rather than a branch so that adding a third blocker later needs
        /// no new decision.
        /// </summary>
        public static int Longer(int a, int b)
        {
            if (a == NotDerivable) return b;
            if (b == NotDerivable) return a;
            return a > b ? a : b;
        }

        /// <summary>
        /// The estimate turned into a patience, with room for the estimate to be optimistic.
        ///
        /// <paramref name="slack"/> is a gene. The arithmetic assumes the rate holds, and it
        /// does not: researchers get pulled onto hauling, builders get drafted, and a colony
        /// that always finds the real wait longer than its own arithmetic should be able to
        /// learn that rather than being told it.
        ///
        /// The floor exists because the planner is sampled every StateInterval ticks, so a
        /// patience shorter than a few passes is measuring quantisation, not the goal.
        /// </summary>
        public static int Patience(int estimatedTicks, float slack, int floor, int ceiling)
        {
            if (floor < 0) floor = 0;
            if (ceiling < floor) ceiling = floor;
            if (estimatedTicks == NotDerivable) return NotDerivable;

            double scaled = (double)estimatedTicks * (slack <= 0f ? 1f : slack);
            if (scaled > ceiling) scaled = ceiling;
            if (scaled < floor) scaled = floor;
            return (int)scaled;
        }

        /// <summary>
        /// How long a goal is stood down for, once it has been.
        ///
        /// Proportional to the wait it just failed to make good on rather than a flat day: a
        /// goal that held the plan for six days and moved nothing should not be back in an
        /// hour, and one that held it for four hours should not be gone for a week.
        /// </summary>
        public static int DemotionAfter(int patienceTicks, float fraction)
        {
            if (patienceTicks <= 0) return 0;
            if (fraction <= 0f) return 0;

            double ticks = (double)patienceTicks * fraction;
            if (ticks > int.MaxValue) return int.MaxValue;
            return (int)ticks;
        }
    }
}
