namespace AutoColony
{
    /// <summary>
    /// Whether the colony is in a fit state to be the baseline for a training round.
    ///
    /// A round snapshots the world and replays the same stretch of time once per candidate, so
    /// the snapshot decides what every candidate is actually being asked. Taken from a colony
    /// that is one bad hour from dying, the question becomes "can you escape a near-hopeless
    /// position in two days", and the answer is mostly luck: one candidate scored 0.466 and
    /// another 0.000 by being wiped out, from strategies differing in hauling weight and a
    /// production buffer. That spread is noise wearing a score's clothing, and the search
    /// cannot tell the difference.
    ///
    /// Deferring costs one epoch of training. Scoring four candidates on a coin toss costs the
    /// search a whole round of its very limited evidence, and worse, teaches it something
    /// false.
    ///
    /// Free of game types so the judgement can be tested offline.
    /// </summary>
    public static class TrainingPolicy
    {
        /// <summary>
        /// Fewest colonists worth comparing strategies across.
        ///
        /// With one, almost everything that happens is about that person specifically — one bad
        /// roll ends the run regardless of the strategy being judged.
        /// </summary>
        public const int MinColonists = 2;

        /// <summary>Food in hand before a colony's near future is about its strategy at all.</summary>
        public const float MinDaysOfFood = 2f;

        public static bool WorthSnapshotting(int colonists, int downed, float daysOfFood,
                                             bool inEmergency, out string why)
        {
            if (colonists < MinColonists)
            {
                why = colonists + " colonists — too few for a candidate's score to be about the strategy";
                return false;
            }
            if (downed > 0)
            {
                why = downed + " down — every candidate would inherit the casualty and the race to tend them";
                return false;
            }
            if (daysOfFood < MinDaysOfFood)
            {
                why = daysOfFood.ToString("0.0") + " days of food — candidates would be scored on surviving a famine";
                return false;
            }
            if (inEmergency)
            {
                why = "an emergency is in progress — the snapshot would hand every candidate the same crisis";
                return false;
            }

            why = null;
            return true;
        }
    }
}
