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
