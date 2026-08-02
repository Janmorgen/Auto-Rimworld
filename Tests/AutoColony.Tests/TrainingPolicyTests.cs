using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The snapshot decides what every candidate in a round is actually asked. Taken from a
    /// dying colony, the question is "can you escape this", and the answers are luck.
    /// </summary>
    public class TrainingPolicyTests
    {
        static bool Fit(int colonists, int downed, float food, bool emergency)
        {
            string why;
            return TrainingPolicy.WorthSnapshotting(colonists, downed, food, emergency, out why);
        }

        [Fact]
        public void AHealthyColonyIsWorthComparingCandidatesFrom()
        {
            Assert.True(Fit(4, 0, 8f, false));
        }

        [Fact]
        public void TheColonyThatProducedANoiseRoundIsRefused()
        {
            // One colonist, no food: the round it produced scored 0.466 and 0.000 from
            // strategies differing in hauling weight and a production buffer.
            Assert.False(Fit(1, 0, 0f, false));
        }

        [Fact]
        public void ACasualtyIsInheritedByEveryCandidate()
        {
            Assert.False(Fit(4, 1, 8f, false));
        }

        [Fact]
        public void AFamineIsNotAStrategyTest()
        {
            Assert.False(Fit(4, 0, 0.5f, false));
        }

        [Fact]
        public void AnEmergencyIsHandedToEveryCandidateAlike()
        {
            Assert.False(Fit(4, 0, 8f, true));
        }

        [Fact]
        public void TheReasonIsAlwaysGivenWhenRefusing()
        {
            string why;
            Assert.False(TrainingPolicy.WorthSnapshotting(1, 0, 0f, false, out why));
            Assert.False(string.IsNullOrEmpty(why));

            Assert.True(TrainingPolicy.WorthSnapshotting(4, 0, 8f, false, out why));
            Assert.True(string.IsNullOrEmpty(why));
        }
    }
}
