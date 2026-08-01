namespace AutoColony.Prisoners
{
    /// <summary>What the colony intends to do with a prisoner.</summary>
    public enum Disposition
    {
        /// <summary>Feed and keep, deciding later. The safe default.</summary>
        Hold,

        /// <summary>Wear their resistance down first — recruiting through it outright is slower.</summary>
        Wear,

        /// <summary>Talk them into joining.</summary>
        Recruit,

        /// <summary>Open the door and let them walk. Costs nothing and nobody watches it happen.</summary>
        Release,

        /// <summary>Kill them. Almost always the wrong answer; see the note on the method.</summary>
        Execute
    }

    /// <summary>
    /// What to do with a downed raider once the colony has them.
    ///
    /// A prisoner is an opportunity rather than a problem — a colonist the colony did not have to
    /// grow, or a mouth it cannot feed, depending entirely on which colony it is. So the decision
    /// is the usual one: assess the capability in front of you against the colony's situation,
    /// rather than applying a fixed rule about raiders.
    ///
    /// Free of game types so the judgement can be tested offline.
    /// </summary>
    public static class PrisonerPolicy
    {
        /// <summary>Below this many days of food, another mouth is a real cost.</summary>
        public const float HungryBelowDays = 4f;

        /// <summary>Resistance above this is worth wearing down before trying to recruit.</summary>
        public const float HighResistance = 8f;

        /// <summary>
        /// <paramref name="value"/> is how much use this person would be as a colonist, 0 to 1.
        /// <paramref name="recruitBias"/> is the strategy's appetite for taking people in.
        /// </summary>
        public static Disposition Decide(float value, float resistance, float daysOfFood,
                                         float recruitBias, bool canRecruit, bool executionAllowed)
        {
            value = Clamp01(value);
            recruitBias = Clamp01(recruitBias);

            bool hungry = daysOfFood < HungryBelowDays;
            bool worthKeeping = value * (0.4f + recruitBias) >= 0.25f;

            // A colony that cannot feed itself has no business feeding a prisoner it does not
            // want. Letting them go costs nothing and, unlike the alternative, nobody watches.
            if (hungry && !worthKeeping)
                return executionAllowed && value <= 0.1f ? Disposition.Execute : Disposition.Release;

            if (!canRecruit) return Disposition.Hold;

            if (!worthKeeping)
            {
                // Well fed and uninterested: no reason to hold someone indefinitely, and a
                // prisoner nobody is working on is a prison break waiting to happen.
                return Disposition.Release;
            }

            // Worth having. Resistance has to come down before recruitment will take, and
            // attempting it directly against a high resistance simply wastes the warden's time.
            return resistance > HighResistance ? Disposition.Wear : Disposition.Recruit;
        }

        /// <summary>
        /// Whether the colony should be taking prisoners at all right now.
        ///
        /// Capturing is not free: it costs a colonist a trip across a battlefield, then food and
        /// warden time indefinitely. A colony that is starving, or that has nowhere to put
        /// anyone, should be finishing the fight instead.
        /// </summary>
        public static bool WorthCapturing(float value, float daysOfFood, float recruitBias,
                                          bool bedAvailable, bool safe)
        {
            if (!bedAvailable || !safe) return false;
            if (daysOfFood < 1f) return false;

            // Even a poor prospect is worth taking when the colony is keen and can afford it;
            // a colony with no appetite for recruits should not be collecting people.
            return Clamp01(value) * (0.3f + Clamp01(recruitBias)) >= 0.2f;
        }

        /// <summary>
        /// How useful this person would be, from the numbers a colony can see: whether they can
        /// work at all, how skilled they are, and whether they are going to recover.
        ///
        /// <paramref name="bestSkill"/> and <paramref name="averageSkill"/> are 0-20 RimWorld
        /// skill levels; <paramref name="health"/> is a 0-1 fraction.
        /// </summary>
        public static float Value(int bestSkill, float averageSkill, float health,
                                  bool violentWorkDisabled, bool incapableOfEverything)
        {
            if (incapableOfEverything) return 0f;

            float skill = Clamp01(bestSkill / 16f) * 0.6f + Clamp01(averageSkill / 10f) * 0.4f;
            float worth = skill * Clamp01(health);

            // A pacifist is still a cook, a doctor and a grower — worth less in a fight, not
            // worth nothing.
            if (violentWorkDisabled) worth *= 0.8f;

            return Clamp01(worth);
        }

        static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
    }
}
