namespace AutoColony.Rooms
{
    /// <summary>What is known about a room when deciding how much its problems matter.</summary>
    public struct RoomFacts
    {
        /// <summary>The colony stops functioning without this room, not merely gets sadder.</summary>
        public bool essential;

        /// <summary>The only room of its kind, so its problems have no alternative.</summary>
        public bool unique;

        /// <summary>Colonists who actually use it — sleepers, or everyone for a shared room.</summary>
        public int users;

        /// <summary>Colony size, so occupancy reads as a share rather than a count.</summary>
        public int colonists;
    }

    /// <summary>
    /// How much a room's condition matters to the colony.
    ///
    /// Defects were ranked by *kind* alone: a dark room scored the same whether it was the only
    /// kitchen or the third bedroom nobody sleeps in. But a room is not interchangeable with
    /// another room. The kitchen being cold stops the colony eating; a spare bedroom being cold
    /// costs one person some mood, and only if anyone sleeps there. Ranking on the fault while
    /// ignoring the place is how a colony ends up lighting an empty store room while the room
    /// everyone eats in is the problem.
    ///
    /// Three things make a room matter: whether the colony depends on it, whether there is
    /// another one if it fails, and how many people are actually in it. The first two are
    /// properties of the base; the third is measured.
    ///
    /// The weights are genes rather than constants, because how much to favour the essential
    /// room over the busy one is a genuine strategic trade-off with no single right answer — it
    /// depends on the colony's size, its stage and how often it is in crisis. That is precisely
    /// the sort of question the epoch search exists to answer, and it could not previously be
    /// asked because the ranking had no room term in it at all.
    ///
    /// Free of game types so the judgement can be tested offline.
    /// </summary>
    public static class RoomImportance
    {
        /// <summary>An ordinary room nobody depends on and nobody is in.</summary>
        public const float Baseline = 0.5f;

        /// <summary>
        /// A multiplier on a defect's priority, centred near 1 for a typical room.
        ///
        /// <paramref name="essentialWeight"/> is how much being depended on counts, and
        /// <paramref name="occupancyWeight"/> how much being busy counts. Both come from the
        /// genome.
        /// </summary>
        public static float Of(RoomFacts facts, float essentialWeight, float occupancyWeight)
        {
            float importance = Baseline;

            if (facts.essential) importance += essentialWeight;

            // Being the only one of its kind matters, but only for a room that does something.
            // The single bedroom in a one-room colony is already covered by occupancy.
            if (facts.unique && facts.essential) importance += essentialWeight * 0.5f;

            if (facts.colonists > 0 && facts.users > 0)
            {
                float share = facts.users / (float)facts.colonists;
                if (share > 1f) share = 1f;
                importance += occupancyWeight * share;
            }

            return importance;
        }

        /// <summary>
        /// An empty, replaceable room the colony does not depend on — the floor this returns, so
        /// callers can compare against "nothing special about this place".
        /// </summary>
        public static float Unremarkable(float occupancyWeight)
        {
            return Baseline;
        }
    }
}
