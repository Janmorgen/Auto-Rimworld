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
