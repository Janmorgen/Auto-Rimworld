using AutoColony;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// How much a colony should fear a fight for being few.
    ///
    /// EngagementCaution was 1.0 until somebody was already down, so a colony of three demanded
    /// exactly the margin a colony of twelve demanded. The fights that end colonies begin with
    /// nobody down — twenty-two blood-loss deaths across eight colonies in one session, and every
    /// fatal case was everybody going down together from a standing start.
    /// </summary>
    public class EngagementScarcityTests
    {
        [Fact]
        public void ThreeColonistsFearAFightMoreThanTwelveDo()
        {
            // The whole point. Same threat, same nobody down, and the small colony asks for more
            // because losing one of three costs a third of everything it has.
            float few = CasualtyPolicy.EngagementCaution(3, 0, 1f);
            float many = CasualtyPolicy.EngagementCaution(12, 0, 1f);

            Assert.True(few > many, "three should be more cautious than twelve; " +
                                    few + " vs " + many);
        }

        [Fact]
        public void ItAppliesBeforeAnybodyIsDownAndNotOnlyAfter()
        {
            // The old term arrived after the event it was meant to prevent.
            Assert.Equal(1f, CasualtyPolicy.EngagementCaution(3, 0));
            Assert.True(CasualtyPolicy.EngagementCaution(3, 0, 1f) > 1f);
        }

        [Fact]
        public void ACasualtyStillCountsOnTopOfBeingFew()
        {
            // Both terms, not one replacing the other: a small colony that has already lost
            // somebody is in the worst position of all and should read that way.
            float fewOnly = CasualtyPolicy.EngagementCaution(3, 0, 1f);
            float fewAndHurt = CasualtyPolicy.EngagementCaution(3, 1, 1f);

            Assert.True(fewAndHurt > fewOnly);
        }

        [Fact]
        public void ItFallsAwayAsAColonyGrows()
        {
            // No threshold anywhere — scarcity is one over the hands, so a large colony stops
            // paying for it without anybody choosing a cut-off.
            float three = CasualtyPolicy.EngagementCaution(3, 0, 1f);
            float six = CasualtyPolicy.EngagementCaution(6, 0, 1f);
            float twenty = CasualtyPolicy.EngagementCaution(20, 0, 1f);

            Assert.True(three > six && six > twenty);
            Assert.True(twenty < 1.1f, "twenty hands should barely notice it, got " + twenty);
        }

        [Fact]
        public void AGenomeThatFearsNothingGetsTheOldBehaviourExactly()
        {
            // The gene runs to zero, and at zero this must be the number it was before — or
            // every existing genome changes behaviour for no reason it chose.
            for (int able = 1; able <= 8; able++)
                for (int down = 0; down <= 3; down++)
                    Assert.Equal(CasualtyPolicy.EngagementCaution(able, down),
                                 CasualtyPolicy.EngagementCaution(able, down, 0f), 5);
        }

        [Fact]
        public void ARaidWithNobodyLeftDoesNotDivideByZero()
        {
            Assert.Equal(1f, CasualtyPolicy.EngagementCaution(0, 2, 1f));
        }
    }
}
