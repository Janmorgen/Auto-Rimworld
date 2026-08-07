using AutoColony;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Run 196's deadlock, as arithmetic: a colony that will walk 115 cells for a meal and 55
    /// for a log, on a map where the walls need wood.
    /// </summary>
    public class GatherReachTests
    {
        const int Base = 55;
        const float Stretch = 60f;

        /// <summary>A stocked colony has no reason to walk further, and must not.</summary>
        [Fact]
        public void AStockedStoreDoesNotWiden()
        {
            Assert.Equal(0f, GatherReach.Shortfall(100f, 100f));
            Assert.Equal(0f, GatherReach.Shortfall(500f, 100f));
            Assert.Equal(Base, GatherReach.Radius(Base, 0f, Stretch));
        }

        /// <summary>
        /// The observed case: no wood at all against a target, so the search goes as far as the
        /// stretch allows. This is the reading that would have found run 196's trees.
        /// </summary>
        [Fact]
        public void HoldingNoneOfItReachesAsFarAsTheStretchAllows()
        {
            Assert.Equal(1f, GatherReach.Shortfall(0f, 300f));
            Assert.Equal(115, GatherReach.Radius(Base, 1f, Stretch));
        }

        /// <summary>Half short reaches half as far past the base. Linear, and only that.</summary>
        [Fact]
        public void BeingHalfShortReachesHalfTheExtraDistance()
        {
            Assert.Equal(0.5f, GatherReach.Shortfall(150f, 300f));
            Assert.Equal(85, GatherReach.Radius(Base, 0.5f, Stretch));
        }

        /// <summary>
        /// All three gatherers now derive the radius the same way, so the same shortfall gives
        /// the same reach whatever is being looked for. The asymmetry was the whole fault.
        /// </summary>
        [Fact]
        public void TheSameShortfallReachesTheSameDistanceForEveryGatherer()
        {
            float wood = GatherReach.Shortfall(0f, 300f);
            float steel = GatherReach.Shortfall(0f, 80f);
            float food = 1f;

            int a = GatherReach.Radius(Base, wood, Stretch);
            int b = GatherReach.Radius(Base, steel, Stretch);
            int c = GatherReach.Radius(Base, food, Stretch);

            Assert.Equal(a, b);
            Assert.Equal(b, c);
        }

        /// <summary>
        /// Wanting nothing is not the same as wanting it infinitely. A zero target used to be
        /// the sort of thing that divides by zero and searches the whole map.
        /// </summary>
        [Fact]
        public void AZeroTargetWantsNothing()
        {
            Assert.Equal(0f, GatherReach.Shortfall(0f, 0f));
            Assert.Equal(Base, GatherReach.Radius(Base, GatherReach.Shortfall(0f, 0f), Stretch));
        }

        /// <summary>A stretch of zero pins every gatherer to the base radius — today's chopping.</summary>
        [Fact]
        public void AZeroStretchReproducesTheOldFlatRadius()
        {
            Assert.Equal(Base, GatherReach.Radius(Base, 1f, 0f));
        }

        /// <summary>Nonsense in does not produce a search of the whole map.</summary>
        [Fact]
        public void ShortfallIsBounded()
        {
            Assert.Equal(115, GatherReach.Radius(Base, 5f, Stretch));
            Assert.Equal(Base, GatherReach.Radius(Base, -1f, Stretch));
            Assert.Equal(0, GatherReach.Radius(-10, 1f, 0f));
        }

        /// <summary>
        /// The default reproduces what hunting already did, so this change moves chopping and
        /// mining onto hunting's behaviour rather than inventing a third thing.
        /// </summary>
        [Fact]
        public void TheDefaultMatchesWhatHuntingAlreadyDid()
        {
            for (float u = 0f; u <= 1f; u += 0.25f)
                Assert.Equal(Base + (int)(u * 60f), GatherReach.Radius(Base, u, 60f));
        }
    }
}
