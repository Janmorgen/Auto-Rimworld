using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Raids are sized from the colony itself, so building is buying larger attacks. Nothing in
    /// the director could see that relationship; fortification ramped off raw wealth on a
    /// straight line, which says nothing about whether the colony is keeping up.
    /// </summary>
    public class ThreatForecastTests
    {
        [Fact]
        public void APoorColonyDrawsNothingFromItsWealth()
        {
            Assert.Equal(0f, ThreatForecast.PointsFromWealth(0f), 2);
            Assert.Equal(0f, ThreatForecast.PointsFromWealth(ThreatForecast.WealthFloor), 2);
        }

        [Fact]
        public void TheCurveHitsItsDocumentedAnchors()
        {
            Assert.Equal(ThreatForecast.PointsAtMid,
                         ThreatForecast.PointsFromWealth(ThreatForecast.WealthMid), 1);
            Assert.Equal(ThreatForecast.PointsAtCeiling,
                         ThreatForecast.PointsFromWealth(ThreatForecast.WealthCeiling), 1);
        }

        [Fact]
        public void WealthBeyondTheCeilingBuysNoFurtherTrouble()
        {
            Assert.Equal(ThreatForecast.PointsAtCeiling,
                         ThreatForecast.PointsFromWealth(5000000f), 1);
        }

        [Fact]
        public void TheCurveNeverFallsAsWealthRises()
        {
            float previous = -1f;
            for (float wealth = 0f; wealth <= 1200000f; wealth += 25000f)
            {
                float points = ThreatForecast.PointsFromWealth(wealth);
                Assert.True(points >= previous);
                previous = points;
            }
        }

        [Fact]
        public void MoreColonistsDrawLargerRaids()
        {
            Assert.True(ThreatForecast.ExpectedRaidPoints(50000f, 10) >
                        ThreatForecast.ExpectedRaidPoints(50000f, 3));
        }

        [Fact]
        public void AnUnarmedColonyIsNotReady()
        {
            Assert.Equal(0f, ThreatForecast.Readiness(0f, 500f), 3);
        }

        [Fact]
        public void NoThreatMeansReady()
        {
            Assert.Equal(1f, ThreatForecast.Readiness(0f, 0f), 3);
        }

        [Fact]
        public void AnEmptyColonyReadsAsFullyArmed()
        {
            // The trap this function sets for its callers, pinned so it stays visible.
            //
            // Readiness is strength over the raid a colony's own wealth and headcount invite.
            // A colony with nobody left invites nothing, so the answer is a perfect 1.0. That is
            // right for the question asked and ruinous for a score that reads it at the instant
            // a colony ended: run 132 was wiped out with all four colonists bled out and banked
            // 0.35 of its Defense term on this reading.
            float wipedOut = ThreatForecast.Readiness(
                0f, ThreatForecast.ExpectedRaidPoints(11198f, 0));
            Assert.Equal(1f, wipedOut, 3);

            // Nor is it only the wipe. Below the wealth floor, wealth invites nothing at all, so
            // headcount is the only thing keeping the figure honest for a poor colony — which is
            // every colony for its first several days.
            Assert.Equal(0f, ThreatForecast.ExpectedRaidPoints(11198f, 0), 3);
            Assert.True(ThreatForecast.ExpectedRaidPoints(11198f, 4) > 0f);

            // Hence: anything *scoring* readiness must average it across the epoch, where
            // samples exist only while somebody is alive. See Accumulator.AvgReadiness.
        }

        [Fact]
        public void BuildingWithoutArmingLosesReadiness()
        {
            // The situation the forecast exists to notice: strength flat, wealth climbing.
            float early = ThreatForecast.Readiness(
                120f, ThreatForecast.ExpectedRaidPoints(20000f, 3));
            float later = ThreatForecast.Readiness(
                120f, ThreatForecast.ExpectedRaidPoints(300000f, 3));

            Assert.True(later < early);
            Assert.True(ThreatForecast.Outgrowing(120f, 300000f, 3));
            Assert.False(ThreatForecast.Outgrowing(120f, 20000f, 3));
        }

        [Fact]
        public void ArmingKeepsPaceWithBuilding()
        {
            float poorlyArmed = ThreatForecast.Readiness(
                100f, ThreatForecast.ExpectedRaidPoints(200000f, 5));
            float wellArmed = ThreatForecast.Readiness(
                900f, ThreatForecast.ExpectedRaidPoints(200000f, 5));
            Assert.True(wellArmed > poorlyArmed);
        }
    }
}
