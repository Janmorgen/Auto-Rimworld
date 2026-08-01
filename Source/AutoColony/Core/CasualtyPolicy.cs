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
    }
}
