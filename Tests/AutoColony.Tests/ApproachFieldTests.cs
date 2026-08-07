using AutoColony.Defence;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The three maps a colony can land on, as arithmetic: a corridor, a plain, and a mountain
    /// with no way in at all. Each wants a different answer and the old code had no way to ask.
    /// </summary>
    public class ApproachFieldTests
    {
        const float Threshold = 0.5f;

        public ApproachFieldTests() { ApproachField.Clear(); }

        /// <summary>Never divide by a sample nobody took.</summary>
        [Fact]
        public void NothingSampledIsNotAConcentration()
        {
            Assert.Equal(0f, ApproachField.Concentration(0, 0));
            Assert.Equal("nothing sampled", ApproachField.Verdict(0, 0, 0f, Threshold));
            Assert.Contains("nothing sampled", ApproachField.Explain(Threshold));
        }

        /// <summary>
        /// A mountain base. Not "concentration zero" — a distinct answer, and the one case where
        /// the walling is already done by geology and the colony should spend its wood elsewhere.
        /// </summary>
        [Fact]
        public void NoRouteFromAnyEdgeIsItsOwnAnswer()
        {
            Assert.Equal("nothing walks in; the mountain is the wall",
                         ApproachField.Verdict(48, 0, 0f, Threshold));
            Assert.Equal(0f, ApproachField.Concentration(0, 0));
        }

        /// <summary>A corridor: most approaches through one cell, and worth holding.</summary>
        [Fact]
        public void OneCellCarryingMostApproachesIsAChokepoint()
        {
            float c = ApproachField.Concentration(37, 44);
            Assert.True(c > 0.83f && c < 0.85f);
            Assert.True(ApproachField.IsChokepoint(c, Threshold));
            Assert.Equal("a natural chokepoint", ApproachField.Verdict(48, 44, c, Threshold));
        }

        /// <summary>
        /// Open plain: the busiest cell carries a small share, so funnelling would be real work
        /// and the colony should know that before it starts.
        /// </summary>
        [Fact]
        public void TrafficSpreadThinIsOpenGround()
        {
            float c = ApproachField.Concentration(4, 48);
            Assert.False(ApproachField.IsChokepoint(c, Threshold));
            Assert.Equal("open ground on every side, no chokepoint to hold",
                         ApproachField.Verdict(48, 48, c, Threshold));
        }

        /// <summary>
        /// Denominated in routes, not samples. A sample with no route is a direction nothing can
        /// come from, and counting it would dilute a map that genuinely is one corridor.
        /// </summary>
        [Fact]
        public void SamplesWithNoRouteDoNotDiluteTheConcentration()
        {
            // 20 of 20 real approaches through one cell, from 48 samples taken.
            float c = ApproachField.Concentration(20, 20);
            Assert.Equal(1f, c);
            Assert.True(ApproachField.IsChokepoint(c, Threshold));
        }

        /// <summary>Crossings accumulate and the peak tracks the busiest cell.</summary>
        [Fact]
        public void ThePeakFollowsTheBusiestCell()
        {
            ApproachField.Begin();
            ApproachField.Cross(11);
            ApproachField.Cross(22);
            ApproachField.Cross(22);
            ApproachField.Cross(22);
            ApproachField.Cross(33);

            Assert.Equal(3, ApproachField.CrossingsAt(22));
            Assert.Equal(1, ApproachField.CrossingsAt(11));
            Assert.Equal(0, ApproachField.CrossingsAt(99));
            Assert.Equal(22, ApproachField.PeakCell);
            Assert.Equal(3, ApproachField.PeakCrossings);
        }

        /// <summary>Samples are counted with and without routes, so an empty survey is visible.</summary>
        [Fact]
        public void SamplesAndRoutesAreCountedSeparately()
        {
            ApproachField.Begin();
            ApproachField.Sample(true);
            ApproachField.Sample(false);
            ApproachField.Sample(true);

            Assert.Equal(3, ApproachField.Sampled);
            Assert.Equal(2, ApproachField.RoutesFound);
        }

        /// <summary>
        /// Beginning a survey clears the last one. A field that accumulated across surveys would
        /// describe a base that no longer exists, which is the failure the staleness flag exists
        /// to prevent in the first place.
        /// </summary>
        [Fact]
        public void BeginningASurveyForgetsTheLastOne()
        {
            ApproachField.Begin();
            ApproachField.Cross(7);
            ApproachField.Sample(true);

            ApproachField.Begin();
            Assert.Equal(0, ApproachField.Sampled);
            Assert.Equal(0, ApproachField.CrossingsAt(7));
            Assert.Equal(-1, ApproachField.PeakCell);
        }

        /// <summary>
        /// The colony's own walls change the answer, so anything that changes the base has to be
        /// able to say so — and a fresh field must start stale rather than pretend to be current.
        /// </summary>
        [Fact]
        public void AWallMakesTheFieldStaleAndASurveyMakesItFresh()
        {
            ApproachField.Begin();
            Assert.False(ApproachField.IsStale);

            ApproachField.MarkStale();
            Assert.True(ApproachField.IsStale);

            ApproachField.Begin();
            Assert.False(ApproachField.IsStale);
        }

        /// <summary>A nonsense threshold must not make everything a chokepoint.</summary>
        [Fact]
        public void AZeroThresholdFallsBackRatherThanPassingEverything()
        {
            Assert.False(ApproachField.IsChokepoint(0.1f, 0f));
            Assert.True(ApproachField.IsChokepoint(0.9f, 0f));
        }

        /// <summary>Counts come before the verdict, so an empty survey reads as empty.</summary>
        [Fact]
        public void TheLineNamesTheCountsNotOnlyTheVerdict()
        {
            ApproachField.Begin();
            for (int i = 0; i < 44; i++) ApproachField.Sample(true);
            for (int i = 0; i < 4; i++) ApproachField.Sample(false);
            for (int i = 0; i < 37; i++) ApproachField.Cross(5);

            string line = ApproachField.Explain(Threshold);
            Assert.Contains("48", line);
            Assert.Contains("44", line);
            Assert.Contains("37", line);
            Assert.Contains("natural chokepoint", line);
        }
    }
}
