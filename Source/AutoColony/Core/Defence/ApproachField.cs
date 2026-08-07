using System.Collections.Generic;

namespace AutoColony.Defence
{
    /// <summary>
    /// Where attackers walk in, measured rather than assumed.
    ///
    /// A raid can arrive at any edge tile and the director has never had a representation of
    /// that. Its one defensive siting decision walks `GenRadial.RadialCellsAround(origin, 14)`
    /// and takes the first cell the game accepts — a ring, in an arbitrary direction, with no
    /// idea where anyone comes from. That is the fault this project has now named four times in
    /// one day: a circle where a route was wanted.
    ///
    /// The measure is betweenness. Sample the map edge, walk each sample to the base, and count
    /// how many of those walks cross each cell. Terrain that funnels shows up as a few cells
    /// carrying most of the traffic; open ground shows up as traffic spread thin. Both answers
    /// are useful and the difference decides whether a chokepoint is worth holding or whether
    /// building one would be expensive — `docs/rimworld/combat.md` puts chokepoints, traps and
    /// killboxes all on top of knowing this, and none of them was reachable without it.
    ///
    /// **This decides nothing, deliberately.** Three instruments added today changed what their
    /// decision should be once their number was visible, and one of them was silently inert until
    /// a number appeared. Siting turrets against a field nobody has read yet would repeat that.
    ///
    /// Free of game types, so the verdict can be argued with in a test. Same shape as
    /// <see cref="CapabilityGaps"/>: a static store with Clear and Explain.
    /// </summary>
    public static class ApproachField
    {
        /// <summary>Crossings per cell key, for the cells any sampled walk touched.</summary>
        static readonly Dictionary<int, int> crossings = new Dictionary<int, int>();

        static int sampled, routesFound, peakCell, peakCrossings;
        static bool stale = true;

        /// <summary>Forget the field. A colony that has moved is measuring a different map.</summary>
        public static void Clear()
        {
            crossings.Clear();
            sampled = routesFound = peakCrossings = 0;
            peakCell = -1;
        }

        /// <summary>
        /// The colony's own walls change where attackers walk, which is the entire point — so
        /// anything that changes the base marks this for recomputing rather than letting it
        /// describe a base that no longer exists.
        /// </summary>
        public static void MarkStale() { stale = true; }

        public static bool IsStale { get { return stale; } }

        public static int Sampled { get { return sampled; } }
        public static int RoutesFound { get { return routesFound; } }
        public static int PeakCell { get { return peakCell; } }
        public static int PeakCrossings { get { return peakCrossings; } }

        /// <summary>Begin a survey. Everything from the last one goes.</summary>
        public static void Begin()
        {
            Clear();
            stale = false;
        }

        /// <summary>One walk crossed this cell. Called once per cell per route.</summary>
        public static void Cross(int cellKey)
        {
            int n;
            crossings.TryGetValue(cellKey, out n);
            n++;
            crossings[cellKey] = n;

            if (n > peakCrossings) { peakCrossings = n; peakCell = cellKey; }
        }

        /// <summary>One edge sample was taken, and whether it had a route at all.</summary>
        public static void Sample(bool hadRoute)
        {
            sampled++;
            if (hadRoute) routesFound++;
        }

        /// <summary>
        /// Every cell any walk crossed, for a caller that can do geometry.
        ///
        /// Needed because the peak alone is useless. Every route ends at the base, so the base
        /// carries 100% of them by construction and always wins — the first survey run in anger
        /// reported "100% concentration, 0 cells from the base", which is true, degenerate, and
        /// exactly what the plan's verification line was written to catch.
        ///
        /// The bottleneck worth holding is the *furthest* cell that still carries most of the
        /// traffic: concentration decays outward from the destination, so the question is how far
        /// out it stays high. That needs distances, which need the map, which is next door.
        /// </summary>
        public static IEnumerable<KeyValuePair<int, int>> AllCrossings()
        {
            return crossings;
        }

        /// <summary>How many walks crossed this cell.</summary>
        public static int CrossingsAt(int cellKey)
        {
            int n;
            return crossings.TryGetValue(cellKey, out n) ? n : 0;
        }

        /// <summary>
        /// The busiest cell's share of the routes that exist.
        ///
        /// Denominated in routes found rather than samples taken, because a sample with no route
        /// is not an approach anybody declined to use — it is a direction nothing can come from,
        /// and counting it would dilute the concentration of a map that is genuinely one corridor.
        /// </summary>
        public static float Concentration(int peak, int routes)
        {
            if (routes <= 0 || peak <= 0) return 0f;
            float share = (float)peak / routes;
            return share > 1f ? 1f : share;
        }

        /// <summary>Whether the terrain already funnels hard enough to be worth holding.</summary>
        public static bool IsChokepoint(float concentration, float threshold)
        {
            if (threshold <= 0f) threshold = 0.5f;
            return concentration >= threshold;
        }

        /// <summary>
        /// What the numbers mean, in words, for the record.
        ///
        /// Three answers and they are genuinely different situations. Nothing walking in at all is
        /// a mountain base whose walling is already done by geology. An even spread is open ground
        /// where funnelling costs real wood and should be decided on purpose. A dominant cell is a
        /// corridor the colony already owns and has never once used.
        /// </summary>
        public static string Verdict(int sampledCount, int routes, float concentration,
                                     float threshold)
        {
            if (sampledCount <= 0) return "nothing sampled";
            if (routes <= 0) return "nothing walks in; the mountain is the wall";

            if (IsChokepoint(concentration, threshold))
                return "a natural chokepoint";

            return "open ground on every side, no chokepoint to hold";
        }

        /// <summary>The field in one line, counts first so an empty survey reads as empty.</summary>
        public static string Explain(float threshold)
        {
            if (sampled <= 0) return "approach: nothing sampled yet";

            float c = Concentration(peakCrossings, routesFound);
            if (routesFound <= 0)
                return string.Format("approach: {0} sampled, none with a route — {1}",
                                     sampled, Verdict(sampled, 0, 0f, threshold));

            return string.Format(
                "approach: {0} edge cells sampled, {1} with a route; {2} of them cross the " +
                "busiest cell — {3:P0} concentration, {4}",
                sampled, routesFound, peakCrossings, c, Verdict(sampled, routesFound, c, threshold));
        }
    }
}
