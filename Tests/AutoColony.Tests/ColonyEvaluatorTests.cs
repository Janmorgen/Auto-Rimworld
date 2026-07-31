using System.Collections.Generic;
using AutoColony.Learning;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Property tests on the fitness function.
    ///
    /// The evaluator defines what "playing well" means, so a sign error here would send the
    /// whole search confidently in the wrong direction while every other component looked
    /// healthy. These assert the directions that must never invert.
    /// </summary>
    public class ColonyEvaluatorTests
    {
        static ColonyMetrics Baseline()
        {
            var m = ColonyMetrics.Neutral();
            m.colonists = 5;
            m.colonistBeds = 5;
            m.avgMood = 0.7f;
            m.avgHealth = 0.95f;
            m.daysOfFood = 10f;
            m.wealthTotal = 20000f;
            m.researchFinished = 10;
            m.turrets = 1;
            return m;
        }

        static EpochStart StartFrom(ColonyMetrics m)
        {
            var s = EpochStart.From(m);
            s.wealthTotal = 18000f;
            s.researchFinished = 9;
            return s;
        }

        /// <summary>Runs one epoch's worth of identical observations, then scores it.</summary>
        static float Score(ColonyMetrics end, int deaths = 0, int samples = 40)
        {
            var start = StartFrom(Baseline());
            var acc = new EpochAccumulator();

            var first = end;
            first.cumulativeDeaths = 0;
            acc.ResetFor(first);

            for (int i = 0; i < samples; i++)
            {
                var obs = end;
                obs.cumulativeDeaths = deaths;
                acc.Observe(obs);
            }

            List<ScoreTerm> breakdown;
            return ColonyEvaluator.Evaluate(start, end, acc, out breakdown);
        }

        [Fact]
        public void ScoreAlwaysLiesInUnitInterval()
        {
            var rng = new AcRandom(808);
            for (int i = 0; i < 500; i++)
            {
                var m = Baseline();
                m.colonists = rng.Range(0, 20);
                m.colonistBeds = rng.Range(0, 25);
                m.avgMood = rng.Value;
                m.avgHealth = rng.Value;
                m.daysOfFood = rng.Value * 40f;
                m.wealthTotal = rng.Value * 200000f;
                m.turrets = rng.Range(0, 15);
                m.researchFinished = rng.Range(0, 40);

                float score = Score(m, rng.Range(0, 6));
                Assert.InRange(score, 0f, 1f);
            }
        }

        [Fact]
        public void MoreDeathsNeverRaisesTheScore()
        {
            float previous = float.MaxValue;
            for (int deaths = 0; deaths <= 5; deaths++)
            {
                float score = Score(Baseline(), deaths);
                Assert.True(score <= previous + 1e-6f,
                    "score rose when deaths went up to " + deaths);
                previous = score;
            }
        }

        [Fact]
        public void MoreFoodNeverLowersTheScore()
        {
            float previous = float.MinValue;
            for (float days = 0f; days <= 20f; days += 2f)
            {
                var m = Baseline();
                m.daysOfFood = days;
                float score = Score(m);
                Assert.True(score >= previous - 1e-6f,
                    "score fell when food reserves rose to " + days + " days");
                previous = score;
            }
        }

        [Fact]
        public void BetterMoodNeverLowersTheScore()
        {
            float previous = float.MinValue;
            for (float mood = 0f; mood <= 1f; mood += 0.1f)
            {
                var m = Baseline();
                m.avgMood = mood;
                float score = Score(m);
                Assert.True(score >= previous - 1e-6f, "score fell as mood rose to " + mood);
                previous = score;
            }
        }

        [Fact]
        public void AWipedColonyScoresZero()
        {
            var m = Baseline();
            m.colonists = 0;
            Assert.Equal(0f, Score(m, 5), 5);
        }

        [Fact]
        public void FoodSecurityUsesTheWorstReserveNotTheFinalOne()
        {
            // A colony that starved for most of an epoch and recovered on the last day must
            // not score as though it were comfortable throughout.
            var start = StartFrom(Baseline());

            var end = Baseline();
            end.daysOfFood = 15f;

            var starved = new EpochAccumulator();
            starved.ResetFor(end);
            var lean = end;
            lean.daysOfFood = 0.5f;
            for (int i = 0; i < 39; i++) starved.Observe(lean);
            starved.Observe(end);

            var comfortable = new EpochAccumulator();
            comfortable.ResetFor(end);
            for (int i = 0; i < 40; i++) comfortable.Observe(end);

            List<ScoreTerm> a, b;
            float starvedScore = ColonyEvaluator.Evaluate(start, end, starved, out a);
            float comfortableScore = ColonyEvaluator.Evaluate(start, end, comfortable, out b);

            Assert.True(starvedScore < comfortableScore,
                "identical endpoints must still score differently when the epoch went badly");
        }

        [Fact]
        public void BreakdownContributionsSumToTheScore()
        {
            var start = StartFrom(Baseline());
            var end = Baseline();
            var acc = new EpochAccumulator();
            acc.ResetFor(end);
            for (int i = 0; i < 20; i++) acc.Observe(end);

            List<ScoreTerm> breakdown;
            float score = ColonyEvaluator.Evaluate(start, end, acc, out breakdown);

            float sum = 0f;
            foreach (var term in breakdown) sum += term.Contribution;

            Assert.Equal(score, sum, 4);
        }

        [Fact]
        public void WealthGrowthIsScaleFree()
        {
            // The same proportional growth should score the same for a poor colony and a rich
            // one, otherwise the score inflates as the colony matures and epochs stop comparing.
            var poorStart = new EpochStart { colonists = 5, wealthTotal = 10000f, researchFinished = 9 };
            var richStart = new EpochStart { colonists = 5, wealthTotal = 400000f, researchFinished = 9 };

            var poorEnd = Baseline();
            poorEnd.wealthTotal = 12000f;
            var richEnd = Baseline();
            richEnd.wealthTotal = 480000f;

            var acc = new EpochAccumulator();
            acc.ResetFor(poorEnd);
            for (int i = 0; i < 20; i++) acc.Observe(poorEnd);

            List<ScoreTerm> a, b;
            ColonyEvaluator.Evaluate(poorStart, poorEnd, acc, out a);
            ColonyEvaluator.Evaluate(richStart, richEnd, acc, out b);

            float poorGrowth = a.Find(t => t.name == "Growth").raw;
            float richGrowth = b.Find(t => t.name == "Growth").raw;

            Assert.Equal(poorGrowth, richGrowth, 3);
        }
    }
}
