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

        /// <summary>Strength a comfortable colony wants before picking any elective fight.</summary>
        public const float ComfortableRatio = 2.0f;

        /// <summary>
        /// The floor under an elective fight with prey that fights back.
        ///
        /// Desperation lowers the ordinary bar all the way to 0.5x on the reasoning that
        /// refusing is not the safe option. That is true of a raid at the door and true of
        /// hunting a hare. It is false of walking up to a cougar that is not attacking anybody:
        /// there refusing *is* the safe option, and losing costs colonists, which makes the
        /// hunger that justified the risk permanently worse.
        ///
        /// Twice now a cougar has cost a colony. Run 36 took one at 1.57x and had a colonist
        /// mauled. Run 56 declined the same animal twice against a 1.5x bar, took it at 1.13x
        /// once hunger had lowered the bar to 1.1x, and lost two colonists to the revenge two
        /// days later — by which time the colony was fighting at 0.44x, because the first
        /// mauling had already put people on the floor. A marginal ratio is worse than it looks:
        /// the hunt is judged at today's strength and the revenge arrives at tomorrow's.
        ///
        /// Genuine starvation keeps its own door in <see cref="LastResortWarranted"/>, which
        /// takes the least dangerous animal on the map when nothing safe is left.
        /// </summary>
        public const float DangerousPreyFloor = 1.5f;

        /// <summary>Strength needed before starting a fight with prey of this kind.</summary>
        public static float RequiredRatio(bool preyFightsBack, float desperation, float desperateRatio)
        {
            return RequiredRatio(preyFightsBack, desperation, desperateRatio, DangerousPreyFloor);
        }

        /// <summary>
        /// The same, with the dangerous-prey floor supplied rather than assumed.
        ///
        /// <see cref="DangerousPreyFloor"/> is a prior, not an answer. A colony that has been
        /// mauled by three manhunter packs has learned something specific about what these
        /// fights cost it, and ThreatMemory has been recording exactly that all along —
        /// run 161's went 1.50x, 1.69x, 1.90x across three revenges. It reached the module that
        /// fights the revenge and never reached the module that buys it.
        ///
        /// That is the loop the standing brief asks for: a bad outcome has to cost the director
        /// something it can measure, and the only measurement that closes here is the one it
        /// already takes.
        /// </summary>
        public static float RequiredRatio(bool preyFightsBack, float desperation,
                                          float desperateRatio, float dangerousFloor)
        {
            float d = desperation < 0f ? 0f : (desperation > 1f ? 1f : desperation);
            if (dangerousFloor <= 0f) dangerousFloor = DangerousPreyFloor;

            float floor = preyFightsBack ? dangerousFloor : desperateRatio;
            return ComfortableRatio + (floor - ComfortableRatio) * d;
        }

        /// <summary>Whether this colony should start that fight.</summary>
        public static bool WorthHunting(float colonyStrength, float threat, bool preyFightsBack,
                                        float desperation, float desperateRatio)
        {
            return WorthHunting(colonyStrength, threat, preyFightsBack, desperation,
                                desperateRatio, DangerousPreyFloor);
        }

        /// <summary>Whether this colony should start that fight, at a floor it has learned.</summary>
        public static bool WorthHunting(float colonyStrength, float threat, bool preyFightsBack,
                                        float desperation, float desperateRatio,
                                        float dangerousFloor)
        {
            if (threat <= 0f) return true;
            return colonyStrength >= threat * RequiredRatio(preyFightsBack, desperation,
                                                            desperateRatio, dangerousFloor);
        }
    }
}