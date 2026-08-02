using AutoColony.Rooms;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Furniture went into the first legal cell of the interior, so a room's contents piled into
    /// one corner in iteration order — blocking their own access and costing the room the Space
    /// rating RimWorld scores it on.
    /// </summary>
    public class FurniturePlacementTests
    {
        static PlacementFeatures Cell(float fromDoor, int freeSides, bool wall, float fromOther)
        {
            var f = new PlacementFeatures();
            f.fromDoor = fromDoor;
            f.freeSides = freeSides;
            f.againstWall = wall;
            f.fromOtherFurniture = fromOther;
            return f;
        }

        [Fact]
        public void ABedWantsACornerAwayFromTheDoor()
        {
            var w = FurniturePlacement.DefaultsFor(FurnitureKind.Bed);
            float corner = FurniturePlacement.Score(Cell(6f, 2, true, 4f), w);
            float byTheDoor = FurniturePlacement.Score(Cell(1f, 4, false, 1f), w);
            Assert.True(corner > byTheDoor);
        }

        [Fact]
        public void AWorkbenchWantsSomewhereToStand()
        {
            // Open sides matter more to a bench than a wall at its back does.
            var w = FurniturePlacement.DefaultsFor(FurnitureKind.WorkTable);
            float open = FurniturePlacement.Score(Cell(4f, 4, false, 3f), w);
            float boxedIn = FurniturePlacement.Score(Cell(4f, 1, true, 3f), w);
            Assert.True(open > boxedIn);
        }

        [Fact]
        public void ABedAndABenchDisagreeAboutTheSameCell()
        {
            // The whole reason the weights are per kind: one ordering cannot serve both.
            var cornerCell = Cell(6f, 1, true, 4f);
            var openCell = Cell(4f, 4, false, 4f);

            var bed = FurniturePlacement.DefaultsFor(FurnitureKind.Bed);
            var bench = FurniturePlacement.DefaultsFor(FurnitureKind.WorkTable);

            Assert.True(FurniturePlacement.Score(cornerCell, bed) >
                        FurniturePlacement.Score(openCell, bed));
            Assert.True(FurniturePlacement.Score(openCell, bench) >
                        FurniturePlacement.Score(cornerCell, bench));
        }

        [Fact]
        public void FurnitureDoesNotPileIntoOneCorner()
        {
            var w = FurniturePlacement.DefaultsFor(FurnitureKind.Surface);
            Assert.True(FurniturePlacement.Score(Cell(4f, 4, false, 4f), w) >
                        FurniturePlacement.Score(Cell(4f, 4, false, 1f), w));
        }

        [Fact]
        public void DistanceStopsPayingOnceItIsEnough()
        {
            // Or a lamp would be driven into the furthest corner of the room for nothing.
            Assert.Equal(FurniturePlacement.Saturate(6f, 6f),
                         FurniturePlacement.Saturate(30f, 6f), 3);
        }

        [Fact]
        public void EveryKindHasItsOwnGeneKeys()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (FurnitureKind kind in System.Enum.GetValues(typeof(FurnitureKind)))
                foreach (var aspect in FurniturePlacement.Aspects)
                    Assert.True(seen.Add(FurniturePlacement.GeneKey(kind, aspect)));

            Assert.Equal(4 * FurniturePlacement.Aspects.Length, seen.Count);
        }

        [Fact]
        public void AStrategyCanFlattenAPreferenceEntirely()
        {
            var w = FurniturePlacement.DefaultsFor(FurnitureKind.Bed);
            w.wallHugging = 0f;
            Assert.Equal(FurniturePlacement.Score(Cell(4f, 3, true, 3f), w),
                         FurniturePlacement.Score(Cell(4f, 3, false, 3f), w), 3);
        }
    }
}
