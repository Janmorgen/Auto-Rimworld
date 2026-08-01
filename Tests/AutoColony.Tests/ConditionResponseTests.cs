using AutoColony.Conditions;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Map-wide conditions were invisible to the director entirely. These pin the judgements
    /// that decide whether a colony keeps working through one — which for toxic fallout is the
    /// difference between an inconvenience and a wipe.
    /// </summary>
    public class ConditionResponseTests
    {
        [Fact]
        public void AQuietMapChangesNothing()
        {
            var c = new ActiveConditions();
            Assert.False(c.Any);
            Assert.False(ConditionResponse.OutsideIsDangerous(c));
            Assert.False(ConditionResponse.SuspendElectiveOutdoorWork(c));
            Assert.Equal("nothing", ConditionResponse.Describe(c));
        }

        [Fact]
        public void ToxicFalloutMakesTheOpenSkyTheHazard()
        {
            var c = new ActiveConditions();
            c.toxicFallout = true;
            Assert.True(ConditionResponse.OutsideIsDangerous(c));
            Assert.True(ConditionResponse.SuspendElectiveOutdoorWork(c));
            Assert.True(ConditionResponse.CropsAtRisk(c));
        }

        [Fact]
        public void ASolarFlareIsAPowerCutAndNotAReasonToStayIn()
        {
            var c = new ActiveConditions();
            c.solarFlare = true;
            Assert.True(ConditionResponse.PowerIsOut(c));
            Assert.False(ConditionResponse.OutsideIsDangerous(c));
        }

        [Fact]
        public void AnEclipseStopsTheSunWithoutStoppingWork()
        {
            var c = new ActiveConditions();
            c.eclipse = true;
            Assert.True(ConditionResponse.NoSunlight(c));
            Assert.False(ConditionResponse.OutsideIsDangerous(c));
            Assert.False(ConditionResponse.PowerIsOut(c));
        }

        [Fact]
        public void AColdSnapThreatensTheFieldsButNotThePeopleOutdoors()
        {
            var c = new ActiveConditions();
            c.coldSnap = true;
            Assert.True(ConditionResponse.CropsAtRisk(c));
            Assert.False(ConditionResponse.OutsideIsDangerous(c));
        }

        [Fact]
        public void AHeatWaveIsNeitherAShelterNorACropEmergencyByItself()
        {
            var c = new ActiveConditions();
            c.heatWave = true;
            Assert.True(c.Any);
            Assert.False(ConditionResponse.OutsideIsDangerous(c));
            Assert.False(ConditionResponse.CropsAtRisk(c));
        }

        [Fact]
        public void SeveralAtOnceAreAllNamed()
        {
            var c = new ActiveConditions();
            c.toxicFallout = true;
            c.solarFlare = true;
            string described = ConditionResponse.Describe(c);
            Assert.Contains("toxic fallout", described);
            Assert.Contains("solar flare", described);
        }
    }
}
