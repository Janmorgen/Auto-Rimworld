namespace AutoColony
{
    /// <summary>
    /// Whether a fire is coming, and whether it is still worth going out to.
    ///
    /// The fire response used to be decided on distance alone: anything outside the response
    /// radius was left, on the reasoning that a distant wildfire was never coming and that
    /// chasing one leaves whatever is burning at home unattended. That reasoning is sound. The
    /// test for it was not, because distance says where a fire *is* and nothing about where it
    /// is going.
    ///
    /// Watched it cost a map. Four fires, nearest a hundred cells out, correctly judged distant
    /// and left alone. Four became thirteen, then forty-three, then a hundred and twenty-three;
    /// twenty-seven in-game hours later two hundred and fifty-five cells were burning and the
    /// colony had spent every one of those hours doing nothing, because the front never crossed
    /// the line — it grew until the line was inside it.
    ///
    /// Two samples are enough to tell a front that is coming from one that is not, and the
    /// difference decides whether the cheap moment to answer it has arrived or already passed.
    /// </summary>
    public static class FireFront
    {
        /// <summary>
        /// Fires one colonist can plausibly beat out before the front outruns them.
        ///
        /// Not a measure of effort but of arithmetic: fire spreads to its neighbours on its own
        /// schedule regardless of how many people are running at it, so past a certain size the
        /// front grows faster than it can be put out and everyone sent is merely standing in it.
        /// </summary>
        public const int FightableFiresPerColonist = 6;

        /// <summary>Movement toward the colony smaller than this is sampling noise, not approach.</summary>
        public const float ApproachTolerance = 1f;

        /// <summary>Whether the colony's people could still physically put this front out.</summary>
        public static bool Fightable(int fires, int ableColonists)
        {
            if (ableColonists <= 0) return false;
            return fires <= ableColonists * FightableFiresPerColonist;
        }

        /// <summary>
        /// Whether to go out and meet a fire that has not arrived yet.
        ///
        /// Growing and not receding, while still small enough to beat. A front that is growing
        /// but moving away was genuinely never coming and the old distance rule was right about
        /// it. A front past what the colony can fight is not answered either — that is not
        /// caution but the recognition that sending one colonist into two hundred fires loses
        /// the colonist and not the fire.
        /// </summary>
        public static bool IsClosing(int fires, int previousFires, float nearest,
                                     float previousNearest, int ableColonists)
        {
            if (fires <= 0) return false;
            if (previousFires < 0) return false;           // first sample has nothing to compare

            bool growing = fires > previousFires;
            if (!growing) return false;

            bool receding = previousNearest >= 0f && nearest > previousNearest + ApproachTolerance;
            if (receding) return false;

            return Fightable(fires, ableColonists);
        }
    }
}
