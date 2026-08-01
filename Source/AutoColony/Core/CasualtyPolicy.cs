namespace AutoColony
{
    /// <summary>
    /// Who is not sent to the fight once somebody is already on the ground.
    ///
    /// A downed colonist does not die of the wound. They die of nobody arriving: untended, they
    /// bleed out, and unfed they starve where they fell. Colonies were lost this way with days
    /// of food in the stockpile, because every able body had been drafted into the same fight
    /// that put the others down and there was nobody left standing to carry any of it.
    ///
    /// So a casualty changes the arithmetic of a raid. Answering it is still not optional — the
    /// raid is happening — but the last person able to treat the wounded is worth more holding
    /// a medicine kit than holding a rifle they were never going to turn the fight with.
    /// </summary>
    public static class CasualtyPolicy
    {
        /// <summary>
        /// Whether to keep one colonist out of the fighting to tend the wounded.
        ///
        /// Only ever one, and only while somebody is actually down: two held back would be
        /// giving away the fight, and holding anyone back in a colony with nobody to hold back
        /// for is the jumpiness the draft genes exist to avoid. It also refuses to empty the
        /// line entirely — a lone colonist facing a raid does not get to opt out of it, because
        /// losing the fight and losing the colony are the same outcome at that point.
        /// </summary>
        public static bool ShouldReserveMedic(int ableFighters, int downedColonists)
        {
            if (downedColonists <= 0) return false;
            return ableFighters >= 2;
        }

        /// <summary>
        /// How much better the odds have to be before the few still standing meet a threat in
        /// the open, given how many are already down.
        ///
        /// Desperation scales acceptable risk upward; this is the same idea running the other
        /// way. A colony with three of four on the floor is one lost fight from having nobody
        /// left to tend, feed or carry anyone, and that is not a survivable position however the
        /// fight itself looks — where withdrawing risks only what the raider can do to a
        /// defended room. So the stake, not just the odds, belongs in the decision.
        ///
        /// Watched happen: with three colonists down, the director drafted the fourth against a
        /// single raider on a 95-to-77 advantage, lost her, and the whole colony bled out over
        /// the next four hours with eleven days of food in the store. That fight was worth
        /// having on its numbers and not worth having on its stake.
        ///
        /// This raises the bar rather than forbidding the fight. Answering a threat is never
        /// optional — the raider comes either way — so what changes is whether it is met outside
        /// or from cover.
        /// </summary>
        public static float EngagementCaution(int ableFighters, int downedColonists)
        {
            if (downedColonists <= 0 || ableFighters <= 0) return 1f;
            return 1f + (float)downedColonists / ableFighters;
        }

        /// <summary>
        /// The advantage worth having before abandoning a defensible position.
        ///
        /// Half again the enemy's strength. A doorway is worth a great deal — raiders have to
        /// come through it one at a time, into prepared fire — and giving that up for an even
        /// fight is a poor trade whatever the raw numbers say.
        /// </summary>
        public const float MinimumToLeaveCover = 1.5f;

        /// <summary>
        /// The advantage the colony insists on before meeting a threat in the open.
        ///
        /// Answering a raid is never optional, but meeting it *outside* is — and it is elective
        /// exactly when there is somewhere better to meet it. Hunting has always been judged
        /// this way, a comfortable colony demanding roughly two to one before an elective fight;
        /// defence was judged on a bare gene whose default lets a colony charge a threat three
        /// times its strength. One did, at 0.68 against a bar of 0.35, and three of its four
        /// colonists were on the floor within the hour.
        ///
        /// With nowhere to withdraw to the gene stands unaltered, because a colony with no walls
        /// yet is not choosing between two options — the fight is coming to it either way.
        /// </summary>
        /// The floor is applied first and the casualty multiplier on top of it, not the other way
        /// round. Taking the larger of the two would let the floor swallow the multiplier — a
        /// colony with three of four down would demand no more than one with nobody down, which
        /// is the opposite of the point.
        public static float RequiredAdvantage(float geneRatio, int ableFighters,
                                              int downedColonists, bool hasRefuge)
        {
            float required = geneRatio;
            if (hasRefuge && required < MinimumToLeaveCover) required = MinimumToLeaveCover;
            return required * EngagementCaution(ableFighters, downedColonists);
        }
    }
}
