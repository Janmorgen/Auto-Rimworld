using System.Collections.Generic;
using AutoColony.Rooms;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Every room the planner ever sited landed in one of two rows, because the sites it chose
    /// between were a line. The scoring underneath could pick where along that line; it could
    /// not put a freezer behind a kitchen, because behind was not on offer.
    /// </summary>
    public class RoomLatticeTests
    {
        /// <summary>Every position out to a given ring, as (col, row) pairs.</summary>
        static List<int[]> Positions(int maxRing)
        {
            var found = new List<int[]>();
            for (int ring = 0; ring <= maxRing; ring++)
                for (int row = RoomLattice.LowestRow(ring); row <= RoomLattice.HighestRow(ring); row++)
                    for (int col = -ring; col <= ring; col++)
                        if (RoomLattice.Ring(col, row) == ring)
                            found.Add(new int[] { col, row });
            return found;
        }

        [Fact]
        public void TheOldLineIsStillInThere()
        {
            // The two rows the corridor layout could reach — row 0 north of it, row -1 south —
            // at exactly the coordinates the slot arithmetic produced. Nothing that used to be
            // sitable has stopped being sitable.
            Assert.Equal(102, RoomLattice.MinZ(100, 0, 7));    // origin.z + 2
            Assert.Equal(93, RoomLattice.MinZ(100, -1, 7));    // origin.z - 1 - (height - 1)
            Assert.Equal(100, RoomLattice.MinX(100, 0, 7));
            Assert.Equal(106, RoomLattice.MinX(100, 1, 7));    // origin.x + (width - 1)
            Assert.Equal(94, RoomLattice.MinX(100, -1, 7));
        }

        [Fact]
        public void AndThereIsNowSomewhereBehindIt()
        {
            // The whole point. A second row out on each side, which the line had no way to name.
            var rows = new HashSet<int>();
            foreach (var p in Positions(2)) rows.Add(p[1]);

            Assert.True(rows.Count > 2,
                "a lattice that offers two rows is the line it replaced; got " + rows.Count);
            Assert.Contains(1, rows);
            Assert.Contains(-2, rows);
        }

        [Fact]
        public void NeighboursShareAWall()
        {
            // The one thing worth keeping from the corridor: the east wall of one room is the
            // west wall of the next, so it is built once.
            const int width = 7;
            int a = RoomLattice.MinX(100, 0, width);
            int b = RoomLattice.MinX(100, 1, width);
            Assert.Equal(a + width - 1, b);

            const int height = 7;
            int lower = RoomLattice.MinZ(100, 0, height);
            int upper = RoomLattice.MinZ(100, 1, height);
            Assert.Equal(lower + height - 1, upper);
        }

        [Fact]
        public void TheCorridorIsNeverBuiltOn()
        {
            // The guarantee that a base filling in from all sides still has a way through. No
            // footprint at any position, at any size, may cover the origin's own two rows.
            for (int height = 5; height <= 13; height++)
                foreach (var p in Positions(4))
                {
                    int minZ = RoomLattice.MinZ(100, p[1], height);
                    int maxZ = minZ + height - 1;

                    bool coversCorridor = minZ <= 101 && maxZ >= 100;
                    Assert.False(coversCorridor,
                        "row " + p[1] + " at height " + height + " covers the corridor: " +
                        minZ + ".." + maxZ);
                }
        }

        [Fact]
        public void BothSidesOfTheCorridorAreOfferedTogether()
        {
            // Rows -1 and 0 are equally against the corridor. If they did not rank equally, a
            // search working outward would exhaust the north half of the base before offering a
            // single site to the south — the old bias, back by way of the loop order.
            Assert.Equal(RoomLattice.Ring(0, 0), RoomLattice.Ring(0, -1));
            Assert.Equal(RoomLattice.Ring(3, 2), RoomLattice.Ring(3, -3));
        }

        [Fact]
        public void RingsAreDistinctAndComplete()
        {
            // Nothing counted twice and nothing skipped, which is what lets the survey stop at
            // a ring boundary and still have looked at a whole ring.
            var seen = new HashSet<string>();
            foreach (var p in Positions(4))
                Assert.True(seen.Add(p[0] + "," + p[1]),
                    "position " + p[0] + "," + p[1] + " appears in two rings");

            // 9 columns by 10 rows, the rows being one more than the columns because the
            // corridor splits the middle one.
            Assert.Equal(90, seen.Count);
        }

        [Fact]
        public void ItIsFarMoreGroundThanTheCorridorEverHad()
        {
            // Forty slots was the corridor's whole ceiling, and it was a ceiling on the base's
            // size rather than on its shape.
            Assert.True(Positions(4).Count > 40);
        }
    }
}
