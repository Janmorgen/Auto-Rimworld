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

        // ------------------------------------------------------------------ barracks

        [Fact]
        public void ASharedBedroomIsHeldToAHigherFloorThanAPrivateOne()
        {
            // SleptInBedroom pays -2 up to +8; SleptInBarracks pays -7 up to +4. A shared room
            // is worse at the floor and lower at the ceiling, so the same impressiveness is
            // not the same outcome and cannot be the same standard.
            Assert.True(RoomQuality.StandardFor("Bedroom", 2).impressiveness >
                        RoomQuality.StandardFor("Bedroom", 1).impressiveness);
        }

        [Fact]
        public void ADullRoomPassesAlonePassesAndFailsWhenShared()
        {
            Assert.Null(RoomQuality.Shortfall("Bedroom", 1, "rather tight", 1, "dull", 1));
            Assert.NotNull(RoomQuality.Shortfall("Bedroom", 1, "rather tight", 1, "dull", 2));
        }

        [Fact]
        public void TheSharedRoomComplaintSaysWhyItCostsMore()
        {
            var shortfall = RoomQuality.Shortfall("Bedroom", 2, "average-sized", 0, "awful", 3);
            Assert.Contains("barracks", shortfall);
            Assert.Contains("3 are sharing", shortfall);
        }

        [Fact]
        public void SharingOnlyMattersForBedrooms()
        {
            // Two beds in a hospital is a hospital, and two beds in a prison is what a prison
            // is for. Neither reads as a barracks and neither should be penalised as one.
            Assert.Equal(RoomQuality.StandardFor("Hospital", 1).impressiveness,
                         RoomQuality.StandardFor("Hospital", 4).impressiveness);
            Assert.Equal(RoomQuality.StandardFor("Prison", 1).impressiveness,
                         RoomQuality.StandardFor("Prison", 4).impressiveness);
        }

        [Fact]
        public void TheDefaultBedCountLeavesTheOldJudgementUnchanged()
        {
            // The one-argument overloads are still used where a bed count is not to hand, and
            // must keep meaning exactly what they meant before.
            Assert.Equal(RoomQuality.StandardFor("Bedroom").impressiveness,
                         RoomQuality.StandardFor("Bedroom", 1).impressiveness);
        }

        // ------------------------------------------------------------------ the new roles

        [Fact]
        public void ARecRoomIsJudgedLikeSomewhereLivedIn()
        {
            // Every stage of JoyActivityInImpressiveRecRoom is positive, so the room only pays
            // when it is nice — but an awful one wastes the whole reason it was built.
            var standard = RoomQuality.StandardFor("Recreation");
            Assert.True(standard.impressivenessMatters);
            Assert.Equal(2, standard.space);
        }

        [Theory]
        [InlineData("Tomb")]
        [InlineData("Barn")]
        public void NobodyMindsHowATombOrABarnLooks(string role)
        {
            Assert.False(RoomQuality.StandardFor(role).impressivenessMatters);
            Assert.Null(RoomQuality.Shortfall(role, 1, "rather tight", 0, "awful"));
        }

        [Fact]
        public void EveryRoleTheLayoutCanBuildHasAnOpinionRatherThanTheFallback()
        {
            // A role added to the enum and forgotten here silently gets the unknown-role
            // fallback, which asks only for enclosure — so a new room would never be judged
            // and nothing would say so.
            string[] lived = { "Bedroom", "Prison", "Dining", "Hospital", "Recreation" };
            foreach (var role in lived)
                Assert.True(RoomQuality.StandardFor(role).impressivenessMatters, role);

            string[] worked = { "Kitchen", "Research", "Workshop", "Storage" };
            foreach (var role in worked)
                Assert.Equal(2, RoomQuality.StandardFor(role).space);
        }

        // ------------------------------------------------------------------ gating a repurpose

        [Fact]
        public void ABedroomSizedShellIsTooSmallToBecomeAWorkshop()
        {
            // The case that motivated gating repurposing on this: run 36 turned a 6x6 bedroom
            // into a workshop, and the result finished at 17.9 space — stage 1, "rather tight" —
            // against a workshop profile drawn at 9x7. Walls do not move afterwards.
            Assert.True(1 < RoomQuality.StandardFor("Workshop").space);
        }

        [Fact]
        public void ThatSameShellIsFineForTheRolesDrawnThatSmall()
        {
            // The guard must not refuse every repurpose, only the ones that buy a room too
            // small to do the job. A bedroom shell is still a fine bedroom, prison or freezer.
            Assert.True(1 >= RoomQuality.StandardFor("Bedroom").space);
            Assert.True(1 >= RoomQuality.StandardFor("Prison").space);
            Assert.True(1 >= RoomQuality.StandardFor("Freezer").space);
            Assert.True(1 >= RoomQuality.StandardFor("Power").space);
        }

        [Fact]
        public void AnOrdinarySevenBySevenShellClearsEveryWorkRole()
        {
            // A 7x7 rates 35 space, which is stage 2 — the floor every worked-in role asks for.
            // If this ever fails, repurposing has been gated into uselessness.
            Assert.True(2 >= RoomQuality.StandardFor("Workshop").space);
            Assert.True(2 >= RoomQuality.StandardFor("Kitchen").space);
            Assert.True(2 >= RoomQuality.StandardFor("Research").space);
            Assert.True(2 >= RoomQuality.StandardFor("Storage").space);
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
