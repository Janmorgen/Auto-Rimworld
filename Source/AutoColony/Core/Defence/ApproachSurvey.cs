using System;
using Verse;
using Verse.AI;

namespace AutoColony.Defence
{
    /// <summary>
    /// Walks the map edge to the base and counts what the walks cross.
    ///
    /// The map-side half of <see cref="ApproachField"/>. Everything here needs a Map and so
    /// cannot be tested offline; the arithmetic and the verdict live next door where they can be.
    ///
    /// **Uniform sampling on purpose.** The director does not know which edge a raid will pick,
    /// and a prior that spreads evenly is the honest statement of that. Weighting the sample
    /// toward where past raids came from would be learning, and would want a colony's worth of
    /// evidence before it beat the flat prior.
    ///
    /// **A colonist is the walker.** `FindPathNow` wants a pawn, and the traversal that matters is
    /// "somebody who will open a door rather than be stopped by one" — which is true of raiders
    /// and of colonists alike. It is not true of insects, which is a real limit and is why the
    /// verdict describes the ordinary walking raid and says so.
    /// </summary>
    public static class ApproachSurvey
    {
        /// <summary>
        /// Sample the perimeter, walk each sample home, and fill the field.
        ///
        /// Costly enough to be worth doing rarely — roughly fifty paths across a whole map — and
        /// cheap enough that doing it when the base changes is nothing. The caller decides when.
        /// </summary>
        public static void Run(Map map, IntVec3 origin, Pawn walker, int spacing)
        {
            ApproachField.Begin();
            if (map == null || walker == null || !walker.Spawned || !origin.IsValid) return;
            if (spacing < 2) spacing = 2;

            int w = map.Size.x, h = map.Size.z;

            // Round the perimeter: both horizontal edges, then both vertical ones. The corners
            // are covered by the horizontal pass and skipped by the vertical, so nothing is
            // sampled twice and no direction is quietly weighted double.
            for (int x = 0; x < w; x += spacing)
            {
                WalkHome(map, new IntVec3(x, 0, 0), origin, walker);
                WalkHome(map, new IntVec3(x, 0, h - 1), origin, walker);
            }
            for (int z = spacing; z < h - 1; z += spacing)
            {
                WalkHome(map, new IntVec3(0, 0, z), origin, walker);
                WalkHome(map, new IntVec3(w - 1, 0, z), origin, walker);
            }
        }

        static void WalkHome(Map map, IntVec3 from, IntVec3 origin, Pawn walker)
        {
            if (!from.InBounds(map)) return;

            PawnPath path = null;
            try
            {
                // An edge cell in rock or water is not a way in and is not a failed approach
                // either — it is simply not an edge anybody could stand on. Counted as a sample
                // without a route, so the two are told apart in the record.
                if (!from.Walkable(map)) { ApproachField.Sample(false); return; }

                var parms = TraverseParms.For(walker, Danger.Deadly, TraverseMode.ByPawn, false);
                if (!map.reachability.CanReach(from, origin, PathEndMode.OnCell, parms))
                {
                    ApproachField.Sample(false);
                    return;
                }

                path = map.pathFinder.FindPathNow(from, origin, walker, null);
                if (path == null || !path.Found) { ApproachField.Sample(false); return; }

                // NodesReversed is the whole route rather than what is left of it, which matters:
                // the walk has not been taken, it is being imagined. Probed against the assembly
                // before it was written, because a node accessor that compiles and yields nothing
                // would produce an all-zero field indistinguishable from a map nobody can cross.
                var nodes = path.NodesReversed;
                if (nodes == null || nodes.Count == 0) { ApproachField.Sample(false); return; }

                for (int i = 0; i < nodes.Count; i++)
                {
                    var cell = nodes[i];
                    if (cell.InBounds(map)) ApproachField.Cross(map.cellIndices.CellToIndex(cell));
                }
                ApproachField.Sample(true);
            }
            catch (Exception) { ApproachField.Sample(false); }
            finally { if (path != null) path.ReleaseToPool(); }
        }
    }
}
