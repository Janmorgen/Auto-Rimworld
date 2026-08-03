namespace AutoColony.Rooms
{
    /// <summary>
    /// What the game thinks of a room the planner built, and whether that is good enough.
    ///
    /// RimWorld rates every enclosed room and shows it on hover under the room-stats overlay:
    /// a label for the room's role, and a banded word for each stat — "cramped", "average-sized",
    /// "awful", "mediocre". Those bands are the game's own, declared on each RoomStatDef, and the
    /// director reads them rather than inventing thresholds so a mod that redefines them moves
    /// this with it.
    ///
    /// Only the stats that construction decides are judged here. Cleanliness is left out on
    /// purpose: it is a work-priority outcome, not a building one — a spotless room and a filthy
    /// one can be the same room on different days depending on whether anybody swept it — so
    /// holding the builder responsible for it would score the wrong subsystem. Space and beauty
    /// are the two the builder actually chooses, through room dimensions and through wall
    /// material and furniture; impressiveness is what the colonists feel, and is the game's own
    /// combination of the two.
    ///
    /// The standards are floors, not targets. Stage 0 of any stat is the band the game itself
    /// calls the worst one, and a room landing there is a construction the colony would have
    /// been better off building differently. Everything above that is left alone — how nice a
    /// bedroom ought to be is a judgement that belongs to the genome and the score, not to a
    /// fixed rule in here.
    /// </summary>
    public static class RoomQuality
    {
        /// <summary>The lowest band a role can live with, as an index into the game's stages.</summary>
        public struct Standard
        {
            /// <summary>Minimum Space stage. 0 is "cramped", 1 "rather tight", 2 "average-sized".</summary>
            public int space;

            /// <summary>Minimum Impressiveness stage. 0 is "awful", 1 "dull", 2 "mediocre".</summary>
            public int impressiveness;

            /// <summary>Whether anybody's mood actually reads this room's impressiveness.</summary>
            public bool impressivenessMatters;
        }

        /// <summary>
        /// What a role needs from its room.
        ///
        /// Two families. Rooms people *live* in — bedrooms, the dining room, the hospital, cells —
        /// feed a mood thought off impressiveness, so an awful one costs the colony every day it
        /// stands. Rooms people *work* in do not: nobody is upset by an ugly workshop, but a
        /// cramped one cannot fit the bench it exists for, so space is the whole of the standard.
        /// </summary>
        public static Standard StandardFor(string role)
        {
            var s = new Standard();
            s.space = 1;
            s.impressiveness = 0;
            s.impressivenessMatters = false;

            switch (role)
            {
                // Lived in. An awful room is felt every night.
                case "Bedroom":
                case "Prison":
                    s.space = 1;
                    s.impressiveness = 1;
                    s.impressivenessMatters = true;
                    break;

                case "Dining":
                case "Hospital":
                    s.space = 2;
                    s.impressiveness = 1;
                    s.impressivenessMatters = true;
                    break;

                // Worked in. Space is what the equipment needs; looks are nobody's complaint.
                case "Kitchen":
                case "Research":
                case "Workshop":
                case "Storage":
                    s.space = 2;
                    break;

                // Machinery. Wants to be enclosed and little else.
                case "Power":
                case "Freezer":
                    s.space = 1;
                    break;
            }
            return s;
        }

        /// <summary>
        /// What is wrong with this room, or null when it meets the floor for its role.
        ///
        /// Phrased with the game's own words for the band it landed in, because those are what
        /// the overlay shows and what makes a chronicle line checkable against the screen.
        /// </summary>
        public static string Shortfall(string role, int spaceStage, string spaceLabel,
                                       int impressivenessStage, string impressivenessLabel)
        {
            var standard = StandardFor(role);

            bool tooSmall = spaceStage < standard.space;
            bool tooGrim = standard.impressivenessMatters &&
                           impressivenessStage < standard.impressiveness;

            if (tooSmall && tooGrim)
                return "it is " + spaceLabel + " and " + impressivenessLabel;
            if (tooSmall)
                return "it is " + spaceLabel;
            if (tooGrim)
                return "it is " + impressivenessLabel;

            return null;
        }

        /// <summary>
        /// Whether a shortfall is the builder's to answer at all.
        ///
        /// Space cannot be remedied after the fact — the walls are where they are, and the only
        /// answer is to site the next one bigger, which is the genome's business rather than the
        /// upkeep module's. Grimness can: a lamp and a pot cost almost nothing and move it.
        /// Raising a defect for a shortfall nothing can act on would put a permanent complaint
        /// into the survey that every pass would try and fail to satisfy.
        /// </summary>
        public static bool Actionable(string role, int spaceStage, int impressivenessStage)
        {
            var standard = StandardFor(role);
            return standard.impressivenessMatters &&
                   impressivenessStage < standard.impressiveness;
        }
    }
}
