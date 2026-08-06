using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Days of food the colony will still have when it comes to eat it.
    ///
    /// The arithmetic only — DirectorContext touches Map and cannot be compiled here — but the
    /// arithmetic is the part that was nearly wrong, so it is the part worth pinning.
    /// </summary>
    public class FoodKeepingTests
    {
        const float Horizon = 3f;   // ColonyState.SpoilingSoonDays

        static float Keeping(float total, float spoiling)
        {
            float lost = spoiling - Horizon;
            if (lost < 0f) lost = 0f;
            float keeping = total - lost;
            return keeping < 0f ? 0f : keeping;
        }

        [Fact]
        public void FoodThatRotsSoonIsStillFoodToday()
        {
            // The mistake nearly shipped: subtracting the whole spoiling figure. Three days of
            // food all of which rots within three days is three days of food — the colony eats
            // it. Treating it as zero would starve a colony that is fine.
            Assert.Equal(3f, Keeping(3f, 3f), 3);
        }

        [Fact]
        public void ASurplusThatCannotBeEatenInTimeIsNotSecurity()
        {
            // Run 168: 15.0 days in store, 7.2 of it spoiling within the horizon. Four and a bit
            // of those days cannot be eaten before they rot, whatever the larder says.
            float k = Keeping(15f, 7.2f);
            Assert.True(k > 10f && k < 11.5f, "expected about 10.8, got " + k);
        }

        [Fact]
        public void NothingSpoilingMeansTheNumberIsUnchanged()
        {
            // A freezer, or pemmican. The whole point of building one is that this stops biting.
            Assert.Equal(20f, Keeping(20f, 0f), 3);
        }

        [Fact]
        public void AnEntirelySpoilingLarderStillFeedsTheColonyForTheHorizon()
        {
            // Twelve days of food all rotting inside three: the colony gets three days out of
            // it, not zero and not twelve.
            Assert.Equal(3f, Keeping(12f, 12f), 3);
        }

        [Fact]
        public void ItNeverGoesNegative()
        {
            Assert.Equal(0f, Keeping(0f, 0f), 3);
            Assert.True(Keeping(1f, 40f) >= 0f);
        }
    }
}
