using AutoColony.Rooms;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Whether the planner can tell a far site from an absurd one.
    ///
    /// Run 189 sited a 9x9 Storage room at x=34 with the rest of the base between x=133 and
    /// x=146 — a hundred cells out, every haul a two-hundred-cell round trip — because the
    /// distance cost returned 1.0 at forty cells and 1.0 at four hundred, and "wants to be near
    /// rock" then decided.
    /// </summary>
    public class RoomSprawlTests
    {
        [Fact]
        public void FortyOneCellsAndFourHundredUsedToScoreTheSame()
        {
            // The fault, pinned as it was.
            Assert.Equal(RoomSiting.Cost(41f, 40f), RoomSiting.Cost(400f, 40f), 5);
        }

        [Fact]
        public void AndNowTheyDoNot()
        {
            float near = RoomSiting.Cost(41f, 40f, 3f);
            float far = RoomSiting.Cost(400f, 40f, 3f);

            Assert.True(far > near * 2f, "a hundred cells out must cost more; " +
                                         near + " vs " + far);
        }

        [Fact]
        public void ACeilingOfOneIsExactlyTheOldBehaviour()
        {
            // Every existing genome must site rooms as it always did, or the change alters
            // colonies that never asked for it.
            for (float d = 0f; d <= 500f; d += 7f)
                Assert.Equal(RoomSiting.Cost(d, 40f), RoomSiting.Cost(d, 40f, 1f), 5);
        }

        [Fact]
        public void NearSitesAreUntouchedWhateverTheCeiling()
        {
            // Inside the knee the curve is unchanged, so ordinary siting does not move.
            for (float d = 0f; d < 40f; d += 3f)
                Assert.Equal(RoomSiting.Cost(d, 40f), RoomSiting.Cost(d, 40f, 3f), 5);
        }

        [Fact]
        public void TheCeilingBoundsIt()
        {
            // Uncapped, four hundred cells would score ten against every other term's one and
            // compactness would decide every room in every colony by itself.
            Assert.Equal(3f, RoomSiting.Cost(4000f, 40f, 3f), 5);
        }

        [Fact]
        public void ASprawlingSiteLosesToACloseOneAllElseEqual()
        {
            var close = new SiteFeatures { buildable = 1f, fromOrigin = 20f, toResource = 30f };
            var sprawl = new SiteFeatures { buildable = 1f, fromOrigin = 120f, toResource = 0f };
            var w = new SiteWeights { compactness = 1f, resourceAffinity = 1f };

            Assert.True(RoomSiting.Score(close, w, 3f) > RoomSiting.Score(sprawl, w, 3f),
                "sitting on the rock a hundred cells out must not beat sitting at home");
        }

        [Fact]
        public void ForestGroundLosesToOpenGroundAllElseEqual()
        {
            // Run 191: "clearing 56 obstructions from the Storage room's footprint before
            // building it", three days after siting, on eighty-one cells of forest. The scorer
            // had no idea — BuildableFraction excludes cells holding an edifice, and a tree is
            // not an edifice, so a wood counts as perfectly buildable and then costs fifty-six
            // jobs before the first wall.
            var open = new SiteFeatures { buildable = 1f, toClear = 0f };
            var forest = new SiteFeatures { buildable = 1f, toClear = 0.69f };
            var w = new SiteWeights { openGround = 1f };

            Assert.True(RoomSiting.Score(open, w) > RoomSiting.Score(forest, w),
                "eighty-one cells of trees must cost something");
        }

        [Fact]
        public void AGenomeThatDoesNotMindTreesIsUnaffected()
        {
            // The weight runs to zero, and at zero clearing must not enter the score at all —
            // otherwise every existing genome sites rooms differently for a reason it did not
            // choose.
            var forest = new SiteFeatures { buildable = 1f, toClear = 0.69f };
            var w = new SiteWeights { openGround = 0f };

            Assert.Equal(RoomSiting.Score(new SiteFeatures { buildable = 1f }, w),
                         RoomSiting.Score(forest, w), 5);
        }

        [Fact]
        public void ClearingIsSeparateFromBuildability()
        {
            // They are different questions and must stay so: a blueprint CAN be placed on a
            // forested cell, which is why buildable is 1.0 there. What cannot happen is the wall
            // going up before somebody cuts the tree.
            var forest = new SiteFeatures { buildable = 1f, toClear = 1f };
            Assert.NotEqual(float.NegativeInfinity,
                            RoomSiting.Score(forest, new SiteWeights { openGround = 1f }));
        }
    }
}
