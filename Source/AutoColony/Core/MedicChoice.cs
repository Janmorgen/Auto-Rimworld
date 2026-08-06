namespace AutoColony
{
    /// <summary>
    /// Which colonist to hold back to tend the wounded.
    ///
    /// The choice was the highest Medicine skill, with colonist value as a tiebreak. Run 162 shows
    /// what that misses. A rhinoceros revenge on day 4 put Pansy on the floor bleeding and took
    /// Poole's right leg. Poole had Medicine 7 — the best in the colony by a distance — and was
    /// duly held back from the fight to tend. Six hours later Pansy died of blood loss with
    /// twenty-two medicine in store and the doctor still on their way.
    ///
    /// Nothing was misweighted. The colony picked the best doctor and never asked whether the
    /// best doctor could walk. A one-legged surgeon is the wrong choice against a deadline, and
    /// the deadline is the part that was missing: ColonyState already reads
    /// HealthUtility.TicksUntilDeathDueToBloodLoss for every bleeding colonist and then keeps
    /// only a count of them, discarding the number. Its own doc comment says "the distinction is
    /// a deadline" and describes run 116 losing two colonists to exactly this.
    ///
    /// So this is the same shape twice over. It is the proxy-for-the-real-thing row in goal.md's
    /// table — a measured deadline reduced to "somebody is bleeding", which cannot answer whether
    /// help arrives in time. And it is the pattern the trade module already got right:
    /// ChooseNegotiator picks the best Social who CanReach, because a negotiator who cannot get
    /// to the trader is not a negotiator. One module learned to ask and the other did not.
    ///
    /// Free of game types so the trade-off can be argued with in a test.
    /// </summary>
    public static class MedicChoice
    {
        /// <summary>No route, or no way to work out how long one would take.</summary>
        public const int Unreachable = -1;

        /// <summary>
        /// How much use this colonist is to somebody bleeding, higher being better.
        ///
        /// Skill still decides between medics who can get there, which is what the old ranking
        /// had right. What it adds is that getting there is a condition of being any use at all,
        /// and that arriving with nothing to spare is worth less than arriving early — a doctor
        /// who reaches the patient in the last minute of their last hour has no time to fetch
        /// medicine, and the run 162 death happened with medicine in store.
        ///
        /// Skill is taken as skill + 1 so that a colonist with no training standing beside the
        /// patient outranks a surgeon who will arrive after they are dead. That is not a
        /// judgement about medicine; it is the arithmetic of a deadline.
        /// </summary>
        public static float Usefulness(int skill, int ticksToReach, int ticksUntilDeath)
        {
            if (skill < 0) skill = 0;

            // No deadline known — nobody is bleeding to a clock, so the old question is the right
            // one and skill alone answers it.
            if (ticksUntilDeath <= 0) return skill + 1;

            if (ticksToReach == Unreachable || ticksToReach < 0) return 0f;

            // Arrives to a corpse. Worth nothing, however good they are.
            if (ticksToReach >= ticksUntilDeath) return 0f;

            float margin = 1f - (float)ticksToReach / ticksUntilDeath;
            return (skill + 1) * margin;
        }

        /// <summary>
        /// How long this colonist needs to cross that ground, in ticks.
        ///
        /// Move speed is in cells per second and a second is sixty ticks. Deliberately ignores
        /// pathing: the straight-line distance understates a walk around a wall, so this is
        /// optimistic, and an optimistic estimate that still says "too late" is a conclusion
        /// worth acting on. A pessimistic one would have to be trusted before it could be.
        /// </summary>
        public static int TicksToCross(float distanceCells, float cellsPerSecond)
        {
            if (distanceCells <= 0f) return 0;
            if (cellsPerSecond <= 0f) return Unreachable;

            return (int)(distanceCells * 60f / cellsPerSecond);
        }
    }
}
