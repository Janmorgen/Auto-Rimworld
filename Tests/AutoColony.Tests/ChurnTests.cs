using AutoColony;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The bed argument, stated as arithmetic.
    ///
    /// Every case here is drawn from the three runs in which a bed was built and pulled back out
    /// about twice a day, and from the two fixes that did not hold.
    /// </summary>
    public class ChurnTests
    {
        const int Day = 60000;
        const int TwoDays = 2 * Day;

        public ChurnTests() { Churn.Clear(); }

        /// <summary>
        /// A colony that builds something once is not arguing with itself.
        ///
        /// The planner tops a room up on every pass, so the overwhelmingly common case is the
        /// same direction over and over. If that read as churn the instrument would fire on
        /// every room in the base and mean nothing.
        /// </summary>
        [Fact]
        public void PlacingTheSameThingRepeatedlyIsNotAnArgument()
        {
            for (int i = 0; i < 20; i++)
                Churn.Record("Bed", 7, true, i * 1250, TwoDays);

            Assert.Equal(0, Churn.Reversals("Bed", 7, 20 * 1250, TwoDays));
            Assert.False(Churn.IsSawing("Bed", 7, 1, 20 * 1250, TwoDays));
        }

        /// <summary>
        /// Changing one's mind once is a correction, and the colony is allowed to make them.
        ///
        /// This is the guard against the fix being worse than the fault: a director frozen out
        /// of ever undoing a decision cannot fix its own mistakes either.
        /// </summary>
        [Fact]
        public void OneReversalIsACorrectionAndIsToleratedAtTheDefault()
        {
            Churn.Record("Bed", 7, true, 0, TwoDays);
            Churn.Record("Bed", 7, false, 5000, TwoDays);

            Assert.Equal(1, Churn.Reversals("Bed", 7, 5000, TwoDays));
            Assert.False(Churn.IsSawing("Bed", 7, 2, 5000, TwoDays));
        }

        /// <summary>
        /// The observed fault: in, out, in, out. Run 195 stood at four beds for three colonists,
        /// and the earlier runs showed the same bed changing hands about twice a day.
        /// </summary>
        [Fact]
        public void ABedGoingInAndOutTwiceADayIsSawing()
        {
            Churn.Record("Bed", 7, true, 0, TwoDays);
            Churn.Record("Bed", 7, false, 30000, TwoDays);
            Churn.Record("Bed", 7, true, 60000, TwoDays);
            Churn.Record("Bed", 7, false, 90000, TwoDays);

            Assert.Equal(3, Churn.Reversals("Bed", 7, 90000, TwoDays));
            Assert.True(Churn.IsSawing("Bed", 7, 2, 90000, TwoDays));
        }

        /// <summary>
        /// A rebuild long afterwards is a new decision, not the old argument resumed.
        ///
        /// Without this a colony that removed a bed in its first week could never place one in
        /// that room again, which is a worse fault than the one being fixed.
        /// </summary>
        [Fact]
        public void AQuietSpellEndsTheArgument()
        {
            Churn.Record("Bed", 7, true, 0, TwoDays);
            Churn.Record("Bed", 7, false, 30000, TwoDays);
            Churn.Record("Bed", 7, true, 60000, TwoDays);
            Assert.True(Churn.IsSawing("Bed", 7, 1, 60000, TwoDays));

            // Ten quiet days, then the planner places one again.
            int later = 60000 + 10 * Day;
            Churn.Record("Bed", 7, true, later, TwoDays);

            Assert.Equal(0, Churn.Reversals("Bed", 7, later, TwoDays));
            Assert.False(Churn.IsSawing("Bed", 7, 1, later, TwoDays));
        }

        /// <summary>
        /// Two rooms arguing separately are two arguments. Keying on the thing alone would let a
        /// busy base look like one enormous fight and stand down everywhere.
        /// </summary>
        [Fact]
        public void ArgumentsAreKeptPerPlace()
        {
            Churn.Record("Bed", 7, true, 0, TwoDays);
            Churn.Record("Bed", 7, false, 1000, TwoDays);
            Churn.Record("Bed", 7, true, 2000, TwoDays);

            Assert.True(Churn.IsSawing("Bed", 7, 1, 2000, TwoDays));
            Assert.False(Churn.IsSawing("Bed", 99, 1, 2000, TwoDays));
        }

        /// <summary>
        /// And per thing, so a room whose bed is settled can still have its stove argued over.
        /// </summary>
        [Fact]
        public void ArgumentsAreKeptPerThing()
        {
            Churn.Record("Bed", 7, true, 0, TwoDays);
            Churn.Record("Bed", 7, false, 1000, TwoDays);
            Churn.Record("Bed", 7, true, 2000, TwoDays);

            Assert.True(Churn.IsSawing("Bed", 7, 1, 2000, TwoDays));
            Assert.False(Churn.IsSawing("ElectricStove", 7, 1, 2000, TwoDays));
        }

        /// <summary>
        /// How long it has been going on, which is the number that separates a fight starting
        /// now from one that has run for a week. Same distinction CapabilityGaps draws.
        /// </summary>
        [Fact]
        public void TheAgeOfTheArgumentIsMeasuredFromItsStart()
        {
            Churn.Record("Bed", 7, true, 1000, TwoDays);
            Churn.Record("Bed", 7, false, 31000, TwoDays);
            Churn.Record("Bed", 7, true, 61000, TwoDays);

            Assert.Equal(60000, Churn.StandingFor("Bed", 7, 61000, TwoDays));
        }

        /// <summary>Nothing ever recorded is not an argument, and must not read as one.</summary>
        [Fact]
        public void SomethingNeverTouchedIsSettled()
        {
            Assert.Equal(0, Churn.Reversals("Bed", 7, 5000, TwoDays));
            Assert.Equal(-1, Churn.StandingFor("Bed", 7, 5000, TwoDays));
            Assert.False(Churn.IsSawing("Bed", 7, 1, 5000, TwoDays));
        }

        /// <summary>
        /// The roadmap of live arguments, worst first — so a check can ask what the colony is
        /// currently sawing at without knowing what to look for.
        /// </summary>
        [Fact]
        public void LiveArgumentsAreListedWorstFirst()
        {
            Churn.Record("Bed", 7, true, 0, TwoDays);
            Churn.Record("Bed", 7, false, 1000, TwoDays);

            Churn.Record("Wall", 9, true, 0, TwoDays);
            Churn.Record("Wall", 9, false, 1000, TwoDays);
            Churn.Record("Wall", 9, true, 2000, TwoDays);
            Churn.Record("Wall", 9, false, 3000, TwoDays);

            var all = Churn.All(3000, TwoDays);
            Assert.Equal(2, all.Count);
            Assert.Equal("Wall", all[0].what);
            Assert.Equal(3, all[0].reversals);
            Assert.Equal("Bed", all[1].what);
        }

        /// <summary>A settled thing is left out of the list entirely, not listed at zero.</summary>
        [Fact]
        public void SettledThingsAreNotListed()
        {
            Churn.Record("Bed", 7, true, 0, TwoDays);
            Churn.Record("Bed", 7, true, 1000, TwoDays);

            Assert.Empty(Churn.All(1000, TwoDays));
        }

        /// <summary>
        /// The line the module prints when it stands down has to name the thing, the count and
        /// the duration, or it is the same unreadable message this session has fixed four times.
        /// </summary>
        [Fact]
        public void TheExplanationNamesTheCountAndTheDuration()
        {
            Churn.Record("Bed", 7, true, 0, TwoDays);
            Churn.Record("Bed", 7, false, 30000, TwoDays);
            Churn.Record("Bed", 7, true, 60000, TwoDays);

            string why = Churn.Explain("Bed", 7, 60000, TwoDays);
            Assert.Contains("Bed", why);
            Assert.Contains("2 times", why);
            Assert.Contains("1.0 days", why);
        }

        /// <summary>A memory window of zero means never forget, not forget immediately.</summary>
        [Fact]
        public void AZeroWindowNeverForgets()
        {
            Churn.Record("Bed", 7, true, 0, 0);
            Churn.Record("Bed", 7, false, 100 * Day, 0);

            Assert.Equal(1, Churn.Reversals("Bed", 7, 100 * Day, 0));
        }

        /// <summary>Days to ticks, in one place so the two sides forget on the same schedule.</summary>
        [Fact]
        public void TheMemoryWindowConvertsDaysToTicks()
        {
            Assert.Equal(120000, Churn.MemoryTicks(2f));
            Assert.Equal(30000, Churn.MemoryTicks(0.5f));
            Assert.Equal(0, Churn.MemoryTicks(0f));
            Assert.Equal(0, Churn.MemoryTicks(-1f));
        }
    }
}
