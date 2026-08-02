namespace AutoColony
{
    /// <summary>
    /// When a starving colony should take a fight it expects to lose.
    ///
    /// Hunting is the one place the director will knowingly pick a losing fight, on the
    /// reasoning that refusing food is not survival but a slower way to lose. That reasoning is
    /// sound and the trigger for it was not, which cost a colony two thirds of its people in six
    /// hours — so the trigger lives here, where it can be argued with in a test rather than
    /// inferred from a chronicle after the fact.
    /// </summary>
    public static class HuntPolicy
    {
        /// <summary>Below this the colony is not desperate enough to fight anything it fears.</summary>
        public const float DesperateEnough = 0.85f;

        /// <summary>
        /// Whether an escalation to a fight the colony would rather refuse is actually warranted.
        ///
        /// The pass that prompts the question has just failed to designate anything: every
        /// animal it considered was one it judged too dangerous. That reads like "there is
        /// nothing safe to hunt" and usually is not, because the animals it considered exclude
        /// everything already marked. A hunt module doing its job marks all the safe prey within
        /// a pass or two, after which no pass can ever designate anything new and every pass
        /// concludes the colony has nothing safe left — while its hunters are out killing.
        ///
        /// The escalation then picks the least dangerous animal *not already marked*, which for
        /// exactly that reason is the most dangerous animal on the map.
        ///
        /// Two conditions turn a false alarm back into a real one:
        ///
        /// Nothing already in flight. Standing hunts are food on its way, and the colony is not
        /// out of options while its hunters are working. Only hunts the colony still endorses
        /// count — a designation on something it would now refuse is not a plan.
        ///
        /// Nobody down. A colony with someone on the floor has already lost the strength this
        /// fight would be judged on, needs its remaining people tending rather than hunting, and
        /// is the least able to absorb what losing produces. Losing does not merely fail to
        /// yield meat: a wounded Megasloth goes manhunter and follows the hunters home.
        /// </summary>
        public static bool LastResortWarranted(int designatedThisPass, int huntsAlreadyStanding,
                                               int colonistsDowned, float desperation,
                                               int candidatesAvailable)
        {
            if (designatedThisPass > 0) return false;      // the pass found something after all
            if (candidatesAvailable <= 0) return false;    // nothing to escalate onto
            if (desperation <= DesperateEnough) return false;

            if (huntsAlreadyStanding > 0) return false;    // food is already coming
            if (colonistsDowned > 0) return false;         // the worst moment to start a fight

            return true;
        }
    }
}
