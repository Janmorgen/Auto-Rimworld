using System.Collections.Generic;

namespace AutoColony.Furniture
{
    /// <summary>A map cell, free of Verse so the geometry below can be tested offline.</summary>
    public struct SeatCell
    {
        public int x;
        public int z;

        public SeatCell(int x, int z) { this.x = x; this.z = z; }
    }

    /// <summary>
    /// Where a chair has to stand to count as being at the thing it serves.
    ///
    /// Split out from <see cref="SeatingRule"/> because this is the part that was wrong and the
    /// part a test can reach. The rule read <c>ExpandedBy(1).EdgeCells</c>, which is the ring of
    /// cells around the footprint *including its four corners*; RimWorld walks
    /// <c>GenAdj.CardinalDirections</c> from the chair and asks whether the edifice there is a
    /// table. A diagonal chair is not at the table, so every corner stool was furniture that
    /// satisfied the director and not the game — the table stayed unusable, AteWithoutTable went
    /// on firing, and the remedy went on ordering tables. Six tables, thirteen stools, two
    /// colonists.
    ///
    /// The doc comment on the old version already said corners were excluded. Only the code
    /// disagreed.
    /// </summary>
    public static class SeatingGeometry
    {
        /// <summary>
        /// The cells orthogonally touching a footprint, corners excluded.
        ///
        /// For a 1×2 table that is six cells rather than the ten the ring gives — the four
        /// corners are exactly the difference, and exactly the ones a pawn cannot sit in.
        /// </summary>
        public static List<SeatCell> CardinalRing(int minX, int maxX, int minZ, int maxZ)
        {
            var cells = new List<SeatCell>();

            for (int x = minX; x <= maxX; x++)
            {
                cells.Add(new SeatCell(x, minZ - 1));
                cells.Add(new SeatCell(x, maxZ + 1));
            }
            for (int z = minZ; z <= maxZ; z++)
            {
                cells.Add(new SeatCell(minX - 1, z));
                cells.Add(new SeatCell(maxX + 1, z));
            }
            return cells;
        }
    }
}
