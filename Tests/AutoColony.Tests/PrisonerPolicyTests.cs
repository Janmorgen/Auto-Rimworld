using AutoColony.Prisoners;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// What to do with a downed raider.
    ///
    /// Tested offline because the failure modes are judgements rather than crashes: a colony
    /// that collects prisoners it cannot feed starves quietly, and one that lets a skilled
    /// surgeon walk out of the gate never finds out what it missed. Neither shows up as an
    /// exception and both take in-game weeks to notice.
    /// </summary>
    public class PrisonerPolicyTests
    {
        const float WellFed = 20f;
        const float Starving = 1.5f;
        const float Keen = 0.9f;
        const float Indifferent = 0.1f;

        static Disposition Decide(float value, float resistance, float food, float bias)
        {
            return PrisonerPolicy.Decide(value, resistance, food, bias,
                                         canRecruit: true, executionAllowed: false);
        }

        // ------------------------------------------------------------ worth keeping

        [Fact]
        public void ASkilledPrisonerIsRecruitedWhenTheColonyIsKeen()
        {
            Assert.Equal(Disposition.Recruit, Decide(0.8f, 0f, WellFed, Keen));
        }

        [Fact]
        public void HighResistanceIsWornDownBeforeRecruitingIsAttempted()
        {
            // Attempting recruitment through heavy resistance just wastes the warden's time.
            Assert.Equal(Disposition.Wear, Decide(0.8f, 20f, WellFed, Keen));
        }

        [Fact]
        public void ResistanceBelowTheThresholdGoesStraightToRecruiting()
        {
            Assert.Equal(Disposition.Recruit,
                         Decide(0.8f, PrisonerPolicy.HighResistance - 0.1f, WellFed, Keen));
        }

        // ------------------------------------------------------------ not worth keeping

        [Fact]
        public void AUselessPrisonerIsReleasedRatherThanHeldForever()
        {
            // Holding someone nobody is working on is a prison break waiting to happen.
            Assert.Equal(Disposition.Release, Decide(0.02f, 0f, WellFed, Indifferent));
        }

        [Fact]
        public void AStarvingColonyDoesNotFeedAPrisonerItDoesNotWant()
        {
            Assert.Equal(Disposition.Release, Decide(0.1f, 0f, Starving, Indifferent));
        }

        [Fact]
        public void AStarvingColonyStillKeepsSomeoneGenuinelyValuable()
        {
            var decision = Decide(0.9f, 0f, Starving, Keen);
            Assert.True(decision == Disposition.Recruit || decision == Disposition.Wear);
        }

        [Fact]
        public void ExecutionIsNeverChosenUnlessExplicitlyAllowed()
        {
            // Releasing costs nothing and nobody watches it happen; executing carries a lasting
            // mood penalty for every colonist who sees it.
            for (float value = 0f; value <= 1f; value += 0.1f)
                for (float food = 0f; food < 30f; food += 5f)
                    Assert.NotEqual(Disposition.Execute, Decide(value, 0f, food, 0.5f));
        }

        [Fact]
        public void ExecutionOnlyAppliesToTheWorthlessAndOnlyWhenPermitted()
        {
            Assert.Equal(Disposition.Execute,
                PrisonerPolicy.Decide(0f, 0f, Starving, Indifferent, true, executionAllowed: true));

            // Still not someone with something to offer.
            Assert.NotEqual(Disposition.Execute,
                PrisonerPolicy.Decide(0.9f, 0f, Starving, Keen, true, executionAllowed: true));
        }

        [Fact]
        public void WithoutTheAbilityToRecruitAPrisonerIsSimplyHeld()
        {
            Assert.Equal(Disposition.Hold,
                PrisonerPolicy.Decide(0.9f, 0f, WellFed, Keen, canRecruit: false,
                                      executionAllowed: false));
        }

        [Fact]
        public void AppetiteForRecruitsChangesTheAnswerForAMarginalPrisoner()
        {
            Assert.NotEqual(Decide(0.3f, 0f, WellFed, Keen), Decide(0.3f, 0f, WellFed, Indifferent));
        }

        // ------------------------------------------------------------ whether to capture at all

        [Fact]
        public void NobodyIsCapturedWithoutAPrisonBed()
        {
            // The deadlock this whole feature had to break: no bed, no capture.
            Assert.False(PrisonerPolicy.WorthCapturing(0.9f, WellFed, Keen,
                                                       bedAvailable: false, safe: true));
        }

        [Fact]
        public void NobodyIsCapturedWhileTheFightIsStillOn()
        {
            Assert.False(PrisonerPolicy.WorthCapturing(0.9f, WellFed, Keen,
                                                       bedAvailable: true, safe: false));
        }

        [Fact]
        public void NobodyIsCapturedByAColonyWithNoFood()
        {
            Assert.False(PrisonerPolicy.WorthCapturing(0.9f, 0.5f, Keen, true, true));
        }

        [Fact]
        public void AGoodProspectIsCapturedByAColonyThatCanAffordIt()
        {
            Assert.True(PrisonerPolicy.WorthCapturing(0.8f, WellFed, Keen, true, true));
        }

        [Fact]
        public void AColonyWithNoAppetiteDoesNotCollectPeople()
        {
            Assert.False(PrisonerPolicy.WorthCapturing(0.2f, WellFed, 0f, true, true));
        }

        // ------------------------------------------------------------ appraisal

        [Fact]
        public void SomeoneIncapableOfEverythingIsWorthNothing()
        {
            Assert.Equal(0f, PrisonerPolicy.Value(20, 20f, 1f, false, incapableOfEverything: true));
        }

        [Fact]
        public void SkillRaisesWorthAndInjuryLowersIt()
        {
            float healthy = PrisonerPolicy.Value(15, 8f, 1f, false, false);
            float hurt = PrisonerPolicy.Value(15, 8f, 0.3f, false, false);
            float unskilled = PrisonerPolicy.Value(2, 1f, 1f, false, false);

            Assert.True(healthy > hurt);
            Assert.True(healthy > unskilled);
        }

        [Fact]
        public void APacifistIsWorthLessButNotNothing()
        {
            float fighter = PrisonerPolicy.Value(15, 8f, 1f, false, false);
            float pacifist = PrisonerPolicy.Value(15, 8f, 1f, true, false);

            Assert.True(pacifist < fighter);
            Assert.True(pacifist > 0f);   // still a cook, a doctor and a grower
        }

        [Fact]
        public void WorthIsAlwaysBounded()
        {
            Assert.InRange(PrisonerPolicy.Value(99, 99f, 5f, false, false), 0f, 1f);
            Assert.InRange(PrisonerPolicy.Value(0, 0f, 0f, true, false), 0f, 1f);
        }
    }
}
