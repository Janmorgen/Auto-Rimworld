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
            e.colonists = 3;
            e.downed = 0;
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
            e.downed = e.colonists;
            e.downedFraction = 0.7f;
            Assert.Equal("incapacity", Postmortem.Cause(e));
        }

        [Fact]
        public void AnEarlyEmptyLarderInTheHistoryIsNotStarvation()
        {
            // Watched in game: a colony wiped out by a raid on day 3 was called starvation
            // because minDaysOfFood was 0.0. Every colony reads 0.0 on day one whatever it has,
            // since ResourceCounter only sees stockpiled goods and nothing is hauled yet.
            var e = Healthy();
            e.colonists = 1;
            e.downed = 1;
            e.daysOfFood = 11.5f;
            e.minDaysOfFood = 0f;
            e.deaths = 3;
            e.downedFraction = 0.09f;
            Assert.Equal("incapacity", Postmortem.Cause(e));
            Assert.DoesNotContain("starvation", Postmortem.Describe(e));
        }

        [Fact]
        public void TheColonyIsJudgedAsItWasWhileAliveNotAfterwards()
        {
            // After a wipe the larder climbs because nobody is left to eat from it, so the
            // evidence has to be the last living snapshot rather than the empty map.
            var e = Healthy();
            e.colonists = 2;
            e.downed = 2;
            e.daysOfFood = 11.5f;
            string line = Postmortem.Describe(e);
            Assert.Contains("last alive with 2 colonists (2 down)", line);
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
            Assert.False(CasualtyPolicy.ShouldReserveMedic(4, 0, 0));
        }

        [Fact]
        public void OneIsHeldBackOnceSomeoneIsDown()
        {
            Assert.True(CasualtyPolicy.ShouldReserveMedic(2, 1, 0));
            Assert.True(CasualtyPolicy.ShouldReserveMedic(5, 1, 0));
        }

        [Fact]
        public void TheLastColonistStandingStillHasToAnswerTheRaid()
        {
            Assert.False(CasualtyPolicy.ShouldReserveMedic(1, 2, 0));
            Assert.False(CasualtyPolicy.ShouldReserveMedic(0, 1, 0));
        }

        [Fact]
        public void TheLastColonistStandingTendsInsteadWhenSomeoneIsBleedingOut()
        {
            // Runs 132, 134 and 135 all ended here: three on the floor, one upright, and the
            // rule keeping that one in a line the colony had already decided to withdraw from.
            // Run 135 spent four hours of it standing in a refuge with twenty-five medicine in
            // store, and lost all four.
            Assert.True(CasualtyPolicy.ShouldReserveMedic(1, 3, 3));
            Assert.True(CasualtyPolicy.ShouldReserveMedic(1, 1, 1));

            // Down but not bleeding can wait for the fight to end — that is the original rule,
            // and it is still right. Only the clock overrides it.
            Assert.False(CasualtyPolicy.ShouldReserveMedic(1, 2, 0));
        }

        [Fact]
        public void AHealthyColonyMeetsAThreatOnItsOwnTerms()
        {
            Assert.Equal(1f, CasualtyPolicy.EngagementCaution(4, 0), 3);
        }

        [Fact]
        public void TheLastOneStandingHoldsCoverOnOddsFourWouldHaveTaken()
        {
            // The fight that lost the colony: one able colonist, three already down, a 1.23x
            // advantage and a default engage ratio around 0.35. Four times the bar refuses it.
            float caution = CasualtyPolicy.EngagementCaution(1, 3);
            Assert.Equal(4f, caution, 3);
            Assert.True(1.23f < 0.35f * caution);

            // The same odds at full strength are still worth meeting in the open.
            Assert.True(1.23f > 0.35f * CasualtyPolicy.EngagementCaution(4, 0));
        }

        [Fact]
        public void ARoomToHoldMakesTheOpenElective()
        {
            // The fight that lost run 7: a 0.68x advantage cleared a bare gene bar of 0.35 and
            // downed three of four colonists. With a room to withdraw into it is refused.
            float required = CasualtyPolicy.RequiredAdvantage(0.35f, 4, 0, true, true);
            Assert.True(0.68f < required);
            Assert.Equal(CasualtyPolicy.MinimumToLeaveCover, required, 3);
        }

        [Fact]
        public void WithNowhereToWithdrawToTheGeneStands()
        {
            // A colony with no walls yet is not choosing between two options.
            // With beds to recover into, the gene stands unaltered.
            Assert.Equal(0.35f, CasualtyPolicy.RequiredAdvantage(0.35f, 4, 0, false, true), 3);
            Assert.True(0.68f > CasualtyPolicy.RequiredAdvantage(0.35f, 4, 0, false, true));
        }

        [Fact]
        public void AStrategyBolderThanTheCoverFloorIsNotHeldBackByIt()
        {
            // The floor is a minimum, not an override — a gene demanding more still demands it.
            Assert.Equal(3f, CasualtyPolicy.RequiredAdvantage(3f, 4, 0, true, true), 3);
        }

        [Fact]
        public void CasualtiesStillRaiseTheBarAboveTheCoverFloor()
        {
            Assert.True(CasualtyPolicy.RequiredAdvantage(0.35f, 1, 3, true, true) >
                        CasualtyPolicy.MinimumToLeaveCover);
        }

        [Fact]
        public void ADayZeroColonyWithNoBedsRefusesTheFightsThatKilledColonies()
        {
            // Across the observed runs, every early fight taken at these ratios preceded severe
            // colonist loss; those at 1.59x and above did not.
            float bar = CasualtyPolicy.RequiredAdvantage(0.35f, 3, 0, false, false);
            Assert.True(0.68f < bar);
            Assert.True(0.96f < bar);
            Assert.True(1.09f < bar);

            Assert.True(1.59f > bar);
            Assert.True(1.94f > bar);
            Assert.True(3.02f > bar);
        }

        [Fact]
        public void ARefugeStillOutranksTheNoBedFloor()
        {
            // Both apply to a colony with walls but no beds; the cover floor is the higher bar.
            Assert.Equal(CasualtyPolicy.MinimumToLeaveCover,
                         CasualtyPolicy.RequiredAdvantage(0.35f, 3, 0, true, false), 3);
        }

        [Fact]
        public void TheBarRisesWithHowMuchOfTheColonyIsAlreadyDown()
        {
            Assert.True(CasualtyPolicy.EngagementCaution(2, 1) < CasualtyPolicy.EngagementCaution(1, 1));
            Assert.True(CasualtyPolicy.EngagementCaution(2, 1) < CasualtyPolicy.EngagementCaution(2, 3));
        }
    }
}
