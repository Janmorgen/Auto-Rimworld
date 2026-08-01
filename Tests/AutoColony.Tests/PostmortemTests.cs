using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The point of a post-mortem line is that a reader who never saw the run can act on it, so
    /// these check the cases that were actually mis-diagnosed by hand over the overnight run —
    /// above all the colony that starved with food in the stockpile.
    /// </summary>
    public class PostmortemTests
    {
        static LossEvidence Healthy()
        {
            var e = new LossEvidence();
            e.day = 30;
            e.samples = 400;
            e.daysOfFood = 5f;
            e.minDaysOfFood = 4f;
            e.avgMood = 0.7f;
            e.avgHealth = 0.95f;
            return e;
        }

        [Fact]
        public void AnEmptyLarderIsStarvation()
        {
            var e = Healthy();
            e.daysOfFood = 0f;
            e.minDaysOfFood = 0f;
            Assert.Equal("starvation", Postmortem.Cause(e));
        }

        [Fact]
        public void FoodInStoreWithEveryoneDownIsNotStarvation()
        {
            // The case that killed a colony with 3.2 days of food in the stockpile: nobody was
            // left standing to carry it. Calling that starvation points at the wrong remedy.
            var e = Healthy();
            e.daysOfFood = 3.2f;
            e.minDaysOfFood = 3f;
            e.downedFraction = 0.7f;
            Assert.Equal("incapacity", Postmortem.Cause(e));
        }

        [Fact]
        public void ARaidWithDeathsIsARaid()
        {
            var e = Healthy();
            e.raids = 2;
            e.deaths = 3;
            Assert.Equal("raid", Postmortem.Cause(e));
        }

        [Fact]
        public void StarvationOutranksARaidWhenTheLarderWasAlsoEmpty()
        {
            var e = Healthy();
            e.raids = 1;
            e.deaths = 1;
            e.daysOfFood = 0f;
            e.minDaysOfFood = 0f;
            Assert.Equal("starvation", Postmortem.Cause(e));
        }

        [Fact]
        public void ProlongedFireIsFire()
        {
            var e = Healthy();
            e.fireFraction = 0.5f;
            Assert.Equal("fire", Postmortem.Cause(e));
        }

        [Fact]
        public void CollapsedMoodIsNamedEvenWithNothingElseWrong()
        {
            var e = Healthy();
            e.avgMood = 0.2f;
            Assert.Equal("mood collapse", Postmortem.Cause(e));
        }

        [Fact]
        public void NothingImplicatedIsSaidToBeUnexplainedRatherThanGuessedAt()
        {
            Assert.Equal("unexplained", Postmortem.Cause(Healthy()));
        }

        [Fact]
        public void AnUnobservedEpochDoesNotReadAsAnEmptyLarder()
        {
            // minDaysOfFood is 999 before anything is sampled, and a zeroed one would otherwise
            // look like a colony that ran dry.
            var e = new LossEvidence();
            e.samples = 0;
            e.daysOfFood = 6f;
            Assert.Equal("unexplained", Postmortem.Cause(e));
        }

        [Fact]
        public void TheLineSaysHowMuchToTrustItselfWhenBarelyObserved()
        {
            var e = Healthy();
            e.samples = 2;
            Assert.Contains("only 2 observations", Postmortem.Describe(e));
        }

        [Fact]
        public void TheLineCarriesTheChainAndNotOnlyTheVerdict()
        {
            var e = Healthy();
            e.daysOfFood = 0f;
            e.minDaysOfFood = 0f;
            e.raids = 1;
            e.deaths = 2;
            e.downedFraction = 0.4f;
            e.worstComplaint = "ApparelDamaged";
            e.worstComplaintMood = 6.5f;

            string line = Postmortem.Describe(e);
            Assert.Contains("COLONY LOST on day 30", line);
            Assert.Contains("starvation", line);
            Assert.Contains("1 raid", line);
            Assert.Contains("2 deaths", line);
            Assert.Contains("40%", line);
            Assert.Contains("ApparelDamaged", line);
        }
    }

    public class CasualtyPolicyTests
    {
        [Fact]
        public void NobodyIsHeldBackWhileNobodyIsDown()
        {
            Assert.False(CasualtyPolicy.ShouldReserveMedic(4, 0));
        }

        [Fact]
        public void OneIsHeldBackOnceSomeoneIsDown()
        {
            Assert.True(CasualtyPolicy.ShouldReserveMedic(2, 1));
            Assert.True(CasualtyPolicy.ShouldReserveMedic(5, 1));
        }

        [Fact]
        public void TheLastColonistStandingStillHasToAnswerTheRaid()
        {
            Assert.False(CasualtyPolicy.ShouldReserveMedic(1, 2));
            Assert.False(CasualtyPolicy.ShouldReserveMedic(0, 1));
        }
    }
}
