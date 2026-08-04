using System;

namespace AutoColony.Furniture
{
    /// <summary>
    /// The arithmetic behind <see cref="FuelUpkeep"/>, free of Verse so it can be tested.
    ///
    /// Two questions, both about labour rather than about wood. A colony can be rich in wood and
    /// still unable to keep its fires lit, because carrying it is a job somebody has to take and
    /// there are only ever three or four somebodies.
    /// </summary>
    public static class FuelBudget
    {
        /// <summary>
        /// Whether the colony is behind on carrying fuel.
        ///
        /// One dry hopper per colonist is the line. Below it, the colony is keeping up and the
        /// dryness is the normal churn of something having just been used; at or above it, every
        /// colonist already owes a trip and the queue is not shortening.
        ///
        /// The comparison is against colonists rather than against a constant on purpose. Two
        /// dry hoppers is nothing to a colony of six and is most of the work a colony of two can
        /// do in a day, and a fixed threshold would be wrong for one of them whichever number
        /// were chosen.
        /// </summary>
        public static bool BehindOnFuel(int hoppersWantingFuel, int colonists)
        {
            if (hoppersWantingFuel <= 0) return false;
            return hoppersWantingFuel >= Math.Max(1, colonists);
        }

        /// <summary>
        /// Whether one more fire is something the colony can carry.
        ///
        /// The first of a kind always passes. A colony with no cooler at all in a heatwave has a
        /// problem a cooler solves, and refusing it because the stove is empty trades one
        /// emergency for another. What this stops is the second and the fifth.
        /// </summary>
        public static bool CanKeepAnotherFed(int hoppersWantingFuel, int colonists, int alreadyBuilt)
        {
            if (alreadyBuilt <= 0) return true;
            return !BehindOnFuel(hoppersWantingFuel, colonists);
        }
    }
}
