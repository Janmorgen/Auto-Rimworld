using System.Collections.Generic;
using AutoColony.Furniture;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The corners are the whole point of these.
    ///
    /// The shipped version returned the full ring around the footprint, which put stools on
    /// diagonals. RimWorld reads cardinal directions only, so those stools satisfied the director
    /// and not the game: the table stayed unusable, the AteWithoutTable thought went on firing,
    /// and the remedy went on ordering more tables. A test that only counted cells would have
    /// passed the broken version, so these name the corners explicitly.
    /// </summary>
    public class SeatingGeometryTests
    {
        static bool Has(List<SeatCell> cells, int x, int z)
        {
            for (int i = 0; i < cells.Count; i++)
                if (cells[i].x == x && cells[i].z == z) return true;
            return false;
        }

        [Fact]
        public void SingleCellHasFourNeighboursNotEight()
        {
            var cells = SeatingGeometry.CardinalRing(5, 5, 5, 5);

            Assert.Equal(4, cells.Count);
            Assert.True(Has(cells, 5, 4));
            Assert.True(Has(cells, 5, 6));
            Assert.True(Has(cells, 4, 5));
            Assert.True(Has(cells, 6, 5));
        }

        [Fact]
        public void CornersAreExcluded()
        {
            var cells = SeatingGeometry.CardinalRing(5, 5, 5, 5);

            Assert.False(Has(cells, 4, 4));
            Assert.False(Has(cells, 6, 6));
            Assert.False(Has(cells, 4, 6));
            Assert.False(Has(cells, 6, 4));
        }

        [Fact]
        public void TwoByOneTableGivesSixSeatsNotTen()
        {
            // The ring around a 1x2 footprint is ten cells; four of them are corners.
            var cells = SeatingGeometry.CardinalRing(10, 10, 20, 21);

            Assert.Equal(6, cells.Count);
            Assert.True(Has(cells, 9, 20));
            Assert.True(Has(cells, 11, 21));
            Assert.True(Has(cells, 10, 19));
            Assert.True(Has(cells, 10, 22));
            Assert.False(Has(cells, 9, 19));
            Assert.False(Has(cells, 11, 22));
        }

        [Fact]
        public void EverySeatTouchesTheFootprintOrthogonally()
        {
            var cells = SeatingGeometry.CardinalRing(0, 1, 0, 1);

            foreach (var cell in cells)
            {
                bool insideX = cell.x >= 0 && cell.x <= 1;
                bool insideZ = cell.z >= 0 && cell.z <= 1;

                // Exactly one axis leaves the rect. Both leaving is a diagonal, neither is inside.
                Assert.True(insideX ^ insideZ);
            }
        }

        [Fact]
        public void NoCellIsInsideTheFootprint()
        {
            var cells = SeatingGeometry.CardinalRing(3, 6, 3, 6);

            foreach (var cell in cells)
                Assert.False(cell.x >= 3 && cell.x <= 6 && cell.z >= 3 && cell.z <= 6);
        }
    }
}
