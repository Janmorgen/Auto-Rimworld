using AutoColony.Combat;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Positioning did not exist: every drafted colonist was sent to the base origin and told to
    /// shoot whatever was nearest. These pin the trade-offs that replaced it — all of which are
    /// genes, because none of them has a fixed right answer.
    /// </summary>
    public class FiringPositionTests
    {
        static PositionWeights Default()
        {
            var w = new PositionWeights();
            w.cover = 4f;
            w.standoff = 0.3f;
            w.preferredRange = 12f;
            w.spread = 2f;
            w.chokepoint = 1.5f;
            w.indoors = 1f;
            return w;
        }

        static PositionFeatures Cell(float cover, float toThreat, float toAlly)
        {
            var f = new PositionFeatures();
            f.cover = cover;
            f.toThreat = toThreat;
            f.toNearestAlly = toAlly;
            return f;
        }

        [Fact]
        public void CoverBeatsOpenGround()
        {
            var w = Default();
            Assert.True(FiringPosition.Score(Cell(0.75f, 12f, 5f), w) >
                        FiringPosition.Score(Cell(0f, 12f, 5f), w));
        }

        [Fact]
        public void StandingApartBeatsStandingTogether()
        {
            // One grenade landing among everybody is what a shared rally point produces.
            var w = Default();
            Assert.True(FiringPosition.Score(Cell(0.5f, 12f, 5f), w) >
                        FiringPosition.Score(Cell(0.5f, 12f, 1f), w));
        }

        [Fact]
        public void RangeIsWantedNearAPreferenceRatherThanMaximised()
        {
            var w = Default();
            float atRange = FiringPosition.Score(Cell(0.5f, 12f, 5f), w);
            Assert.True(atRange > FiringPosition.Score(Cell(0.5f, 1f, 5f), w));
            Assert.True(atRange > FiringPosition.Score(Cell(0.5f, 40f, 5f), w));
        }

        [Fact]
        public void AChokepointIsWorthSomething()
        {
            var w = Default();
            var plain = Cell(0.5f, 12f, 5f);
            var door = plain;
            door.chokepoint = true;
            Assert.True(FiringPosition.Score(door, w) > FiringPosition.Score(plain, w));
        }

        [Fact]
        public void AStrategyCanDecideCoverIsWorthNothing()
        {
            // A colony with only clubs wants to close, and the search must be able to say so.
            var w = Default();
            w.cover = 0f;
            Assert.Equal(FiringPosition.Score(Cell(0f, 12f, 5f), w),
                         FiringPosition.Score(Cell(1f, 12f, 5f), w), 3);
        }

        [Fact]
        public void SpreadStopsPayingBeyondAFewCells()
        {
            // Or the colony would scatter across the map chasing a benefit that has flattened.
            Assert.Equal(FiringPosition.SpreadValue(5f), FiringPosition.SpreadValue(30f), 3);
            Assert.True(FiringPosition.SpreadValue(3f) > FiringPosition.SpreadValue(1f));
        }

        [Fact]
        public void ShufflingBetweenNearIdenticalCellsIsNotWorthIt()
        {
            // Re-issuing the order restarts the job and the colonist never fires.
            Assert.False(FiringPosition.WorthMoving(10f, 10.2f));
            Assert.True(FiringPosition.WorthMoving(10f, 14f));
        }

        [Fact]
        public void NoAllyPlacedYetIsNotTreatedAsCrowding()
        {
            Assert.Equal(0f, FiringPosition.SpreadValue(0f), 3);
        }
    }
}
