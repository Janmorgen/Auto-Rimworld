using AutoColony.Learning;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The archive is what makes learning survive a colony, so a silent failure here would
    /// look exactly like "the mod never improves" while everything else worked.
    /// Writes go to a temp directory via the Verse shim, never to real save data.
    /// </summary>
    public class StrategyArchiveTests
    {
        static StrategyGenome Distinct(int seed)
        {
            return StrategyGenome.Default().Mutate(new AcRandom((ulong)seed), 0.5f, 1f);
        }

        [Fact]
        public void ContributedStrategyComesBackOut()
        {
            StrategyArchive.ResetAll();
            var genome = Distinct(1);

            StrategyArchive.Contribute("temperate|rough", genome, 0.75f, 5, "TestColony");

            var seed = StrategyArchive.GetSeed("temperate|rough");
            Assert.NotNull(seed);
            Assert.Equal(0.75f, seed.score, 4);
            Assert.Equal("TestColony", seed.sourceColony);
            Assert.Equal(0f, seed.genome.DistanceTo(genome), 5);
        }

        [Fact]
        public void OnlyBetterStrategiesReplaceTheStoredOne()
        {
            StrategyArchive.ResetAll();
            var good = Distinct(2);
            var worse = Distinct(3);

            StrategyArchive.Contribute("ctx", good, 0.9f, 5, "Good");
            StrategyArchive.Contribute("ctx", worse, 0.3f, 5, "Worse");

            var seed = StrategyArchive.GetSeed("ctx");
            Assert.Equal(0.9f, seed.score, 4);
            Assert.Equal(0f, seed.genome.DistanceTo(good), 5);
        }

        [Fact]
        public void AnUnknownContextFallsBackToTheGlobalBest()
        {
            StrategyArchive.ResetAll();
            var genome = Distinct(4);
            StrategyArchive.Contribute("boreal|rough", genome, 0.8f, 3, "Somewhere");

            var seed = StrategyArchive.GetSeed("desert|losing-is-fun");

            Assert.NotNull(seed);
            Assert.Equal(0f, seed.genome.DistanceTo(genome), 5);
        }

        [Fact]
        public void AnEmptyArchiveOffersNoSeed()
        {
            StrategyArchive.ResetAll();
            Assert.Null(StrategyArchive.GetSeed("anything"));
        }

        [Fact]
        public void BanditPriorsPersistAcrossColonies()
        {
            StrategyArchive.ResetAll();

            var research = new Bandit();
            for (int i = 0; i < 10; i++) research.Update("Electricity", 1f);

            StrategyArchive.ContributeBandits(research, new Bandit());

            Assert.True(StrategyArchive.ResearchPrior.ArmFor("Electricity").pulls > 0f);
        }

        [Fact]
        public void ContributionIgnoresUnscoredStrategies()
        {
            StrategyArchive.ResetAll();
            StrategyArchive.Contribute("ctx", Distinct(5), float.NaN, 1, "NoScore");

            Assert.Null(StrategyArchive.GetSeed("ctx"));
        }
    }
}
