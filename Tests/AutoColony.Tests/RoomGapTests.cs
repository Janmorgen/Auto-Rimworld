using AutoColony;
using AutoColony.Rooms;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Once sites stopped being a line, "near the origin" and "near the other rooms" stopped
    /// being the same fact, and a scorer with only the first could put a room forty cells out in
    /// a direction the base had never gone while a perfectly good site sat against an existing
    /// wall.
    /// </summary>
    public class RoomGapTests
    {
        static SiteFeatures Site(float fromOrigin, float toNearestRoom)
        {
            var f = new SiteFeatures();
            f.buildable = 1f;
            f.fromOrigin = fromOrigin;
            f.toNearestRoom = toNearestRoom;
            return f;
        }

        [Fact]
        public void ACorridorCouldNotLeaveAHoleAndAGridCan()
        {
            // Same distance from the origin, so compactness cannot tell these apart. One is
            // beside its neighbours and one is on its own.
            var beside = Site(30f, 8f);
            var adrift = Site(30f, 45f);
            var w = new SiteWeights { compactness = 1f, isolation = 1f };

            Assert.True(RoomSiting.Score(beside, w, 3f, 20f) > RoomSiting.Score(adrift, w, 3f, 20f),
                "a room on its own must lose to one against a wall at the same distance out");
        }

        [Fact]
        public void CompactnessAloneIsBlindToIt()
        {
            // Pinning why the term had to exist: with isolation off, the two sites above are
            // literally the same score.
            var w = new SiteWeights { compactness = 1f };
            Assert.Equal(RoomSiting.Score(Site(30f, 8f), w, 3f, 20f),
                         RoomSiting.Score(Site(30f, 45f), w, 3f, 20f), 5);
        }

        [Fact]
        public void TheFirstRoomHasNothingToBeFarFrom()
        {
            // A colony's first room must not be marked down for standing alone; it has no
            // choice. The sentinel is negative rather than large for exactly this.
            var w = new SiteWeights { isolation = 2f };
            var first = Site(10f, -1f);
            var touching = Site(10f, 0f);

            Assert.Equal(RoomSiting.Score(touching, w, 3f, 20f),
                         RoomSiting.Score(first, w, 3f, 20f), 5);
        }

        [Fact]
        public void AGapKeepsCostingPastItsKnee()
        {
            // Like distance from the origin and unlike the affinity terms: a gap is walked
            // across on every trip between the two rooms, for ever, so twice the gap must not
            // read as the same gap.
            var w = new SiteWeights { isolation = 1f };
            float atKnee = RoomSiting.Score(Site(0f, 21f), w, 3f, 20f);
            float wayOut = RoomSiting.Score(Site(0f, 200f), w, 3f, 20f);

            Assert.True(wayOut < atKnee - 0.5f,
                "two hundred cells of gap must cost more than twenty-one; " +
                atKnee + " vs " + wayOut);
        }

        [Fact]
        public void ButItCannotSwampEverythingElse()
        {
            // Bounded by the same sprawl ceiling as compactness, and for the same reason: one
            // uncapped term decides every room in every colony on its own.
            var w = new SiteWeights { isolation = 1f };
            Assert.Equal(RoomSiting.Score(Site(0f, 4000f), w, 3f, 20f),
                         RoomSiting.Score(Site(0f, 40000f), w, 3f, 20f), 5);
        }

        [Fact]
        public void AGenomeWithNoOpinionSitesRoomsAsItAlwaysDid()
        {
            // The weight runs to zero, and at zero the gap must not enter the score at all.
            var w = new SiteWeights { compactness = 1f, isolation = 0f };
            Assert.Equal(RoomSiting.Score(Site(30f, 0f), w, 3f, 20f),
                         RoomSiting.Score(Site(30f, 400f), w, 3f, 20f), 5);
        }

        /// <summary>
        /// The tolerance is chosen as a walking time and converted to cells against the colony's
        /// own speed, because that is the form the cost is actually paid in.
        /// </summary>
        [Fact]
        public void TheToleranceIsAWalkRatherThanADistance()
        {
            float unencumbered = Reach.Cells(0.1f, 4.6f);
            float limping = Reach.Cells(0.1f, 2.3f);

            Assert.True(unencumbered > limping,
                "the same patience must buy a slow colony a tighter base");
            Assert.Equal(19f, unencumbered, 0);
        }

        [Fact]
        public void AWalkAndItsDistanceAgree()
        {
            // Cells and Hours are the same statement read in opposite directions; if they ever
            // disagree, one of the two callers is quietly measuring something else.
            for (float hours = 0.05f; hours <= 1f; hours += 0.05f)
                Assert.Equal(hours, Reach.Hours(Reach.Cells(hours, 4.6f), 4.6f), 4);
        }

        [Fact]
        public void NoSpeedIsNotADistanceOfZero()
        {
            // The absence of a measurement, not a measurement of nothing — the same rule Hours
            // follows, and the reason the planner falls back rather than siting everything on
            // top of itself.
            Assert.Equal(Reach.Unreachable, Reach.Cells(0.1f, 0f));
            Assert.Equal(Reach.Unreachable, Reach.Cells(-1f, 4.6f));
        }
    }
}
