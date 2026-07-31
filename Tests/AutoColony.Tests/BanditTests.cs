using System.Collections.Generic;
using AutoColony.Learning;
using Xunit;

namespace AutoColony.Tests
{
    public class BanditTests
    {
        static readonly List<string> TwoArms = new List<string> { "bad", "good" };

        static float Pull(AcRandom rng, string arm, float badRate, float goodRate)
        {
            float p = arm == "good" ? goodRate : badRate;
            return rng.Value < p ? 1f : 0f;
        }

        [Fact]
        public void TriesEveryArmBeforeSettling()
        {
            var bandit = new Bandit();
            var seen = new HashSet<string>();
            var arms = new List<string> { "a", "b", "c", "d" };

            for (int i = 0; i < 4; i++)
            {
                var pick = bandit.Select(arms, 0.7f);
                seen.Add(pick);
                bandit.Update(pick, 0.5f);
            }

            Assert.Equal(4, seen.Count);
        }

        [Fact]
        public void ConvergesOnTheBetterArm()
        {
            var bandit = new Bandit();
            var rng = new AcRandom(5150);

            int goodPicksLate = 0;
            const int total = 600;

            for (int t = 0; t < total; t++)
            {
                var pick = bandit.Select(TwoArms, 0.5f);
                bandit.Update(pick, Pull(rng, pick, 0.2f, 0.8f));
                if (t >= total - 100 && pick == "good") goodPicksLate++;
            }

            Assert.True(goodPicksLate >= 70,
                "expected the better arm to dominate late selections, got " + goodPicksLate + "/100");
        }

        [Fact]
        public void TracksAShiftInWhichArmIsBest()
        {
            // The whole reason the bandit discounts: what pays off early in a colony often
            // stops paying off later, and an undiscounted mean would never notice.
            var bandit = new Bandit();
            var rng = new AcRandom(24680);

            for (int t = 0; t < 400; t++)
            {
                var pick = bandit.Select(TwoArms, 0.5f);
                bandit.Update(pick, Pull(rng, pick, 0.15f, 0.85f));
            }

            // Now invert the world: "bad" becomes the good arm.
            int badPicksLate = 0;
            const int after = 600;
            for (int t = 0; t < after; t++)
            {
                var pick = bandit.Select(TwoArms, 0.5f);
                bandit.Update(pick, Pull(rng, pick, 0.85f, 0.15f));
                if (t >= after - 100 && pick == "bad") badPicksLate++;
            }

            Assert.True(badPicksLate >= 60,
                "discounting should let the bandit follow the switch, got " + badPicksLate + "/100");
        }

        [Fact]
        public void UntriedArmsOutrankExperiencedOnes()
        {
            var bandit = new Bandit();
            bandit.Update("known", 1f);

            Assert.True(bandit.Score("fresh", 0.5f) > bandit.Score("known", 0.5f),
                "an unexplored option must be worth trying at least once");
        }

        [Fact]
        public void SelectHandlesEmptyAndNullInput()
        {
            var bandit = new Bandit();
            Assert.Null(bandit.Select(null, 0.5f));
            Assert.Null(bandit.Select(new List<string>(), 0.5f));
        }

        [Fact]
        public void MeanReflectsObservedRewards()
        {
            var bandit = new Bandit();
            for (int i = 0; i < 50; i++) bandit.Update("x", 1f);

            Assert.InRange(bandit.ArmFor("x").Mean, 0.9f, 1.0f);
        }

        [Fact]
        public void MergingPriorsCarriesExperienceAcross()
        {
            var source = new Bandit();
            for (int i = 0; i < 20; i++) source.Update("proven", 1f);

            var target = new Bandit();
            target.MergeFrom(source, 0.5f);

            Assert.True(target.ArmFor("proven").pulls > 0f);
            Assert.InRange(target.ArmFor("proven").Mean, 0.9f, 1.0f);
        }
    }
}
