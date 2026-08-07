using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The list of things the colony wants and cannot have.
    ///
    /// Run 170 produced a complete diagnosis of a missing capability on day 0 and had nowhere to
    /// put it. Thirteen days later the colony had no medicine and a colonist bleeding out, and
    /// nothing could tell that the answer it had chosen — buy or find some — had never once
    /// worked.
    /// </summary>
    public class CapabilityGapsTests
    {
        const int Day = 60000;

        [Fact]
        public void AGapThirteenDaysOldIsNotTheSameAsOneFoundThisMinute()
        {
            // The distinction that did not exist. A bool suppressed the repeat message, so a gap
            // that had stood for a fortnight left exactly as much trace as a fresh one: none.
            CapabilityGaps.Clear();
            CapabilityGaps.Report("herbal medicine", "Plants", 8f, 4f, 6 * 2500);

            for (int day = 1; day <= 13; day++)
                CapabilityGaps.Report("herbal medicine", "Plants", 8f, 4f, day * Day);

            float days = CapabilityGaps.StandingFor("herbal medicine", 13 * Day) / (float)Day;
            Assert.True(days > 12.5f, "expected about thirteen days standing, got " + days);
        }

        [Fact]
        public void ReportingItAgainDoesNotRestartTheClock()
        {
            // Modules report every pass. If that reset the age, nothing would ever look old.
            CapabilityGaps.Clear();
            CapabilityGaps.Report("power", "ComponentIndustrial", 6f, 0f, 0);
            CapabilityGaps.Report("power", "ComponentIndustrial", 6f, 2f, 5 * Day);

            Assert.Equal(5 * Day, CapabilityGaps.StandingFor("power", 5 * Day));
        }

        [Fact]
        public void ClosingIsAsMuchAFactAsOpening()
        {
            CapabilityGaps.Clear();
            CapabilityGaps.Report("herbal medicine", "Plants", 8f, 4f, 0);
            Assert.True(CapabilityGaps.IsOpen("herbal medicine"));

            CapabilityGaps.Close("herbal medicine");
            Assert.False(CapabilityGaps.IsOpen("herbal medicine"));
            Assert.Equal(-1, CapabilityGaps.StandingFor("herbal medicine", 9 * Day));
        }

        [Fact]
        public void TheShortfallIsHowFarShortAndNeverNegative()
        {
            CapabilityGaps.Clear();
            CapabilityGaps.Report("herbal medicine", "Plants", 8f, 4f, 0);
            Assert.Equal(4f, CapabilityGaps.Oldest().Shortfall, 3);

            CapabilityGaps.Report("herbal medicine", "Plants", 8f, 9f, Day);
            Assert.Equal(0f, CapabilityGaps.Oldest().Shortfall, 3);
        }

        [Fact]
        public void TheRoadmapIsOldestFirst()
        {
            // Oldest first because the oldest is the one whose chosen answer has had the longest
            // to work and has not.
            CapabilityGaps.Clear();
            CapabilityGaps.Report("power", "ComponentIndustrial", 6f, 0f, 3 * Day);
            CapabilityGaps.Report("herbal medicine", "Plants", 8f, 4f, 1 * Day);
            CapabilityGaps.Report("refrigeration", "research", 1f, 0f, 7 * Day);

            var all = CapabilityGaps.All();
            Assert.Equal("herbal medicine", all[0].capability);
            Assert.Equal("refrigeration", all[2].capability);
            Assert.Equal("herbal medicine", CapabilityGaps.Oldest().capability);
        }

        [Fact]
        public void AColonyWantingNothingItLacksSaysSo()
        {
            CapabilityGaps.Clear();
            Assert.Null(CapabilityGaps.Oldest());
            Assert.Contains("nothing", CapabilityGaps.Explain(0));
        }
    }
}
