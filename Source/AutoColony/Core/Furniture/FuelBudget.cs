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

        /// <summary>
        /// Hoppers standing dry with nothing on the map that could fill them.
        ///
        /// This is a different failure from being behind, and the two want opposite responses.
        /// Behind means the colony is short of hands and will catch up; the answer is to stop
        /// adding to the pile and let Hauling work. Dry with no fuel means the job the game is
        /// waiting for cannot be taken by anyone, however the work table is set — run 110 sat at
        /// eight dry hoppers on a map with zero wood and no tree that yields any.
        ///
        /// Treating the second as the first is what made the whole thing look like a labour
        /// problem: Hauling was at the top of the table, so the reading was "the colony cannot
        /// keep up", when the truth was that there was nothing to keep up with.
        /// </summary>
        public static bool NoFuelToBeHad(int hoppersWantingFuel, int fuelOnHand, int fuelStanding)
        {
            return hoppersWantingFuel > 0 && fuelOnHand <= 0 && fuelStanding <= 0;
        }

        /// <summary>
        /// Fires out, no logs to carry, but timber still standing.
        ///
        /// The middle state, and the one that was invisible. It looks exactly like a supply
        /// failure from the hopper's end — nothing to haul — and exactly like a labour failure
        /// from the work table's end, since Hauling is already at the top and achieving nothing.
        /// It is neither: it is a *chopping* failure, and the lever is the wood target.
        /// </summary>
        public static bool FuelUncut(int hoppersWantingFuel, int fuelOnHand, int fuelStanding)
        {
            return hoppersWantingFuel > 0 && fuelOnHand <= 0 && fuelStanding > 0;
        }

        /// <summary>
        /// Whether to build something that burns at all.
        ///
        /// Unlike <see cref="CanKeepAnotherFed"/> this refuses the *first* one too. A stove on a
        /// map with no wood is not a kitchen, it is a wall with a bill list — and every hour
        /// spent hauling to it, cooking at it, or planning a room around it is spent on
        /// something that will never light.
        /// </summary>
        public static bool WorthBuildingABurner(int hoppersWantingFuel, int fuelOnHand,
                                                int fuelStanding, int burnersAlreadyBuilt)
        {
            if (burnersAlreadyBuilt <= 0 && hoppersWantingFuel <= 0) return true;
            return !NoFuelToBeHad(hoppersWantingFuel, fuelOnHand, fuelStanding);
        }
    }
}
