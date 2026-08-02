using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The colonies lost overnight nearly all died in the window between "the larder is nearly
    /// empty" and "the hunt lands", so what matters here is where escalation now happens rather
    /// than the arithmetic itself.
    /// </summary>
    public class FoodTimingTests
    {
        /// <summary>Desperation as the resource module computes it, so the threshold can be located.</summary>
        static float Desperation(float daysOfFood, float target, float aggression)
        {
            return AcMath.Clamp01(FoodTiming.Urgency(daysOfFood, target) * 0.8f + aggression * 0.2f);
        }

        const float LastResortAt = 0.85f;

        [Fact]
        public void ComfortableColonyIsNotUrgent()
        {
            Assert.Equal(0f, FoodTiming.Urgency(20f, 8f), 3);
        }

        [Fact]
        public void UrgencyIsFullBeforeTheLarderIsActuallyEmpty()
        {
            // At the lead time itself there is nothing left to eat by the time food could land.
            Assert.Equal(1f, FoodTiming.Urgency(FoodTiming.SupplyLeadDays, 8f), 3);
            Assert.Equal(1f, FoodTiming.Urgency(0f, 8f), 3);
        }

        [Fact]
        public void LastResortNowFiresWithMarginRatherThanAtZero()
        {
            // The behaviour this replaced: measured on the food in store, a colony on the
            // default eight-day target only reached last-resort hunting at an empty larder.
            float oldDesperationAtOneDay =
                AcMath.Clamp01(AcMath.Clamp01(1f - 1f / 8f) * 0.8f + 0.5f * 0.2f);
            Assert.True(oldDesperationAtOneDay < LastResortAt);

            // It now fires while a day and a half of food is still in hand — enough for the
            // hunt to fail once and be tried again.
            Assert.True(Desperation(1.5f, 8f, 0.5f) >= LastResortAt);
            Assert.True(Desperation(1f, 8f, 0.5f) >= LastResortAt);
        }

        [Fact]
        public void AWellStockedColonyStillDoesNotPanic()
        {
            // The margin must not turn into permanent desperation: four days of food on an
            // eight-day target is a colony that is fine.
            Assert.True(Desperation(4f, 8f, 0.5f) < LastResortAt);
            Assert.True(Desperation(8f, 8f, 0.5f) < LastResortAt);
        }

        [Fact]
        public void ATargetOfZeroIsTreatedAsAlwaysUrgentRatherThanDividingByIt()
        {
            Assert.Equal(1f, FoodTiming.Urgency(5f, 0f), 3);
        }
    }
}

namespace AutoColony.Tests
{
    /// <summary>
    /// The Food security term scored exactly 0.00 in every epoch of every run, because the
    /// worst-food statistic it uses is 0.0 for any colony that lived through its first day.
    /// </summary>
    public class WorstFoodTests
    {
        static ColonyMetrics At(float daysOfFood)
        {
            var m = ColonyMetrics.Neutral();
            m.daysOfFood = daysOfFood;
            return m;
        }

        static EpochAccumulator Fresh()
        {
            var acc = new EpochAccumulator();
            acc.ResetFor(ColonyMetrics.Neutral());
            return acc;
        }

        [Fact]
        public void TheOpeningHoursWithNothingStockpiledDoNotCountAsStarving()
        {
            // Day one: everything is still on the ground and ResourceCounter reports nothing.
            var acc = Fresh();
            acc.Observe(At(0f));
            acc.Observe(At(0f));
            acc.Observe(At(6f));
            acc.Observe(At(9f));

            Assert.Equal(6f, acc.WorstFood, 3);
        }

        [Fact]
        public void AnEmptyLarderAfterStockingUpIsStillTheRealLow()
        {
            var acc = Fresh();
            acc.Observe(At(8f));
            acc.Observe(At(0f));
            acc.Observe(At(4f));

            Assert.Equal(0f, acc.WorstFood, 3);
        }

        [Fact]
        public void AColonyThatNeverStockpiledAnythingScoresZeroNotPerfect()
        {
            // The sentinel is 999, which divided into a target would read as flawless security.
            var acc = Fresh();
            acc.Observe(At(0f));
            acc.Observe(At(0f));

            Assert.Equal(0f, acc.WorstFood, 3);
        }

        [Fact]
        public void AWellStockedEpochNoLongerScoresTheSameAsAStarvingOne()
        {
            var stocked = Fresh();
            var starving = Fresh();
            for (int i = 0; i < 10; i++)
            {
                stocked.Observe(At(i == 0 ? 0f : 20f));
                starving.Observe(At(i == 0 ? 0f : 0.2f));
            }

            Assert.True(stocked.WorstFood > starving.WorstFood);
        }

        [Fact]
        public void ResettingAnEpochForgetsThatFoodWasEverSeen()
        {
            var acc = Fresh();
            acc.Observe(At(5f));
            acc.ResetFor(ColonyMetrics.Neutral());
            acc.Observe(At(0f));

            Assert.Equal(0f, acc.WorstFood, 3);
        }
    }
}

namespace AutoColony.Tests
{
    /// <summary>
    /// The director deliberately leaves a wildfire that will never reach the base — a designed,
    /// measured behaviour. Scoring on every fire on the map punished it for doing so.
    /// </summary>
    public class DistantFireScoringTests
    {
        static ColonyMetrics With(int fires, int near)
        {
            var m = ColonyMetrics.Neutral();
            m.fires = fires;
            m.firesNearBase = near;
            return m;
        }

        static EpochAccumulator Fresh()
        {
            var acc = new EpochAccumulator();
            acc.ResetFor(ColonyMetrics.Neutral());
            return acc;
        }

        [Fact]
        public void AWildfireAcrossTheMapIsNotTheColonyBurning()
        {
            // Observed: one fire 93 cells away, correctly ignored, reported as "fire burning for
            // 57% of the epoch" and charged against the infrastructure score for all of it.
            var acc = Fresh();
            for (int i = 0; i < 10; i++) acc.Observe(With(fires: 1, near: 0));

            Assert.Equal(0f, acc.FireFraction, 3);
        }

        [Fact]
        public void AFireAtTheColonyStillCounts()
        {
            var acc = Fresh();
            for (int i = 0; i < 10; i++) acc.Observe(With(fires: 1, near: 1));

            Assert.Equal(1f, acc.FireFraction, 3);
        }

        [Fact]
        public void OnlyTheSamplesWithFireAtHomeCount()
        {
            var acc = Fresh();
            for (int i = 0; i < 5; i++) acc.Observe(With(fires: 3, near: 1));
            for (int i = 0; i < 5; i++) acc.Observe(With(fires: 3, near: 0));

            Assert.Equal(0.5f, acc.FireFraction, 3);
        }
    }
}
