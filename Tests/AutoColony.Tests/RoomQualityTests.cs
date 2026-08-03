using AutoColony.Rooms;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The stage indices below are the game's own bands, from Core/Defs/Rooms/RoomStats.xml:
    ///
    ///   Space           0 cramped   1 rather tight (12.5)  2 average-sized (29)  3 somewhat spacious (55)
    ///   Impressiveness  0 awful     1 dull (20)            2 mediocre (30)       3 decent (40)
    ///
    /// Nothing here hardcodes the score thresholds themselves — the production code asks the def
    /// for the index — so these tests are about the policy applied to a band, not about the band.
    /// </summary>
    public class RoomQualityTests
    {
        // ------------------------------------------------------------------ worked-in rooms

        [Theory]
        [InlineData("Kitchen")]
        [InlineData("Research")]
        [InlineData("Workshop")]
        [InlineData("Storage")]
        public void AWorkRoomIsJudgedOnSpaceAloneNoMatterHowGrim(string role)
        {
            // Awful to look at, but big enough to work in: nobody's mood reads a workshop.
            Assert.Null(RoomQuality.Shortfall(role, 2, "average-sized", 0, "awful"));
        }

        [Theory]
        [InlineData("Kitchen")]
        [InlineData("Research")]
        [InlineData("Workshop")]
        [InlineData("Storage")]
        public void AWorkRoomTooSmallForItsEquipmentIsAShortfall(string role)
        {
            var shortfall = RoomQuality.Shortfall(role, 1, "rather tight", 3, "decent");
            Assert.Equal("it is rather tight", shortfall);
        }

        // ------------------------------------------------------------------ lived-in rooms

        [Theory]
        [InlineData("Bedroom")]
        [InlineData("Prison")]
        [InlineData("Dining")]
        [InlineData("Hospital")]
        public void ALivedInRoomThatIsAwfulIsAShortfallEvenWhenItIsLarge(string role)
        {
            var shortfall = RoomQuality.Shortfall(role, 3, "somewhat spacious", 0, "awful");
            Assert.Equal("it is awful", shortfall);
        }

        [Fact]
        public void ABedroomOnlyNeedsToClearTheWorstBandNotToBeNice()
        {
            // "dull" is stage 1 and that is the floor — the genome decides whether to do better.
            Assert.Null(RoomQuality.Shortfall("Bedroom", 1, "rather tight", 1, "dull"));
        }

        [Fact]
        public void BothFaultsAreNamedTogether()
        {
            var shortfall = RoomQuality.Shortfall("Hospital", 0, "cramped", 0, "awful");
            Assert.Equal("it is cramped and awful", shortfall);
        }

        // ------------------------------------------------------------------ machinery

        [Theory]
        [InlineData("Power")]
        [InlineData("Freezer")]
        public void MachineryRoomsWantOnlyToBeEnclosed(string role)
        {
            Assert.Null(RoomQuality.Shortfall(role, 1, "rather tight", 0, "awful"));
        }

        [Theory]
        [InlineData("Power")]
        [InlineData("Freezer")]
        public void EvenMachineryIsFlaggedWhenItIsOutrightCramped(string role)
        {
            Assert.Equal("it is cramped", RoomQuality.Shortfall(role, 0, "cramped", 5, "somewhat impressive"));
        }

        // ------------------------------------------------------------------ what can be acted on

        [Fact]
        public void GrimnessIsWorthRaisingBecauseFurnitureCanStillMoveIt()
        {
            Assert.True(RoomQuality.Actionable("Bedroom", 2, 0));
        }

        [Fact]
        public void CrampednessIsNotWorthRaisingBecauseWallsCannotBeMoved()
        {
            // A permanent complaint no remedy can satisfy is how a survey ends up retrying
            // the same impossible job on every pass, so this must stay false.
            Assert.False(RoomQuality.Actionable("Workshop", 0, 5));
        }

        [Fact]
        public void ACrampedWorkRoomIsReportedButNotActedOn()
        {
            Assert.NotNull(RoomQuality.Shortfall("Workshop", 0, "cramped", 5, "somewhat impressive"));
            Assert.False(RoomQuality.Actionable("Workshop", 0, 5));
        }

        // ------------------------------------------------------------------ cleanliness stays out

        [Fact]
        public void NothingInTheStandardRefersToCleanliness()
        {
            // Cleanliness is a work-priority outcome, not a construction one. A room the
            // builder built well scores badly the day nobody sweeps it, and holding the
            // builder to that would blame the wrong subsystem. Guarded by the shape of the
            // Standard itself: it carries space and impressiveness, and nothing else.
            var standard = RoomQuality.StandardFor("Kitchen");
            Assert.Equal(2, standard.space);
            Assert.False(standard.impressivenessMatters);
        }

        [Fact]
        public void AnUnknownRoleFallsBackToWantingOnlyToBeEnclosed()
        {
            var standard = RoomQuality.StandardFor("SomethingAModAdded");
            Assert.Equal(1, standard.space);
            Assert.False(standard.impressivenessMatters);
            Assert.Null(RoomQuality.Shortfall("SomethingAModAdded", 1, "rather tight", 0, "awful"));
        }
    }
}
