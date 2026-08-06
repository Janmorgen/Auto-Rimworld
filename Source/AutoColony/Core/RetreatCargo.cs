namespace AutoColony
{
    /// <summary>
    /// Whether a colonist walking away from a fight should carry the one who cannot walk.
    ///
    /// Three colonists have now been lost to the same few seconds. A raid arrives, everyone is
    /// drafted, somebody goes down, the colony correctly judges the fight lost and withdraws the
    /// standing to a refuge — and the casualty stays where they fell, because the rescue that
    /// would carry them cannot run. NearestCarrier skips every drafted colonist, and during a
    /// withdrawal every colonist is drafted. Run 164 lost Simon to a kidnapping eleven minutes
    /// after he went down, with two able colonists walking past him to the refuge.
    ///
    /// The exclusion is right about what it was written for: handing a work job to a drafted
    /// pawn breaks the draft, and a draft broken mid-fight is how a line collapses. What it
    /// cannot see is that a withdrawal is not a fight. The colony has already decided this one
    /// is not winnable and is spending the next minute walking away from it, so the fighter who
    /// carries the casualty gives up nothing that was being used.
    ///
    /// This is the third time the same chain has cost a colony: run 135's drafted colonist held
    /// while three bled out, this session's fire front judged fightable by people already sent to
    /// a firing line, and now a rescue that cannot happen because rescuing is work and everyone
    /// is drafted. Drafting removes hands from everything that is not the fight, and only the
    /// fight knows it.
    ///
    /// Free of game types so the trade-off can be argued with in a test.
    /// </summary>
    public static class RetreatCargo
    {
        /// <summary>
        /// What carrying this casualty is worth, against holding the line with that colonist.
        ///
        /// Withdrawing is the case this exists for and the case where the answer is easy: the
        /// line is already being given up, so the carry costs nothing. Standing and fighting is
        /// the harder one, and the honest answer there is that a fighter pulled out of a fight
        /// the colony still expects to win is a real loss — so the margin over what the fight
        /// needs is what pays for the rescue.
        ///
        /// <paramref name="strengthSpare"/> is committed strength above what the fight requires.
        /// Negative or zero means the line cannot spare anybody.
        /// </summary>
        public static bool WorthCarrying(bool withdrawing, float carrierValue, float strengthSpare)
        {
            // Nothing to hold. The fighters are walking away either way, and one of them can
            // walk away carrying somebody.
            if (withdrawing) return true;

            if (carrierValue <= 0f) return false;

            // Still fighting, and the line only spares someone it does not need.
            return strengthSpare >= carrierValue;
        }

        /// <summary>
        /// How good a choice this colonist is to do the carrying, higher being better.
        ///
        /// The nearest able body, weighted against what removing them costs. Distance dominates
        /// because a casualty on the ground with hostiles nearby is on a clock measured in
        /// seconds, and the fastest carrier is very often the only one who matters.
        ///
        /// Returns zero for anyone who cannot get there at all, so an unreachable colonist is
        /// never chosen over a reachable worse one.
        /// </summary>
        public static float CarrierFitness(float distanceCells, float carrierValue,
                                           float cellsPerSecond)
        {
            if (distanceCells < 0f || cellsPerSecond <= 0f) return 0f;

            int ticks = MedicChoice.TicksToCross(distanceCells, cellsPerSecond);
            if (ticks == MedicChoice.Unreachable) return 0f;

            // A tick of walking is a tick the raiders have. Value is the tiebreak rather than
            // the term, because the colony would rather send its best fighter and keep the
            // casualty than keep the fighter in a line it has already abandoned.
            float speed = 1f / (1f + ticks / 60f);
            float cheapness = carrierValue > 0f ? 1f / (1f + carrierValue / 100f) : 1f;

            return speed * (0.75f + 0.25f * cheapness);
        }
    }
}
