using AutoColony.Goals;
using AutoColony.Learning;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The arithmetic behind how long a goal deserves to hold the plan.
    ///
    /// GoalPlanner itself cannot be tested here — it touches Map, Pawn and DefDatabase, which
    /// this project bars by construction — so the parts that were wrong were extracted until
    /// they could be reached. These are those parts.
    /// </summary>
    public class GoalPatienceTests
    {
        const int Day = 60000;

        [Fact]
        public void AResearchBlockedGoalIsNotStoodDownBeforeItsResearchCouldLand()
        {
            // The bug this whole change exists for. PreservedFood, Comfort and WoodSupply are
            // gated on research; their urgency cannot move until a project completes, and no
            // project completes in half a day. Under the old flat FocusGraceTicks = 30000 they
            // were demoted every single time they took the focus.
            //
            // 500 points remaining at 260 a day is a shade under two days of waiting.
            float ratePerTick = 260f / Day;
            int eta = GoalPatience.TicksToFinish(500f, ratePerTick);
            int patience = GoalPatience.Patience(eta, 1f, 5000, 6 * Day);

            Assert.True(patience > Day, "a two-day wait must buy more than a day of patience");

            // Half a day in, which is exactly where the old constant gave up.
            var verdict = FocusWatch.Judge(
                focusTicks: Day / 2, patienceTicks: patience,
                urgencyNow: 0.55f, urgencyAtWindowStart: 0.55f,
                workUnderway: false, isImmediate: false);

            Assert.Equal(FocusVerdict.Hold, verdict);
        }

        [Fact]
        public void ResearchNobodyIsDoingBuysNoPatience()
        {
            // A rate of zero does not mean wait for ever. It means the goal is not waiting on
            // the work taking time, it is waiting on the work being possible — a different
            // goal's problem. A colony with no researcher must not sit on Pemmican indefinitely.
            Assert.Equal(GoalPatience.NotDerivable, GoalPatience.TicksToFinish(500f, 0f));
            Assert.Equal(GoalPatience.NotDerivable, GoalPatience.Patience(
                GoalPatience.NotDerivable, 1.5f, 5000, 6 * Day));
        }

        [Fact]
        public void PatienceIsTheLongerOfTwoBlockers()
        {
            // PowerGoal wants both a Power room and Electricity. Finishing the room early buys
            // nothing while the research is outstanding.
            Assert.Equal(3 * Day, GoalPatience.Longer(3 * Day, 1 * Day));
            Assert.Equal(3 * Day, GoalPatience.Longer(1 * Day, 3 * Day));
        }

        [Fact]
        public void AnUndefinedBlockerDoesNotSwallowADefinedOne()
        {
            // A goal wanting a room it has not sited yet still knows how long its research
            // will take. Longer() must not treat "no estimate" as "no wait".
            Assert.Equal(2 * Day, GoalPatience.Longer(GoalPatience.NotDerivable, 2 * Day));
            Assert.Equal(2 * Day, GoalPatience.Longer(2 * Day, GoalPatience.NotDerivable));
            Assert.Equal(GoalPatience.NotDerivable,
                         GoalPatience.Longer(GoalPatience.NotDerivable, GoalPatience.NotDerivable));
        }

        [Fact]
        public void PatienceIsBoundedBelowByThePlannerSOwnCadence()
        {
            // Shorter than a few planner passes measures quantisation, not the goal.
            Assert.Equal(5000, GoalPatience.Patience(10, 1f, 5000, 6 * Day));
            Assert.Equal(6 * Day, GoalPatience.Patience(99 * Day, 1f, 5000, 6 * Day));
        }

        [Fact]
        public void DemotionLengthScalesWithTheWaitItInterrupted()
        {
            // A goal that held the plan six days and moved nothing should not be back in an
            // hour; one that held it four hours should not be gone for a week.
            Assert.Equal(3 * Day, GoalPatience.DemotionAfter(3 * Day, 1f));
            Assert.Equal(Day, GoalPatience.DemotionAfter(4 * Day, 0.25f));
            Assert.True(GoalPatience.DemotionAfter(6 * Day, 1f) >
                        GoalPatience.DemotionAfter(Day / 4, 1f));
        }

        [Fact]
        public void AGoalThatImprovesResetsItsWindow()
        {
            // Preserves the original rule: improving at all is enough. The question is whether
            // the work is doing anything, not whether it is doing it quickly.
            var verdict = FocusWatch.Judge(
                focusTicks: 5 * Day, patienceTicks: Day,
                urgencyNow: 0.40f, urgencyAtWindowStart: 0.55f,
                workUnderway: false, isImmediate: false);

            Assert.Equal(FocusVerdict.ResetWindow, verdict);
        }

        [Fact]
        public void BuildingGoingUpIsNotAGoalGoingNowhere()
        {
            var verdict = FocusWatch.Judge(
                focusTicks: 5 * Day, patienceTicks: Day,
                urgencyNow: 0.67f, urgencyAtWindowStart: 0.67f,
                workUnderway: true, isImmediate: false);

            Assert.Equal(FocusVerdict.ResetWindow, verdict);
        }

        [Fact]
        public void AFireIsNeverStoodDownForStillBurning()
        {
            // Immediate goals report urgency 1 whatever happens, so they read as "not
            // improving" every time.
            var verdict = FocusWatch.Judge(
                focusTicks: 9 * Day, patienceTicks: Day,
                urgencyNow: 1f, urgencyAtWindowStart: 1f,
                workUnderway: false, isImmediate: true);

            Assert.Equal(FocusVerdict.Hold, verdict);
        }

        [Fact]
        public void APatienceThatCouldNotBeEstimatedNeverDemotes()
        {
            // The caller substitutes what it has learned before getting here; if even that is
            // absent there is nothing to judge against and holding is the honest answer.
            var verdict = FocusWatch.Judge(
                focusTicks: 9 * Day, patienceTicks: GoalPatience.NotDerivable,
                urgencyNow: 0.5f, urgencyAtWindowStart: 0.5f,
                workUnderway: false, isImmediate: false);

            Assert.Equal(FocusVerdict.Hold, verdict);
        }

        [Fact]
        public void TheFirstReadingAfterAReloadIsABaselineNotARate()
        {
            // The planner keeps no state across a save reload, so the meter starts empty every
            // load. Without this rule the first sample differences against a zero baseline and
            // reports an enormous rate — the colony would conclude it could research anything
            // in an afternoon.
            var pace = new ColonyPace();

            Assert.Equal(0f, pace.Rate("research", 4000f, 100000));   // baseline only
            Assert.True(pace.HasReading("research"));

            float rate = pace.Rate("research", 4260f, 100000 + Day);
            Assert.True(rate > 0f);
            Assert.Equal(260f / Day, rate, 5);
        }

        [Fact]
        public void AClockThatWentBackwardsIsANewColonyNotARate()
        {
            var pace = new ColonyPace();
            pace.Rate("research", 9000f, 500000);

            // A reload into an earlier save. Re-baseline rather than report a negative rate.
            Assert.Equal(0f, pace.Rate("research", 100f, 1000));
            Assert.Equal(0f, pace.Rate("research", 100f, 1000));
        }

        [Fact]
        public void APileThatGrewIsNotARateAtWhichItWillClear()
        {
            // Queuing more construction than gets finished is real and common, and it is not
            // progress towards an empty pile.
            var pace = new ColonyPace();
            pace.Drain("construction:Bedroom", 400f, 100000);

            Assert.Equal(0f, pace.Drain("construction:Bedroom", 900f, 100000 + Day));

            float rate = pace.Drain("construction:Bedroom", 700f, 100000 + 2 * Day);
            Assert.True(rate > 0f);
        }

        [Fact]
        public void LearnedPatienceFallsBackToTheGeneUntilTheKindHasBeenMet()
        {
            // Mirrors ThreatMemory.ForceFor: a fresh colony starts from an evolved prior rather
            // than a guess, and the two disagree only where experience has earned it.
            PatienceMemory.Clear();
            Assert.Equal(1.5f, PatienceMemory.RatioFor(BlockerKind.Research, 1.5f), 3);

            PatienceMemory.RecordOutcome(BlockerKind.Research, 2 * Day, 4 * Day);
            Assert.NotEqual(1.5f, PatienceMemory.RatioFor(BlockerKind.Research, 1.5f));
        }

        [Fact]
        public void AWaitThatTookLongerThanTheArithmeticRaisesTheRatio()
        {
            PatienceMemory.Clear();
            float before = PatienceMemory.For(BlockerKind.Construction).ratio;

            // Builders kept getting drafted: twice the predicted wait.
            PatienceMemory.RecordOutcome(BlockerKind.Construction, 2 * Day, 4 * Day);

            Assert.True(PatienceMemory.For(BlockerKind.Construction).ratio > before);
        }

        [Fact]
        public void LearnedPatienceIsBounded()
        {
            // The floor stops a colony that once got lucky concluding research is instant; the
            // ceiling stops a run of interruptions convincing it nothing ever finishes.
            PatienceMemory.Clear();
            for (int i = 0; i < 60; i++)
                PatienceMemory.RecordOutcome(BlockerKind.Research, Day, 40 * Day);
            Assert.True(PatienceMemory.For(BlockerKind.Research).ratio <= PatienceMemory.MaxRatio);

            PatienceMemory.Clear();
            for (int i = 0; i < 60; i++)
                PatienceMemory.RecordOutcome(BlockerKind.Research, 40 * Day, Day);
            Assert.True(PatienceMemory.For(BlockerKind.Research).ratio >= PatienceMemory.MinRatio);
        }

        [Fact]
        public void AnUnfinishedWaitTeachesNothing()
        {
            // Only a spell that ended in the goal's own terms is evidence about how long the
            // work takes. One interrupted by something more urgent is not.
            PatienceMemory.Clear();
            PatienceMemory.RecordOutcome(BlockerKind.Research, 0, 5 * Day);
            PatienceMemory.RecordOutcome(BlockerKind.Research, 5 * Day, 0);

            Assert.Equal(0, PatienceMemory.For(BlockerKind.Research).spells);
        }
    }

    /// <summary>
    /// How much food the colony wants, once the calendar is allowed a say.
    /// </summary>
    public class FoodTargetTests
    {
        [Fact]
        public void APermanentSummerColonyKeepsTheGenomeSNumber()
        {
            // No gap is coming, so there is nothing to hoard against. Zero barren days is a
            // real answer and must not be read as "unknown".
            Assert.Equal(5f, FoodTarget.Days(5f, 60, 0, 1.3f), 3);
        }

        [Fact]
        public void AColonyFarFromWinterIsNotMadeToHoard()
        {
            // Forty days of growing left against a fifteen-day winter: still time to sow, so
            // demanding a winter's food now would be the flat target's mistake in reverse.
            Assert.Equal(5f, FoodTarget.Days(5f, 40, 15, 1.3f), 3);
        }

        [Fact]
        public void AColonyOnTheEdgeOfWinterWantsEnoughToCrossIt()
        {
            // Run 159: four days of growing left, a real winter ahead, and it bought four days
            // of food because that is what the flat gene asked for.
            float days = FoodTarget.Days(5f, 4, 20, 1.3f);
            Assert.True(days > 20f, "must want more than the gap itself, got " + days);
        }

        [Fact]
        public void TheGeneStillWinsWhenItAsksForMore()
        {
            // A cautious genome is not overruled by a short winter.
            Assert.Equal(30f, FoodTarget.Days(30f, 2, 10, 1.3f), 3);
        }

        [Fact]
        public void AnIceSheetWantsAYearAndDoesNotDivideByZero()
        {
            float days = FoodTarget.Days(5f, 0, 60, 1.3f);
            Assert.True(days >= 60f);
        }
    }
}
