using System.Collections.Generic;

namespace AutoColony
{
    /// <summary>
    /// How far away something is by walking, rather than by drawing a circle.
    ///
    /// The director measures distance two ways and has never noticed. `TicksToReach` asks the
    /// game whether a colonist can reach a casualty at all and then prices the walk with
    /// `(a - b).LengthHorizontal` — a straight line. `HostilesWithin` does not even ask the first
    /// question: it compares squared straight-line distance against a radius. Both are circles,
    /// and a circle cannot tell a threat forty cells away behind a mountain from one eighty cells
    /// away down open ground. The second arrives first.
    ///
    /// Run 206 is what that costs. An infestation put ten hostiles inside rock at `danger None`;
    /// the radius said they were close, the colony withdrew, stood down, withdrew again, and
    /// spent from day 4 20h to day 5 00h flipping four times an hour with `roomsEver: 0`. Not one
    /// of those insects could reach anybody, and nothing in the decision could express that.
    ///
    /// **The output is time, not distance.** This codebase already prefers a deadline to a flag
    /// everywhere it has one — the blood-loss clock, the cold forecast, the patience ETA — and
    /// "hours until they arrive" is the form that lets a withdrawal ask whether the meal, the
    /// tend or the wall can be finished first. A radius can only ever answer "near".
    ///
    /// Free of game types so the arithmetic and the pruning rule can be argued with in a test;
    /// the callers do the reachability and the pathfinding, which need the map.
    /// </summary>
    public static class Reach
    {
        /// <summary>No route at all. Distinct from "far", and the two must never share a number.</summary>
        public const float Unreachable = -1f;

        /// <summary>Seconds of game time in one in-game hour, for turning a walk into a clock.</summary>
        public const float SecondsPerHour = 2500f / 60f;

        /// <summary>
        /// How long a walk of this many cells takes, in hours, at this speed.
        ///
        /// Returns <see cref="Unreachable"/> for a negative distance, which is how the callers
        /// pass "no path" through without inventing a large number that later reads as a
        /// measurement. A sentinel rendered as a duration has already cost this project a
        /// debugging session — `-1.0 hours of walking` in the casualty message.
        /// </summary>
        public static float Hours(float cells, float cellsPerSecond)
        {
            if (cells < 0f) return Unreachable;
            if (cellsPerSecond <= 0f) return Unreachable;
            return cells / cellsPerSecond / SecondsPerHour;
        }

        /// <summary>
        /// The same walk read the other way round: how many cells fit inside a length of time.
        ///
        /// The planner needs this because a tolerance for distance is not really a tolerance for
        /// distance. Nobody has an opinion about forty cells; they have an opinion about how long
        /// they are willing to spend walking between two rooms, which is a duration, and which
        /// turns into a different number of cells for a colony of amputees than for one on
        /// go-juice. Stating the tolerance as time and converting it here keeps the strategy in
        /// the units it was actually formed in.
        ///
        /// Returns <see cref="Unreachable"/> where <see cref="Hours"/> would, for the same
        /// reason: a speed of zero is the absence of a measurement, not a distance of zero.
        /// </summary>
        public static float Cells(float hours, float cellsPerSecond)
        {
            if (hours < 0f) return Unreachable;
            if (cellsPerSecond <= 0f) return Unreachable;
            return hours * cellsPerSecond * SecondsPerHour;
        }

        /// <summary>
        /// Whether a candidate this far away in a straight line could still beat the best path
        /// found so far.
        ///
        /// The whole reason pathfinding every hostile every pass is affordable. A path is never
        /// shorter than the straight line between its ends, so sorting candidates by straight-line
        /// distance and walking them in order lets the search stop as soon as the next candidate's
        /// straight line is already longer than the best real path — everything after it is
        /// further still. On an ordinary raid that is one or two paths instead of forty.
        ///
        /// An admissible lower bound, in the A* sense, used for pruning rather than for guiding.
        /// </summary>
        public static bool CouldBeat(float straightLineCells, float bestPathCells)
        {
            if (bestPathCells < 0f) return true;          // nothing found yet, everything is worth trying
            return straightLineCells < bestPathCells;
        }

        /// <summary>
        /// The shortest of a set of walks, ignoring the ones with no route.
        ///
        /// Returns <see cref="Unreachable"/> when nothing has a route, which is a different fact
        /// from "the nearest is far away" and is what the infestation case needs to say.
        /// </summary>
        public static float Nearest(List<float> distances)
        {
            float best = Unreachable;
            if (distances == null) return best;

            for (int i = 0; i < distances.Count; i++)
            {
                float d = distances[i];
                if (d < 0f) continue;
                if (best < 0f || d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// Whether something is close enough to act on, given how long it takes to arrive.
        ///
        /// Unreachable is never close, which is the whole point: ten insects sealed in rock are
        /// not a threat however few cells away they sit. A colony that cannot express that
        /// withdraws from them, stands down, and withdraws again for ever.
        /// </summary>
        public static bool Imminent(float hours, float withinHours)
        {
            if (hours < 0f) return false;
            return hours <= withinHours;
        }

        /// <summary>
        /// How good a place this is to breach, for a colonist sealed into a pocket.
        ///
        /// Lower is better. The cost of opening a way out is the work of removing whatever is in
        /// the way plus the walk that remains on the far side — so a thin wall onto a long
        /// detour can lose to a thicker one that opens straight onto the base. Nothing here knows
        /// what a wall or a rock is; the caller supplies the work and the remaining distance, and
        /// the two are added on the same scale so neither silently dominates.
        ///
        /// A far side that cannot be reached from the rest of the colony is not an exit at all —
        /// it is a second pocket — and scores as unusable rather than as merely expensive.
        /// </summary>
        public static float BreachCost(float workToRemove, float cellsBeyond, float workWeight)
        {
            if (cellsBeyond < 0f) return float.MaxValue;      // opens onto nowhere
            if (workToRemove < 0f) workToRemove = 0f;
            if (workWeight < 0f) workWeight = 0f;
            return workToRemove * workWeight + cellsBeyond;
        }
    }
}
